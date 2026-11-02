namespace RhinoSplat.Core;

/// <summary>Double precision coordinates used by the three-pair similarity fit.</summary>
public readonly record struct AlignmentPoint(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public static AlignmentPoint operator +(AlignmentPoint a, AlignmentPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static AlignmentPoint operator -(AlignmentPoint a, AlignmentPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static AlignmentPoint operator *(AlignmentPoint a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    internal double Dot(AlignmentPoint b) => X * b.X + Y * b.Y + Z * b.Z;
    internal AlignmentPoint Cross(AlignmentPoint b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    internal double Length => Math.Sqrt(Dot(this));
}

public static class ThreePointAlignment
{
    /// <summary>
    /// Least-squares translation, proper rotation and positive uniform scale.
    /// Three points span a plane: align the oriented planes, then solve the remaining
    /// in-plane rotation analytically. No shear or reflection is introduced.
    /// </summary>
    public static bool TryFit(IReadOnlyList<AlignmentPoint> source, IReadOnlyList<AlignmentPoint> target,
        out ColmapSimilarityTransform transform, out double rms, out string error)
    {
        transform = default; rms = 0; error = string.Empty;
        if (source.Count != 3 || target.Count != 3)
        { error = "Select exactly three source/reference pairs."; return false; }
        if (!TryFrame(source, out var sc, out var sx, out var sy, out var sz, out var se)
            || !TryFrame(target, out var tc, out var tx, out var ty, out var tz, out var te))
        { error = "Choose three distinct, well-spaced points on each model, away from a straight line."; return false; }

        double cosineTerm = 0, sineTerm = 0, energy = 0;
        for (var i = 0; i < 3; i++)
        {
            var a = (source[i] - sc) * (1 / se);
            var b = (target[i] - tc) * (1 / te);
            var ax = a.Dot(sx); var ay = a.Dot(sy);
            var bx = b.Dot(tx); var by = b.Dot(ty);
            cosineTerm += ax * bx + ay * by;
            sineTerm += ax * by - ay * bx;
            energy += ax * ax + ay * ay;
        }
        var magnitude = Math.Sqrt(cosineTerm * cosineTerm + sineTerm * sineTerm);
        var c = cosineTerm / magnitude; var s = sineTerm / magnitude;
        var ux = tx * c + ty * s;
        var uy = tx * -s + ty * c;
        // R = [ux uy tz] [sx sy sz]^T.
        var r0 = ux * sx.X + uy * sy.X + tz * sz.X;
        var r1 = ux * sx.Y + uy * sy.Y + tz * sz.Y;
        var r2 = ux * sx.Z + uy * sy.Z + tz * sz.Z;
        var scale = (te / se) * magnitude / energy;
        var translation = tc - (r0 * sc.X + r1 * sc.Y + r2 * sc.Z) * scale;
        if (!double.IsFinite(scale) || scale <= 1e-8 || !translation.IsFinite
            || !r0.IsFinite || !r1.IsFinite || !r2.IsFinite)
        { error = "The selected points produce an unsupported scale or coordinate range."; return false; }
        transform = new(scale, r0.X, r1.X, r2.X, r0.Y, r1.Y, r2.Y, r0.Z, r1.Z, r2.Z,
            translation.X, translation.Y, translation.Z);
        double squaredError = 0;
        for (var i = 0; i < 3; i++)
        {
            var p = transform.Apply(source[i].X, source[i].Y, source[i].Z);
            var delta = new AlignmentPoint(p.X, p.Y, p.Z) - target[i];
            squaredError += delta.Dot(delta);
        }
        rms = Math.Sqrt(squaredError / 3);
        if (!double.IsFinite(rms))
        { transform = default; error = "The selected coordinates exceed the supported numeric range."; return false; }
        return true;
    }

    private static bool TryFrame(IReadOnlyList<AlignmentPoint> points, out AlignmentPoint center,
        out AlignmentPoint x, out AlignmentPoint y, out AlignmentPoint z, out double extent)
    {
        center = x = y = z = default; extent = 0;
        if (points.Any(p => !p.IsFinite)) return false;
        var a = points[1] - points[0]; var b = points[2] - points[0];
        extent = Math.Max(a.Length, Math.Max(b.Length, (points[2] - points[1]).Length));
        if (!double.IsFinite(extent) || extent <= 0) return false;
        a *= 1 / extent; b *= 1 / extent;
        var normal = a.Cross(b);
        // Relative area rejects repeated or nearly collinear pairs at any model scale.
        if (normal.Length <= 1e-6) return false;
        x = a * (1 / a.Length); z = normal * (1 / normal.Length); y = z.Cross(x);
        center = points[0] + (points[1] - points[0]) * (1.0 / 3) + (points[2] - points[0]) * (1.0 / 3);
        return center.IsFinite;
    }
}
