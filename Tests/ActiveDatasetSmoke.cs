using System.Diagnostics;
using RhinoSplat.Core;

const string camera = "1 SIMPLE_RADIAL 40 30 35 20 15 0.05\n";
const string observations = "10 11 1 12 13 -1 14 15 -1 16 17 -1";
const string pose = "1 1 0 0 0 0 0 0 1 nested/frame.jpg\n" + observations + "\n";
const string points = "1 1 2 3 255 128 64 0.1 1 0\n";
if (args.FirstOrDefault() == "--real-colmap")
{
    await ActiveDatasetReal.Run(args[1], args[2]);
    return;
}
if (args.Length > 0)
{
    if (args[0] == "-hide_banner") { File.WriteAllText(args[^1], "imported pixels"); return; }
    if (args.Contains("-h")) { Console.WriteLine("FeatureExtraction.use_gpu FeatureMatching.use_gpu"); return; }
    string Value(string flag) => args[Array.IndexOf(args, flag) + 1];
    var stage = args[0];
    var start = args.Contains("--database_path") ? Value("--database_path") : Value("--output_path");
    var folder = new DirectoryInfo(Path.GetDirectoryName(start)!);
    while (!File.Exists(Path.Combine(folder.FullName, ".test-root"))) folder = folder.Parent ?? throw new Exception("No fixture root");
    var root = folder.FullName;
    var operation = stage == "model_converter" ? stage + ":" + Value("--output_type") : stage;
    File.AppendAllText(Path.Combine(root, "order.txt"), operation + "\n");
    if (File.Exists(Path.Combine(root, "stall")) && File.ReadAllText(Path.Combine(root, "stall")) == operation)
    {
        File.WriteAllText(Path.Combine(root, "pid"), Environment.ProcessId.ToString());
        await Task.Delay(TimeSpan.FromSeconds(40));
    }
    if (File.Exists(Path.Combine(root, "fail")) && File.ReadAllText(Path.Combine(root, "fail")) == operation) Environment.Exit(7);
    if (stage == "feature_extractor") File.WriteAllText(Value("--database_path"), "features");
    if (stage == "mapper")
    {
        var model = Path.Combine(Value("--output_path"), "0"); Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(model, "cameras.bin"), camera);
        File.WriteAllText(Path.Combine(model, "images.bin"), pose);
        File.WriteAllText(Path.Combine(model, "points3D.bin"), points);
    }
    if (stage == "image_undistorter")
    {
        var output = Value("--output_path");
        Directory.CreateDirectory(Path.Combine(output, "images", "nested"));
        File.WriteAllText(Path.Combine(output, "images", "nested", "frame.jpg"), "corrected pixels");
        var sparse = Path.Combine(output, "sparse"); Directory.CreateDirectory(sparse);
        foreach (var file in ActiveDataset.ModelFiles) File.Copy(Path.Combine(Value("--input_path"), file), Path.Combine(sparse, file));
        File.WriteAllText(Path.Combine(sparse, "cameras.bin"), "1 PINHOLE 40 30 35 35 20 15\n");
        if (File.Exists(Path.Combine(root, "incomplete"))) File.Delete(Path.Combine(sparse, "images.bin"));
    }
    if (stage == "model_converter")
    {
        var bin = Value("--output_type") == "BIN";
        foreach (var name in new[] { "cameras", "images", "points3D" })
            File.Copy(Path.Combine(Value("--input_path"), name + (bin ? ".txt" : ".bin")),
                Path.Combine(Value("--output_path"), name + (bin ? ".bin" : ".txt")), true);
    }
    Console.WriteLine("completed " + operation);
    return;
}

