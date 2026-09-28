using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSplat.Core;

public sealed record ColmapRunResult(
    bool Succeeded,
    bool WasCancelled,
    int? ExitCode,
    string DatabasePath,
    string Log,
    string Status);

public static class ColmapFeatureExtractionRunner
{
    public static async Task<ColmapRunResult> RunAsync(
        string executable,
        string projectRoot,
        bool showTerminal,
        CancellationToken cancellationToken)
    {
        using var lease = DatasetLease.Acquire(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ToolValidation.Colmap(executable);
        var root = Path.GetFullPath(projectRoot);
        var images = Path.Combine(root, "images");
        var colmap = Path.Combine(root, "colmap");
        var database = Path.Combine(colmap, "database.db");

        if (!validation.IsValid)
            return new ColmapRunResult(false, false, null, database, string.Empty, validation.Message);
        if (!Directory.Exists(images))
            return new ColmapRunResult(false, false, null, database, string.Empty, $"Images folder was not found: {images}");
        if (!Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories).Any())
            return new ColmapRunResult(false, false, null, database, string.Empty, "The images folder is empty. Add photographs before running COLMAP.");

        Directory.CreateDirectory(colmap);
        var logPath = Path.Combine(colmap, "feature-extraction.log");
        ColmapLiveLog.Create(logPath);
        if (showTerminal)
            ColmapLiveLog.OpenTerminal(logPath, "Splatypus — COLMAP feature extraction");
        // Rhino does not necessarily inherit the same PATH/current directory as an
        // interactive terminal. COLMAP's native DLLs are installed next to its EXE,
        // so launch from that folder and make it first in the child process PATH.
        var executableDirectory = Path.GetDirectoryName(validation.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return new ColmapRunResult(false, false, null, database, string.Empty, "Could not determine COLMAP's installation folder.");

        var startInfo = new ProcessStartInfo
        {
            FileName = validation.ExecutablePath,
            WorkingDirectory = executableDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        startInfo.Environment["PATH"] = executableDirectory + Path.PathSeparator + inheritedPath;
        startInfo.ArgumentList.Add("feature_extractor");
        startInfo.ArgumentList.Add("--database_path");
        startInfo.ArgumentList.Add(database);
        startInfo.ArgumentList.Add("--image_path");
        startInfo.ArgumentList.Add(images);

        try
        {
            await ColmapPlatform.ConfigureCpuAsync(startInfo, true, cancellationToken).ConfigureAwait(false);
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));

            var stdoutTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardOutput, logPath);
            var stderrTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardError, logPath);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var log = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

            if (cancellationToken.IsCancellationRequested)
                return new ColmapRunResult(false, true, null, database, log, "COLMAP feature extraction was cancelled.");

            var succeeded = process.ExitCode == 0;
            return new ColmapRunResult(
                succeeded,
                false,
                process.ExitCode,
                database,
                log,
                succeeded ? "COLMAP feature extraction completed." : "COLMAP feature extraction failed; inspect the log.");
        }
        catch (Exception exception)
        {
            return new ColmapRunResult(false, cancellationToken.IsCancellationRequested, null, database, exception.ToString(), "COLMAP could not be started; inspect the log.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and Kill.
        }
    }
}
