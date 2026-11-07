using Grasshopper.Kernel;
using Rhino;

namespace RhinoSplat.Grasshopper;

public sealed class CaptureStartup : GH_AssemblyPriority
{
    public override GH_LoadingInstruction PriorityLoad()
    {
        RhinoApp.Idle += Cleanup;
        return GH_LoadingInstruction.Proceed;
    }

    private static void Cleanup(object? sender, EventArgs e)
    {
        RhinoApp.Idle -= Cleanup;
        try { ViewportCaptureSession.CleanupAbandonedModes(); AutoBackgroundComponent.CleanupAbandonedModes(); }
        catch (Exception ex) { RhinoApp.WriteLine("Splatypus display-mode cleanup: " + ex.Message); }
    }
}
