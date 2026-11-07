namespace RhinoSplat.Core;

public static class BrushLogPreview
{
    public static string Create(string log, string exportPath)
    {
        if (string.IsNullOrEmpty(log)) return string.Empty;
        const int maximumCharacters = 6000, maximumLines = 50;
        var tail = log.Length > maximumCharacters ? log[^maximumCharacters..] : log;
        var lines = tail.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        var truncated = log.Length > maximumCharacters || lines.Length > maximumLines;
        if (lines.Length > maximumLines) lines = lines[^maximumLines..];
        var preview = string.Join(Environment.NewLine, lines);
        return truncated ? $"Recent Brush output. Full log: {Path.Combine(exportPath, "brush-training.log")}{Environment.NewLine}{preview}" : preview;
    }
}
