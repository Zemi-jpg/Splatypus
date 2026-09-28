using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RhinoSplat.Core;

if (args.Length > 0 && args[0] == "--progress-child")
{
    Console.Write("stdout live fragment"); Console.Out.Flush();
    Console.Error.Write("stderr live fragment"); Console.Error.Flush();
    while (!File.Exists(args[1])) await Task.Delay(25);
    Console.WriteLine(" — completed");
    return;
}

var checks = 0;
void Check(bool passed, string name) { if (!passed) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
var root = Path.Combine(Path.GetTempPath(), "RhinoSplat-DownloadTools-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var fixture = new FixtureHandler();
using var client = new HttpClient(fixture);
Task FakeValidation(ToolPackage package, string path, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    if (!File.Exists(path)) throw new IOException("Executable missing");
    return Task.CompletedTask;
}
var installer = new ToolDownloadInstaller(client, FakeValidation);
byte[] Zip(params (string Name, string Text)[] entries)
{
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
    return memory.ToArray();
}
var bytes = Zip(("bin/test.exe", "fixture"), ("bin/dependency.dll", "dependency"));
fixture.Bytes = bytes;
var package = new ToolPackage("FFmpeg", "1", "fixture", "https://example.invalid/tool.zip", Convert.ToHexString(SHA256.HashData(bytes)), "bin/test.exe");
var packages = new[] { package };
var result = await installer.RunPackagesAsync(root, packages, false, null, default);
Check(result.FFmpeg == "" && fixture.Calls == 0, "Read-only discovery does not download");
Check(result.LogFile == "" && !Directory.Exists(Path.Combine(root, "logs")), "Read-only discovery does not create a log or terminal");
result = await installer.RunPackagesAsync(root, packages, true, null, default);
Check(File.Exists(result.FFmpeg) && fixture.Calls == 1, "Install verifies archive and returns executable path");
var firstLog = result.LogFile;
var logText = File.ReadAllText(firstLog);
Check(logText.Contains("Verifying FFmpeg checksum") && logText.Contains("Extracting") && logText.Contains("Installation attempt finished"), "Saved install log contains progress and completion");
var original = result.FFmpeg;
result = await installer.RunPackagesAsync(root, packages, true, null, default);
Check(result.FFmpeg == original && fixture.Calls == 1, "Repeated Download reuses complete installation without network");
Check(result.LogFile != firstLog && File.Exists(firstLog), "Each attempt retains its own log");
File.Delete(Path.Combine(Path.GetDirectoryName(original)!, "dependency.dll"));
result = await installer.RunPackagesAsync(root, packages, false, null, default);
Check(result.FFmpeg == "", "Missing dependency invalidates receipt and suppresses output");
result = await installer.RunPackagesAsync(root, packages, true, null, default);
Check(result.FFmpeg != original && File.Exists(original), "Repair uses a new directory and preserves earlier installation");
var goodPath = result.FFmpeg;
var bad = package with { Id = "bad-checksum", Sha256 = new string('0', 64) };
result = await installer.RunPackagesAsync(root, new[] { bad }, true, null, default);
Check(result.FFmpeg == "" && result.Status.Contains("checksum mismatch") && !File.Exists(Path.Combine(root, bad.Id + ".json")), "Checksum failure never publishes a receipt or path");
Check(File.ReadAllText(result.LogFile).Contains("checksum mismatch"), "Failure details are written to the log");
Check(!Directory.GetDirectories(Path.Combine(root, "installations"), "bad-checksum-*").Any(), "Failed install cleans up only its own staging directory");
fixture.Bytes = Zip(("../escape.txt", "bad"));
var traversal = package with { Id = "traversal", Sha256 = Convert.ToHexString(SHA256.HashData(fixture.Bytes)) };
result = await installer.RunPackagesAsync(root, new[] { traversal }, true, null, default);
Check(result.FFmpeg == "" && result.Status.Contains("escapes"), "Archive path traversal rejected");
fixture.Bytes = bytes;
fixture.Fail = true;
result = await installer.RunPackagesAsync(root, new[] { package, package with { Name = "Brush", Id = "offline" } }, true, null, default);
Check(result.FFmpeg == goodPath && result.Brush == "" && !result.Ready, "Partial network failure preserves successful tool paths");
fixture.Fail = false;
using (var cancel = new CancellationTokenSource())
{
    result = await installer.RunPackagesAsync(root, new[] { package with { Id = "cancel" } }, true,
        text => { if (text.StartsWith("Extracting")) cancel.Cancel(); }, cancel.Token);
    Check(result.Cancelled && result.FFmpeg == "" && !File.Exists(Path.Combine(root, "cancel.json")), "Cancellation during extraction does not publish a partial install");
    Check(File.ReadAllText(result.LogFile).Contains("Installation cancelled"), "Cancellation is visible in retained log");
}
Check(File.Exists(goodPath), "Failure and cancellation preserve completed installs");
fixture.DelayMilliseconds = 100;
var concurrentRoot = Path.Combine(root, "concurrent");
var beforeCalls = fixture.Calls;
var concurrent = await Task.WhenAll(
    Task.Run(() => installer.RunPackagesAsync(concurrentRoot, packages, true, null, default)),
    Task.Run(() => installer.RunPackagesAsync(concurrentRoot, packages, true, null, default)));
Check(concurrent[0].FFmpeg == concurrent[1].FFmpeg && fixture.Calls == beforeCalls + 1,
    "Concurrent components share one installation under a folder lock");
fixture.DelayMilliseconds = 0;
var windows = ToolDownloadCatalog.Get(true, Architecture.X64, true);
var cpu = ToolDownloadCatalog.Get(true, Architecture.X64, false);
var mac = ToolDownloadCatalog.Get(false, Architecture.Arm64, true);
Check(windows[1].Id != cpu[1].Id && cpu[1].Cpu && !windows[1].Cpu, "CUDA and CPU variants have isolated installations");
Check(mac[0].Kind == "conda" && mac[1].Cpu && mac[2].Kind == "tar", "Apple Silicon selects native tools and CPU COLMAP");
try { ToolDownloadCatalog.Get(false, Architecture.X64, false); Check(false, "Intel Mac must be explicit"); }
catch (PlatformNotSupportedException e) { Check(e.Message.Contains("Intel"), "Intel Mac reports absent upstream Brush binary"); }

var calls = new List<(ToolDownloadRequest Request, bool Install, TaskCompletionSource<ToolDownloadResult> Completion)>();
using (var session = new ToolDownloadSession((request, install, progress, token) =>
{
    var completion = new TaskCompletionSource<ToolDownloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (calls) calls.Add((request, install, completion));
    token.Register(() => completion.TrySetCanceled(token));
    return completion.Task;
}))
{
    async Task WaitCalls(int count)
    {
        var timeout = Stopwatch.StartNew();
        while (true) { lock (calls) if (calls.Count >= count) return; if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Session timeout"); await Task.Delay(10); }
    }
    async Task WaitCompleted()
    {
        var timeout = Stopwatch.StartNew();
        while (!session.HasChanges) { if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Completion timeout"); await Task.Delay(10); }
    }
    var request = new ToolDownloadRequest(root);
    var state = session.Update(request, false, false);
    await WaitCalls(1);
    Check(state.Busy && !calls[0].Install, "Session performs initial discovery in background");
    session.Update(request, true, false);
    calls[0].Completion.SetResult(new("", "", "", "", "Not installed"));
    await WaitCompleted(); session.Update(request, true, false); await WaitCalls(2);
    Check(calls[1].Install, "Download during discovery queues one installation");
    state = session.Update(request with { ShowTerminal = true }, true, false);
    Check(state.Busy && !calls[1].Completion.Task.IsCanceled, "Changing terminal preference does not cancel active installation");
    calls[1].Completion.SetResult(new("ffmpeg", "colmap", "brush", "versions", "Ready"));
    await WaitCompleted(); state = session.Update(request, true, false);
    Check(state.Result?.Ready == true && !state.Busy && calls.Count == 2, "Held True does not restart installation");
    var other = request with { Folder = root + "-other" };
    state = session.Update(other, false, false); await WaitCalls(3);
    Check(state.Result is null && state.Busy, "Changing folder clears stale output paths");
    session.Update(other, false, true); await WaitCompleted();
    state = session.Update(other, false, false);
    Check(state.Result?.Cancelled == true && !state.Busy, "Cancelled work does not automatically restart");
}

var gate = Path.Combine(root, "release-child");
string ReadLiveLog(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
using (var liveLog = new ToolDownloadLog(root))
{
    var stdoutSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stderrSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var child = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "RhinoSplat.DownloadTools.SmokeTest.exe" : "RhinoSplat.DownloadTools.SmokeTest");
    var execution = ToolDownloadInstaller.ExecuteAsync(child, new[] { "--progress-child", gate }, TimeSpan.FromSeconds(15), default, fragment =>
    {
        liveLog.Append(fragment);
        var text = ReadLiveLog(liveLog.Path);
        if (text.Contains("stdout live fragment")) stdoutSeen.TrySetResult();
        if (text.Contains("stderr live fragment")) stderrSeen.TrySetResult();
    });
    try
    {
        await Task.WhenAll(stdoutSeen.Task, stderrSeen.Task).WaitAsync(TimeSpan.FromSeconds(8));
        Check(!execution.IsCompleted, "stdout and stderr flush to log before process exit, even without newlines");
    }
    finally { File.WriteAllText(gate, "continue"); }
    var output = await execution;
    Check(output.Contains("completed") && ReadLiveLog(liveLog.Path).Contains("completed"), "Process output remains available for validation and terminal log");
}

if (args.Contains("--live"))
{
    var workspace = Path.GetFullPath(args[Array.IndexOf(args, "--live") + 1]);
    var archives = Path.Combine(workspace, "archives");
    using var liveClient = new HttpClient(new ArchiveHandler(archives));
    var live = new ToolDownloadInstaller(liveClient, ToolDownloadInstaller.ValidateAsync);
    foreach (var cuda in new[] { true, false })
    {
        var livePackages = ToolDownloadCatalog.Get(true, Architecture.X64, cuda);
        var liveResult = await live.RunPackagesAsync(Path.Combine(workspace, "tools"), livePackages, true, Console.WriteLine, default);
        Console.WriteLine(liveResult.Status);
        Check(liveResult.Ready, "Real Windows packages start and support required CLI options (CUDA=" + cuda + ")");
        File.WriteAllText(Path.Combine(workspace, cuda ? "paths-cuda.json" : "paths-cpu.json"), System.Text.Json.JsonSerializer.Serialize(liveResult));
        if (!cuda)
        {
            var command = new ProcessStartInfo(liveResult.Colmap) { WorkingDirectory = Path.GetDirectoryName(liveResult.Colmap)! };
            await ColmapPlatform.ConfigureCpuAsync(command, true, default);
            Check(command.ArgumentList.Contains("--SiftExtraction.use_gpu") && command.ArgumentList.Contains("0"), "Downloaded CPU COLMAP propagates CPU mode to extraction");
            command.ArgumentList.Clear(); await ColmapPlatform.ConfigureCpuAsync(command, false, default);
            Check(command.ArgumentList.Contains("--SiftMatching.use_gpu"), "Downloaded CPU COLMAP propagates CPU mode to matching");
        }
    }
}
Console.WriteLine($"{checks} Download Tools checks passed.");

sealed class FixtureHandler : HttpMessageHandler
{
    public byte[] Bytes = Array.Empty<byte>(); public int Calls; public bool Fail; public int DelayMilliseconds;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Interlocked.Increment(ref Calls); if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token); return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) }; }
}
sealed class ArchiveHandler(string folder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var file = url.Contains("nocuda") ? "colmap-cpu.zip" : url.Contains("colmap") ? "colmap-cuda.zip" : url.Contains("brush") ? "brush.zip" : "ffmpeg.zip";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(Path.Combine(folder, file))) });
    }
}
