using System.Globalization;

namespace RhinoSplat.Core;

public enum ColmapMeshMethod { Poisson = 0, DenseDelaunay = 1, SparseDelaunay = 2 }
public sealed record ColmapMeshRequest(string Executable, string DatasetPath,
    ColmapMeshMethod Method = ColmapMeshMethod.Poisson, int TargetFaces = 100000,
    int MaxImageSize = 1600, int PoissonDepth = 10, double PoissonTrim = 10,
    int ReductionAccuracy = 5, bool RefineOnly = false, bool ShowTerminal = false,
    string SparseModelOverride = "");
public sealed record ColmapMeshProgress(string Stage, int Completed, int Total);
public sealed record ColmapMeshResult(bool Succeeded, bool WasCancelled, int? ExitCode,
    string MeshPath, string RawMeshPath, int OriginalFaces, int FinalFaces,
    string Log, string Status, PlyMeshData? Mesh = null);

/// <summary>Native COLMAP reconstruction, followed by a host-provided mesh reducer.</summary>
public static class ColmapMeshRunner
{
    public static async Task<ColmapMeshResult> RunAsync(ColmapMeshRequest request,
        Func<PlyMeshData, int, int, CancellationToken, PlyMeshData> reduce,
        CancellationToken token, Action<ColmapMeshProgress>? progress = null)
    {
        var log = "";
        try
        {
            token.ThrowIfCancellationRequested();
            Validate(request);
            var dataset = new ActiveDataset(request.DatasetPath);
            using var lease = DatasetLease.Acquire(dataset.Root);
            var destination = Path.Combine(dataset.Colmap, "mesh");
            dataset.CheckOwnedPath(destination);
            var rawDestination = Path.Combine(destination, "raw.ply");
            var meshDestination = Path.Combine(destination, "mesh.ply");
            using var transaction = new DatasetTransaction(dataset);
            var workspace = Path.Combine(transaction.Work, "mesh");
            Directory.CreateDirectory(workspace);
            var raw = Path.Combine(workspace, "raw.ply");
            var output = Path.Combine(workspace, "mesh.ply");
            var logPath = Path.Combine(dataset.Colmap, "mesh-reconstruction.log");
            ColmapLiveLog.Create(logPath);
            if (request.ShowTerminal) ColmapLiveLog.OpenTerminal(logPath, "Splatypus — Mesh reconstruction");
            var total = request.RefineOnly ? 1 : request.Method == ColmapMeshMethod.SparseDelaunay ? 2 : 5;
            var completed = 0;
            async Task Run(string stage, params string[] args)
            {
                progress?.Invoke(new(stage, completed, total));
                ColmapLiveLog.AppendLine(logPath, stage);
                log += await DatasetColmapProcess.RunInDirectory(request.Executable, workspace, logPath, token, args).ConfigureAwait(false);
                completed++;
            }
            if (request.RefineOnly)
            {
                if (!File.Exists(rawDestination)) throw new IOException("Run a reconstruction first; Refine Only needs this project's saved raw mesh.");
                raw = rawDestination;
            }
            else
            {
                var validation = ToolValidation.Colmap(request.Executable);
                if (!validation.IsValid) throw new IOException(validation.Message);
                request = request with { Executable = validation.ExecutablePath };
                var sparse = string.IsNullOrWhiteSpace(request.SparseModelOverride) ? dataset.Sparse : Path.GetFullPath(request.SparseModelOverride);
                ActiveDataset.ValidatePair(dataset.Images, sparse);
                if (request.Method == ColmapMeshMethod.SparseDelaunay)
                    await Run("Meshing sparse model", "delaunay_mesher", "--input_path", sparse, "--input_type", "sparse", "--output_path", raw);
                else
                {
                    if (OperatingSystem.IsMacOS() || File.Exists(Path.Combine(Path.GetDirectoryName(request.Executable)!, ToolDownloadInstaller.CpuMarker)))
                        throw new IOException("Dense reconstruction requires a CUDA-enabled COLMAP installation and NVIDIA GPU. Use Method 2 (Sparse Delaunay) on this setup; its mesh has less detail and requires COLMAP with CGAL.");
                    var size = request.MaxImageSize.ToString(CultureInfo.InvariantCulture);
                    await Run("Undistorting mesh workspace", "image_undistorter", "--image_path", dataset.Images,
                        "--input_path", sparse, "--output_path", workspace, "--output_type", "COLMAP", "--max_image_size", size);
                    await Run("Computing depth maps (CUDA)", "patch_match_stereo", "--workspace_path", workspace,
                        "--workspace_format", "COLMAP", "--PatchMatchStereo.geom_consistency", "true", "--PatchMatchStereo.max_image_size", size);
                    var fused = Path.Combine(workspace, "fused.ply");
                    await Run("Fusing dense points", "stereo_fusion", "--workspace_path", workspace,
                        "--workspace_format", "COLMAP", "--input_type", "geometric", "--output_path", fused);
                    if (request.Method == ColmapMeshMethod.Poisson)
                        await Run("Meshing with Poisson", "poisson_mesher", "--input_path", fused, "--output_path", raw,
                            "--PoissonMeshing.depth", request.PoissonDepth.ToString(CultureInfo.InvariantCulture),
                            "--PoissonMeshing.trim", request.PoissonTrim.ToString(CultureInfo.InvariantCulture));
                    else
                        await Run("Meshing dense model", "delaunay_mesher", "--input_path", workspace, "--input_type", "dense", "--output_path", raw);
                }
            }
            progress?.Invoke(new("Reducing mesh for Rhino", completed, total));
            var mesh = PlyMeshData.Read(raw, token);
            var original = mesh.Faces.Length;
            if (request.TargetFaces > 0 && original > request.TargetFaces)
                mesh = reduce(mesh, request.TargetFaces, request.ReductionAccuracy, token);
            token.ThrowIfCancellationRequested();
            mesh.Validate();
            if (mesh.Faces.Length > original) throw new IOException("Mesh reduction unexpectedly increased the face count.");
            mesh.Write(output, token);
            var status = $"Mesh ready: {original:N0} → {mesh.Faces.Length:N0} faces.";
            if (request.TargetFaces > 0 && mesh.Faces.Length > request.TargetFaces)
                status += $" The requested target of {request.TargetFaces:N0} could not be reached; try a higher reduction accuracy or target.";
            if (request.RefineOnly) status += " Refined the saved raw mesh; reconstruction settings were not rerun.";
            log += status + Environment.NewLine;
            ColmapLiveLog.AppendLine(logPath, status);
            if (request.RefineOnly) transaction.Commit(token, (output, meshDestination));
            else transaction.Commit(token, (workspace, destination));
            progress?.Invoke(new("Done", total, total));
            return new(true, false, 0, meshDestination, rawDestination, original, mesh.Faces.Length, log, status, mesh);
        }
        catch (Exception error)
        {
            var cancelled = error is OperationCanceledException || token.IsCancellationRequested;
            return new(false, cancelled, (error as DatasetColmapException)?.ExitCode, "", "", 0, 0,
                log + error, (cancelled ? "Mesh reconstruction cancelled. " : "Mesh reconstruction failed: " + error.Message + " ") + "Previous mesh files were retained.");
        }
    }

    public static void Validate(ColmapMeshRequest request)
    {
        if (request.TargetFaces < 0 || request.TargetFaces is > 0 and < 4) throw new ArgumentException("Target Faces must be 0 (no reduction) or at least 4.");
        if (request.ReductionAccuracy < 1 || request.ReductionAccuracy > 10) throw new ArgumentException("Reduction Accuracy must be between 1 and 10.");
        if (request.RefineOnly) return;
        if (!Enum.IsDefined(request.Method)) throw new ArgumentException("Method must be 0 (Poisson), 1 (Dense Delaunay), or 2 (Sparse Delaunay).");
        if (request.MaxImageSize < 320 || request.MaxImageSize > 8192) throw new ArgumentException("Max Image Size must be between 320 and 8192 pixels.");
        if (request.PoissonDepth < 6 || request.PoissonDepth > 14) throw new ArgumentException("Poisson Depth must be between 6 and 14; high values require much more memory.");
        if (!double.IsFinite(request.PoissonTrim) || request.PoissonTrim < 0) throw new ArgumentException("Poisson Trim must be finite and nonnegative.");
    }
}
