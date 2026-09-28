namespace RhinoSplat.Core;

public static class FFmpegLogPreview
{
    public const int MaximumCharacters = 6000;
    public const int MaximumLines = 50;

    public static string Create(string log, string imagesPath)
    {
        if (string.IsNullOrEmpty(log)) return string.Empty;
        // Bound work as well as output size: never split/format an entire large log on the UI thread.
        var tail = log.Length > MaximumCharacters ? log[^MaximumCharacters..] : log;
        var lines = tail.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        var truncated = log.Length > MaximumCharacters || lines.Length > MaximumLines;
        if (lines.Length > MaximumLines) lines = lines[^MaximumLines..];
        if (!truncated) return string.Join(Environment.NewLine, lines);
        var logPath = string.IsNullOrWhiteSpace(imagesPath) ? "ffmpeg-import.log in the project folder"
            : Path.Combine(Path.GetDirectoryName(imagesPath) ?? imagesPath, "ffmpeg-import.log");
        return $"Recent FFmpeg output (last {MaximumLines} lines, up to {MaximumCharacters:N0} characters)." +
            $"{Environment.NewLine}Full log: {logPath}{Environment.NewLine}{Environment.NewLine}" +
            string.Join(Environment.NewLine, lines);
    }
}
