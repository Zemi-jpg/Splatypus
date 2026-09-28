using RhinoSplat.Core;
var root = Path.Combine(Path.GetTempPath(), "RhinoSplat-platform-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
Check(!ToolValidation.Colmap("").IsValid, "Empty executable rejected");
Check(!ToolValidation.Colmap(Path.Combine(root, "missing")).IsValid, "Missing executable rejected");
Check(!ToolValidation.Colmap("bad\0path").IsValid, "Invalid executable path returns diagnostic");
var path = Path.Combine(root, OperatingSystem.IsWindows() ? "tool with spaces.exe" : "tool with spaces");
File.WriteAllText(path, "fixture");
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Check(!ToolValidation.Colmap(path).IsValid, "Unix non-executable rejected");
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
Check(ToolValidation.Colmap(path).IsValid, "Platform executable accepted with spaces");
Check(FFmpegToolValidation.Validate(path).IsValid, "FFmpeg uses platform executable rules");
if (OperatingSystem.IsWindows())
{
    var extensionless = Path.Combine(root, "colmap"); File.WriteAllText(extensionless, "fixture");
    Check(!ToolValidation.Colmap(extensionless).IsValid, "Windows extension requirement retained");
}
Console.WriteLine("Platform checks passed on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
