using System;
using System.IO;
namespace RhinoSplat.Core;
public sealed record ToolValidationResult(bool IsValid, string ExecutablePath, string Message);
public static class ToolValidation
{
    public static ToolValidationResult Colmap(string executablePath) => Validate(executablePath, "COLMAP");
    public static ToolValidationResult Validate(string executablePath, string tool)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new(false, string.Empty, $"Select the {tool} executable using its full path.");
        try
        {
            var fullPath = Path.GetFullPath(executablePath.Trim());
            if (!File.Exists(fullPath)) return new(false, fullPath, $"{tool} executable was not found.");
            if (OperatingSystem.IsWindows())
            {
                if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                    return new(false, fullPath, $"{tool} must point to an .exe file on Windows.");
            }
            else if ((File.GetUnixFileMode(fullPath) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                return new(false, fullPath, $"{tool} file does not have execute permission.");
            return new(true, fullPath, $"{tool} executable is ready.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(false, string.Empty, $"Invalid {tool} executable: {exception.Message}");
        }
    }
}
