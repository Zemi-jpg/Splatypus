using System.Diagnostics;
using RhinoSplat.Core;

// Controlled subprocess exercises the real launcher, output pipes and process cancellation.
if (args.Contains("--total-steps"))
{
    var steps = int.Parse(args[Array.IndexOf(args, "--total-steps") + 1]);
    File.WriteAllText("checkpoint.ply", "already exported checkpoint");
    File.WriteAllText("started.txt", Environment.ProcessId + "\n" + string.Join("\n", args));
    if (steps > 2) await Task.Delay(TimeSpan.FromMinutes(2));
    for (var i = 0; i < 8000; i++) Console.WriteLine("Brush test diagnostic " + i + new string('x', 80));
    Console.WriteLine("FINAL TRAINING SUMMARY");
    Environment.Exit(steps == 2 ? 7 : 0);
    return;
}
var root = Path.Combine(Path.GetTempPath(), "RhinoSplat-BrushBackground-Tests", Guid.NewGuid().ToString("N"));
var dataset = Path.Combine(root, "dataset");
Directory.CreateDirectory(Path.Combine(dataset, "images"));
Directory.CreateDirectory(Path.Combine(dataset, "sparse", "0"));
File.WriteAllText(Path.Combine(dataset, "sparse", "0", "cameras.bin"), "fixture");
var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "RhinoSplat.BrushBackground.SmokeTest.exe" : "RhinoSplat.BrushBackground.SmokeTest");
var request = new BrushTrainingRequest(executable, dataset, root, "stall", 99, false, false, false, false);
Console.WriteLine("Test fixtures: " + root);
var passed = 0;
void Check(bool condition, string text)
{
    if (!condition) throw new InvalidOperationException(text);
    passed++; Console.WriteLine("PASS " + text);
}
async Task Wait(Func<bool> condition)
{
    var watch = Stopwatch.StartNew();
    while (!condition())
    {
        if (watch.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Background work did not settle.");
        await Task.Delay(20);
    }
}
bool Running(int pid)
{
    try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
    catch (ArgumentException) { return false; }
}
async Task<BrushTrainingState> Finish(BrushTrainingSession session, BrushTrainingRequest input)
{
    BrushTrainingState state = session.Update(input, true, false);
    await Wait(() => { state = session.Update(input, true, false); return !state.IsTraining; });
    return state;
}
using (var session = new BrushTrainingSession())
{
    var watch = Stopwatch.StartNew();
    var state = session.Update(request, true, false);
    Check(state.IsTraining && watch.ElapsedMilliseconds < 500, "Starting training returns promptly without waiting for the child process.");
    var marker = Path.Combine(root, "output", "stall", "started.txt");
    await Wait(() => File.Exists(marker) && new FileInfo(marker).Length > 0);
    var pid = int.Parse(File.ReadAllLines(marker)[0]);
    try
    {
        var changed = request with { JobName = "changed", TotalSteps = 1 };
        watch.Restart();
        for (var i = 0; i < 100; i++) session.Update(changed, i % 2 == 0, false);
        Check(watch.ElapsedMilliseconds < 500 && !Directory.Exists(Path.Combine(root, "output", "changed")) && Running(pid),
            "Recomputes and input changes stay responsive and cannot restart an active job.");
        Check(File.ReadAllLines(marker).Contains("99"), "The active job retains its original input snapshot.");
        Check(File.ReadAllLines(marker).Contains("sp_{iter}.ply"), "Brush receives the new SP export filename pattern.");
        watch.Restart();
        state = session.Update(request, true, true);
        Check(state.IsCancelling && watch.ElapsedMilliseconds < 500, "Cancel returns promptly.");
        state = await Finish(session, request);
        await Wait(() => !Running(pid));
        Check(state.Result?.WasCancelled == true, "Cancellation stops the owned process and publishes a cancelled result.");
        Check(File.Exists(Path.Combine(root, "output", "stall", "checkpoint.ply")), "Cancellation retains previously exported checkpoints.");
        for (var i = 0; i < 100; i++) state = session.Update(request, true, false);
        Check(!state.IsTraining, "A held Run toggle cannot restart training after cancellation.");
    }
    finally { if (Running(pid)) { using var process = Process.GetProcessById(pid); process.Kill(true); } }
}
var completeRequest = request with { JobName = "complete", TotalSteps = 1 };
using (var session = new BrushTrainingSession())
{
    var notifications = 0;
    session.ResultAvailable += () => Interlocked.Increment(ref notifications);
    session.Update(completeRequest, true, false);
    var done = await Finish(session, completeRequest);
    await Wait(() => Volatile.Read(ref notifications) == 1);
    Check(done.Result?.Succeeded == true && done.Result.ExitCode == 0, "Successful child completion publishes results.");
    var result = done.Result!;
    var preview = BrushLogPreview.Create(result.Log, result.ExportPath);
    Check(preview.Length < 6400 && preview.Contains("FINAL TRAINING SUMMARY") && preview.Contains("brush-training.log"),
        "Large logs produce a bounded preview with final diagnostics and full log path.");
    Check(new FileInfo(Path.Combine(result.ExportPath, "brush-training.log")).Length > 6000, "The complete log stays on disk.");
    for (var i = 0; i < 100; i++) done = session.Update(completeRequest, true, false);
    await Task.Delay(200);
    Check(!done.IsTraining && ReferenceEquals(result, done.Result) && notifications == 1 && !session.HasCompletedWork,
        "Completed training stays idle without repeated jobs or completion notifications.");
    var failureRequest = request with { JobName = "failure", TotalSteps = 2 };
    session.Update(failureRequest, false, false);
    session.Update(failureRequest, true, false);
    done = await Finish(session, failureRequest);
    Check(done.Result?.Succeeded == false && done.Result.ExitCode == 7, "A new Run edge starts the next job and preserves failure exit codes.");
}
var disposeRequest = request with { JobName = "dispose" };
var disposal = new BrushTrainingSession();
disposal.Update(disposeRequest, true, false);
var disposalMarker = Path.Combine(root, "output", "dispose", "started.txt");
await Wait(() => File.Exists(disposalMarker) && new FileInfo(disposalMarker).Length > 0);
var disposalPid = int.Parse(File.ReadAllLines(disposalMarker)[0]);
disposal.Dispose();
await Wait(() => !Running(disposalPid));
Check(!Running(disposalPid), "Removing or unloading the component cancels its owned process.");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try
    {
        await BrushTrainingRunner.RunAsync(executable, dataset, root, "complete", 1, false, false, true, false, cancellation.Token);
        throw new Exception("Expected cancellation.");
    }
    catch (OperationCanceledException) { }
    Check(File.Exists(Path.Combine(root, "output", "complete", "checkpoint.ply")), "An already-cancelled Replace request cannot delete exports.");
}
var sourcePly = Path.Combine(root, "rhinosplat_30000.ply");
using (var writer = new BinaryWriter(File.Create(sourcePly)))
{
    writer.Write(System.Text.Encoding.ASCII.GetBytes("ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nproperty float rot_0\nproperty float rot_1\nproperty float rot_2\nproperty float rot_3\nend_header\n"));
    foreach (var value in new float[] { 1, 2, 3, 1, 0, 0, 0 }) writer.Write(value);
}
var originalBytes = File.ReadAllBytes(sourcePly);
var rotated = BrushPlyRhinoRotation.CreateVisualizationCopy(sourcePly, default);
Check(rotated.Succeeded && rotated.OutputPath.EndsWith("_rgs.ply") && File.Exists(rotated.OutputPath),
    "Visualization rotation writes a separate _rgs.ply file.");
Check(File.ReadAllBytes(sourcePly).SequenceEqual(originalBytes), "Visualization export preserves the original PLY byte for byte.");
using (var input = File.OpenRead(rotated.OutputPath))
{
    var line = new System.Text.StringBuilder();
    while (true)
    {
        var b = input.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        if (b == '\n') { if (line.ToString() == "end_header") break; line.Clear(); }
        else line.Append((char)b);
    }
    using var reader = new BinaryReader(input);
    Check(reader.ReadSingle() == 1 && reader.ReadSingle() == -3 && reader.ReadSingle() == 2,
        "Visualization copy retains the requested +90-degree X rotation, without reflection.");
}
var findLatest = typeof(BrushTrainingRunner).GetMethod("FindLatestBrushPly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
File.SetLastWriteTimeUtc(rotated.OutputPath, DateTime.UtcNow.AddMinutes(1));
Check((string?)findLatest.Invoke(null, new object[] { root }) == sourcePly,
    "Existing _rgs copies are excluded when selecting the original training export.");
Console.WriteLine($"All {passed} Brush background and visualization checks passed.");
