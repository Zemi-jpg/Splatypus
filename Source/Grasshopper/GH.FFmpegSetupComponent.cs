using System;
using System.Linq;
using Grasshopper.Kernel;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class FFmpegSetupComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("ffmpeg-setup");

    public FFmpegSetupComponent()
        : base("SP FFmpeg Setup", "FFmpeg", "Validates FFmpeg. FFmpeg automatically selects its built-in decoder for each imported image or video format.", "Splatypus", "02 dataset") { }

    protected override void RegisterInputParams(GH_InputParamManager p) =>
        p.AddTextParameter("FFmpeg Executable", "Exe", "Full path to the FFmpeg executable.", GH_ParamAccess.item);

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("Valid", "Valid", "Whether FFmpeg can be used.", GH_ParamAccess.item);
        p.AddTextParameter("FFmpeg", "FFmpeg", "Validated full path to the FFmpeg executable.", GH_ParamAccess.item);
        p.AddTextParameter("Decoder Command", "Decoders", "Optional command for inspecting FFmpeg's available decoders.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Validation status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var executable = string.Empty;
        if (!da.GetData(0, ref executable)) return;
        var validation = FFmpegToolValidation.Validate(executable);
        da.SetData(0, validation.IsValid);
        da.SetData(1, validation.ExecutablePath);
        if (validation.IsValid)
            da.SetData(2, Quote(validation.ExecutablePath) + " -hide_banner -decoders");
        da.SetData(3, validation.Message);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("E430CF4F-6D5F-4C84-806A-7EC1495A2C30");

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
