using System;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

#nullable enable

namespace RhinoSplat.Renderer;

/// <summary>
/// Stable control surface for the Grasshopper adapter. The first renderer
/// implementation can be replaced by a native GPU backend without changing
/// the Grasshopper contract.
/// </summary>
public sealed class SplatRendererService
{
    private PointCloud? _preview;
    private readonly PreviewConduit _conduit = new();

    public void SetPreview(PointCloud preview)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
    }

    public int PreviewCount => _preview?.Count ?? 0;

    public void Show(PointCloud preview, int pointSize)
    {
        SetPreview(preview);
        _conduit.Cloud = _preview;
        _conduit.PointSize = pointSize;
        _conduit.Enabled = true;
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    public void Hide()
    {
        _preview = null;
        _conduit.Cloud = null;
        _conduit.Enabled = false;
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    public void Clear() => Hide();

    private sealed class PreviewConduit : DisplayConduit
    {
        public PointCloud? Cloud { get; set; }
        public int PointSize { get; set; } = 2;

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (Cloud is { Count: > 0 })
                e.Display.DrawPointCloud(Cloud, PointSize);
        }
    }
}
