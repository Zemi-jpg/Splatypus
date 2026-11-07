using System.Text.Json;

namespace RhinoSplat.Core;

public enum ColmapMatchingMode { Exhaustive = 0, Sequential = 1, Spatial = 2, Auto = 3 }
public sealed record ColmapMatchingDecision(ColmapMatchingMode Mode, string Reason);

public static class ColmapMatching
{
    public static ColmapMatchingDecision Resolve(ColmapMatchingMode mode, string root)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Matching must be 0 Exhaustive, 1 Sequential, 2 Spatial or 3 Auto.");
        if (mode is ColmapMatchingMode.Exhaustive or ColmapMatchingMode.Sequential)
            return new(mode, "Selected by the user.");
        IReadOnlyList<ColmapImagePrior> images;
        try { images = ColmapDatabase.ReadImages(Path.Combine(root, "colmap", "database.db")); }
        catch (Exception error) when (error is IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            if (mode == ColmapMatchingMode.Spatial) throw new ArgumentException("Spatial matching needs readable image positions in the COLMAP database. " + error.Message);
            return new(ColmapMatchingMode.Exhaustive, "Auto could not verify database positions or sequence membership; using Exhaustive. " + error.Message);
        }
        var positioned = images.Count(image => image.HasPosition);
        var complete = images.Count >= 2 && positioned == images.Count
            && images.Select(image => (image.X, image.Y)).Distinct().Count() > 1;
        if (complete) return new(ColmapMatchingMode.Spatial, $"Verified usable positions for all {images.Count} database images.");
        if (mode == ColmapMatchingMode.Spatial)
            throw new ArgumentException($"Spatial matching needs distinct usable positions for every image; found {positioned}/{images.Count}. "
                + "Drone video telemetry is not automatically transferred to extracted frames. Use Sequential for video or Exhaustive for mixed/unpositioned images.");
        try
        {
            var path = Path.Combine(root, MediaImportManifest.FileName);
            var manifest = File.Exists(path) ? JsonSerializer.Deserialize<MediaImportManifest>(File.ReadAllText(path)) : null;
            if (manifest is { Version: 1, Sources.Length: 1 } && manifest.Sources[0] is { IsVideo: true } source
                && images.Count >= 2 && VideoFrameReader.Read(root, Array.Empty<string>(), out _).Single() is { } video
                && images.Select(image => image.Name).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(video.Frames.Select(frame => Path.GetFileName(frame.Path))))
                return new(ColmapMatchingMode.Sequential, "All database images belong to one recorded video sequence.");
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or ArgumentException) { }
        return new(ColmapMatchingMode.Exhaustive, $"Auto found {positioned}/{images.Count} positions and no complete single-video sequence; using Exhaustive for coverage.");
    }
}
