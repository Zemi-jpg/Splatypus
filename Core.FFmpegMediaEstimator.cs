using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RhinoSplat.Core;

public sealed record FFmpegMediaEstimateResult(int? EstimatedImages, bool WasCancelled, string Status);

/// <summary>Reads metadata only; never creates a project, log, or output image.</summary>
public sealed class FFmpegMediaEstimator
{
    // FPS is deliberately absent: a slider change only repeats the arithmetic.
    private readonly ConcurrentDictionary<DurationKey, double> _durations = new();
    private readonly record struct DurationKey(string Source, long Length, long Modified,
        string Probe, long ProbeLength, long ProbeModified);

    public async Task<FFmpegMediaEstimateResult> EstimateAsync(string executable, string projectRoot,
        IReadOnlyList<string> sources, double framesPerSecond, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(projectRoot))
                return Failure("Project Root is required to exclude the import output folder.");
            if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
                return Failure("Frames Per Second must be a finite number greater than zero.");

            // Use exactly the rate sent to FFmpeg by the importer.
            var rateText = FFmpegMediaImporter.FormatFramesPerSecond(framesPerSecond);
            var rate = double.Parse(rateText, CultureInfo.InvariantCulture);
            if (rate <= 0)
                return Failure("Frames Per Second is too small for the importer's six-decimal precision.");

            var validation = FFmpegToolValidation.Validate(executable);
            if (!validation.IsValid) return Failure(validation.Message);
            var imagesPath = Path.Combine(Path.GetFullPath(projectRoot), "images");
            var files = FFmpegMediaImporter.ExpandSources(sources, imagesPath, out var error, cancellationToken);
            if (error is not null) return Failure(error);
            if (files.Count == 0)
                return Failure("Provide at least one existing image, video, or folder containing media.");

            var probePath = Path.Combine(Path.GetDirectoryName(validation.ExecutablePath)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            var count = 0;
            foreach (var source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FFmpegMediaImporter.IsStillImage(source))
                {
                    count = checked(count + 1);
                    continue;
                }
                if (!File.Exists(probePath))
                    return Failure($"ffprobe was not found beside FFmpeg. Place it here to estimate videos: {probePath}");

                var info = new FileInfo(source);
                var probeInfo = new FileInfo(probePath);
                var key = new DurationKey(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks,
                    probeInfo.FullName, probeInfo.Length, probeInfo.LastWriteTimeUtc.Ticks);
                if (!_durations.TryGetValue(key, out var duration))
                {
                    duration = await ProbeDurationAsync(probePath, source, cancellationToken).ConfigureAwait(false);
                    // Bound the in-memory cache for long-lived Grasshopper documents.
                    if (_durations.Count >= 4096) _durations.Clear();
                    _durations[key] = duration;
                }

                // FFmpeg's fps filter rounds to the nearest output timestamp by default.
                // Round each source separately, since each is extracted independently.
                var frames = Math.Round(duration * rate, MidpointRounding.AwayFromZero);
                if (!double.IsFinite(frames) || frames > int.MaxValue - count)
                    return Failure("Estimated output exceeds the supported image count. Reduce FPS or the number of sources.");
                count += (int)frames;
            }

            return new(count, false, $"Estimated output: {count:N0} JPEGs at {rateText} fps from {files.Count:N0} source(s). " +
                "One JPEG per still image; video counts are approximate and may differ at clip boundaries.");
        }
        catch (OperationCanceledException)
        {
            return new(null, true, "Media estimate was cancelled.");
        }
        catch (OverflowException)
        {
            return Failure("Estimated output exceeds the supported image count. Reduce FPS or the number of sources.");
        }
        catch (Exception exception)
        {
            return Failure($"Could not estimate media: {exception.Message}");
        }
    }

    private static async Task<double> ProbeDurationAsync(string executable, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=duration:format=duration", "-of", "json", "-i", source })
            startInfo.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        string stdout;
        string stderr;
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdoutTask, stderrTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            timeout.Token.ThrowIfCancellationRequested();
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Probing {Path.GetFileName(source)} took longer than 10 seconds.");
        }

        if (process.ExitCode != 0)
        {
            var detail = stderr.Trim();
            if (detail.Length > 800) detail = detail[..800];
            throw new InvalidDataException($"ffprobe could not read {Path.GetFileName(source)}. {detail}");
        }
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidDataException($"No video stream was found in {Path.GetFileName(source)}.");
        // Prefer the imported video stream over the container, which may include longer audio.
        if (TryDuration(streams[0], out var duration)) return duration;
        if (root.TryGetProperty("format", out var format) && TryDuration(format, out duration)) return duration;
        throw new InvalidDataException($"No usable video duration was found in {Path.GetFileName(source)}.");
    }

    private static bool TryDuration(JsonElement element, out double duration)
    {
        duration = 0;
        return element.TryGetProperty("duration", out var value)
            && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
            && double.IsFinite(duration) && duration > 0;
    }

    private static FFmpegMediaEstimateResult Failure(string message) => new(null, false, message);
}
