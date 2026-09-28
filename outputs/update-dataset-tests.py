from pathlib import Path
root=Path(__file__).resolve().parent.parent
p=root/'Tests/ColmapPipelineSmoke.cs';s=p.read_text(encoding='utf-8-sig')
start=s.index('    var root = stage switch');end=s.index('    File.AppendAllText',start)
s=s[:start]+'''    var probe = args.Contains("--database_path") ? Value("--database_path") : Value("--output_path");
    var folder = new DirectoryInfo(Path.GetDirectoryName(probe)!);
    while (!File.Exists(Path.Combine(folder.FullName, ".test-root"))) folder = folder.Parent ?? throw new Exception("Missing fixture root");
    var root = folder.FullName;
''' + s[end:]
start=s.index('        File.WriteAllText(Path.Combine(model, "cameras.bin")');end=s.index('    return;',start)
s=s[:start]+'''        File.WriteAllText(Path.Combine(model, "cameras.bin"), "1 PINHOLE 40 30 35 35 20 15\\n");
        File.WriteAllText(Path.Combine(model, "images.bin"), "1 1 0 0 0 0 0 0 1 test.jpg\\n\\n");
        File.WriteAllText(Path.Combine(model, "points3D.bin"), "1 1 2 3 255 128 64 0.1 1 0\\n");
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
''' + s[end:]
s=s.replace('    return new(executable, root);','    File.WriteAllText(Path.Combine(root, ".test-root"), "");\n    return new(executable, root, Undistort: true);')
s=s.replace('"mapper", "image_undistorter", "model_converter"','"mapper", "model_converter", "image_undistorter", "model_converter", "model_converter"')
s=s.replace('File.Exists(result.DatabasePath) && File.Exists', '!File.Exists(result.DatabasePath) && File.Exists')
s=s.replace('.Length == 5, "Existing outputs', '.Length == 7, "Existing outputs')
s=s.replace('Path.Combine(full.ProjectRoot, "colmap", "dense", "keep.txt")','Path.Combine(full.ProjectRoot, "images", "keep.txt")')
s=s.replace('Path.Combine(result.DensePath, "keep.txt")','Path.Combine(result.DatasetPath, "images", "keep.txt")')
p.write_text(s,encoding='utf-8')
p=root/'Tests/BrushDatasetSmoke.cs'
p.write_text('''using RhinoSplat.Core;

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
Check(!BrushDatasetPreparer.Prepare(root, active.Sparse, true).Succeeded, "Retired model override is refused.");
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
''',encoding='utf-8')
