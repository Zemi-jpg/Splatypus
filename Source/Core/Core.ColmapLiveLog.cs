using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

#nullable enable

namespace RhinoSplat.Core;

internal static class ColmapLiveLog
{
    private static readonly object WriteLock = new();
    private static readonly System.Threading.AsyncLocal<string?> CombinedLog = new();

    public static IDisposable MirrorTo(string logPath)
    {
        var scope = new LogScope(CombinedLog.Value);
        CombinedLog.Value = logPath;
        return scope;
    }
    private sealed class LogScope(string? previous) : IDisposable
    {
        public void Dispose() => CombinedLog.Value = previous;
    }
    public static void AppendLine(string logPath, string line)
    {
        lock (WriteLock)
        {
            File.AppendAllText(logPath, line + Environment.NewLine);
            var combined = CombinedLog.Value;
            if (combined is not null && !string.Equals(combined, logPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                File.AppendAllText(combined, line + Environment.NewLine);
        }
    }

    public static void Create(string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, $"Splatypus started {DateTime.Now:O}{Environment.NewLine}");
    }

    public static void OpenTerminal(string logPath, string title)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // Quote independently for the shell and AppleScript; never interpolate raw paths.
                var shellPath = "'" + Path.GetFullPath(logPath).Replace("'", "'\"'\"'") + "'";
                var shellCommand = "tail -n 50 -f " + shellPath;
                var script = "tell application \"Terminal\" to do script \"" + shellCommand.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("-e");
                info.ArgumentList.Add(script);
                Process.Start(info)?.Dispose();
                return;
            }
            if (!OperatingSystem.IsWindows()) return;
            var escapedPath = logPath.Replace("'", "''");
            var escapedTitle = title.Replace("'", "''");
            var command = $"$Host.UI.RawUI.WindowTitle = '{escapedTitle}'; Get-Content -LiteralPath '{escapedPath}' -Wait";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -Command \"" + command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = true,
                CreateNoWindow = false
            });
        }
        catch
        {
            // A terminal is optional; processing continues if the OS cannot show it.
        }
    }

    public static async Task<string> ReadAndAppendAsync(StreamReader reader, string logPath)
    {
        var text = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            text.AppendLine(line);
            AppendLine(logPath, line);
        }

        return text.ToString();
    }
}
