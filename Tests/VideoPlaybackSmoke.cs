using RhinoSplat.Core;

var passed = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); passed++; Console.WriteLine("PASS " + description); }
void Close(double actual, double expected, string description) => Check(Math.Abs(actual - expected) < 1e-9, description);
void Reject(Action action, string description) { try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { Check(true, description); return; } throw new Exception(description); }
var root = Path.Combine(Path.GetTempPath(), "Splatypus-video-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "images"));
try
{
    var sources = Enumerable.Range(1, 15).Select(i => new MediaSourceRecord($"photo{i}.jpg", $"sp_media_{i:D4}_photo{i}", false)).ToList();
    // Same video filename, different source folders; filenames with spaces; a single-frame clip.
    sources.AddRange(Enumerable.Range(16, 4).Select(i => new MediaSourceRecord($"folder{i}/clip with spaces.mp4", $"sp_media_{i:D4}_clip with spaces", true)));
    foreach (var source in sources)
    {
        if (!source.IsVideo) File.WriteAllText(Path.Combine(root, "images", source.Prefix + ".jpg"), "fixture");
        else foreach (var number in source == sources[^1] ? new[] { 1 } : new[] { 10, 1, 3 })
            File.WriteAllText(Path.Combine(root, "images", $"{source.Prefix}_{number:D6}.jpg"), "fixture");
    }
    File.WriteAllText(Path.Combine(root, "images", "unrelated_000001.jpg"), "fixture");
    new MediaImportManifest(1, 2, sources.ToArray()).Save(root);
    var videos = VideoFrameReader.Read(root, Array.Empty<string>(), -1, out var warning);
    Check(videos.Count == 4 && videos.Sum(v => v.Frames.Count) == 10, "15 stills and 4 videos produce exactly 4 video branches and no photographs");
    Check(warning == "" && videos.All(v => v.FramesPerSecond == 2), "Recorded timing overrides legacy FPS");
    Check(videos[0].Frames.Select(f => f.Number).SequenceEqual(new long[] { 1, 3, 10 }), "Frame order is numeric and missing frame numbers are retained in timing");
    Close(videos[0].Frames[1].Time, 1, "Frame 3 at 2 FPS has time 1 second");
    Close(videos[0].Duration, 5, "Duration includes the last extracted frame interval");
    Check(videos.Select(v => v.Prefix).Distinct().Count() == 4 && videos[^1].Frames.Count == 1, "Same-name videos stay separate, including single-frame clips");

    var model = Path.Combine(root, "colmap", "sparse-text"); Directory.CreateDirectory(model);
    File.WriteAllText(Path.Combine(model, "cameras.txt"), "# intrinsics\n1 PINHOLE 800 600 400 300 400 300\n2 SIMPLE_RADIAL 800 600 300 400 300 0.1\n");
    File.WriteAllText(Path.Combine(model, "images.txt"),
        $"1 1 0 0 0 -1000000 -2 -3 1 {Path.GetFileName(videos[0].Frames[0].Path)}\n\n" +
        $"2 -1 0 0 0 -1000009 -2 -3 2 {Path.GetFileName(videos[0].Frames[2].Path)}\n\n" +
        "3 0.7071067811865476 0 0.7071067811865476 0 0 0 -10 1 rotated.jpg\n\n");
    var poses = VideoCameraReader.Read(model);
    var track = VideoCameraReader.Track(videos[0], Path.Combine(root, "images"), poses);
    Check(track.Keys.Count == 2 && track.Video.Frames.Count == 3, "Unregistered frames remain in Frames but do not invent camera keys");
    var first = track.Sample(0, true)!; var middle = track.Sample(2.25, true)!;
    Close(first.Centre.X, 1000000, "Double precision camera centre from inverse COLMAP pose");
    Close(first.Centre.Y, 2, "Camera translation inversion");
    Close(first.Forward.Z, 1, "COLMAP camera looks along positive Z");
    Close(first.Up.Y, -1, "COLMAP downward image Y becomes Rhino upward camera vector");
    Close(first.VerticalFov, Math.PI / 2, "Vertical field of view uses fy");
    Close(middle.Centre.X, 1000004.5, "Interpolation respects missing-pose time gaps");
    Close(middle.Forward.Z, 1, "Antipodal quaternion signs do not spin the camera");
    Close(track.Sample(100, true)!.Centre.X, 1000009, "After last key holds final pose");
    Close(poses["rotated.jpg"].Centre.X, -10, "Nonidentity world-to-camera rotation is inverted");
    Close(poses["rotated.jpg"].Forward.X, -1, "Nonidentity camera forward is inverted correctly");
    Check(track.FrameAt(1.2) == videos[0].Frames[1].Path, "Frame output follows extraction time");
    Check(new VideoCameraTrack(videos[0], Array.Empty<VideoCameraKey>()).Sample(0, true) is null, "Unregistered video has no fabricated pose");
    var stationary = new VideoCameraTrack(videos[0], new[] { new VideoCameraKey(1, first) });
    Check(stationary.Sample(0, true) == first && stationary.Sample(4, true) == first, "One registered frame is stationary at both ends");
    var curved = new VideoCameraTrack(videos[0], new[] { new VideoCameraKey(0, first), new VideoCameraKey(1, first with { Centre = new(1000001, 3, 3) }), new VideoCameraKey(3, first with { Centre = new(1000003, 2, 3) }) });
    Check(curved.Sample(1, true)!.Centre.Y > 2 && curved.Sample(1, true)!.Centre.Y < 2.5, "Smoothing softens intermediate jitter instead of forcing every measured pose");
    Close(curved.Sample(1, false)!.Centre.Y, 3, "Disabling smoothing retains exact original key positions");
    var epsilon = 1e-5;
    var left = (curved.Sample(1, true)!.Centre.Y - curved.Sample(1 - epsilon, true)!.Centre.Y) / epsilon;
    var right = (curved.Sample(1 + epsilon, true)!.Centre.Y - curved.Sample(1, true)!.Centre.Y) / epsilon;
    Check(Math.Abs(left - right) < 1e-4, "Smooth position velocity stays continuous across irregularly timed keys");
    Close(VideoPlaybackTime.Advance(1, 2, 0.5, 10, false), 2, "Half-speed real-time playback");
    Close(VideoPlaybackTime.Advance(9, 3, 1, 10, false), 10, "Playback stops at sequence end");
    Close(VideoPlaybackTime.Advance(9, 23, 1, 10, true), 2, "Looping tolerates delays longer than a sequence");
    Reject(() => VideoPlaybackTime.Advance(0, 1, 0, 10, false), "Invalid speed rejected");
    Reject(() => track.Sample(double.NaN, false), "Nonfinite seek rejected");
    File.WriteAllText(Path.Combine(model, "cameras.txt"), "1 UNKNOWN 800 600 300 400 300\n");
    Reject(() => VideoCameraReader.Read(model), "Unsupported camera model rejected explicitly");

    // Legacy mode is opt-in and rejects ambiguous duplicate video names.
    File.Delete(Path.Combine(root, MediaImportManifest.FileName));
    Reject(() => VideoFrameReader.Read(root, Array.Empty<string>(), 2, out _), "Legacy dataset never guesses videos from numeric still filenames");
    Check(VideoFrameReader.Read(root, new[] { sources[^1].Source }, out _).Count == 4, "Same-name legacy videos remain separate in imported source order");
    File.WriteAllText(Path.Combine(root, "images", "rs_media_0001_unique_000001.jpg"), "fixture");
    File.WriteAllText(Path.Combine(root, "images", "rs_media_0001_unique_1000000.jpg"), "fixture");
    var legacy = VideoFrameReader.Read(root, new[] { "unique.mov" }, 4, out warning);
    Check(legacy.Count == 1 && legacy[0].Frames[1].Number == 1000000 && warning.Contains("Older"), "Explicit legacy video matching supports old prefixes and large frame numbers");
    new MediaImportManifest(1, 2, new[] { new MediaSourceRecord("empty.mp4", "sp_media_0020_empty", true) }).Save(root);
    Check(VideoFrameReader.Read(root, Array.Empty<string>(), 2, out _).Single().Frames.Count == 0, "Partial imports retain empty source branches");
    new MediaImportManifest(2, 2, sources.ToArray()).Save(root);
    Reject(() => VideoFrameReader.Read(root, Array.Empty<string>(), 2, out _), "Unknown manifest version rejected");
    Console.WriteLine($"SUCCESS: {passed} video frame and playback checks.");
    VideoRefinementChecks.Run();
}
finally { Directory.Delete(root, true); }
