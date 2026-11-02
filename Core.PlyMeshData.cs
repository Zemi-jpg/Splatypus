using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RhinoSplat.Core;

public readonly record struct MeshVertex(double X, double Y, double Z, byte R = 0, byte G = 0, byte B = 0);
public readonly record struct MeshTriangle(int A, int B, int C);

/// <summary>Triangle PLY interchange for COLMAP (ASCII and both binary byte orders).</summary>
public sealed record PlyMeshData(MeshVertex[] Vertices, MeshTriangle[] Faces, bool HasColors)
{
    private sealed record Property(string Name, string Type, string? CountType = null);
    private sealed record Element(string Name, int Count, List<Property> Properties);
    public void Validate()
    {
        if (Vertices.Length == 0 || Faces.Length == 0) throw new IOException("COLMAP produced an empty mesh. Try a lower Poisson Trim or a different method.");
        foreach (var v in Vertices)
            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z)) throw new IOException("Mesh contains non-finite coordinates.");
        foreach (var f in Faces)
            if (f.A < 0 || f.B < 0 || f.C < 0 || f.A >= Vertices.Length || f.B >= Vertices.Length || f.C >= Vertices.Length
                || f.A == f.B || f.A == f.C || f.B == f.C) throw new IOException("Mesh contains an invalid triangle.");
    }

    public static PlyMeshData Read(string path, CancellationToken token = default)
    {
        using var stream = File.OpenRead(path);
        string Line()
        {
            var bytes = new List<byte>();
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException("Incomplete PLY header.");
                if (b == 10) return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                if (bytes.Count >= 8192 || stream.Position > 1048576) throw new IOException("PLY header is too large.");
                bytes.Add((byte)b);
            }
        }
        if (Line() != "ply") throw new IOException("Expected a PLY mesh.");
        string? format = null;
        var elements = new List<Element>();
        while (true)
        {
            var line = Line();
            if (line == "end_header") break;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || fields[0] is "comment" or "obj_info") continue;
            if (fields[0] == "format" && fields.Length == 3 && fields[2] == "1.0") format = fields[1];
            else if (fields[0] == "element" && fields.Length == 3 && int.TryParse(fields[2], out var count) && count >= 0 && count <= 20000000)
                elements.Add(new(fields[1], count, new()));
            else if (fields[0] == "property" && elements.Count > 0)
            {
                if (fields.Length == 3) elements[^1].Properties.Add(new(fields[2], fields[1]));
                else if (fields.Length == 5 && fields[1] == "list") elements[^1].Properties.Add(new(fields[4], fields[3], fields[2]));
                else throw new IOException("Invalid PLY property.");
            }
            else throw new IOException("Unsupported PLY header: " + line);
        }
        if (format is not ("ascii" or "binary_little_endian" or "binary_big_endian")) throw new IOException("Unsupported PLY format.");
        var ve = elements.SingleOrDefault(e => e.Name == "vertex") ?? throw new IOException("PLY has no vertices.");
        var fe = elements.SingleOrDefault(e => e.Name == "face") ?? throw new IOException("PLY has no faces; a point cloud cannot be loaded as a mesh.");
        if (new[] { "x", "y", "z" }.Any(name => !ve.Properties.Any(p => p.Name == name && p.CountType is null))) throw new IOException("PLY vertex coordinates are missing.");
        if (!fe.Properties.Any(p => p.Name is "vertex_indices" or "vertex_index" && p.CountType is not null)) throw new IOException("PLY triangle indices are missing.");
        // Avoid allocating enormous arrays from a corrupt/truncated header.
        if ((long)ve.Count * 6 + (long)fe.Count * 4 > stream.Length - stream.Position) throw new IOException("PLY body is shorter than its declared mesh.");
        var colors = new[] { "red", "green", "blue" }.All(name => ve.Properties.Any(p => p.Name == name && p.CountType is null));
        var vertices = new MeshVertex[ve.Count];
        var faces = new MeshTriangle[fe.Count];
        using var binary = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        using var ascii = format == "ascii" ? new StreamReader(stream, Encoding.ASCII, false, 65536, leaveOpen: true) : null;
        var big = format == "binary_big_endian";
        string Word()
        {
            var word = new StringBuilder();
            int c;
            do { c = ascii!.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c));
            while (c >= 0 && !char.IsWhiteSpace((char)c))
            {
                if (word.Length > 128) throw new IOException("Invalid PLY scalar.");
                word.Append((char)c); c = ascii!.Read();
            }
            if (word.Length == 0) throw new EndOfStreamException("Incomplete PLY body.");
            return word.ToString();
        }
        double Scalar(string type)
        {
            if (type is not ("char" or "int8" or "uchar" or "uint8" or "short" or "int16" or "ushort" or "uint16" or "int" or "int32" or "uint" or "uint32" or "float" or "float32" or "double" or "float64")) throw new IOException("Unsupported PLY scalar: " + type);
            if (ascii is not null) return double.Parse(Word(), CultureInfo.InvariantCulture);
            return type switch
            {
                "char" or "int8" => binary.ReadSByte(), "uchar" or "uint8" => binary.ReadByte(),
                "short" or "int16" => big ? BinaryPrimitives.ReverseEndianness(binary.ReadInt16()) : binary.ReadInt16(),
                "ushort" or "uint16" => big ? BinaryPrimitives.ReverseEndianness(binary.ReadUInt16()) : binary.ReadUInt16(),
                "int" or "int32" => big ? BinaryPrimitives.ReverseEndianness(binary.ReadInt32()) : binary.ReadInt32(),
                "uint" or "uint32" => big ? BinaryPrimitives.ReverseEndianness(binary.ReadUInt32()) : binary.ReadUInt32(),
                "float" or "float32" => big ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReverseEndianness(binary.ReadInt32())) : binary.ReadSingle(),
                _ => big ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReverseEndianness(binary.ReadInt64())) : binary.ReadDouble()
            };
        }
        int Integer(string type, int max)
        {
            var value = Scalar(type);
            if (!double.IsFinite(value) || value < 0 || value > max || value != Math.Truncate(value)) throw new IOException("Invalid PLY list count or vertex index.");
            return (int)value;
        }
        foreach (var element in elements)
        for (var i = 0; i < element.Count; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            double x = 0, y = 0, z = 0;
            byte r = 0, g = 0, b = 0;
            foreach (var property in element.Properties)
            {
                if (property.CountType is not null)
                {
                    var count = Integer(property.CountType, 1000000);
                    if (element.Name == "face" && property.Name is "vertex_indices" or "vertex_index")
                    {
                        if (count != 3) throw new IOException("Expected a triangle mesh from COLMAP.");
                        faces[i] = new(Integer(property.Type, vertices.Length - 1), Integer(property.Type, vertices.Length - 1), Integer(property.Type, vertices.Length - 1));
                    }
                    else for (var j = 0; j < count; j++) Scalar(property.Type);
                }
                else
                {
                    var value = Scalar(property.Type);
                    if (element.Name != "vertex") continue;
                    switch (property.Name)
                    {
                        case "x": x = value; break; case "y": y = value; break; case "z": z = value; break;
                        case "red": r = checked((byte)value); break; case "green": g = checked((byte)value); break; case "blue": b = checked((byte)value); break;
                    }
                }
            }
            if (element.Name == "vertex") vertices[i] = new(x, y, z, r, g, b);
        }
        var mesh = new PlyMeshData(vertices, faces, colors);
        mesh.Validate();
        return mesh;
    }

    public void Write(string path, CancellationToken token = default)
    {
        Validate();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        var header = $"ply\nformat binary_little_endian 1.0\nelement vertex {Vertices.Length}\nproperty double x\nproperty double y\nproperty double z\n";
        if (HasColors) header += "property uchar red\nproperty uchar green\nproperty uchar blue\n";
        header += $"element face {Faces.Length}\nproperty list uchar int vertex_indices\nend_header\n";
        writer.Write(Encoding.ASCII.GetBytes(header));
        for (var i = 0; i < Vertices.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var v = Vertices[i]; writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z);
            if (HasColors) { writer.Write(v.R); writer.Write(v.G); writer.Write(v.B); }
        }
        for (var i = 0; i < Faces.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var f = Faces[i]; writer.Write((byte)3); writer.Write(f.A); writer.Write(f.B); writer.Write(f.C);
        }
    }
}
