using System.Diagnostics;
using RhinoSplat.Core;

// A controlled COLMAP stand-in exercises the real process runners and their output paths.
if (args.Length > 0)
{
    if (args.Contains("-h")) { Console.WriteLine("FeatureExtraction.use_gpu FeatureMatching.use_gpu"); return; }
    string Value(string flag) => args[Array.IndexOf(args, flag) + 1];
    var stage = args[0];
    var probe = args.Contains("--database_path") ? Value("--database_path") : Value("--output_path");
    var folder = new DirectoryInfo(Path.GetDirectoryName(probe)!);
    while (!File.Exists(Path.Combine(folder.FullName, ".test-root"))) folder = folder.Parent ?? throw new Exception("Missing fixture root");
    var root = folder.FullName;
    File.AppendAllText(Path.Combine(root, "order.txt"), stage + "\n");
    Console.WriteLine("running " + stage);
    if (File.Exists(Path.Combine(root, "stall")))
    {
        File.WriteAllText(Path.Combine(root, "pid"), Environment.ProcessId.ToString());
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
    if (File.Exists(Path.Combine(root, "fail")) && File.ReadAllText(Path.Combine(root, "fail")) == stage)
    { Console.Error.WriteLine("controlled failure"); Environment.Exit(7); }
    if (stage == "feature_extractor") File.WriteAllText(Value("--database_path"), "database");
    if (stage == "mapper")
    {
        var model = Path.Combine(Value("--output_path"), "0");
        Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(model, "cameras.bin"), "1 PINHOLE 40 30 35 35 20 15\n");
        File.WriteAllText(Path.Combine(model, "images.bin"), "1 1 0 0 0 0 0 0 1 test.jpg\n\n");
        File.WriteAllText(Path.Combine(model, "points3D.bin"), "1 1 2 3 255 128 64 0.1 1 0\n");
    }
    if (stage == "image_undistorter")
    {
        var images = Path.Combine(Value("--output_path"), "images"); Directory.CreateDirectory(images);
        File.Copy(Path.Combine(Value("--image_path"), "test.jpg"), Path.Combine(images, "test.jpg"));
        var sparse = Path.Combine(Value("--output_path"), "sparse"); Directory.CreateDirectory(sparse);
        foreach (var name in ActiveDataset.ModelFiles) File.Copy(Path.Combine(Value("--input_path"), name), Path.Combine(sparse, name));
    }
    if (stage == "model_converter")
        foreach (var name in new[] { "cameras", "images", "points3D" })
            File.Copy(Path.Combine(Value("--input_path"), name + ".bin"), Path.Combine(Value("--output_path"), name + ".txt"), true);
    return;
}

var fixtures = Path.Combine(Path.GetTempPath(), "Splatypus-ColmapPipeline-Tests", Guid.NewGuid().ToString("N"));
var executable = Path.Combine(AppContext.BaseDirectory, "RhinoSplat.ColmapPipeline.SmokeTest" + (OperatingSystem.IsWindows() ? ".exe" : ""));
ColmapPipelineRequest Request(string name)
{
    var root = Path.Combine(fixtures, name);
    Directory.CreateDirectory(Path.Combine(root, "images"));
    File.WriteAllText(Path.Combine(root, "images", "test.jpg"), "fixture");
    File.WriteAllText(Path.Combine(root, ".test-root"), "");
    return new(executable, root, Undistort: true);
}
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++; Console.WriteLine("PASS " + message);
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
var full = Request("full");
var steps = new List<ColmapPipelineProgress>();
var result = await ColmapPipelineRunner.RunAsync(full, CancellationToken.None, steps.Add);
Check(result.Succeeded && result.Completed == 6 && result.Total == 6, "All six stages complete.");
var expected = new[] { "feature_extractor", "exhaustive_matcher", "mapper", "model_converter", "image_undistorter", "model_converter", "model_converter" };
Check(File.ReadAllLines(Path.Combine(full.ProjectRoot, "order.txt")).SequenceEqual(expected), "Processes execute in the correct order.");
Check(steps.Last().Completed == 6 && steps.Select(x => x.Completed).SequenceEqual(Enumerable.Range(0, 7)), "Progress increases monotonically to completion.");
Check(!File.Exists(result.DatabasePath) && File.Exists(Path.Combine(result.SparsePath, "0", "cameras.bin"))
    && Directory.Exists(Path.Combine(result.DensePath, "images")) && File.Exists(Path.Combine(result.TextPath, "cameras.txt")), "Output paths match individual components.");
