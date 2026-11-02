using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RhinoSplat.Core;

// Stored beside images, not inside it: COLMAP should only see image files.
public sealed record MediaSourceRecord(string Source, string Prefix, bool IsVideo)
{
    public double[]? FrameTimes { get; init; }
}
public sealed record MediaImportManifest(int Version, double FramesPerSecond, MediaSourceRecord[] Sources)
{
    public const string FileName = "ffmpeg-media.json";
    public bool SkipNearDuplicates { get; init; }

    public void Save(string root)
    {
        var path = Path.Combine(root, FileName);
        new ActiveDataset(root).CheckOwnedPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record VideoFrame(string Path, long Number, double Time);
public sealed record VideoFrames(string Source, string Prefix, double FramesPerSecond, IReadOnlyList<VideoFrame> Frames)
{
    public double Duration => Frames.Count == 0 ? 0 : Frames[^1].Time + 1 / FramesPerSecond;
}

public static class VideoFrameReader
{
    public static bool IsSourceMedia(string path) => FFmpegMediaImporter.IsVideo(path) || FFmpegMediaImporter.IsStillImage(path);
    // Older sequences have no recoverable timing. Use a neutral two-frame/second pace;
    // the player Speed control is the user-facing timing control.
    public static IReadOnlyList<VideoFrames> Read(string root, IReadOnlyList<string> media, out string warning) =>
        Read(root, media, 2, out warning);

    public static IReadOnlyList<VideoFrames> Read(string root, IReadOnlyList<string> legacyVideos, double legacyFps, out string warning)
    {
        root = Path.GetFullPath(root);
        var images = Path.Combine(root, "images");
        if (!Directory.Exists(images)) throw new DirectoryNotFoundException("The dataset images folder was not found.");
        var path = Path.Combine(root, MediaImportManifest.FileName);
        MediaImportManifest manifest;
        warning = string.Empty;
        if (File.Exists(path))
        {
            manifest = JsonSerializer.Deserialize<MediaImportManifest>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The media import record is empty.");
            if (manifest.Version != 1 || manifest.Sources is null) throw new InvalidDataException("Unsupported media import record.");
        }
        else
        {
            // Source media disambiguates a still named clip_000001 from an extracted frame.
            if (legacyVideos.Count == 0)
                throw new InvalidDataException("This older dataset has no media record. Connect the original mixed media folder or files to Media; videos are selected automatically.");
            var files = ImageFiles(images).Select(Path.GetFileNameWithoutExtension).ToArray();
            var records = new List<MediaSourceRecord>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mediaFiles = new List<string>();
            foreach (var item in legacyVideos.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                var full = Path.GetFullPath(item.Trim());
                if (Directory.Exists(full)) mediaFiles.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories));
                else mediaFiles.Add(full);
            }
            var videos = mediaFiles.Where(FFmpegMediaImporter.IsVideo).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
            var stillStems = mediaFiles.Where(FFmpegMediaImporter.IsStillImage).Select(f => FFmpegMediaImporter.SanitizeFileStem(Path.GetFileNameWithoutExtension(f))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ambiguousStill = false;
            foreach (var video in videos)
            {
                var stem = FFmpegMediaImporter.SanitizeFileStem(Path.GetFileNameWithoutExtension(video));
                var expression = new Regex(@"^(?<prefix>(?:sp|rs)_media_\d+_" + Regex.Escape(stem) + @")_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var prefixes = files.Select(f => expression.Match(f!)).Where(m => m.Success).Select(m => m.Groups["prefix"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // Multiple imports with the same basename are still distinct frame groups.
                // Without old provenance the exact original folder cannot be recovered.
                foreach (var prefix in prefixes)
                {
                    var matching = files.Where(f => expression.IsMatch(f!) && expression.Match(f!).Groups["prefix"].Value == prefix).ToArray();
                    var name = matching.Length == 1 ? Regex.Replace(matching[0]!, @"^(?:sp|rs)_media_\d+_", "", RegexOptions.IgnoreCase) : "";
                    if (stillStems.Contains(name)) { ambiguousStill = true; continue; }
                    if (claimed.Add(prefix)) records.Add(new MediaSourceRecord(video, prefix, true));
                }
            }
            if (videos.Length > 0 && records.Count == 0)
                throw new InvalidDataException("No extracted frames match videos in Media. Connect the folder used for this dataset's import.");
            records.Sort((a, b) => SourceIndex(a.Prefix).CompareTo(SourceIndex(b.Prefix)));
            manifest = new MediaImportManifest(1, legacyFps, records.ToArray());
            warning = "Older import: using a default playback pace. Adjust Speed in Video Playback.";
            if (ambiguousStill) warning += " Ambiguous single files matching photograph names were excluded; reimport to recover exact provenance.";
            if (records.GroupBy(r => Path.GetFileNameWithoutExtension(r.Source), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                warning += " Same-name videos are kept as separate imported groups; original folders were not recorded.";
        }
        if (!double.IsFinite(manifest.FramesPerSecond) || manifest.FramesPerSecond <= 0)
            throw new InvalidDataException("The extraction frame rate must be finite and greater than zero.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allFiles = ImageFiles(images).ToArray();
        var result = new List<VideoFrames>();
        foreach (var source in manifest.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Source) || string.IsNullOrWhiteSpace(source.Prefix)
                || source.Prefix.IndexOfAny(new[] { '/', '\\', '*', '?' }) >= 0 || !seen.Add(source.Prefix))
                throw new InvalidDataException("Invalid or duplicate media source in the import record.");
            if (!source.IsVideo) continue;
            var expression = new Regex("^" + Regex.Escape(source.Prefix) + @"_(\d+)\.(?:jpg|png)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var frames = new List<VideoFrame>();
            foreach (var file in allFiles)
            {
                var match = expression.Match(Path.GetFileName(file));
                if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
                {
                    var time = (number - 1) / manifest.FramesPerSecond;
                    if (source.FrameTimes is { } times)
                    {
                        if (number > times.Length || !double.IsFinite(times[number - 1]) || times[number - 1] < 0)
                            throw new InvalidDataException("Missing or invalid selected-frame timing. Reimport this video.");
                        time = times[number - 1];
                    }
                    else if (manifest.SkipNearDuplicates)
                        throw new InvalidDataException("Selected-frame timing is unavailable. Reimport this video.");
                    frames.Add(new VideoFrame(file, number, time));
                }
            }
            frames.Sort((a, b) => a.Number.CompareTo(b.Number));
            if (frames.Select(f => f.Number).Distinct().Count() != frames.Count)
                throw new InvalidDataException($"Duplicate frame numbers for '{source.Source}'.");
            if (frames.Zip(frames.Skip(1)).Any(pair => pair.Second.Time <= pair.First.Time))
                throw new InvalidDataException("Video frame timestamps must increase.");
            result.Add(new VideoFrames(source.Source, source.Prefix, manifest.FramesPerSecond, frames.AsReadOnly()));
        }
        return result.AsReadOnly();
    }

    private static long SourceIndex(string prefix) => long.TryParse(prefix.Split('_')[2], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : long.MaxValue;
    private static IEnumerable<string> ImageFiles(string images) => Directory.EnumerateFiles(images)
        .Where(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));
}
