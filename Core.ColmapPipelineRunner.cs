using System.Collections.Concurrent;
using System.Text;

namespace RhinoSplat.Core;

public sealed record ColmapPipelineRequest(string Executable, string ProjectRoot,
    bool Extract = true, bool Match = true, bool Sparse = true, bool Undistort = false,
    bool Export = true, bool ReplaceExisting = false, bool ShowTerminal = false);

public sealed record ColmapPipelineProgress(int Completed, int Total, string Stage);

public sealed record ColmapPipelineResult(bool Succeeded, bool WasCancelled, int? ExitCode,
    string DatabasePath, string SparsePath, string DensePath, string TextPath,
    string Log, string Status, int Completed, int Total)
{
    public string DatasetPath { get; init; } = string.Empty;
}

/// <summary>Runs the same stages as the individual COLMAP components, stopping at the first failure.</summary>
public static class ColmapPipelineRunner
{
    private static readonly ConcurrentDictionary<string, byte> ActiveRoots = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static async Task<ColmapPipelineResult> RunAsync(ColmapPipelineRequest request,
        CancellationToken cancellationToken, Action<ColmapPipelineProgress>? progress = null)
    {
        var log = new StringBuilder();
        var database = string.Empty; var sparse = string.Empty; var dense = string.Empty; var text = string.Empty;
        var completed = 0;
        var total = 1 + new[] { request.Extract, request.Match, request.Sparse, request.Undistort, request.Export }.Count(x => x);
        string? ownedRoot = null;
        string? logPath = null;
        ColmapPipelineResult Result(bool success, bool cancelled, int? code, string status) =>
            new(success, cancelled, code, database, sparse, dense, text, log.ToString(), status, completed, total) { DatasetPath = dense };
        void Append(string value, bool writeLive = true)
        {
            log.AppendLine(value);
            if (writeLive && logPath is not null) ColmapLiveLog.AppendLine(logPath, value);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new(0, total, "Setup"));
            var validation = ToolValidation.Colmap(request.Executable);
            if (!validation.IsValid) return Result(false, false, null, validation.Message);
            if (string.IsNullOrWhiteSpace(request.ProjectRoot))
                return Result(false, false, null, "Connect Project Root from SP Project.");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ProjectRoot));
            var colmap = Path.Combine(root, "colmap");
            database = Path.Combine(colmap, "database.db");
            sparse = Path.Combine(colmap, "sparse");
            dense = root; // Legacy result property now aliases the active dataset; no dense copy is produced.
            text = Path.Combine(colmap, "sparse-text");
            if (!ActiveRoots.TryAdd(root, 0))
                return Result(false, false, null, "Another COLMAP pipeline is already running for this project.");
            ownedRoot = root;
            using var lease = DatasetLease.Acquire(root);

            // Check prerequisites before any selected stage changes the project.
            var images = Path.Combine(root, "images");
            if ((request.Extract || request.Sparse || request.Undistort)
                && (!Directory.Exists(images) || !Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories).Any()))
                return Result(false, false, null, "The images folder is missing or empty. Add photographs first.");
            if (!request.Extract && (request.Match || request.Sparse) && !File.Exists(database))
                return Result(false, false, null, "Enable Extract or supply an existing COLMAP database.");
            if (!request.Sparse && (request.Undistort || request.Export) && !Directory.Exists(Path.Combine(sparse, "0")))
                return Result(false, false, null, "Enable Sparse or supply an existing sparse model 0.");
            foreach (var output in new[] { (request.Sparse, sparse) })
                if (output.Item1 && !request.ReplaceExisting && Directory.Exists(output.Item2)
                    && Directory.EnumerateFileSystemEntries(output.Item2).Any())
                    return Result(false, false, null, $"Output already exists: {output.Item2}. Enable Replace or disable that stage.");

            Directory.CreateDirectory(colmap);
            logPath = Path.Combine(colmap, "pipeline.log");
            ColmapLiveLog.Create(logPath);
            using var liveLog = ColmapLiveLog.MirrorTo(logPath);
            if (request.ShowTerminal) ColmapLiveLog.OpenTerminal(logPath, "SP — COLMAP All");
            Append("Setup: COLMAP validated; working folder prepared.");
            completed++;
            var exe = validation.ExecutablePath;
            const bool terminal = false; // The pipeline owns one terminal; every stage streams into its log.
            var replace = request.ReplaceExisting;
            var stages = new (bool Enabled, string Name, Func<Task<ColmapRunResult>> Run)[]
            {
                (request.Extract, "Feature extraction", () => ColmapFeatureExtractionRunner.RunAsync(exe, root, terminal, cancellationToken)),
                (request.Match, "Exhaustive matching", () => ColmapExhaustiveMatchingRunner.RunAsync(exe, root, terminal, cancellationToken)),
                (request.Sparse, "Sparse reconstruction", () => ColmapSparseMapperRunner.RunAsync(exe, root, terminal, replace, cancellationToken)),
                (request.Undistort, "Image undistortion", () => ColmapImageUndistorterRunner.RunAsync(exe, root, terminal, replace, cancellationToken)),
                (request.Export, "Text export", () => ColmapModelExportRunner.RunAsync(exe, root, terminal, replace, cancellationToken))
            };
            foreach (var stage in stages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!stage.Enabled) { Append(stage.Name + ": skipped."); continue; }
                progress?.Invoke(new(completed, total, stage.Name));
                Append($"--- {stage.Name} ---");
                var result = await stage.Run().ConfigureAwait(false);
                Append(result.Log, writeLive: false); // Process lines were already streamed live.
                Append(result.Status);
                if (result.WasCancelled || cancellationToken.IsCancellationRequested)
                    return Result(false, true, null, $"Cancelled during {stage.Name}. Later stages were not run.");
                if (!result.Succeeded)
                    return Result(false, false, result.ExitCode, $"{stage.Name} failed: {result.Status} Later stages were not run.");
                completed++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(new(completed, total, "Done"));
            Append($"COLMAP pipeline completed: {completed}/{total} selected stages, including setup.");
            return Result(true, false, 0, $"COLMAP pipeline completed: {completed}/{total} selected stages, including setup.");
        }
        catch (OperationCanceledException)
        {
            Append("COLMAP pipeline was cancelled. Later stages were not run.");
            return Result(false, true, null, "COLMAP pipeline was cancelled. Later stages were not run.");
        }
        catch (Exception exception)
        {
            Append(exception.ToString());
            return Result(false, cancellationToken.IsCancellationRequested, null, "COLMAP pipeline stopped: " + exception.Message);
        }
        finally
        {
            if (ownedRoot is not null) ActiveRoots.TryRemove(ownedRoot, out _);
        }
    }
}
