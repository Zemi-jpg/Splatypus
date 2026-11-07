using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace RhinoSplat.Grasshopper;

/// <summary>A scoped viewport picker; no baked objects or persistent mouse callbacks.</summary>
internal sealed class SparsePreviewPicker : GetPoint
{
    private readonly Point3d[] _points;
    private int _hover = -1;
    public int SelectedIndex { get; private set; } = -1;

    public SparsePreviewPicker(Point3d[] points)
    {
        _points = points;
        PermitObjectSnap(false);
        PermitConstraintOptions(false);
        AcceptNumber(false, false);
    }

    protected override void OnMouseMove(GetPointMouseEventArgs e)
    {
        _hover = HitTest(e.Viewport, e.WindowPoint, _points);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(GetPointMouseEventArgs e)
    {
        // Get(true) keeps this callback active until mouse-up. Do not trust a stale
        // hover sample, or a typed construction-plane coordinate, as a cloud point.
        SelectedIndex = e.LeftButtonDown ? HitTest(e.Viewport, e.WindowPoint, _points) : -1;
        base.OnMouseDown(e);
    }

    protected override void OnDynamicDraw(GetPointDrawEventArgs e)
    {
        base.OnDynamicDraw(e);
        if (_hover >= 0)
            e.Display.DrawPoint(_points[_hover], PointStyle.Circle, 8, System.Drawing.Color.Gold);
    }

    internal static int HitTest(RhinoViewport viewport, System.Drawing.Point cursor, Point3d[] points)
    {
        using var pick = new PickContext { View = viewport.ParentView, PickStyle = PickStyle.PointPick };
        pick.SetPickTransform(viewport.GetPickTransform(cursor));
        if (!viewport.GetFrustumLine(cursor.X, cursor.Y, out var line)) return -1;
        pick.PickLine = line;
        pick.UpdateClippingPlanes();
        return pick.PickFrustumTest(points, out var index, out _, out _) ? index : -1;
    }
}

internal sealed class QuickAlignPreview : DisplayConduit
{
    private readonly PointCloud _cloud;
    public List<Point3d> Source { get; } = new();
    public List<Point3d> Target { get; } = new();

    public QuickAlignPreview(PointCloud cloud) => _cloud = cloud;

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        e.IncludeBoundingBox(_cloud.GetBoundingBox(true));
        foreach (var p in Target) e.IncludeBoundingBox(new BoundingBox(p, p));
    }

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        e.Display.DrawPointCloud(_cloud, 2, Color.CornflowerBlue);
        DrawMarkers(e, Source, Color.Gold, "S");
        DrawMarkers(e, Target, Color.LimeGreen, "R");
    }

    private static void DrawMarkers(DrawEventArgs e, List<Point3d> points, Color color, string prefix)
    {
        for (var i = 0; i < points.Count; i++)
        {
            e.Display.DrawPoint(points[i], PointStyle.Circle, 7, color);
            var screen = e.Viewport.WorldToClient(points[i]);
            e.Display.Draw2dText(prefix + (i + 1), color, new Point2d(screen.X + 10, screen.Y - 10), false, 16);
        }
    }
}
