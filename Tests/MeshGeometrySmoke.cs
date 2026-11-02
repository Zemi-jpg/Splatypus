using RhinoSplat.Core;
using RhinoSplat.Grasshopper;

internal static class MeshGeometrySmoke
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Run(string[] args)
    {
        using var core = args.Contains("--in-rhino") ? null : new Rhino.Runtime.InProcess.RhinoCore(new[] { "/nosplash", "/notemplate" }, Rhino.Runtime.InProcess.WindowStyle.Hidden);
        var data = PlyMeshData.Read(args[0]);
        using var original = ColmapMeshGeometry.ToRhino(data);
        if (!original.IsValid || original.Faces.Count != data.Faces.Length) throw new Exception("Original Rhino conversion failed.");
        var reduced = Task.Run(() => ColmapMeshGeometry.Reduce(data, 1000, 7, default)).GetAwaiter().GetResult();
        using var mesh = ColmapMeshGeometry.ToRhino(reduced);
        if (!mesh.IsValid || mesh.Faces.Count > 1000 || mesh.Faces.Count < 4 || !reduced.HasColors) throw new Exception("Rhino reduction did not retain valid colored geometry at the target.");
        var box = mesh.GetBoundingBox(true);
        if (Math.Abs(box.Min.X + 1) > .1 || Math.Abs(box.Max.X - 1) > .1 || box.Center.DistanceTo(Rhino.Geometry.Point3d.Origin) > .1) throw new Exception("Reduction changed coordinate scale or position.");
        reduced.Write(Path.Combine(Path.GetDirectoryName(args[0])!, "rhino-reduced.ply"));
        if (original.Faces.Count != data.Faces.Length) throw new Exception("Reduction mutated the source.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = false;
        try { ColmapMeshGeometry.Reduce(data, 500, 5, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled) throw new Exception("Cancellation was ignored.");
        Console.WriteLine($"PASS Rhino native reducer on background thread: {data.Faces.Length:N0} → {mesh.Faces.Count:N0} triangles; valid colored mesh, coordinates and source preserved, cancellation honored.");
    }
}
