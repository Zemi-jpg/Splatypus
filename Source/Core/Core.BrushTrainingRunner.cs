using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSplat.Core;

public sealed record BrushRunResult(bool Succeeded, bool WasCancelled, int? ExitCode, string ExportPath, string VisualizationPlyPath, string Log, string Status);

public static class BrushTrainingRunner
{
    public static async Task<BrushRunResult> RunAsync(string executable, string dataset, string projectRoot, string jobName, int totalSteps, bool withViewer, bool showTerminal, bool replaceExisting, bool rotateForVisualization, CancellationToken cancellationToken,
        int maxResolution = 1920, int maxSplats = 10000000)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DatasetLease.Acquire(projectRoot);
        var fullDataset = Path.GetFullPath(dataset);
        var safeName = string.Concat((jobName ?? string.Empty).Where(character => !Path.GetInvalidFileNameChars().Contains(character))).Trim();
        safeName = string.IsNullOrWhiteSpace(safeName) ? "brush" : safeName;
        if (safeName is "." or "..")
            return new BrushRunResult(false, false, null, string.Empty, string.Empty, string.Empty, "Job Name must name a folder inside output.");
        var exportPath = Path.Combine(Path.GetFullPath(projectRoot), "output", safeName);
        if (!File.Exists(executable))
            return new BrushRunResult(false, false, null, exportPath, string.Empty, string.Empty, $"Brush executable was not found: {executable}");
        if (!Directory.Exists(Path.Combine(fullDataset, "images")) || !File.Exists(Path.Combine(fullDataset, "sparse", "0", "cameras.bin")))
            return new BrushRunResult(false, false, null, exportPath, string.Empty, string.Empty, "Brush dataset is incomplete. Complete SP Brush Dataset first.");
        if (totalSteps <= 0)
            return new BrushRunResult(false, false, null, exportPath, string.Empty, string.Empty, "Total Steps must be greater than zero.");
        if (maxResolution <= 0 || maxSplats <= 0)
            return new(false, false, null, exportPath, string.Empty, string.Empty, "Maximum Training Resolution and Maximum Splat Count must be greater than zero.");

        if (Directory.Exists(exportPath) && Directory.EnumerateFileSystemEntries(exportPath).Any())
        {
            if (!replaceExisting)
                return new BrushRunResult(false, false, null, exportPath, string.Empty, string.Empty, $"Brush export already exists: {exportPath}. Set Replace to True to replace it.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(exportPath, recursive: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(exportPath);
        var logPath = Path.Combine(exportPath, "brush-training.log");
        ColmapLiveLog.Create(logPath);
        if (showTerminal)
            ColmapLiveLog.OpenTerminal(logPath, "Splatypus — Brush training");

        var info = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executable),
            WorkingDirectory = exportPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(fullDataset);
        info.ArgumentList.Add("--total-steps"); info.ArgumentList.Add(totalSteps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--max-resolution"); info.ArgumentList.Add(maxResolution.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--max-splats"); info.ArgumentList.Add(maxSplats.ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("--export-path"); info.ArgumentList.Add(exportPath);
        info.ArgumentList.Add("--export-name"); info.ArgumentList.Add("sp_{iter}.ply");
        if (withViewer) info.ArgumentList.Add("--with-viewer");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = info };
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var stdoutTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardOutput, logPath);
            var stderrTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardError, logPath);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var log = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
            if (cancellationToken.IsCancellationRequested)
                return new BrushRunResult(false, true, null, exportPath, string.Empty, log, "Brush training was cancelled.");
            var succeeded = process.ExitCode == 0;
            if (!succeeded)
                return new BrushRunResult(false, false, process.ExitCode, exportPath, string.Empty, log, "Brush training failed; inspect the log.");
            if (!rotateForVisualization)
                return new BrushRunResult(true, false, process.ExitCode, exportPath, string.Empty, log, "Brush training completed.");

            var brushPly = FindLatestBrushPly(exportPath);
            if (brushPly is null)
                return new BrushRunResult(false, false, process.ExitCode, exportPath, string.Empty, log, "Brush training completed, but no PLY export was found to rotate for visualization.");
            var rotation = BrushPlyRhinoRotation.CreateVisualizationCopy(brushPly, cancellationToken);
            return new BrushRunResult(rotation.Succeeded, false, process.ExitCode, exportPath, rotation.OutputPath, log,
                rotation.Succeeded ? $"Brush training completed. {rotation.Status}" : $"Brush training completed, but {rotation.Status}");
        }
        catch (Exception exception)
        {
            return new BrushRunResult(false, cancellationToken.IsCancellationRequested, null, exportPath, string.Empty, exception.ToString(), "Brush could not be started; inspect the log.");
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string? FindLatestBrushPly(string exportPath)
    {
        if (!Directory.Exists(exportPath)) return null;
        return Directory.EnumerateFiles(exportPath, "sp_*.ply", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(exportPath, "rhinosplat_*.ply", SearchOption.TopDirectoryOnly))
            .Where(path => !path.EndsWith("_rhino.ply", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("_rgs.ply", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
