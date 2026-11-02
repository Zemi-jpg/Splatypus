using System.Globalization;

namespace RhinoSplat.Core;

public readonly record struct CameraVector(double X, double Y, double Z)
{
    public static CameraVector operator +(CameraVector a, CameraVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static CameraVector operator -(CameraVector a, CameraVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static CameraVector operator *(CameraVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static CameraVector Cross(CameraVector a, CameraVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

public readonly record struct CameraRotation(double W, double X, double Y, double Z)
{
    public CameraRotation Normalized()
    {
        var length = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        if (!double.IsFinite(length) || length < 1e-15) throw new InvalidDataException("Invalid camera rotation.");
        return new(W / length, X / length, Y / length, Z / length);
    }

    public CameraVector Rotate(CameraVector point)
    {
        var axis = new CameraVector(X, Y, Z);
        var cross = CameraVector.Cross(axis, point) * 2;
        return point + cross * W + CameraVector.Cross(axis, cross);
    }

    public static CameraRotation Slerp(CameraRotation a, CameraRotation b, double t)
    {
        var dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        if (dot < 0) { b = new(-b.W, -b.X, -b.Y, -b.Z); dot = -dot; }
        dot = Math.Clamp(dot, -1, 1);
        var left = 1 - t;
        var right = t;
        if (dot < 0.9995)
        {
            var angle = Math.Acos(dot);
            left = Math.Sin((1 - t) * angle) / Math.Sin(angle);
            right = Math.Sin(t * angle) / Math.Sin(angle);
        }
        return new CameraRotation(a.W * left + b.W * right, a.X * left + b.X * right,
            a.Y * left + b.Y * right, a.Z * left + b.Z * right).Normalized();
    }
}

public sealed record VideoCameraPose(CameraVector Centre, CameraRotation CameraToWorld, double VerticalFov, double Aspect)
{
    public CameraVector Forward => CameraToWorld.Rotate(new(0, 0, 1));
    public CameraVector Up => CameraToWorld.Rotate(new(0, -1, 0));
    public CameraVector Right => CameraToWorld.Rotate(new(1, 0, 0));
}

public sealed record VideoCameraKey(double Time, VideoCameraPose Pose);

public sealed class VideoCameraTrack
{
    private readonly Lazy<VideoCameraSmoothing> smoothed;
    public VideoFrames Video { get; }
    public IReadOnlyList<VideoCameraKey> Keys { get; }
    public VideoCameraTrack(VideoFrames video, IEnumerable<VideoCameraKey> keys)
    {
        Video = video;
        var ordered = keys.OrderBy(k => k.Time).ToArray();
        if (ordered.Any(k => !double.IsFinite(k.Time) || k.Time < 0) || ordered.Select(k => k.Time).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("Camera key times must be finite, nonnegative and unique.");
        Keys = Array.AsReadOnly(ordered);
        smoothed = new Lazy<VideoCameraSmoothing>(() => new VideoCameraSmoothing(Keys));
    }

    public VideoCameraPose? Sample(double time, bool smooth)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (smooth) return smoothed.Value.Sample(time);
        if (Keys.Count == 0) return null;
        if (time <= Keys[0].Time) return Keys[0].Pose;
        if (time >= Keys[^1].Time) return Keys[^1].Pose;
        var low = 0;
        var high = Keys.Count - 1;
        while (high - low > 1) { var mid = (low + high) / 2; if (Keys[mid].Time <= time) low = mid; else high = mid; }
        var a = Keys[low];
        var b = Keys[high];
        var span = b.Time - a.Time;
        var t = (time - a.Time) / span;
        var centre = a.Pose.Centre * (1 - t) + b.Pose.Centre * t;
        return new(centre, CameraRotation.Slerp(a.Pose.CameraToWorld, b.Pose.CameraToWorld, t),
            a.Pose.VerticalFov * (1 - t) + b.Pose.VerticalFov * t, a.Pose.Aspect * (1 - t) + b.Pose.Aspect * t);
    }

    public string FrameAt(double time)
    {
        if (Video.Frames.Count == 0) return string.Empty;
        var low = 0; var high = Video.Frames.Count - 1;
        while (low < high) { var mid = (low + high + 1) / 2; if (Video.Frames[mid].Time <= time) low = mid; else high = mid - 1; }
        return Video.Frames[low].Path;
    }
}

public static class VideoCameraReader
{
    // COLMAP stores world-to-camera rotation and translation; playback needs the inverse.
    public static IReadOnlyDictionary<string, VideoCameraPose> Read(string model)
    {
        var cameras = new Dictionary<string, (double Fov, double Aspect)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(model, "cameras.txt")))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 7) throw new InvalidDataException("Malformed cameras.txt camera.");
            var width = Number(f[2]); var height = Number(f[3]);
            var simple = f[1] is "SIMPLE_PINHOLE" or "SIMPLE_RADIAL" or "RADIAL" or "SIMPLE_RADIAL_FISHEYE" or "RADIAL_FISHEYE";
            var dual = f[1] is "PINHOLE" or "OPENCV" or "OPENCV_FISHEYE" or "FULL_OPENCV" or "FOV" or "THIN_PRISM_FISHEYE" or "RAD_TAN_THIN_PRISM_FISHEYE";
            if (!simple && !dual) throw new InvalidDataException($"Unsupported camera model: {f[1]}.");
            var fy = Number(f[simple ? 4 : 5]);
            if (width <= 0 || height <= 0 || fy <= 0) throw new InvalidDataException("Invalid camera dimensions or focal length.");
            cameras.Add(f[0], (2 * Math.Atan(height / (2 * fy)), width / height));
        }
        var result = new Dictionary<string, VideoCameraPose>(StringComparer.Ordinal);
        foreach (var f in ActiveDataset.ReadImagePoses(Path.Combine(model, "images.txt")))
        {
            var worldToCamera = new CameraRotation(Number(f[1]), Number(f[2]), Number(f[3]), Number(f[4])).Normalized();
            var inverse = new CameraRotation(worldToCamera.W, -worldToCamera.X, -worldToCamera.Y, -worldToCamera.Z);
            var centre = inverse.Rotate(new(-Number(f[5]), -Number(f[6]), -Number(f[7])));
            if (!cameras.TryGetValue(f[8], out var camera)) throw new InvalidDataException($"Camera {f[8]} is missing from cameras.txt.");
            result.Add(f[9].Replace('\\', '/'), new(centre, inverse, camera.Fov, camera.Aspect));
        }
        return result;
    }

    public static VideoCameraTrack Track(VideoFrames video, string imagesRoot, IReadOnlyDictionary<string, VideoCameraPose> poses)
    {
        var keys = new List<VideoCameraKey>();
        foreach (var frame in video.Frames)
            if (poses.TryGetValue(Path.GetRelativePath(imagesRoot, frame.Path).Replace('\\', '/'), out var pose))
                keys.Add(new(frame.Time, pose));
        return new VideoCameraTrack(video, keys);
    }

    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
        ? n : throw new InvalidDataException("Invalid number in COLMAP camera model.");
}

// Real elapsed time, independent of Grasshopper's solution frequency.
public static class VideoPlaybackTime
{
    public static double Advance(double position, double elapsed, double speed, double duration, bool loop)
    {
        if (!double.IsFinite(position) || !double.IsFinite(elapsed) || !double.IsFinite(speed) || speed <= 0 || !double.IsFinite(duration) || duration < 0)
            throw new ArgumentOutOfRangeException(nameof(speed));
        if (duration == 0) return 0;
        var value = Math.Max(0, position + Math.Max(0, elapsed) * speed);
        return loop ? value % duration : Math.Min(value, duration);
    }
}
