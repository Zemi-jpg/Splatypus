using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class BrushSplatPreviewComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("splat-preview");

    public BrushSplatPreviewComponent()
        : base("SP Splat Preview", "Splat Preview", "Loads Brush's binary Gaussian-splat PLY as a coloured, decimated Grasshopper point-cloud preview without changing its coordinates.", "Splatypus", "06 Preview") { }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Brush PLY", "PLY", "Path to a Brush sp_*.ply export.", GH_ParamAccess.item);
        p.AddIntegerParameter("Maximum Splats", "Max", "Maximum splats to draw in Rhino. Use a lower value for faster viewport interaction.", GH_ParamAccess.item, 150000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Preview Cloud", "Cloud", "Coloured point-cloud proxy using the PLY's original coordinates.", GH_ParamAccess.item);
        p.AddIntegerParameter("Loaded Splats", "Count", "Number of sampled splats in the preview.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "PLY loading status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var path = string.Empty;
        var maximum = 150000;
        if (!da.GetData(0, ref path)) return;
        da.GetData(1, ref maximum);
        if (maximum <= 0) { da.SetData(2, "Maximum Splats must be greater than zero."); return; }
        if (!File.Exists(path)) { da.SetData(2, $"PLY was not found: {path}"); return; }

        try
        {
            var cloud = LoadPreview(path, maximum, out var total);
            da.SetData(0, cloud); da.SetData(1, cloud.Count);
            da.SetData(2, $"Loaded {cloud.Count:N0} of {total:N0} Brush splats as Grasshopper preview geometry using the PLY's original coordinates.");
        }
        catch (Exception exception)
        {
            da.SetData(2, $"Could not load Brush PLY: {exception.Message}");
        }
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("CD3DA63C-B9B2-4E58-BFBD-4E5F672B09C7");

    private static PointCloud LoadPreview(string path, int maximum, out int vertexCount)
    {
        using var stream = File.OpenRead(path);
        var (header, headerEndOffset) = ReadHeader(stream);
        if (!header.TryGetValue("format", out var format) || !format.StartsWith("binary_little_endian", StringComparison.Ordinal))
            throw new InvalidDataException("Only binary_little_endian Brush PLY files are supported.");
        if (!header.TryGetValue("vertex", out var vertexText) || !int.TryParse(vertexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out vertexCount))
            throw new InvalidDataException("PLY vertex count was not found.");
        var properties = header["properties"].Split('|');
        var x = Array.IndexOf(properties, "x"); var y = Array.IndexOf(properties, "y"); var z = Array.IndexOf(properties, "z");
        var dc0 = Array.IndexOf(properties, "f_dc_0"); var dc1 = Array.IndexOf(properties, "f_dc_1"); var dc2 = Array.IndexOf(properties, "f_dc_2");
        if (x < 0 || y < 0 || z < 0 || dc0 < 0 || dc1 < 0 || dc2 < 0)
            throw new InvalidDataException("PLY does not contain Brush position and colour attributes.");
        var stride = properties.Length * sizeof(float);
        var every = Math.Max(1, (int)Math.Ceiling(vertexCount / (double)maximum));
        var cloud = new PointCloud();
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        for (var vertex = 0; vertex < vertexCount; vertex += every)
        {
            stream.Position = headerEndOffset + (long)vertex * stride;
            var values = new float[properties.Length];
            for (var i = 0; i < values.Length; i++) values[i] = reader.ReadSingle();
            var colour = Color.FromArgb(ToByte(values[dc0]), ToByte(values[dc1]), ToByte(values[dc2]));
            // Preserve the file's coordinates. Axis conversion belongs to the
            // explicit Rhino-copy exporter, not to preview (which also loads those copies).
            cloud.Add(new Point3d(values[x], values[y], values[z]), colour);
        }
        return cloud;
    }

    private static (Dictionary<string, string> Header, long DataOffset) ReadHeader(Stream stream)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var properties = new List<string>();
        var line = new List<byte>();
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("PLY header ended unexpectedly.");
            if (value == '\n')
            {
                var text = Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r'); line.Clear();
                if (text == "end_header")
                {
                    result["properties"] = string.Join("|", properties);
                    return (result, stream.Position);
                }
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[0] == "format") result["format"] = parts[1];
                if (parts.Length >= 3 && parts[0] == "element" && parts[1] == "vertex") result["vertex"] = parts[2];
                if (parts.Length >= 3 && parts[0] == "property") properties.Add(parts[^1]);
            }
            else line.Add((byte)value);
        }
    }

    private static byte ToByte(float dc) => (byte)Math.Clamp((int)Math.Round((0.5 + 0.2820947918 * dc) * 255.0), 0, 255);
}
