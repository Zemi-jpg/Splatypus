using System;
using Grasshopper.Kernel;

namespace RhinoSplat.Grasshopper;

public sealed class HelloSplatComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("hello-splat");

    public HelloSplatComponent()
        : base("SP Hello", "Hello", "Confirms that the SP assembly loaded correctly.", "Splatypus", "01 Setup")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters) =>
        parameters.AddTextParameter("Message", "M", "Text to display.", GH_ParamAccess.item, "Ready");

    protected override void RegisterOutputParams(GH_OutputParamManager parameters) =>
        parameters.AddTextParameter("Status", "S", "Assembly loading status.", GH_ParamAccess.item);

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        var message = "Ready";
        dataAccess.GetData(0, ref message);
        dataAccess.SetData(0, $"SP is loaded: {message}");
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("C049D316-0678-493D-9638-3289EDAA3E90");
}
