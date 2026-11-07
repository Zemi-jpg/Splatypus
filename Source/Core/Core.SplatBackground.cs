using System.Buffers.Binary;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace RhinoSplat.Core;

public readonly record struct BackgroundSplat(double X, double Y, double Z, Color Color, double Opacity);
public sealed record BackgroundSplatData(BackgroundSplat[] Samples, int Total);
public readonly record struct BackgroundColorSample(Color Color, double Height, double Weight);
public sealed record SplatBackgroundColors(Color Solid, Color Top, Color Bottom, int Count);

/// <summary>Bounded PLY sampling and a robust, baked-color backdrop estimate. No renderer or lighting changes.</summary>
public static class SplatBackground
{
    private sealed record Property(string Name, string Type, int Offset, int Size);

    public static BackgroundSplatData Read(string path, int maximum = 50000)
    {
        if (maximum is < 100 or > 500000) throw new ArgumentException("Maximum Samples must be 100–500,000.");
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
        if (Line() != "ply") throw new IOException("Expected a PLY file; export other splat formats to PLY first.");
        string? format = null, element = null;
        var count = -1; var stride = 0; var prefix = false;
        var properties = new List<Property>();
        while (true)
        {
            var line = Line();
            if (line == "end_header") break;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] is "comment" or "obj_info") continue;
            if (parts[0] == "format" && parts.Length == 3 && parts[2] == "1.0") format = parts[1];
            else if (parts[0] == "element" && parts.Length == 3 && int.TryParse(parts[2], out var n) && n >= 0)
            {
                element = parts[1];
                if (element == "vertex") { if (count >= 0) throw new IOException("Duplicate PLY vertex element."); count = n; }
                else if (count < 0 && n > 0) prefix = true;
            }
            else if (parts[0] == "property" && element is not null)
            {
                if (element != "vertex") continue;
                if (parts.Length != 3) throw new IOException("List properties are unsupported in PLY vertices.");
                var size = Size(parts[1]);
                if (properties.Any(p => p.Name == parts[2])) throw new IOException("Duplicate PLY vertex property.");
                properties.Add(new(parts[2], parts[1], stride, size));
                stride = checked(stride + size);
                if (stride > 65536) throw new IOException("PLY vertex record is too large.");
            }
            else throw new IOException("Invalid PLY header: " + line);
        }
        if (prefix) throw new IOException("PLY vertices must be the first nonempty element.");
        if (format is not ("ascii" or "binary_little_endian" or "binary_big_endian")) throw new IOException("Unsupported PLY format.");
        if (count <= 0 || stride == 0) throw new IOException("PLY has no splats.");
        int Index(string name) => properties.FindIndex(p => p.Name == name);
        var x = Index("x"); var y = Index("y"); var z = Index("z");
        var rgb = new[] { Index("red"), Index("green"), Index("blue") };
        var dc = new[] { Index("f_dc_0"), Index("f_dc_1"), Index("f_dc_2") };
        var useDc = dc.All(i => i >= 0);
        if (x < 0 || y < 0 || z < 0 || !useDc && rgb.Any(i => i < 0))
            throw new IOException("PLY needs x/y/z and f_dc_0/1/2 or red/green/blue colors.");
        var opacity = Index("opacity"); var alpha = Index("alpha");
        var dataStart = stream.Position;
        if (format != "ascii" && (long)count * stride > stream.Length - dataStart) throw new IOException("PLY body is truncated.");
        if (format == "ascii" && (long)count * properties.Count * 2 - 1 > stream.Length - dataStart) throw new IOException("PLY body is truncated.");
        using var ascii = format == "ascii" ? new StreamReader(stream, Encoding.ASCII, false, 65536, true) : null;
        string Word()
        {
            var word = new StringBuilder(); int c;
            do { c = ascii!.Read(); } while (c >= 0 && char.IsWhiteSpace((char)c));
            while (c >= 0 && !char.IsWhiteSpace((char)c))
            {
                if (word.Length > 128) throw new IOException("Invalid PLY scalar.");
                word.Append((char)c); c = ascii!.Read();
            }
            if (word.Length == 0) throw new EndOfStreamException("PLY body is truncated.");
            return word.ToString();
        }
        var samples = new List<BackgroundSplat>(Math.Min(count, maximum));
        var values = new double[properties.Count]; var record = new byte[stride];
        var take = Math.Min(count, maximum); var next = 0;
        for (var sample = 0; sample < take; sample++)
        {
            // Midpoints of equal-size strata avoid always selecting a periodic first row.
            var vertex = (int)((sample + 0.5) * count / take);
            if (ascii is null)
            {
                stream.Position = dataStart + (long)vertex * stride;
                stream.ReadExactly(record);
                for (var i = 0; i < values.Length; i++) values[i] = Scalar(record.AsSpan(properties[i].Offset, properties[i].Size), properties[i].Type, format == "binary_big_endian");
            }
            else
            {
                for (; next <= vertex; next++)
                    for (var i = 0; i < values.Length; i++) values[i] = double.Parse(Word(), CultureInfo.InvariantCulture);
            }
            var indices = useDc ? dc : rgb;
            if (new[] { x, y, z }.Concat(indices).Any(i => !double.IsFinite(values[i]))) continue;
            var a = opacity >= 0 ? 1 / (1 + Math.Exp(-values[opacity])) : alpha >= 0 ? values[alpha] / 255 : 1;
            if (!double.IsFinite(a) || a < 0.02) continue;
            byte Channel(int i) => (byte)Math.Clamp(Math.Round(useDc ? (0.5 + 0.28209479177387814 * values[i]) * 255 : values[i]), 0, 255);
            samples.Add(new(values[x], values[y], values[z], Color.FromArgb(Channel(indices[0]), Channel(indices[1]), Channel(indices[2])), Math.Clamp(a, 0, 1)));
        }
        if (samples.Count == 0) throw new IOException("PLY has no usable colored splats (non-finite or nearly transparent samples were ignored).");
        return new(samples.ToArray(), count);
    }

    public static SplatBackgroundColors Estimate(IReadOnlyList<BackgroundColorSample> samples)
    {
        var valid = samples.Where(s => double.IsFinite(s.Height) && double.IsFinite(s.Weight) && s.Weight > 0).ToArray();
        if (valid.Length == 0) throw new ArgumentException("No visible splat colors were found. Check PLY alignment and camera frames.");
        var solid = Dominant(valid);
        var top = valid.Where(s => s.Height > 0.25).ToArray();
        var bottom = valid.Where(s => s.Height < -0.25).ToArray();
        return new(solid, top.Length >= 8 ? Dominant(top) : solid, bottom.Length >= 8 ? Dominant(bottom) : solid, valid.Length);
    }

    private static Color Dominant(BackgroundColorSample[] samples)
    {
        // Smoothed RGB histogram rejects isolated bright/dark floaters without
        // averaging unrelated sky and ground colors into a muddy backdrop.
        var bins = new double[4096];
        foreach (var s in samples) bins[(s.Color.R >> 4) * 256 + (s.Color.G >> 4) * 16 + (s.Color.B >> 4)] += s.Weight;
        var best = -1.0; var cr = 0; var cg = 0; var cb = 0;
        for (var r = 0; r < 16; r++) for (var g = 0; g < 16; g++) for (var b = 0; b < 16; b++)
        {
            var score = 0.0;
            for (var dr = Math.Max(0, r - 1); dr <= Math.Min(15, r + 1); dr++)
            for (var dg = Math.Max(0, g - 1); dg <= Math.Min(15, g + 1); dg++)
            for (var db = Math.Max(0, b - 1); db <= Math.Min(15, b + 1); db++) score += bins[dr * 256 + dg * 16 + db];
            if (score > best) { best = score; cr = r; cg = g; cb = b; }
        }
        double red = 0, green = 0, blue = 0, weight = 0;
        foreach (var s in samples)
        {
            if (Math.Abs((s.Color.R >> 4) - cr) > 1 || Math.Abs((s.Color.G >> 4) - cg) > 1 || Math.Abs((s.Color.B >> 4) - cb) > 1) continue;
            red += s.Color.R * s.Weight; green += s.Color.G * s.Weight; blue += s.Color.B * s.Weight; weight += s.Weight;
        }
        return Color.FromArgb((int)Math.Round(red / weight), (int)Math.Round(green / weight), (int)Math.Round(blue / weight));
    }

    private static int Size(string type) => type switch
    {
        "char" or "int8" or "uchar" or "uint8" => 1,
        "short" or "int16" or "ushort" or "uint16" => 2,
        "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
        "double" or "float64" => 8,
        _ => throw new IOException("Unsupported PLY scalar: " + type)
    };

    private static double Scalar(ReadOnlySpan<byte> data, string type, bool big) => type switch
    {
        "char" or "int8" => (sbyte)data[0], "uchar" or "uint8" => data[0],
        "short" or "int16" => big ? BinaryPrimitives.ReadInt16BigEndian(data) : BinaryPrimitives.ReadInt16LittleEndian(data),
        "ushort" or "uint16" => big ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data),
        "int" or "int32" => big ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data),
        "uint" or "uint32" => big ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data),
        "float" or "float32" => BitConverter.Int32BitsToSingle(big ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data)),
        _ => BitConverter.Int64BitsToDouble(big ? BinaryPrimitives.ReadInt64BigEndian(data) : BinaryPrimitives.ReadInt64LittleEndian(data))
    };
}
