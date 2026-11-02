using System.Diagnostics;
using System.Text;
using RhinoSplat.Core;

if (args.FirstOrDefault() == "--real-poisson") { await ColmapMeshReal.Run(args[1], args[2]); return; }

static PlyMeshData Octahedron() => new(new MeshVertex[]
    { new(1,0,0,255,0,0), new(-1,0,0,0,255,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) },
    new MeshTriangle[] { new(0,2,4), new(2,1,4), new(1,3,4), new(3,0,4), new(2,0,5), new(1,2,5), new(3,1,5), new(0,3,5) }, true);

// Child process impersonates COLMAP to exercise command boundaries and failure/cancellation.
if (args.Length > 0)
{
    string Value(string flag) => args[Array.IndexOf(args, flag) + 1];
    var output = args.Contains("--output_path") ? Value("--output_path") : Value("--workspace_path");
    var dir = new DirectoryInfo(Path.GetDirectoryName(output)!);
    while (!File.Exists(Path.Combine(dir.FullName, ".mesh-test"))) dir = dir.Parent ?? throw new Exception("No fixture root");
    var root = dir.FullName;
    File.AppendAllText(Path.Combine(root, "commands"), string.Join('|', args) + "\n");
    if (File.Exists(Path.Combine(root, "stall"))) { File.WriteAllText(Path.Combine(root, "pid"), Environment.ProcessId.ToString()); await Task.Delay(40000); }
    if (File.Exists(Path.Combine(root, "fail")) && File.ReadAllText(Path.Combine(root, "fail")) == args[0]) { Console.Error.WriteLine("simulated stage failure"); Environment.Exit(7); }
    if (args[0] is "poisson_mesher" or "delaunay_mesher")
    {
        if (File.Exists(Path.Combine(root, "empty"))) File.WriteAllText(output, "not a mesh");
        else Octahedron().Write(output);
    }
    return;
}

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
var fixture = Path.Combine(Path.GetTempPath(), "Splatypus-MeshTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var dataset = new ActiveDataset(Path.Combine(fixture, "project with spaces"));
Directory.CreateDirectory(dataset.Images); Directory.CreateDirectory(dataset.Sparse);
File.WriteAllText(Path.Combine(dataset.Root, ".mesh-test"), "");
File.WriteAllText(Path.Combine(dataset.Images, "photo.jpg"), "original image");
foreach (var file in ActiveDataset.ModelFiles) File.WriteAllText(Path.Combine(dataset.Sparse, file), "original " + file);
var exe = Environment.ProcessPath!;
var request = new ColmapMeshRequest(exe, dataset.Root, TargetFaces: 4);
PlyMeshData FakeReduce(PlyMeshData mesh, int target, int accuracy, CancellationToken token) => mesh with { Faces = mesh.Faces.Take(target).ToArray() };
string Commands() => File.ReadAllText(Path.Combine(dataset.Root, "commands"));
string Snapshot() => string.Join("|", Directory.EnumerateFiles(dataset.Images).Concat(Directory.EnumerateFiles(dataset.Sparse)).Select(File.ReadAllText));
var snapshot = Snapshot();
var stages = new List<ColmapMeshProgress>();
var result = await ColmapMeshRunner.RunAsync(request, FakeReduce, default, stages.Add);
Check(result.Succeeded && result.OriginalFaces == 8 && result.FinalFaces == 4, "Dense Poisson publishes a reduced mesh and face counts: " + result.Status);
Check(stages.Last().Completed == 5 && stages.Last().Total == 5, "All five dense stages report progress.");
var commands = Commands().Split('\n', StringSplitOptions.RemoveEmptyEntries);
Check(commands.Select(x => x.Split('|')[0]).SequenceEqual(new[] { "image_undistorter", "patch_match_stereo", "stereo_fusion", "poisson_mesher" }), "Native dense commands execute in documented order.");
Check(commands[0].Contains("--max_image_size|1600") && commands[1].Contains("--PatchMatchStereo.geom_consistency|true") && commands[3].Contains("--PoissonMeshing.depth|10"), "Dense controls reach the native command arguments.");
Check(Snapshot() == snapshot && PlyMeshData.Read(result.RawMeshPath).Faces.Length == 8 && PlyMeshData.Read(result.MeshPath).Faces.Length == 4, "Active dataset and full-detail raw mesh remain intact.");
var oldMesh = File.ReadAllBytes(result.MeshPath);
result = await ColmapMeshRunner.RunAsync(request with { Executable = "", RefineOnly = true, TargetFaces = 0 }, FakeReduce, default);
Check(result.Succeeded && result.FinalFaces == 8 && Commands().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 4, "Refine Only uses raw mesh and works without a COLMAP executable.");
result = await ColmapMeshRunner.RunAsync(request with { RefineOnly = true }, (mesh, _, _, _) => mesh, default);
Check(result.Succeeded && result.Status.Contains("could not be reached") && result.FinalFaces == 8, "An unattainable face target is reported honestly.");
var kept = File.ReadAllBytes(result.MeshPath);
foreach (var stage in new[] { "image_undistorter", "patch_match_stereo", "stereo_fusion", "poisson_mesher" })
{
    File.WriteAllText(Path.Combine(dataset.Root, "fail"), stage);
    var failed = await ColmapMeshRunner.RunAsync(request, FakeReduce, default);
    Check(!failed.Succeeded && failed.ExitCode == 7 && failed.Log.Contains("simulated stage failure") && File.ReadAllBytes(result.MeshPath).SequenceEqual(kept), "Failure retains prior mesh and native log: " + stage);
}
File.Delete(Path.Combine(dataset.Root, "fail"));
File.WriteAllText(Path.Combine(dataset.Root, "empty"), "");
var invalid = await ColmapMeshRunner.RunAsync(request, FakeReduce, default);
Check(!invalid.Succeeded && File.ReadAllBytes(result.MeshPath).SequenceEqual(kept), "Malformed mesher output is rejected before publication.");
File.Delete(Path.Combine(dataset.Root, "empty"));
var brokenReduction = await ColmapMeshRunner.RunAsync(request, (_, _, _, _) => throw new IOException("reducer failure"), default);
Check(!brokenReduction.Succeeded && File.ReadAllBytes(result.MeshPath).SequenceEqual(kept), "Reducer failure retains previous mesh.");
using (var cancellation = new CancellationTokenSource())
{
    File.WriteAllText(Path.Combine(dataset.Root, "stall"), "");
    var task = ColmapMeshRunner.RunAsync(request, FakeReduce, cancellation.Token);
    var watch = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(dataset.Root, "pid"))) { if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException(); await Task.Delay(20); }
    var concurrent = await ColmapMeshRunner.RunAsync(request, FakeReduce, default);
    Check(!concurrent.Succeeded && concurrent.Status.Contains("already running"), "Concurrent jobs cannot modify the same project.");
    cancellation.Cancel();
    var cancelled = await task;
    Check(cancelled.WasCancelled && File.ReadAllBytes(result.MeshPath).SequenceEqual(kept), "Cancellation stops child process and retains mesh.");
    File.Delete(Path.Combine(dataset.Root, "stall"));
}
File.Delete(Path.Combine(dataset.Root, "commands"));
var aligned = Path.Combine(dataset.Colmap, "aligned", "sparse", "0"); Directory.CreateDirectory(aligned);
foreach (var file in ActiveDataset.ModelFiles) File.WriteAllText(Path.Combine(aligned, file), "aligned model");
result = await ColmapMeshRunner.RunAsync(request with { Method = ColmapMeshMethod.SparseDelaunay, SparseModelOverride = aligned }, FakeReduce, default);
Check(result.Succeeded && Commands().StartsWith("delaunay_mesher|") && Commands().Contains("--input_type|sparse") && Commands().Contains(aligned) && !Commands().Contains("patch_match"), "Sparse Delaunay uses the aligned override without dense or CUDA stages.");
result = await ColmapMeshRunner.RunAsync(request with { Method = ColmapMeshMethod.DenseDelaunay }, FakeReduce, default);
Check(result.Succeeded && Commands().Contains("--input_type|dense"), "Dense Delaunay uses a dense workspace.");
Check(Snapshot() == snapshot && !Directory.EnumerateDirectories(dataset.Colmap, ".dataset-stage-*").Any(), "Every outcome preserves the dataset and cleans staging.");
foreach (var bad in new[] { request with { TargetFaces = -1 }, request with { TargetFaces = 3 }, request with { Method = (ColmapMeshMethod)3 }, request with { PoissonTrim = double.NaN }, request with { ReductionAccuracy = 11 }, request with { MaxImageSize = 0 }, request with { PoissonDepth = 20 } })
    Check(!(await ColmapMeshRunner.RunAsync(bad, FakeReduce, default)).Succeeded, "Invalid reconstruction setting is rejected.");