Check(expected.All(x => result.Log.Contains("running " + x)) && File.ReadAllText(Path.Combine(full.ProjectRoot, "colmap", "pipeline.log")).Contains("running model_converter"), "All stage logs are preserved.");
Check(File.ReadLines(Path.Combine(full.ProjectRoot, "colmap", "pipeline.log")).Count(l => l == "running feature_extractor") == 1
    && File.ReadLines(Path.Combine(full.ProjectRoot, "colmap", "pipeline.log")).Count(l => l == "running model_converter") == 3,
    "Combined terminal log includes each subprocess line once.");
Check(result.Log.Contains("COLMAP model saved to: " + result.TextPath)
    && File.ReadAllText(Path.Combine(full.ProjectRoot, "colmap", "model-export.log")).Contains("COLMAP model saved to: " + result.TextPath)
    && File.ReadAllText(Path.Combine(full.ProjectRoot, "colmap", "pipeline.log")).Contains("COLMAP model saved to: " + result.TextPath),
    "Successful export reports its final saved path in component and terminal logs.");
result = await ColmapPipelineRunner.RunAsync(full, CancellationToken.None);
Check(!result.Succeeded && result.Status.Contains("Replace") && File.ReadAllLines(Path.Combine(full.ProjectRoot, "order.txt")).Length == 7, "Existing outputs are refused before any subprocess runs.");
var exportOnly = full with { Extract = false, Match = false, Sparse = false, Undistort = false, ReplaceExisting = true };
File.WriteAllText(Path.Combine(full.ProjectRoot, "images", "keep.txt"), "keep");
File.WriteAllText(Path.Combine(full.ProjectRoot, "colmap", "sparse-text", "replace.txt"), "old");
result = await ColmapPipelineRunner.RunAsync(exportOnly, CancellationToken.None);
Check(result.Succeeded && result.Completed == 2 && File.ReadAllLines(Path.Combine(full.ProjectRoot, "order.txt")).Last() == "model_converter", "Export-only reuses existing model and counts only selected stages.");
Check(File.Exists(Path.Combine(result.DatasetPath, "images", "keep.txt")) && !File.Exists(Path.Combine(result.TextPath, "replace.txt")), "Replace only modifies enabled stage output.");
var missing = Request("missing") with { Extract = false };
result = await ColmapPipelineRunner.RunAsync(missing, CancellationToken.None);
Check(!result.Succeeded && result.Status.Contains("database") && !File.Exists(Path.Combine(missing.ProjectRoot, "order.txt")), "Skipped prerequisite validation prevents launching processes.");
var failure = Request("failure");
File.WriteAllText(Path.Combine(failure.ProjectRoot, "fail"), "exhaustive_matcher");
result = await ColmapPipelineRunner.RunAsync(failure, CancellationToken.None);
Check(!result.Succeeded && result.ExitCode == 7 && result.Completed == 2 && File.ReadAllLines(Path.Combine(failure.ProjectRoot, "order.txt")).Length == 2, "Failure preserves exit code and stops later stages.");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    result = await ColmapPipelineRunner.RunAsync(Request("pre-cancel"), cancelled.Token);
    Check(result.WasCancelled && result.Completed == 0, "Pre-cancelled runs do not prepare or launch work.");
}
result = await ColmapPipelineRunner.RunAsync(new(executable, ""), CancellationToken.None);
Check(!result.Succeeded && result.Status.Contains("Project Root"), "Blank project roots fail without using the working directory.");

