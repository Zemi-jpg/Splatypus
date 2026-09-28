using System.Diagnostics;
using RhinoSplat.Core;

internal static class ActiveDatasetReal
{
    public static async Task Run(string colmap, string ffmpeg)
    {
        var dataset = new ActiveDataset(Path.Combine(Path.GetTempPath(), "Splatypus-RealColmap", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(dataset.Images, "nested"));
        Directory.CreateDirectory(dataset.Sparse); Directory.CreateDirectory(dataset.Text);
        async Task Process(string exe, params string[] arguments)
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in arguments) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var log = await stdout + await stderr;
            if (process.ExitCode != 0) throw new Exception(log);
        }
        var passed = 0;
        void Check(bool okay, string message)
        {
            if (!okay) throw new Exception(message);
            Console.WriteLine("PASS " + message); passed++;
        }
        await Process(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "color=c=red:s=40x30", "-frames:v", "1",
            Path.Combine(dataset.Images, "nested", "frame.jpg"));
        File.WriteAllText(Path.Combine(dataset.Text, "cameras.txt"), "1 SIMPLE_RADIAL 40 30 35 20 15 0.05\n");
        File.WriteAllText(Path.Combine(dataset.Text, "images.txt"), "1 1 0 0 0 0 0 0 1 nested/frame.jpg\n20 15 1\n");
        File.WriteAllText(Path.Combine(dataset.Text, "points3D.txt"), "1 1 2 3 255 128 64 0.1 1 0\n");
        await Process(colmap, "model_converter", "--input_path", dataset.Text, "--output_path", dataset.Sparse, "--output_type", "BIN");
        dataset.Validate();
        var brush = BrushDatasetPreparer.Prepare(dataset.Root);
        Check(brush.Succeeded, "Real binary model prepares the single Brush dataset.");
        var undistort = await ColmapImageUndistorterRunner.RunAsync(colmap, dataset.Root, false, false, default);
        Check(undistort.Succeeded, "Real COLMAP undistorts and replaces the active pair: " + undistort.Status);
        var cameras = File.ReadAllText(Path.Combine(dataset.Text, "cameras.txt"));
        Check(cameras.Contains("PINHOLE"), "Real undistortion publishes corrected camera calibration.");
        var similarity = new ColmapSimilarityTransform(2, 1,0,0, 0,1,0, 0,0,1, 10,20,30);
        var basePoints = File.ReadAllBytes(Path.Combine(dataset.Sparse, "points3D.bin"));
        var alignment = await ColmapModelTransformRunner.RunAsync(colmap, dataset.Root, similarity, default);
        Check(alignment.Succeeded, "Real COLMAP converts the aligned active model back to BIN: " + alignment.Status);
        Check(File.ReadAllText(Path.Combine(alignment.TextModelPath, "cameras.txt")) == cameras
            && File.ReadLines(Path.Combine(alignment.TextModelPath, "points3D.txt")).Any(l => l.StartsWith("1 12 24 36 ")),
            "Real alignment preserves corrected intrinsics while transforming points.");
        alignment = await ColmapModelTransformRunner.RunAsync(colmap, dataset.Root, similarity, default);
        Check(alignment.Succeeded && File.ReadLines(Path.Combine(alignment.TextModelPath, "points3D.txt")).Any(l => l.StartsWith("1 12 24 36 "))
            && basePoints.SequenceEqual(File.ReadAllBytes(Path.Combine(dataset.Sparse, "points3D.bin"))), "Repeated real alignment preserves the base and does not compound transforms.");
        Check(BrushDatasetPreparer.Prepare(dataset.Root, alignment.SparseModelPath).Succeeded, "Brush accepts the separate real aligned model.");
        Check(File.ReadAllBytes(Path.Combine(brush.DatasetPath, "sparse", "0", "points3D.bin"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(alignment.SparseModelPath, "points3D.bin")))
            && File.ReadAllBytes(Path.Combine(brush.DatasetPath, "images", "nested", "frame.jpg"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(dataset.Images, "nested", "frame.jpg"))),
            "Existing Brush links see the real corrected images and aligned binary model.");
        Check(!Directory.EnumerateDirectories(dataset.Colmap, ".dataset-stage-*").Any()
            && !Directory.Exists(Path.Combine(dataset.Colmap, "dense")), "Real operations clean up staging and keep alignment separate.");
        Console.WriteLine($"{passed} real COLMAP checks passed. Fixture: {dataset.Root}");
    }
}