var fixtures = Path.Combine(Path.GetTempPath(), "Splatypus-ActiveDataset-Tests", Guid.NewGuid().ToString("N"));
var exe = Path.Combine(AppContext.BaseDirectory, "RhinoSplat.ActiveDataset.SmokeTest" + (OperatingSystem.IsWindows() ? ".exe" : ""));
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description); passed++;
}
ActiveDataset Make(string name)
{
    var d = new ActiveDataset(Path.Combine(fixtures, name));
    Directory.CreateDirectory(Path.Combine(d.Images, "nested"));
    Directory.CreateDirectory(d.Sparse); Directory.CreateDirectory(d.Text);
    File.WriteAllText(Path.Combine(d.Root, ".test-root"), "");
    File.WriteAllText(Path.Combine(d.Images, "nested", "frame.jpg"), "original pixels");
    foreach (var (n, value) in new[] { ("cameras", camera), ("images", pose), ("points3D", points) })
    { File.WriteAllText(Path.Combine(d.Sparse, n + ".bin"), value); File.WriteAllText(Path.Combine(d.Text, n + ".txt"), value); }
    File.WriteAllText(Path.Combine(d.Colmap, "database.db"), "features");
    return d;
}
string Snapshot(ActiveDataset d) => string.Join("|", Directory.GetFiles(d.Images, "*", SearchOption.AllDirectories).Order().Select(File.ReadAllText)
    .Concat(ActiveDataset.ModelFiles.Select(n => File.ReadAllText(Path.Combine(d.Sparse, n))))
    .Concat(ActiveDataset.TextFiles.Select(n => File.ReadAllText(Path.Combine(d.Text, n)))));
var transform = new ColmapSimilarityTransform(2, 1,0,0, 0,1,0, 0,0,1, 10,20,30);
var d = Make("undistort-then-align");
var ready = BrushDatasetPreparer.Prepare(d.Root);
Check(ready.Succeeded, "One dataset prepares Brush: " + ready.Status);
var brush = ready.DatasetPath;
Check(BrushDatasetPreparer.Prepare(d.Root).Succeeded, "Prepare is idempotent; no Replace switch is needed.");
var undistort = await ColmapImageUndistorterRunner.RunAsync(exe, d.Root, false, false, default);
Check(undistort.Succeeded, "Undistortion overwrites active pair: " + undistort.Status);
Check(File.ReadAllText(Path.Combine(d.Images, "nested", "frame.jpg")) == "corrected pixels"
    && File.ReadAllText(Path.Combine(d.Sparse, "cameras.bin")).Contains("PINHOLE"), "Pixels and corrected intrinsics are published together.");
Check(!File.Exists(Path.Combine(d.Colmap, "database.db")), "Undistortion retires incompatible feature database.");
Check(File.ReadAllText(Path.Combine(brush, "images", "nested", "frame.jpg")) == "corrected pixels"
    && File.ReadAllText(Path.Combine(brush, "sparse", "0", "cameras.bin")).Contains("PINHOLE"), "Previously prepared Brush handoff immediately sees both replacements.");
var baseSnapshot = Snapshot(d);
var aligned = await ColmapModelTransformRunner.RunAsync(exe, d.Root, transform, default);
Check(aligned.Succeeded, "Alignment accepts the undistorted active dataset: " + aligned.Status);
Check(File.ReadAllText(Path.Combine(aligned.TextModelPath, "points3D.txt")).StartsWith("1 12 24 36 ")
    && File.ReadAllText(Path.Combine(aligned.TextModelPath, "images.txt")).Contains("-10 -20 -30 1 nested/frame.jpg"), "Points and camera poses share the same transform.");
Check(File.ReadAllLines(Path.Combine(aligned.TextModelPath, "images.txt"))[1] == observations, "2D observation lines with many fields remain unchanged.");
Check(ActiveDataset.ReadImagePoses(Path.Combine(aligned.TextModelPath, "images.txt")).Single()[9] == "nested/frame.jpg",
    "Camera preview reads only pose lines, not multi-point observation lines.");
Check(Snapshot(d) == baseSnapshot && File.ReadAllText(Path.Combine(brush, "sparse", "0", "points3D.bin")) == points,
    "Alignment leaves the base images, binary/text model and default Brush link unchanged.");
