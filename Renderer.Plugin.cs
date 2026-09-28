using System;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

#nullable enable

[assembly: PlugInDescription(DescriptionType.Organization, "RhinoSplat")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: Guid("2C5661BA-E0C7-49AA-B6B9-81FCD3EBC19E")]

namespace RhinoSplat.Renderer;

/// <summary>
/// Installable Rhino renderer host. Grasshopper will address this plug-in by
/// Id rather than owning viewport state itself.
/// </summary>
public sealed class RendererPlugIn : PlugIn
{
    public static readonly Guid PlugInId = new("2C5661BA-E0C7-49AA-B6B9-81FCD3EBC19E");

    public RendererPlugIn()
    {
        Instance = this;
    }

    public static RendererPlugIn? Instance { get; private set; }

    private readonly SplatRendererService _renderer = new();

    public void ShowPreview(Rhino.Geometry.PointCloud cloud, int pointSize) => _renderer.Show(cloud, pointSize);

    public void HidePreview() => _renderer.Hide();

    public override PlugInLoadTime LoadTime => PlugInLoadTime.WhenNeeded;
}