var background = Request("background");
File.WriteAllText(Path.Combine(background.ProjectRoot, "stall"), "");
using (var session = new ColmapPipelineSession())
{
    var watch = Stopwatch.StartNew();
    var state = session.Update(background, true, false);
    Check(state.IsRunning && watch.ElapsedMilliseconds < 500, "Starting the pipeline returns promptly.");
    await Wait(() => File.Exists(Path.Combine(background.ProjectRoot, "pid")) && new FileInfo(Path.Combine(background.ProjectRoot, "pid")).Length > 0);
    var pid = int.Parse(File.ReadAllText(Path.Combine(background.ProjectRoot, "pid")));
    try
    {
        await Wait(() => File.ReadAllText(Path.Combine(background.ProjectRoot, "colmap", "pipeline.log")).Contains("running feature_extractor"));
        Check(Running(pid), "The combined log receives live output while the current stage is still running.");
        result = await ColmapPipelineRunner.RunAsync(background, CancellationToken.None);
        Check(!result.Succeeded && result.Status.Contains("already running"), "Duplicate pipelines for one project are refused.");
        var changed = Request("changed");
        for (var i = 0; i < 50; i++) session.Update(changed, i % 2 == 0, false);
        Check(!File.Exists(Path.Combine(changed.ProjectRoot, "order.txt")), "Input changes cannot launch a second job while busy.");
        state = session.Update(background, true, true);
        Check(state.IsCancelling, "Cancel acknowledges immediately.");
        await Wait(() => { state = session.Update(background, true, false); return !state.IsRunning; });
        await Wait(() => !Running(pid));
        Check(state.Result?.WasCancelled == true && File.ReadAllLines(Path.Combine(background.ProjectRoot, "order.txt")).Length == 1, "Cancellation kills the process and stops subsequent stages.");
        Check(!session.Update(background, true, false).IsRunning, "Held Run does not restart a completed or cancelled job.");
        var setupOnly = background with { Extract = false, Match = false, Sparse = false, Undistort = false, Export = false };
        session.Update(setupOnly, false, false);
        session.Update(setupOnly, true, false);
        await Wait(() => { state = session.Update(setupOnly, true, false); return !state.IsRunning; });
        Check(state.Result?.Succeeded == true && state.Result.Total == 1, "A new Run edge starts a setup-only job after cancellation.");
    }
    finally { if (Running(pid)) { using var process = Process.GetProcessById(pid); process.Kill(true); } }
}
var disposal = Request("dispose");
File.WriteAllText(Path.Combine(disposal.ProjectRoot, "stall"), "");
var disposable = new ColmapPipelineSession();
disposable.Update(disposal, true, false);
await Wait(() => File.Exists(Path.Combine(disposal.ProjectRoot, "pid")) && new FileInfo(Path.Combine(disposal.ProjectRoot, "pid")).Length > 0);
var disposalPid = int.Parse(File.ReadAllText(Path.Combine(disposal.ProjectRoot, "pid")));
disposable.Dispose();
await Wait(() => !Running(disposalPid));
Check(!Running(disposalPid), "Disposal stops owned processing.");
using (var quiet = new ColmapPipelineSession())
{
    var notifications = 0;
    quiet.StateChanged += () => Interlocked.Increment(ref notifications);
    var input = Request("quiet");
    quiet.Update(input, true, false, false);
    ColmapPipelineState state = null!;
    await Wait(() => { state = quiet.Update(input, true, false, false); return !state.IsRunning; });
    await Wait(() => Volatile.Read(ref notifications) > 0);
    Check(state.Result?.Succeeded == true && notifications == 1, "Progress=False suppresses intermediate notifications but publishes completion.");
}
Console.WriteLine($"{passed} checks passed. Fixtures: {fixtures}");
