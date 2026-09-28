using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace RhinoSplat.Core;

public sealed record BrushPlyRotationResult(bool Succeeded, string OutputPath, string Status);

/// <summary>Creates a separately rotated PLY for the RhinoGaussianSplatting viewer.</summary>
public static class BrushPlyRhinoRotation
{
    private const float InverseSqrtTwo = 0.7071067811865476f;
    private const string RhinoCoordinateMarker = "comment SP visualization: RhinoGaussianSplatting (+90deg X)";

    public static BrushPlyRotationResult CreateVisualizationCopy(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
            return new BrushPlyRotationResult(false, string.Empty, $"Brush PLY was not found: {inputPath}");

        var outputPath = Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, Path.GetFileNameWithoutExtension(inputPath) + "_rgs.ply");
        var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            var header = ReadHeader(input);
            var layout = ParseLayout(header);
            ValidateLayout(layout);

            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                var outputHeader = header.Replace("comment Vertical axis: y", "comment Vertical axis: z");
                outputHeader = outputHeader.Replace("end_header", RhinoCoordinateMarker + "\nend_header");
                output.Write(Encoding.ASCII.GetBytes(outputHeader));

                var record = new byte[layout.Stride];
                for (long vertex = 0; vertex < layout.VertexCount; vertex++)
                {
                    if ((vertex & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
                    input.ReadExactly(record);
                    Rotate(record, layout);
                    output.Write(record);
                }
            }
            File.Move(temporaryPath, outputPath, overwrite: true);
            return new BrushPlyRotationResult(true, outputPath, $"Created visualization PLY for RhinoGaussianSplatting (+90° around X): {outputPath}");
        }
        catch (OperationCanceledException)
        {
            return new BrushPlyRotationResult(false, string.Empty, "Visualization PLY conversion was cancelled.");
        }
        catch (Exception exception)
        {
            return new BrushPlyRotationResult(false, string.Empty, $"Could not create visualization PLY: {exception.Message}");
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    private static void Rotate(byte[] record, Layout layout)
    {
        var x = ReadFloat(record, layout.X);
        var y = ReadFloat(record, layout.Y);
        var z = ReadFloat(record, layout.Z);
        WriteFloat(record, layout.X, x);
        WriteFloat(record, layout.Y, -z);
        WriteFloat(record, layout.Z, y);

        var w = ReadFloat(record, layout.Rotation[0]);
        var qx = ReadFloat(record, layout.Rotation[1]);
        var qy = ReadFloat(record, layout.Rotation[2]);
        var qz = ReadFloat(record, layout.Rotation[3]);
        var rw = InverseSqrtTwo * (w - qx);
        var rx = InverseSqrtTwo * (w + qx);
        var ry = InverseSqrtTwo * (qy - qz);
        var rz = InverseSqrtTwo * (qy + qz);
        var length = MathF.Sqrt(rw * rw + rx * rx + ry * ry + rz * rz);
        if (length > 0.000001f) { rw /= length; rx /= length; ry /= length; rz /= length; }
        WriteFloat(record, layout.Rotation[0], rw);
        WriteFloat(record, layout.Rotation[1], rx);
        WriteFloat(record, layout.Rotation[2], ry);
        WriteFloat(record, layout.Rotation[3], rz);
    }

    private static float ReadFloat(byte[] data, int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, sizeof(float)));
    private static void WriteFloat(byte[] data, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, sizeof(float)), value);

    private static string ReadHeader(Stream input)
    {
        var bytes = new List<byte>();
        var line = new StringBuilder();
        while (true)
        {
            var value = input.ReadByte();
            if (value < 0) throw new InvalidDataException("PLY header has no end_header line.");
            bytes.Add((byte)value);
            if (value == '\n')
            {
                if (line.ToString().TrimEnd('\r') == "end_header") return Encoding.ASCII.GetString(bytes.ToArray());
                line.Clear();
            }
            else line.Append((char)value);
        }
    }

    private static Layout ParseLayout(string header)
    {
        if (!header.Contains("format binary_little_endian", StringComparison.Ordinal))
            throw new InvalidDataException("Only binary_little_endian PLY files are supported.");
        long vertexCount = -1;
        var stride = 0;
        var isVertexElement = false;
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rawLine in header.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0] == "element")
            {
                isVertexElement = parts[1] == "vertex";
                if (isVertexElement && !long.TryParse(parts[2], out vertexCount)) throw new InvalidDataException("PLY vertex count is invalid.");
                continue;
            }
            if (!isVertexElement || parts.Length < 3 || parts[0] != "property") continue;
            if (parts[1] == "list") throw new InvalidDataException("List vertex properties are not supported.");
            offsets[parts[2]] = stride;
            stride += TypeSize(parts[1]);
        }
        return new Layout(vertexCount, stride, offsets);
    }

    private static int TypeSize(string type) => type switch
    {
        "char" or "int8" or "uchar" or "uint8" => 1,
        "short" or "int16" or "ushort" or "uint16" => 2,
        "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
        "double" or "float64" => 8,
        _ => throw new InvalidDataException($"Unsupported PLY property type: {type}")
    };

    private static void ValidateLayout(Layout layout)
    {
        if (layout.VertexCount < 0 || layout.Stride <= 0) throw new InvalidDataException("PLY vertex layout is invalid.");
        foreach (var name in new[] { "x", "y", "z", "rot_0", "rot_1", "rot_2", "rot_3" })
            if (!layout.Offsets.ContainsKey(name)) throw new InvalidDataException($"PLY is missing '{name}'.");
    }

    private sealed class Layout
    {
        public Layout(long vertexCount, int stride, Dictionary<string, int> offsets)
        {
            VertexCount = vertexCount; Stride = stride; Offsets = offsets;
            X = offsets["x"]; Y = offsets["y"]; Z = offsets["z"];
            Rotation = new[] { offsets["rot_0"], offsets["rot_1"], offsets["rot_2"], offsets["rot_3"] };
        }
        public long VertexCount { get; }
        public int Stride { get; }
        public Dictionary<string, int> Offsets { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public int[] Rotation { get; }
    }
}