var ply = Path.Combine(fixture, "mesh.ply");
var original = Octahedron(); original.Write(ply);
var roundTrip = PlyMeshData.Read(ply);
Check(original.Vertices.SequenceEqual(roundTrip.Vertices) && original.Faces.SequenceEqual(roundTrip.Faces) && roundTrip.HasColors, "Binary PLY round trip preserves coordinates, colors and triangles.");
var ascii = "ply\nformat ascii 1.0\ncomment fixture\nelement vertex 3\nproperty float z\nproperty float x\nproperty float y\nproperty float nx\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0 1\n0 1 0 1\n0 0 1 1\n3 0 1 2\n";
File.WriteAllText(ply, ascii);
var parsed = PlyMeshData.Read(ply);
Check(parsed.Vertices[1].X == 1 && parsed.Vertices[2].Y == 1 && !parsed.HasColors, "ASCII PLY handles reordered properties and ignores normals.");
using (var writer = new BinaryWriter(File.Create(ply)))
{
    writer.Write(Encoding.ASCII.GetBytes("ply\nformat binary_big_endian 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n"));
    foreach (float v in new float[] { 0,0,0, 1,0,0, 0,1,0 }) writer.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.SingleToInt32Bits(v)));
    writer.Write((byte)3); foreach (var index in new[] { 0, 1, 2 }) writer.Write(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(index));
}
Check(PlyMeshData.Read(ply).Vertices[1].X == 1, "Big-endian PLY is supported.");
foreach (var corrupt in new[] { ascii.Replace("3 0 1 2", "3 0 1 99"), ascii.Replace("3 0 1 2", "3 0 0 2"), ascii.Replace("0 1 0 1", "0 NaN 0 1"), ascii[..^8], ascii.Replace("element vertex 3", "element vertex 20000000") })
{
    File.WriteAllText(ply, corrupt); var rejected = false;
    try { PlyMeshData.Read(ply); } catch (IOException) { rejected = true; }
    Check(rejected, "Malformed mesh geometry is rejected.");
}
var sessionRequest = request with { RefineOnly = true };
using (var session = new ColmapMeshSession(FakeReduce))
{
    var state = session.Update(sessionRequest, true, false);
    var watch = Stopwatch.StartNew();
    while (state.IsRunning) { if (watch.ElapsedMilliseconds > 15000) throw new TimeoutException(); await Task.Delay(20); state = session.Update(sessionRequest, true, false); }
    Check(state.Result?.Succeeded == true && !session.Update(sessionRequest, true, false).IsRunning, "Background completion is collected and a held Run does not restart.");
    session.Update(sessionRequest, false, false);
    Check(session.Update(sessionRequest, true, false).IsRunning, "A new rising edge starts another refinement.");
}
Console.WriteLine($"All {passed} mesh checks passed. Fixture: {fixture}");
