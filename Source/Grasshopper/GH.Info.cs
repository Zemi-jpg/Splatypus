using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class RhinoSplatInfo : GH_AssemblyInfo
{
    public override string Name => "Splatypus";
    public override Bitmap? Icon => ComponentIcons.Get("hello-splat");
    public override string Description => "COLMAP and Gaussian-splat workflow tools for Rhino and Grasshopper.";
    public override Guid Id => new("7DCA094A-626A-4B5B-AE1C-A626641FCF3C");
    public override string AuthorName => typeof(RhinoSplatInfo).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Zeming Li";
    public override string AuthorContact => typeof(RhinoSplatInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "SupportContact")?.Value ?? "lizeming2021@gmail.com";
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
