using System.Diagnostics;
using System.Globalization;
using RhinoSplat.Core;

internal static class ColmapMeshReal
{
    internal static async Task Run(string executable, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var input = Path.Combine(outputFolder, "oriented-sphere.ply");
        var output = Path.Combine(outputFolder, "native-poisson.ply");
        const int count = 5000;
        using (var writer = File.CreateText(input))
        {
            writer.Write($"ply\nformat ascii 1.0\nelement vertex {count}\nproperty float x\nproperty float y\nproperty float z\nproperty float nx\nproperty float ny\nproperty float nz\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n");
            for (var i = 0; i < count; i++)
            {
                var z = 1 - 2.0 * (i + 0.5) / count; var angle = Math.PI * (3 - Math.Sqrt(5)) * i;
                var radius = Math.Sqrt(1 - z*z); var x = radius * Math.Cos(angle); var y = radius * Math.Sin(angle);
                writer.WriteLine(FormattableString.Invariant($"{x:R} {y:R} {z:R} {x:R} {y:R} {z:R} 200 80 40"));
            }
        }
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetFullPath(outputFolder) };
        foreach (var arg in new[] { "poisson_mesher", "--input_path", input, "--output_path", output, "--PoissonMeshing.depth", "7", "--PoissonMeshing.trim", "0", "--PoissonMeshing.num_threads", "2" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var log = await stdout + await stderr; File.WriteAllText(Path.Combine(outputFolder, "native-poisson.log"), log);
        if (process.ExitCode != 0) throw new Exception(log);
        var mesh = PlyMeshData.Read(output);
        if (mesh.Faces.Length < 1000 || !mesh.HasColors || mesh.Vertices.Any(v => Math.Abs(Math.Sqrt(v.X*v.X+v.Y*v.Y+v.Z*v.Z)-1) > .1)) throw new Exception("Unexpected native Poisson geometry.");
        Console.WriteLine($"PASS Real COLMAP Poisson: {mesh.Vertices.Length:N0} colored vertices, {mesh.Faces.Length:N0} triangles; sphere error < 0.1. {output}");
    }
}
