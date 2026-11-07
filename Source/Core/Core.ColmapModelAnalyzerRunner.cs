using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSplat.Core;

public static class ColmapModelAnalyzerRunner
{
    public static async Task<ColmapRunResult> RunAsync(string executable, string projectRoot, bool showTerminal, CancellationToken cancellationToken)
    {
        var validation = ToolValidation.Colmap(executable);
        var root = Path.GetFullPath(projectRoot);
        var model = Path.Combine(root, "colmap", "sparse", "0");
        if (!validation.IsValid)
            return new ColmapRunResult(false, false, null, model, string.Empty, validation.Message);
        if (!Directory.Exists(model))
            return new ColmapRunResult(false, false, null, model, string.Empty, "Sparse model 0 was not found. Complete sparse reconstruction first.");

        var executableDirectory = Path.GetDirectoryName(validation.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return new ColmapRunResult(false, false, null, model, string.Empty, "Could not determine COLMAP's installation folder.");
        var logPath = Path.Combine(root, "colmap", "model-analyzer.log");
        ColmapLiveLog.Create(logPath);
        if (showTerminal)
            ColmapLiveLog.OpenTerminal(logPath, "Splatypus — COLMAP model analyzer");

        var startInfo = new ProcessStartInfo { FileName = validation.ExecutablePath, WorkingDirectory = executableDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        startInfo.Environment["PATH"] = executableDirectory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        startInfo.ArgumentList.Add("model_analyzer");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(model);

        try
        {
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
                return new ColmapRunResult(false, true, null, model, log, "COLMAP model analysis was cancelled.");
            var succeeded = process.ExitCode == 0;
            return new ColmapRunResult(succeeded, false, process.ExitCode, model, log, succeeded ? "COLMAP sparse-model analysis completed." : "COLMAP model analysis failed; inspect the log.");
        }
        catch (Exception exception)
        {
            return new ColmapRunResult(false, cancellationToken.IsCancellationRequested, null, model, exception.ToString(), "COLMAP could not be started; inspect the log.");
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