Check(File.ReadAllText(Path.Combine(aligned.SparseModelPath, "cameras.bin")).Contains("PINHOLE"), "Aligned output preserves corrected intrinsics.");
Check(BrushDatasetPreparer.Prepare(d.Root, aligned.SparseModelPath).Succeeded
    && File.ReadAllText(Path.Combine(brush, "sparse", "0", "points3D.bin")).StartsWith("1 12 24 36 "), "Brush can explicitly follow the separate aligned output.");
var repeated = await ColmapModelTransformRunner.RunAsync(exe, d.Root, transform, default);
Check(repeated.Succeeded && Snapshot(d) == baseSnapshot
    && File.ReadAllText(Path.Combine(repeated.SparseModelPath, "points3D.bin")).StartsWith("1 12 24 36 "), "Repeated alignment always starts from the base, without compounding.");
var adjusted = await ColmapModelTransformRunner.RunAsync(exe, d.Root, transform with { Tx = 50 }, default);
Check(adjusted.Succeeded && Snapshot(d) == baseSnapshot
    && File.ReadAllText(Path.Combine(brush, "sparse", "0", "points3D.bin")).StartsWith("1 52 24 36 "), "Changed transforms replace the aligned branch and existing Brush link from the same base.");
var alignedSnapshot = File.ReadAllText(Path.Combine(adjusted.SparseModelPath, "points3D.bin"));
Check(!Directory.Exists(Path.Combine(d.Colmap, "dense")) && Directory.Exists(Path.Combine(d.Colmap, "aligned"))
    && !Directory.EnumerateDirectories(d.Colmap, ".dataset-stage-*").Any(), "Only the separate aligned output remains after staging cleanup.");
var original = Make("original-alignment");
aligned = await ColmapModelTransformRunner.RunAsync(exe, original.Root, transform, default);
Check(aligned.Succeeded && File.ReadAllText(Path.Combine(original.Sparse, "cameras.bin")) == camera, "Original images can be aligned without undistortion.");
var saved = Snapshot(d);
File.WriteAllText(Path.Combine(d.Root, "fail"), "model_converter:BIN");
aligned = await ColmapModelTransformRunner.RunAsync(exe, d.Root, transform, default);
Check(!aligned.Succeeded && Snapshot(d) == saved, "Failed alignment conversion leaves all active files unchanged.");
Check(File.ReadAllText(Path.Combine(adjusted.SparseModelPath, "points3D.bin")) == alignedSnapshot, "Failed alignment also preserves the previous aligned output.");
File.WriteAllText(Path.Combine(d.Root, "fail"), "model_converter:TXT");
undistort = await ColmapImageUndistorterRunner.RunAsync(exe, d.Root, false, true, default);
Check(!undistort.Succeeded && undistort.ExitCode == 7 && Snapshot(d) == saved, "Failed text conversion retains the active pair and reports the process exit code.");
File.Delete(Path.Combine(d.Root, "fail")); File.WriteAllText(Path.Combine(d.Root, "incomplete"), "");
undistort = await ColmapImageUndistorterRunner.RunAsync(exe, d.Root, false, true, default);
Check(!undistort.Succeeded && Snapshot(d) == saved, "Incomplete successful tool output is rejected before replacement.");
File.Delete(Path.Combine(d.Root, "incomplete"));
using (var cancel = new CancellationTokenSource())
{
    File.WriteAllText(Path.Combine(d.Root, "stall"), "image_undistorter");
    var pending = ColmapImageUndistorterRunner.RunAsync(exe, d.Root, false, true, cancel.Token);
    var watch = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(d.Root, "pid")) || new FileInfo(Path.Combine(d.Root, "pid")).Length == 0)
    { if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException(); await Task.Delay(20); }
    var concurrent = await ColmapModelTransformRunner.RunAsync(exe, d.Root, transform, default);
    Check(!concurrent.Succeeded && concurrent.Status.Contains("already running"), "Concurrent alignment cannot overwrite a dataset being undistorted.");
    cancel.Cancel(); undistort = await pending;
    Check(undistort.WasCancelled && Snapshot(d) == saved, "In-flight cancellation kills the tool and retains the current pair.");
    File.Delete(Path.Combine(d.Root, "stall"));
}
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel(); undistort = await ColmapImageUndistorterRunner.RunAsync(exe, d.Root, false, true, cancel.Token);
    Check(undistort.WasCancelled && Snapshot(d) == saved, "Pre-cancelled operations do not change the active model.");
}
using (var transaction = new DatasetTransaction(d))
{
    var staged = Path.Combine(transaction.Work, "text"); Directory.CreateDirectory(staged); File.WriteAllText(Path.Combine(staged, "new.txt"), "new");
    var second = Path.Combine(transaction.Work, "second"); Directory.CreateDirectory(second);
    var blocker = Path.Combine(d.Colmap, "blocker"); File.WriteAllText(blocker, "blocks destination parent");
    try { transaction.Commit(default, (staged, d.Text), (second, Path.Combine(blocker, "child"))); throw new Exception("Expected replacement failure"); }
    catch (IOException) { Check(Snapshot(d) == saved, "A filesystem failure rolls back earlier replacements."); }
}
Check(!BrushDatasetPreparer.Prepare(d.Root, original.Sparse, true).Succeeded, "Model overrides from another project are rejected.");
Check(BrushDatasetPreparer.Prepare(d.Root).Succeeded
    && File.ReadAllText(Path.Combine(brush, "sparse", "0", "points3D.bin")) == points, "Removing the override restores Brush's automatic base dataset.");
