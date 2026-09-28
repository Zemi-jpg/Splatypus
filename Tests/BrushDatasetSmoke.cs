using RhinoSplat.Core;

var root = Path.Combine(Path.GetTempPath(), "Splatypus-BrushDataset-Tests", Guid.NewGuid().ToString("N"));
var active = new ActiveDataset(root);
Directory.CreateDirectory(Path.Combine(active.Images, "nested"));
File.WriteAllText(Path.Combine(active.Images, "nested", "frame.jpg"), "pixels");
Directory.CreateDirectory(active.Sparse);
foreach (var name in ActiveDataset.ModelFiles) File.WriteAllText(Path.Combine(active.Sparse, name), "original " + name);
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    passed++; Console.WriteLine("PASS " + description);
}
var result = BrushDatasetPreparer.Prepare(root);
Check(result.Succeeded, "Prepares a single linked dataset. " + result.Status);
var dataset = result.DatasetPath;
Check(new DirectoryInfo(Path.Combine(dataset, "images")).ResolveLinkTarget(true) is not null
    && new DirectoryInfo(Path.Combine(dataset, "sparse")).ResolveLinkTarget(true) is not null, "Images and model are aliases, not separate copies.");
Check(BrushDatasetPreparer.Prepare(root).Succeeded, "Preparation can repeat without a Replace toggle.");
Check(BrushDatasetPreparer.Prepare(root, null, false).Succeeded, "Legacy calls without overrides use the same dataset.");
Check(BrushDatasetPreparer.Prepare(root, active.Sparse, true).Succeeded, "Explicit project model overrides remain supported.");
Check(!BrushDatasetPreparer.Prepare(root, null, true, Path.Combine(root, "colmap", "dense")).Succeeded, "Retired undistorted override is refused.");
// A legacy prepared directory with copies is migrated without following its image link.
Directory.Move(dataset, Path.Combine(root, "old-brush"));
Directory.CreateDirectory(Path.Combine(dataset, "images"));
Directory.CreateDirectory(Path.Combine(dataset, "sparse", "0"));
File.WriteAllText(Path.Combine(dataset, "sparse", "0", "cameras.bin"), "stale");
result = BrushDatasetPreparer.Prepare(root);
Check(result.Succeeded && File.ReadAllText(Path.Combine(dataset, "sparse", "0", "cameras.bin")) == "original cameras.bin", "Legacy copied model is replaced by the canonical model link.");
Check(File.ReadAllText(Path.Combine(active.Images, "nested", "frame.jpg")) == "pixels", "Migration preserves source images.");
File.Delete(Path.Combine(active.Sparse, "cameras.bin"));
Check(!BrushDatasetPreparer.Prepare(root).Succeeded, "Missing model file fails readiness.");
Check(Directory.Exists(Path.Combine(dataset, "images")) && File.Exists(Path.Combine(active.Images, "nested", "frame.jpg")), "A validation failure does not remove the current handoff or source data.");
Check(!BrushDatasetPreparer.Prepare(" ").Succeeded, "Blank dataset paths do not use the current directory.");
Console.WriteLine($"{passed} checks passed. Fixtures: {root}");
