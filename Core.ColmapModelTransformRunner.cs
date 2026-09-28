using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RhinoSplat.Core;

/// <summary>
/// A right-handed similarity transform: world' = scale * rotation * world + translation.
/// COLMAP camera intrinsics and poses remain valid only for this class of transform.
/// </summary>
public readonly record struct ColmapSimilarityTransform(
    double Scale,
    double R00, double R01, double R02,
    double R10, double R11, double R12,
    double R20, double R21, double R22,
    double Tx, double Ty, double Tz)
{
    public (double X, double Y, double Z) Apply(double x, double y, double z) =>
    (
        Scale * (R00 * x + R01 * y + R02 * z) + Tx,
        Scale * (R10 * x + R11 * y + R12 * z) + Ty,
        Scale * (R20 * x + R21 * y + R22 * z) + Tz
    );
}

public sealed record ColmapModelTransformResult(
    bool Succeeded,
    bool WasCancelled,
    string TextModelPath,
    string SparseModelPath,
    string Log,
    string Status);

public static class ColmapModelTransformRunner
{
    public static async Task<ColmapModelTransformResult> RunAsync(
        string colmapExecutable, string projectRoot, ColmapSimilarityTransform transform,
        CancellationToken cancellationToken)
    {
        var log = string.Empty;
        var textPath = string.Empty; var sparsePath = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dataset = new ActiveDataset(projectRoot);
            var aligned = Path.Combine(dataset.Colmap, "aligned");
            textPath = Path.Combine(aligned, "sparse-text");
            sparsePath = Path.Combine(aligned, "sparse", "0");
            using var lease = DatasetLease.Acquire(dataset.Root);
            dataset.Validate();
            var validation = ToolValidation.Colmap(colmapExecutable);
            if (!validation.IsValid) throw new IOException(validation.Message);
            if (!double.IsFinite(transform.Scale) || transform.Scale <= 0)
                throw new ArgumentException("Alignment requires a positive uniform scale.");
            using var transaction = new DatasetTransaction(dataset);
            var source = Path.Combine(transaction.Work, "source-text");
            var text = Path.Combine(transaction.Work, "text");
            var sparse = Path.Combine(transaction.Work, "sparse");
            Directory.CreateDirectory(text);
            Directory.CreateDirectory(Path.Combine(sparse, "0"));
            var logPath = Path.Combine(dataset.Colmap, "alignment.log");
            ColmapLiveLog.Create(logPath);
            // Always read the current binary model, including corrected intrinsics after undistortion.
            log = await DatasetColmapProcess.Export(validation.ExecutablePath, dataset.Sparse, source, logPath, cancellationToken);
            ActiveDataset.ValidateText(source, dataset.Images);
            File.Copy(Path.Combine(source, "cameras.txt"), Path.Combine(text, "cameras.txt"));
            TransformImages(Path.Combine(source, "images.txt"), Path.Combine(text, "images.txt"), transform);
            TransformPoints(Path.Combine(source, "points3D.txt"), Path.Combine(text, "points3D.txt"), transform);
            log += await DatasetColmapProcess.Run(validation.ExecutablePath, logPath, cancellationToken,
                "model_converter", "--input_path", text, "--output_path", Path.Combine(sparse, "0"), "--output_type", "BIN");
            ActiveDataset.ValidatePair(dataset.Images, Path.Combine(sparse, "0"));
            ActiveDataset.ValidateText(text, dataset.Images);
            transaction.Commit(cancellationToken, (sparse, Path.GetDirectoryName(sparsePath)!), (text, textPath));
            return new(true, false, textPath, sparsePath, log, "Separate aligned model saved. Every Write transforms the active base model; connect Aligned Sparse Model to Brush's optional model input.");
        }
        catch (Exception error)
        {
            return new(false, error is OperationCanceledException || cancellationToken.IsCancellationRequested,
                textPath, sparsePath, log + error, "Alignment did not replace the aligned output: " + error.Message);
        }
    }

    private static void TransformImages(string sourcePath, string destinationPath, ColmapSimilarityTransform transform)
    {
        using var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(false));
        var observation = false;
        foreach (var line in File.ReadLines(sourcePath))
        {
            if (observation) { writer.WriteLine(line); observation = false; continue; }
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                writer.WriteLine(line);
                continue;
            }

            observation = true;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!IsImageHeader(fields) || !TryParsePose(fields, out var rotation, out var translation))
            {
                writer.WriteLine(line);
                continue;
            }

            var (r00, r01, r02, r10, r11, r12, r20, r21, r22) = QuaternionToMatrix(rotation.Qw, rotation.Qx, rotation.Qy, rotation.Qz);
            var centre =
            (
                X: -(r00 * translation.X + r10 * translation.Y + r20 * translation.Z),
                Y: -(r01 * translation.X + r11 * translation.Y + r21 * translation.Z),
                Z: -(r02 * translation.X + r12 * translation.Y + r22 * translation.Z)
            );
            var newCentre = transform.Apply(centre.X, centre.Y, centre.Z);

            // R' = R * U^T, where U is the rotational part of world' = s U world + d.
            var nr00 = r00 * transform.R00 + r01 * transform.R01 + r02 * transform.R02;
            var nr01 = r00 * transform.R10 + r01 * transform.R11 + r02 * transform.R12;
            var nr02 = r00 * transform.R20 + r01 * transform.R21 + r02 * transform.R22;
            var nr10 = r10 * transform.R00 + r11 * transform.R01 + r12 * transform.R02;
            var nr11 = r10 * transform.R10 + r11 * transform.R11 + r12 * transform.R12;
            var nr12 = r10 * transform.R20 + r11 * transform.R21 + r12 * transform.R22;
            var nr20 = r20 * transform.R00 + r21 * transform.R01 + r22 * transform.R02;
            var nr21 = r20 * transform.R10 + r21 * transform.R11 + r22 * transform.R12;
            var nr22 = r20 * transform.R20 + r21 * transform.R21 + r22 * transform.R22;
            var newRotation = MatrixToQuaternion(nr00, nr01, nr02, nr10, nr11, nr12, nr20, nr21, nr22);
            var newTranslation =
            (
                X: -(nr00 * newCentre.X + nr01 * newCentre.Y + nr02 * newCentre.Z),
                Y: -(nr10 * newCentre.X + nr11 * newCentre.Y + nr12 * newCentre.Z),
                Z: -(nr20 * newCentre.X + nr21 * newCentre.Y + nr22 * newCentre.Z)
            );

            writer.Write(fields[0]); writer.Write(' ');
            writer.Write(Format(newRotation.Qw)); writer.Write(' '); writer.Write(Format(newRotation.Qx)); writer.Write(' ');
            writer.Write(Format(newRotation.Qy)); writer.Write(' '); writer.Write(Format(newRotation.Qz)); writer.Write(' ');
            writer.Write(Format(newTranslation.X)); writer.Write(' '); writer.Write(Format(newTranslation.Y)); writer.Write(' ');
            writer.Write(Format(newTranslation.Z)); writer.Write(' '); writer.Write(fields[8]); writer.Write(' ');
            writer.WriteLine(string.Join(" ", fields.Skip(9)));
        }
    }

    private static void TransformPoints(string sourcePath, string destinationPath, ColmapSimilarityTransform transform)
    {
        using var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(false));
        foreach (var line in File.ReadLines(sourcePath))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                writer.WriteLine(line);
                continue;
            }
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !TryDouble(fields[1], out var x) || !TryDouble(fields[2], out var y) || !TryDouble(fields[3], out var z))
            {
                writer.WriteLine(line);
                continue;
            }
            var point = transform.Apply(x, y, z);
            writer.Write(fields[0]); writer.Write(' '); writer.Write(Format(point.X)); writer.Write(' ');
            writer.Write(Format(point.Y)); writer.Write(' '); writer.Write(Format(point.Z));
            if (fields.Length > 4) { writer.Write(' '); writer.Write(string.Join(" ", fields.Skip(4))); }
            writer.WriteLine();
        }
    }

    private static bool IsImageHeader(string[] fields) => fields.Length >= 10 && long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static bool TryParsePose(string[] fields, out (double Qw, double Qx, double Qy, double Qz) rotation, out (double X, double Y, double Z) translation)
    {
        rotation = default; translation = default;
        if (!TryDouble(fields[1], out var qw) || !TryDouble(fields[2], out var qx) || !TryDouble(fields[3], out var qy) || !TryDouble(fields[4], out var qz)
            || !TryDouble(fields[5], out var tx) || !TryDouble(fields[6], out var ty) || !TryDouble(fields[7], out var tz))
            return false;
        rotation = (qw, qx, qy, qz);
        translation = (tx, ty, tz);
        return true;
    }

    private static bool TryDouble(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static (double R00, double R01, double R02, double R10, double R11, double R12, double R20, double R21, double R22) QuaternionToMatrix(double w, double x, double y, double z)
    {
        var n = Math.Sqrt(w * w + x * x + y * y + z * z);
        w /= n; x /= n; y /= n; z /= n;
        return (1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w),
                2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w),
                2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y));
    }

    private static (double Qw, double Qx, double Qy, double Qz) MatrixToQuaternion(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
    {
        double w, x, y, z;
        var trace = m00 + m11 + m22;
        if (trace > 0)
        {
            var s = Math.Sqrt(trace + 1) * 2; w = 0.25 * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            var s = Math.Sqrt(1 + m00 - m11 - m22) * 2; w = (m21 - m12) / s; x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            var s = Math.Sqrt(1 + m11 - m00 - m22) * 2; w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s;
        }
        else
        {
            var s = Math.Sqrt(1 + m22 - m00 - m11) * 2; w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s;
        }
        var n = Math.Sqrt(w * w + x * x + y * y + z * z);
        return (w / n, x / n, y / n, z / n);
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? string.Empty).Trim().Where(character => !invalid.Contains(character)).ToArray());
        return cleaned is "." or ".." ? string.Empty : cleaned;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
