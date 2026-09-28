using System.Runtime.InteropServices;

namespace RhinoSplat.Core;

public sealed record ToolDownloadRequest(string Folder, bool ColmapCuda = true, bool ShowTerminal = false);
public sealed record ToolDownloadResult(string FFmpeg, string Colmap, string Brush, string Versions, string Status, bool Cancelled = false, string LogFile = "")
{
    public bool Ready => FFmpeg.Length > 0 && Colmap.Length > 0 && Brush.Length > 0;
}

internal sealed record ToolPackage(string Name, string Version, string Id, string Url, string Sha256,
    string Executable, string Kind = "zip", bool Cpu = false);

internal static class ToolDownloadCatalog
{
    internal static ToolPackage[] Get(bool windows, Architecture architecture, bool cuda)
    {
        if (windows && architecture == Architecture.X64)
            return new[]
            {
                new ToolPackage("FFmpeg", "8.0", "ffmpeg-8.0-win-x64",
                    "https://github.com/GyanD/codexffmpeg/releases/download/8.0/ffmpeg-8.0-essentials_build.zip",
                    "647e467caf82b9fa200a562769b5ff4d736aaf725804ed2c64ea9752106fa569", "ffmpeg-8.0-essentials_build/bin/ffmpeg.exe"),
                new ToolPackage("COLMAP", "3.11.1", "colmap-3.11.1-win-x64-" + (cuda ? "cuda" : "cpu"),
                    "https://github.com/colmap/colmap/releases/download/3.11.1/colmap-x64-windows-" + (cuda ? "cuda" : "nocuda") + ".zip",
                    cuda ? "0a7789bb6d586d23649eecbcbdb650473eae3d19bf7359234911bc1722be3de1" : "ca563281992d423c0dd1329546dd7143689d28490a21aff8780e4d5107646955",
                    "bin/colmap.exe", Cpu: !cuda),
                new ToolPackage("Brush", "0.3.0", "brush-0.3.0-win-x64",
                    "https://github.com/ArthurBrussee/brush/releases/download/v0.3.0/brush-app-x86_64-pc-windows-msvc.zip",
                    "b68e3e9cf052d51bf3ee30776fa5a364de7f2ba13b58443128ff797bb7bcfcd6", "brush_app.exe")
            };
        if (!windows && architecture == Architecture.Arm64)
            return new[]
            {
                new ToolPackage("FFmpeg", "7.1.1", "ffmpeg-7.1.1-osx-arm64", "", "", "bin/ffmpeg", "conda"),
                new ToolPackage("COLMAP", "3.11.1", "colmap-3.11.1-osx-arm64", "", "", "bin/colmap", "conda", true),
                new ToolPackage("Brush", "0.3.0", "brush-0.3.0-osx-arm64",
                    "https://github.com/ArthurBrussee/brush/releases/download/v0.3.0/brush-app-aarch64-apple-darwin.tar.xz",
                    "65b2631398c839be3c1d4d7160fe2326389dec87830aac0710985e6690a1048c", "brush_app", "tar")
            };
        throw new PlatformNotSupportedException("Automatic installation supports Windows x64 and Apple Silicon macOS. Intel Macs have no published Brush 0.3.0 binary; install native tools manually and connect their paths to the existing components.");
    }
}
