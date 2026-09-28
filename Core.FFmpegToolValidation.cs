namespace RhinoSplat.Core;
public static class FFmpegToolValidation
{
    public static ToolValidationResult Validate(string executablePath) => ToolValidation.Validate(executablePath, "FFmpeg");
}
