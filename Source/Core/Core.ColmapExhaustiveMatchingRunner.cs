using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSplat.Core;

public static class ColmapExhaustiveMatchingRunner
{
    public static async Task<ColmapRunResult> RunAsync(
        string executable,
        string projectRoot,
        bool showTerminal,
        CancellationToken cancellationToken, ColmapMatchingMode matching = ColmapMatchingMode.Exhaustive)
    {
        using var lease = DatasetLease.Acquire(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = ToolValidation.Colmap(executable);
        var root = Path.GetFullPath(projectRoot);
        var database = Path.Combine(root, "colmap", "database.db");

        if (!validation.IsValid)
            return new ColmapRunResult(false, false, null, database, string.Empty, validation.Message);
        if (!File.Exists(database))
            return new ColmapRunResult(false, false, null, database, string.Empty, "COLMAP database was not found. Complete feature extraction first.");
        ColmapMatchingDecision decision;
        try { decision = ColmapMatching.Resolve(matching, root); }
        catch (ArgumentException error) { return new(false, false, null, database, string.Empty, error.Message); }
        var mode = decision.Mode.ToString().ToLowerInvariant();
        var selection = $"Matching: {decision.Mode}. {decision.Reason}";

        var executableDirectory = Path.GetDirectoryName(validation.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return new ColmapRunResult(false, false, null, database, string.Empty, "Could not determine COLMAP's installation folder.");

        var logPath = Path.Combine(root, "colmap", mode + "-matching.log");
        ColmapLiveLog.Create(logPath);
        ColmapLiveLog.AppendLine(logPath, selection);
        if (showTerminal)
            ColmapLiveLog.OpenTerminal(logPath, "Splatypus — COLMAP " + mode + " matching");

        var startInfo = new ProcessStartInfo
        {
            FileName = validation.ExecutablePath,
            WorkingDirectory = executableDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PATH"] = executableDirectory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        startInfo.ArgumentList.Add(mode + "_matcher");
        startInfo.ArgumentList.Add("--database_path");
        startInfo.ArgumentList.Add(database);

        try
        {
            await ColmapPlatform.ConfigureCpuAsync(startInfo, false, cancellationToken).ConfigureAwait(false);
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var stdoutTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardOutput, logPath);
            var stderrTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardError, logPath);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var log = string.Join(Environment.NewLine, new[] { selection, stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

            if (cancellationToken.IsCancellationRequested)
                return new ColmapRunResult(false, true, null, database, log, $"COLMAP {mode} matching was cancelled.");

            var succeeded = process.ExitCode == 0;
            return new ColmapRunResult(succeeded, false, process.ExitCode, database, log,
                succeeded ? $"COLMAP {mode} matching completed. {decision.Reason}" : $"COLMAP {mode} matching failed; inspect the log.");
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
