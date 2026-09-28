using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RhinoSplat.Core;

public sealed record FFmpegMediaImportResult(
    bool Succeeded,
    bool WasCancelled,
    string ImagesPath,
    IReadOnlyList<string> ImportedImages,
    string Log,
    string Status);

public static class FFmpegMediaImporter
{
    private const string ManagedPrefix = "sp_media_";
    private static IEnumerable<string> ManagedImages(string imagesPath) =>
        Directory.EnumerateFiles(imagesPath, ManagedPrefix + "*.jpg", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(imagesPath, "rs_media_*.jpg", SearchOption.TopDirectoryOnly));
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".gif"
    };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".mpeg", ".mpg", ".mts", ".m2ts", ".ts", ".3gp", ".3g2", ".mxf", ".ogv"
    };

    public static async Task<FFmpegMediaImportResult> RunAsync(
        string executable,
        string projectRoot,
        IReadOnlyList<string> sources,
        double framesPerSecond,
        bool replaceExisting,
        bool showTerminal,
        CancellationToken cancellationToken)
    {
        var imagesPath = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(projectRoot))
                imagesPath = Path.Combine(Path.GetFullPath(projectRoot), "images");
            using var lease = DatasetLease.Acquire(projectRoot);
            return await RunCoreAsync(executable, projectRoot, sources, framesPerSecond, replaceExisting, showTerminal, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new FFmpegMediaImportResult(false, true, imagesPath, Array.Empty<string>(), string.Empty, "FFmpeg media import was cancelled.");
        }
        catch (Exception exception)
        {
            return new FFmpegMediaImportResult(false, false, imagesPath, Array.Empty<string>(), exception.ToString(), "Could not import media with FFmpeg; inspect the log.");
        }
    }

    private static async Task<FFmpegMediaImportResult> RunCoreAsync(
        string executable,
        string projectRoot,
        IReadOnlyList<string> sources,
        double framesPerSecond,
        bool replaceExisting,
        bool showTerminal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(projectRoot))
            return Failure(string.Empty, "Project Root is required.");
        var validation = FFmpegToolValidation.Validate(executable);
        var root = Path.GetFullPath(projectRoot);
        var imagesPath = Path.Combine(root, "images");
        if (!validation.IsValid)
            return Failure(imagesPath, validation.Message);
        if (double.IsNaN(framesPerSecond) || double.IsInfinity(framesPerSecond) || framesPerSecond <= 0.0)
            return Failure(imagesPath, "Frames Per Second must be greater than zero.");

        var sourceFiles = ExpandSources(sources, imagesPath, out var sourceError, cancellationToken);
        if (sourceError is not null)
            return Failure(imagesPath, sourceError);
        if (sourceFiles.Count == 0)
            return Failure(imagesPath, "Provide at least one existing image, video, or folder containing media.");

        var executableDirectory = Path.GetDirectoryName(validation.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return Failure(imagesPath, "Could not determine FFmpeg's installation folder.");

        cancellationToken.ThrowIfCancellationRequested();
        var dataset = new ActiveDataset(root);
        dataset.CheckOwnedPath(imagesPath);
        var existingManaged = Directory.Exists(imagesPath) && ManagedImages(imagesPath).Any();
        if (existingManaged && !replaceExisting)
            return Failure(imagesPath, "Previous FFmpeg-imported images already exist. Set Replace to True to replace them, or move them out of the project's images folder.");

        // New pixels invalidate features, poses and calibration, even if importing later fails.
        // Do this only after validating the request, before changing any images.
        using (var transaction = new DatasetTransaction(dataset))
            transaction.Commit(cancellationToken,
                (null, Path.GetDirectoryName(dataset.Sparse)!), (null, dataset.Text),
                (null, Path.Combine(dataset.Colmap, "database.db")),
                (null, Path.Combine(dataset.Colmap, "database.db-wal")),
                (null, Path.Combine(dataset.Colmap, "database.db-shm")));
        Directory.CreateDirectory(imagesPath);
        if (replaceExisting) DeleteManagedImages(imagesPath, cancellationToken);

        var logPath = Path.Combine(root, "ffmpeg-import.log");
        ColmapLiveLog.Create(logPath);
        if (showTerminal)
            ColmapLiveLog.OpenTerminal(logPath, "Splatypus — FFmpeg media import");

        var imported = new List<string>();
        var log = new List<string>();
        for (var index = 0; index < sourceFiles.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
                return new FFmpegMediaImportResult(false, true, imagesPath, imported, JoinLog(log), "FFmpeg media import was cancelled.");
            var source = sourceFiles[index];
            var stem = SanitizeFileStem(Path.GetFileNameWithoutExtension(source));
            var prefix = $"{ManagedPrefix}{index + 1:D4}_{stem}";
            var isImage = IsStillImage(source);
            var output = isImage
                ? Path.Combine(imagesPath, prefix + ".jpg")
                : Path.Combine(imagesPath, prefix + "_%06d.jpg");

            var arguments = BuildArguments(source, output, isImage, framesPerSecond);
            var processResult = await RunProcessAsync(validation.ExecutablePath, executableDirectory, arguments, logPath, cancellationToken).ConfigureAwait(false);
            log.Add($"{Path.GetFileName(source)}:{Environment.NewLine}{processResult.Log}");
            if (processResult.WasCancelled)
                return new FFmpegMediaImportResult(false, true, imagesPath, imported, JoinLog(log), "FFmpeg media import was cancelled.");
            if (!processResult.Succeeded)
                return new FFmpegMediaImportResult(false, false, imagesPath, imported, JoinLog(log), $"FFmpeg failed while importing {Path.GetFileName(source)}; inspect the log.");

            var created = isImage
                ? (File.Exists(output) ? new[] { output } : Array.Empty<string>())
                : Directory.EnumerateFiles(imagesPath, prefix + "_*.jpg", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!created.Any())
                return new FFmpegMediaImportResult(false, false, imagesPath, imported, JoinLog(log), $"FFmpeg completed but produced no frames for {Path.GetFileName(source)}.");
            imported.AddRange(created);
        }

        return new FFmpegMediaImportResult(true, false, imagesPath, imported, JoinLog(log), $"Imported {imported.Count:N0} JPEG image(s) from {sourceFiles.Count:N0} media source(s).");
    }

    internal static IReadOnlyList<string> ExpandSources(IReadOnlyList<string> sources, string imagesPath, out string? error, CancellationToken cancellationToken = default)
    {
        error = null;
        var files = new List<string>();
        var unique = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var source in sources.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(source.Trim());
            if (File.Exists(fullPath))
            {
                Add(fullPath);
                continue;
            }
            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsKnownMediaFile(file))
                        Add(file);
                }
                continue;
            }
            error = $"Media source was not found: {fullPath}";
            return Array.Empty<string>();
        }
        return files;

        void Add(string file)
        {
            // Never read the output folder back as a source during an import.
            if (file.StartsWith(imagesPath + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return;
            if (unique.Add(file))
                files.Add(file);
        }
    }

    private static bool IsKnownMediaFile(string file) =>
        ImageExtensions.Contains(Path.GetExtension(file)) || VideoExtensions.Contains(Path.GetExtension(file));

    internal static bool IsStillImage(string file) => ImageExtensions.Contains(Path.GetExtension(file));

    internal static string FormatFramesPerSecond(double framesPerSecond) =>
        framesPerSecond.ToString("0.######", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> BuildArguments(string source, string output, bool isImage, double framesPerSecond)
    {
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-n", "-i", source, "-map", "0:v:0" };
        if (isImage)
            arguments.AddRange(new[] { "-frames:v", "1", "-update", "1" });
        else
            arguments.AddRange(new[] { "-vf", "fps=" + FormatFramesPerSecond(framesPerSecond), "-start_number", "1" });
        arguments.AddRange(new[] { "-q:v", "2", output });
        return arguments;
    }

    private static async Task<(bool Succeeded, bool WasCancelled, string Log)> RunProcessAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, string logPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var stdoutTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardOutput, logPath);
            var stderrTask = ColmapLiveLog.ReadAndAppendAsync(process.StandardError, logPath);
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return (process.ExitCode == 0 && !cancellationToken.IsCancellationRequested, cancellationToken.IsCancellationRequested, string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }
        catch (Exception exception)
        {
            return (false, cancellationToken.IsCancellationRequested, exception.ToString());
        }
    }

    private static void DeleteManagedImages(string imagesPath, CancellationToken cancellationToken)
    {
        foreach (var file in ManagedImages(imagesPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "media" : safe;
    }

    private static FFmpegMediaImportResult Failure(string imagesPath, string status) =>
        new(false, false, imagesPath, Array.Empty<string>(), string.Empty, status);

    private static string JoinLog(IEnumerable<string> entries) => string.Join(Environment.NewLine + Environment.NewLine, entries.Where(entry => !string.IsNullOrWhiteSpace(entry)));

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
