using System.Drawing;
using Rhino.Geometry;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

internal static class ColmapMeshGeometry
{
    internal static Mesh ToRhino(PlyMeshData data, CancellationToken token = default)
    {
        var mesh = new Mesh();
        try
        {
            mesh.Vertices.UseDoublePrecisionVertices = true;
            for (var i = 0; i < data.Vertices.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                var v = data.Vertices[i];
                mesh.Vertices.Add(v.X, v.Y, v.Z);
                if (data.HasColors) mesh.VertexColors.Add(Color.FromArgb(v.R, v.G, v.B));
            }
            for (var i = 0; i < data.Faces.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                var f = data.Faces[i]; mesh.Faces.AddFace(f.A, f.B, f.C);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            if (!mesh.IsValid) throw new IOException("COLMAP produced a mesh Rhino cannot use. Try a different meshing method or trim setting.");
            return mesh;
        }
        catch { mesh.Dispose(); throw; }
    }

    internal static PlyMeshData Reduce(PlyMeshData data, int target, int accuracy, CancellationToken token)
    {
        using var mesh = ToRhino(data, token);
        token.ThrowIfCancellationRequested();
        // Already on the session's worker; retain cancellation in Rhino's native reducer.
        var okay = mesh.Reduce(target, true, accuracy, true, token, null, out var problem, false);
        token.ThrowIfCancellationRequested();
        if (!okay) throw new IOException("Rhino mesh reduction failed: " + problem);
        mesh.Faces.ConvertQuadsToTriangles();
        mesh.Compact();
        if (!mesh.IsValid) throw new IOException("Mesh reduction produced invalid geometry.");
        var colors = mesh.VertexColors.Count == mesh.Vertices.Count;
        var vertices = new MeshVertex[mesh.Vertices.Count];
        var faces = new MeshTriangle[mesh.Faces.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var v = mesh.Vertices.Point3dAt(i); var c = colors ? mesh.VertexColors[i] : Color.Black;
            vertices[i] = new(v.X, v.Y, v.Z, c.R, c.G, c.B);
        }
        for (var i = 0; i < faces.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var f = mesh.Faces[i]; faces[i] = new(f.A, f.B, f.C);
        }
        return new(vertices, faces, colors);
    }
}