var broken = Make("broken"); File.Delete(Path.Combine(broken.Sparse, "points3D.bin"));
Check(!BrushDatasetPreparer.Prepare(broken.Root).Succeeded, "Brush rejects incomplete active models.");
var pipeline = Make("pipeline");
var result = await ColmapPipelineRunner.RunAsync(new(exe, pipeline.Root, ReplaceExisting: true, Undistort: true), default);
Check(result.Succeeded && result.Completed == 6 && result.DatasetPath == pipeline.Root, "Full pipeline publishes one active dataset: " + result.Status);
Check(File.ReadAllText(Path.Combine(pipeline.Images, "nested", "frame.jpg")) == "corrected pixels"
    && File.ReadAllText(Path.Combine(pipeline.Text, "cameras.txt")).Contains("PINHOLE"), "Pipeline final text export uses the corrected active calibration.");
var media = Make("new-media");
var source = Path.Combine(fixtures, "source.jpg"); File.WriteAllText(source, "source");
saved = Snapshot(media);
var imported = await FFmpegMediaImporter.RunAsync(exe, media.Root, new[] { source + ".missing" }, 1, true, false, default);
Check(!imported.Succeeded && Snapshot(media) == saved, "Invalid media requests retain the current reconstruction.");
var legacyImage = Path.Combine(media.Images, "rs_media_previous.jpg"); File.WriteAllText(legacyImage, "legacy pixels");
imported = await FFmpegMediaImporter.RunAsync(exe, media.Root, new[] { source }, 1, false, false, default);
Check(!imported.Succeeded && File.Exists(legacyImage), "Legacy RS imports still require Replace before another import.");
var notifications = 0;
void Changed(string root) { if (root == media.Root) notifications++; }
ActiveDataset.Changed += Changed;
imported = await FFmpegMediaImporter.RunAsync(exe, media.Root, new[] { source }, 1, true, false, default);
ActiveDataset.Changed -= Changed;
Check(imported.Succeeded && !Directory.Exists(media.Sparse) && !Directory.Exists(media.Text)
    && !File.Exists(Path.Combine(media.Colmap, "database.db")) && notifications == 1,
    "New media invalidates stale features/model and notifies the single dataset handoff.");
Check(imported.ImportedImages.All(p => Path.GetFileName(p).StartsWith("sp_media_")) && !File.Exists(legacyImage),
    "Replacing a legacy import produces only the new SP filenames.");
var special = Make("spaces & percent %DATA% !project");
Check(BrushDatasetPreparer.Prepare(special.Root).Succeeded, "Dataset links support spaces and shell metacharacters in project names.");
Console.WriteLine($"{passed} checks passed. Fixtures: {fixtures}");
