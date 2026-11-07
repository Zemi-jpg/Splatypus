namespace RhinoSplat.Core;

// Precomputed once per track. Low-pass the measured poses, then use a C2 position
// spline and a C1 spherical cubic rotation curve. No camera data on disk is changed.
internal sealed class VideoCameraSmoothing
{
    private readonly VideoCameraKey[] keys;
    private readonly CameraVector[] second, angular;

    public VideoCameraSmoothing(IReadOnlyList<VideoCameraKey> source)
    {
        keys = source.ToArray();
        for (var pass = 0; pass < 3; pass++)
        {
            var filtered = (VideoCameraKey[])keys.Clone();
            for (var i = 1; i < keys.Length - 1; i++)
            {
                var t = (keys[i].Time - keys[i - 1].Time) / (keys[i + 1].Time - keys[i - 1].Time);
                var a = keys[i - 1].Pose; var b = keys[i + 1].Pose; var p = keys[i].Pose;
                filtered[i] = keys[i] with { Pose = p with
                {
                    Centre = p.Centre * 0.5 + (a.Centre * (1 - t) + b.Centre * t) * 0.5,
                    CameraToWorld = CameraRotation.Slerp(p.CameraToWorld, CameraRotation.Slerp(a.CameraToWorld, b.CameraToWorld, t), 0.5),
                    VerticalFov = p.VerticalFov * 0.5 + (a.VerticalFov * (1 - t) + b.VerticalFov * t) * 0.5
                } };
            }
            keys = filtered;
        }
        second = PositionSecondDerivatives();
        angular = new CameraVector[keys.Length];
        for (var i = 1; i < keys.Length - 1; i++)
        {
            var inverse = Inverse(keys[i].Pose.CameraToWorld);
            angular[i] = (Log(Multiply(inverse, keys[i + 1].Pose.CameraToWorld)) - Log(Multiply(inverse, keys[i - 1].Pose.CameraToWorld)))
                * (1 / (keys[i + 1].Time - keys[i - 1].Time));
        }
    }

    public VideoCameraPose? Sample(double time)
    {
        if (keys.Length == 0) return null;
        if (time <= keys[0].Time) return keys[0].Pose;
        if (time >= keys[^1].Time) return keys[^1].Pose;
        var low = 0; var high = keys.Length - 1;
        while (high - low > 1) { var mid = (low + high) / 2; if (keys[mid].Time <= time) low = mid; else high = mid; }
        var a = keys[low]; var b = keys[high]; var span = b.Time - a.Time;
        var t = (time - a.Time) / span; var u = 1 - t;
        var centre = a.Pose.Centre * u + b.Pose.Centre * t
            + (second[low] * (u * u * u - u) + second[high] * (t * t * t - t)) * (span * span / 6);
        var q0 = a.Pose.CameraToWorld; var q3 = b.Pose.CameraToWorld;
        var q1 = Multiply(q0, Exp(angular[low] * (span / 3))).Normalized();
        var q2 = Multiply(q3, Exp(angular[high] * (-span / 3))).Normalized();
        var qa = CameraRotation.Slerp(q0, q1, t); var qb = CameraRotation.Slerp(q1, q2, t); var qc = CameraRotation.Slerp(q2, q3, t);
        var rotation = CameraRotation.Slerp(CameraRotation.Slerp(qa, qb, t), CameraRotation.Slerp(qb, qc, t), t);
        var ease = t * t * (3 - 2 * t);
        return new(centre, rotation, a.Pose.VerticalFov * (1 - ease) + b.Pose.VerticalFov * ease, a.Pose.Aspect * (1 - ease) + b.Pose.Aspect * ease);
    }

    private CameraVector[] PositionSecondDerivatives()
    {
        var n = keys.Length;
        var result = new CameraVector[n];
        if (n < 2) return result;
        var upper = new double[n]; var diagonal = new double[n]; var lower = new double[n]; var rhs = new CameraVector[n];
        var first = keys[1].Time - keys[0].Time; var last = keys[^1].Time - keys[^2].Time;
        diagonal[0] = 2 * first; upper[0] = first;
        rhs[0] = (keys[1].Pose.Centre - keys[0].Pose.Centre) * (6 / first);
        lower[^1] = last; diagonal[^1] = 2 * last;
        rhs[^1] = (keys[^2].Pose.Centre - keys[^1].Pose.Centre) * (6 / last);
        for (var i = 1; i < n - 1; i++)
        {
            var left = keys[i].Time - keys[i - 1].Time; var right = keys[i + 1].Time - keys[i].Time;
            lower[i] = left; diagonal[i] = 2 * (left + right); upper[i] = right;
            rhs[i] = ((keys[i + 1].Pose.Centre - keys[i].Pose.Centre) * (1 / right)
                - (keys[i].Pose.Centre - keys[i - 1].Pose.Centre) * (1 / left)) * 6;
        }
        for (var i = 1; i < n; i++)
        {
            var factor = lower[i] / diagonal[i - 1];
            diagonal[i] -= factor * upper[i - 1]; rhs[i] -= rhs[i - 1] * factor;
        }
        result[^1] = rhs[^1] * (1 / diagonal[^1]);
        for (var i = n - 2; i >= 0; i--) result[i] = (rhs[i] - result[i + 1] * upper[i]) * (1 / diagonal[i]);
        return result;
    }

    private static CameraRotation Inverse(CameraRotation q) => new(q.W, -q.X, -q.Y, -q.Z);
    private static CameraRotation Multiply(CameraRotation a, CameraRotation b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
    private static CameraVector Log(CameraRotation q)
    {
        q = q.Normalized();
        if (q.W < 0) q = new(-q.W, -q.X, -q.Y, -q.Z);
        var length = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
        var factor = length < 1e-12 ? 1 : Math.Atan2(length, q.W) / length;
        return new(q.X * factor, q.Y * factor, q.Z * factor);
    }
    private static CameraRotation Exp(CameraVector v)
    {
        var angle = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        var factor = angle < 1e-12 ? 1 : Math.Sin(angle) / angle;
        return new(Math.Cos(angle), v.X * factor, v.Y * factor, v.Z * factor);
    }
}
