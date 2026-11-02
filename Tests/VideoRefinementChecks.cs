using RhinoSplat.Core;

internal static class VideoRefinementChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception(message); ++count; Console.WriteLine("PASS " + message); }
        var root = Path.Combine(Path.GetTempPath(), "Splatypus-video-refinement-" + Guid.NewGuid().ToString("N"));
        var images = Path.Combine(root, "images"); var media = Path.Combine(root, "media");
        Directory.CreateDirectory(images); Directory.CreateDirectory(Path.Combine(media, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(media, "z.MOV"), "video");
            File.WriteAllText(Path.Combine(media, "nested", "a.mp4"), "video");
            File.WriteAllText(Path.Combine(media, "a_000001.jpg"), "still with numeric name");
            File.WriteAllText(Path.Combine(media, "photo.jpg"), "still");
            foreach (var prefix in new[] { "sp_media_0010_a", "sp_media_0002_z" })
                foreach (var frame in new[] { 10, 1, 3 }) File.WriteAllText(Path.Combine(images, $"{prefix}_{frame:D6}.jpg"), "frame");
            File.WriteAllText(Path.Combine(images, "sp_media_0011_a_000001.jpg"), "still");
            var groups = VideoFrameReader.Read(root, new[] { media, Path.Combine(media, "z.MOV"), Path.Combine(media, "photo.jpg") }, out var warning);
            Check(groups.Count == 2 && groups[0].Prefix == "sp_media_0002_z" && groups[1].Prefix == "sp_media_0010_a", "Mixed folder discovery is recursive, deduplicated and ordered by imported numeric source index");
            Check(warning.Contains("Ambiguous") && groups[1].Frames.Count == 3, "Numeric photographs are excluded from legacy video discovery");
            Check(groups[0].FramesPerSecond == 2, "Old imports need no FPS input; Speed controls the default pace");

            var model = Path.Combine(root, "colmap", "sparse-text"); Directory.CreateDirectory(model);
            foreach (var file in new[] { "images.txt", "cameras.txt" }) File.WriteAllText(Path.Combine(model, file), "fixture");
            Check(VideoModelSelection.Resolve(root, out _) == model, "No saved alignment selects the base model automatically");
            var aligned = Path.Combine(root, "colmap", "aligned", "sparse-text"); Directory.CreateDirectory(aligned);
            foreach (var file in new[] { "images.txt", "cameras.txt" }) File.Copy(Path.Combine(model, file), Path.Combine(aligned, file));
            File.SetLastWriteTimeUtc(Path.Combine(aligned, "images.txt"), DateTime.UtcNow.AddMinutes(1));
            Check(VideoModelSelection.Resolve(root, out _) == aligned, "Previously saved alignment is discovered automatically");
            File.SetLastWriteTimeUtc(Path.Combine(model, "images.txt"), DateTime.UtcNow.AddMinutes(2));
            Check(VideoModelSelection.Resolve(root, out var status) == aligned && status.Contains(aligned), "Existing alignment takes precedence even when base timestamps are newer; status includes its path");
            File.WriteAllText(Path.Combine(aligned, VideoModelSelection.RevisionFile), VideoModelSelection.Revision(root));
            Check(VideoModelSelection.Resolve(root, out _) == aligned, "Recorded source revision identifies alignment of the current base");
            File.AppendAllText(Path.Combine(model, "images.txt"), "changed");
            Check(VideoModelSelection.Resolve(root, out _) == aligned, "An older revision record cannot silently discard an existing alignment");
            var sparse = Path.Combine(root, "colmap", "sparse", "0"); Directory.CreateDirectory(sparse);
            foreach (var file in new[] { "images.bin", "cameras.bin" }) File.WriteAllText(Path.Combine(sparse, file), "fixture");
            File.WriteAllText(Path.Combine(aligned, VideoModelSelection.RevisionFile), VideoModelSelection.Revision(root));
            File.AppendAllText(Path.Combine(model, "images.txt"), "re-export");
            Check(VideoModelSelection.Resolve(root, out _) == aligned, "Re-exporting text does not retire alignment of unchanged binary cameras");
            var legacy = Path.Combine(root, "colmap", "aligned model", "text"); Directory.CreateDirectory(legacy);
            foreach (var file in new[] { "images.txt", "cameras.txt" }) File.WriteAllText(Path.Combine(legacy, file), "legacy model");
            File.SetLastWriteTimeUtc(Path.Combine(legacy, "images.txt"), DateTime.UtcNow.AddMinutes(3));
            Check(VideoModelSelection.Resolve(root, out _) == legacy, "Discovers aligned model/text and selects the newest complete aligned camera model");
            Check(VideoModelSelection.Resolve(root, model, out var overrideStatus) == model && overrideStatus.Contains("specified"), "Explicit model overrides a detected aligned model");
            Check(VideoModelSelection.Resolve(root, Path.GetDirectoryName(legacy), out _) == legacy, "Override accepts an aligned model parent with a text folder");
            Check(VideoModelSelection.Resolve(root, root, out _) == legacy, "Override accepts a project dataset root");
            Check(VideoModelSelection.Resolve(root, " ", out _) == legacy, "Empty override restores automatic selection");
            var rejected = false;
            try { VideoModelSelection.Resolve(root, Path.Combine(root, "missing-model"), out _); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Missing explicit model reports an error instead of silently using another model");
            foreach (var name in new[] { "aligned_model", "aligned-model", "AlignedModel", "alignment" })
            {
                var alternate = Path.Combine(root, "colmap", "models", name, "text"); Directory.CreateDirectory(alternate);
                foreach (var file in new[] { "images.txt", "cameras.txt" }) File.WriteAllText(Path.Combine(alternate, file), "nested model");
                File.SetLastWriteTimeUtc(Path.Combine(alternate, "images.txt"), DateTime.UtcNow.AddMinutes(4));
                Check(VideoModelSelection.Resolve(root, out _) == alternate, "Discovers nested aligned layout: " + name);
                File.Delete(Path.Combine(alternate, "cameras.txt"));
            }
            foreach (var name in new[] { "aligned-backup", ".aligned-stage", "unaligned" })
            {
                var ignored = Path.Combine(root, "colmap", name, "text"); Directory.CreateDirectory(ignored);
                foreach (var file in new[] { "images.txt", "cameras.txt" }) File.WriteAllText(Path.Combine(ignored, file), "not a live alignment");
                File.SetLastWriteTimeUtc(Path.Combine(ignored, "images.txt"), DateTime.UtcNow.AddMinutes(10));
            }
            Check(VideoModelSelection.Resolve(root, out _) == legacy, "Incomplete, backup, staging and unaligned folders cannot supersede the selected alignment");
            File.Delete(Path.Combine(legacy, "cameras.txt"));
            Check(VideoModelSelection.Resolve(root, out _) == aligned, "Incomplete newest output falls back to another complete alignment");
            File.WriteAllText(Path.Combine(aligned, "cameras.txt"), "");
            Check(VideoModelSelection.Resolve(root, out _) == model, "Falls back to base only when no complete nonempty aligned pair exists");

            CameraRotation Yaw(double angle) => new(Math.Cos(angle / 2), 0, 0, Math.Sin(angle / 2));
            var noisy = new VideoCameraTrack(groups[0], Enumerable.Range(0, 9).Select(i => new VideoCameraKey(i,
                new VideoCameraPose(new(i, i % 2 == 0 ? 0.2 : -0.2, 0), Yaw(0.1 * i + (i % 2 == 0 ? 0.1 : -0.1)), 1, 1.5))));
            double Angle(VideoCameraPose p) => Math.Atan2(p.Right.Y, p.Right.X);
            double Noise(bool smooth) => Enumerable.Range(2, 5).Sum(i => Math.Abs(noisy.Sample(i, smooth)!.Centre.Y));
            Check(Noise(true) < Noise(false) * 0.15, "Smoothing strongly reduces alternating position jitter");
            var raw = Enumerable.Range(2, 5).Sum(i => Math.Abs(Angle(noisy.Sample(i, false)!) - 0.1 * i));
            var smooth = Enumerable.Range(2, 5).Sum(i => Math.Abs(Angle(noisy.Sample(i, true)!) - 0.1 * i));
            Check(smooth < raw * 0.15, "Smoothing strongly reduces angular jitter");
            var h = 0.0001;
            var leftVelocity = (Angle(noisy.Sample(4, true)!) - Angle(noisy.Sample(4 - h, true)!)) / h;
            var rightVelocity = (Angle(noisy.Sample(4 + h, true)!) - Angle(noisy.Sample(4, true)!)) / h;
            Check(Math.Abs(leftVelocity - rightVelocity) < 1e-4, "Spherical cubic rotation has continuous angular velocity at camera keys");
            var leftAccel = (noisy.Sample(4, true)!.Centre.X - 2 * noisy.Sample(4 - h, true)!.Centre.X + noisy.Sample(4 - 2 * h, true)!.Centre.X) / (h * h);
            var rightAccel = (noisy.Sample(4 + 2 * h, true)!.Centre.X - 2 * noisy.Sample(4 + h, true)!.Centre.X + noisy.Sample(4, true)!.Centre.X) / (h * h);
            Check(Math.Abs(leftAccel - rightAccel) < 0.001, "Position acceleration remains continuous through camera keys");
            Check(Math.Abs((noisy.Sample(h, true)!.Centre.X - noisy.Sample(0, true)!.Centre.X) / h) < 0.001, "Smooth path eases away from its stationary starting camera");
            Check(noisy.Sample(0, true) == noisy.Keys[0].Pose && noisy.Sample(8, true) == noisy.Keys[^1].Pose, "Smoothing preserves first and final camera poses");
            Console.WriteLine($"SUCCESS: {count} automatic discovery and smoother motion checks.");
        }
        finally { Directory.Delete(root, true); }
    }
}
