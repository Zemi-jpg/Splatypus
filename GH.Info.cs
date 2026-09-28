using System;
using System.Drawing;
using Grasshopper.Kernel;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class RhinoSplatInfo : GH_AssemblyInfo
{
    public override string Name => "Splatypus";
    public override Bitmap? Icon => ComponentIcons.Get("hello-splat");
    public override string Description => "COLMAP and Gaussian-splat workflow tools for Rhino and Grasshopper.";
    public override Guid Id => new("7DCA094A-626A-4B5B-AE1C-A626641FCF3C");
    public override string AuthorName => "Basar";
    public override string AuthorContact => string.Empty;
}

public sealed class SplatypusAssemblyPriority : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        global::Grasshopper.Instances.ComponentServer.AddCategoryIcon("Splatypus", ComponentIcons.Get("hello-splat"));
        global::Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Splatypus", 'S');
        return GH_LoadingInstruction.Proceed;
    }
}
