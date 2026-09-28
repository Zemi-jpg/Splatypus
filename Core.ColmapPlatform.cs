using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace RhinoSplat.Core;
internal static class ColmapPlatform
{
    // COLMAP renamed these options; inspect this installation instead of assuming a version.
    internal static async Task ConfigureCpuAsync(ProcessStartInfo target, bool extraction, CancellationToken token)
    {
        var managedCpu = System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target.FileName)!, ToolDownloadInstaller.CpuMarker));
        if (!OperatingSystem.IsMacOS() && !managedCpu) return;
        var help = new ProcessStartInfo(target.FileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = target.WorkingDirectory };
        help.Environment["PATH"] = target.Environment["PATH"];
        help.ArgumentList.Add(extraction ? "feature_extractor" : "exhaustive_matcher");
        help.ArgumentList.Add("-h");
        using var process = Process.Start(help) ?? throw new InvalidOperationException("Could not inspect COLMAP options.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        var text = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        var modern = extraction ? "FeatureExtraction.use_gpu" : "FeatureMatching.use_gpu";
        var legacy = extraction ? "SiftExtraction.use_gpu" : "SiftMatching.use_gpu";
        var option = text.Contains(modern, StringComparison.Ordinal) ? modern : text.Contains(legacy, StringComparison.Ordinal) ? legacy : null;
        if (option is null) throw new InvalidOperationException("Could not identify COLMAP's CPU option from command help. Check the installed COLMAP version.");
        target.ArgumentList.Add("--" + option);
        target.ArgumentList.Add("0");
    }
}
