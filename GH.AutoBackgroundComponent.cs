using System.Drawing;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class AutoBackgroundComponent : SpComponent
{
    private (string Path, long Length, long Modified, int Maximum)? _cacheKey;
    private BackgroundSplatData? _data;
    private static AutoBackgroundComponent? _previewOwner;
    private RhinoView? _previewView;
    private RhinoDoc? _previewDocument;
    private DisplayModeDescription? _originalMode;
    private Guid _previewMode;
    private Guid _previewViewId;
    private bool _stopRequested;

    public AutoBackgroundComponent() : base("SP Auto Background", "Auto Background",
        "Estimates a matching solid or gradient backdrop from baked splat PLY colors near camera boundaries. Connect colors to SP View Capture; optional live preview changes only the backdrop.", "Splatypus", "06 Preview") { }
    protected override Bitmap Icon => ComponentIcons.Get("auto-background");
    public override Guid ComponentGuid => new("03B0AA07-49C7-49C9-8B08-BEF1A9A14EA5");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Splat PLY", "PLY", "Gaussian-splat PLY with f_dc colors, or a colored RGB PLY. Use the same coordinates as the viewport scene (for example the _rgs.ply copy). Other formats must be exported to PLY.", GH_ParamAccess.item);
        p.AddPlaneParameter("Frames", "Frames", "Optional camera Frames from SP Camera Planes. Fits one stable backdrop across the batch (up to 64 evenly spaced frames). Without Frames, samples the active viewport when this component solves.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddNumberParameter("Lens", "Lens", "35 mm equivalent lens for Frames. Match SP View Capture. Default 35 mm; ignored when using the active viewport.", GH_ParamAccess.item, 35);
        p.AddNumberParameter("Aspect", "Aspect", "Capture width / height for Frames. Default 1.5 (1920 / 1280). Ignored when using the active viewport.", GH_ParamAccess.item, 1.5);
        p.AddTransformParameter("Transform", "XForm", "Optional transform matching any move/rotate/scale applied to the imported splat object. Stored PLY coordinates are otherwise used unchanged.", GH_ParamAccess.item);
        p[4].Optional = true;
        p.AddIntegerParameter("Maximum Samples", "Max", "100–500,000 sampled PLY vertices. Default 50,000. File samples are cached until the path, size, modification time or sample limit changes.", GH_ParamAccess.item, 50000);
        p.AddBooleanParameter("Preview", "Preview", "Preview the gradient in the active viewport. False, hiding/removing/locking this component, or changing the active GH document restores the normal backdrop. Connect the colors explicitly for capture.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddColourParameter("Background", "BG", "Suggested solid backdrop. Connect only this to SP View Capture Background for a solid fill.", GH_ParamAccess.item);
        p.AddColourParameter("Top", "Top", "Suggested gradient top. Connect to SP View Capture Background Top.", GH_ParamAccess.item);
        p.AddColourParameter("Bottom", "Bottom", "Suggested gradient bottom. Connect to SP View Capture Background together with Top.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Sampling, fallback and alignment guidance. This is a color estimate, not reconstruction of missing scenery.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (da.Iteration > 0) return;
        StopPreview();
        try
        {
            var path = ""; var frames = new List<Plane>(); var lens = 35.0; var aspect = 1.5;
            var transform = Transform.Identity; var maximum = 50000; var preview = false;
            if (!da.GetData(0, ref path)) return;
            da.GetDataList(1, frames); da.GetData(2, ref lens); da.GetData(3, ref aspect);
            da.GetData(4, ref transform); da.GetData(5, ref maximum); da.GetData(6, ref preview);
            if (!transform.IsValid) throw new ArgumentException("Transform must be valid.");
            if (frames.Any(f => !f.IsValid)) throw new ArgumentException("Frames must contain valid camera planes.");
            if (!double.IsFinite(lens) || lens < 1 || lens > 1000 || !double.IsFinite(aspect) || aspect < 0.05 || aspect > 20)
                throw new ArgumentException("Lens must be 1–1000 mm; Aspect must be 0.05–20.");
            var file = new FileInfo(Path.GetFullPath(path));
            var key = (file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, maximum);
            if (_cacheKey != key || _data is null) { _data = SplatBackground.Read(file.FullName, maximum); _cacheKey = key; }
            var view = RhinoDoc.ActiveDoc?.Views.ActiveView;
            var cameras = new List<(Plane Frame, double HalfWidth, double HalfHeight, bool Parallel)>();
            if (frames.Count > 0)
            {
                // Use the same Rhino projection setup as capture, rather than
                // assuming which film-gate dimension Rhino fixes for this aspect.
                using var projection = view is null ? new ViewportInfo() : new ViewportInfo(view.ActiveViewport);
                projection.ChangeToPerspectiveProjection(RhinoMath.UnsetValue, true, lens);
                projection.FrustumAspect = aspect; projection.Camera35mmLensLength = lens;
                if (!projection.GetFrustum(out var left, out var right, out var lower, out var upper, out var near, out _))
                    throw new InvalidOperationException("Could not create a capture projection.");
                var halfWidth = (right - left) / (2 * near);
                var halfHeight = (upper - lower) / (2 * near);
                var take = Math.Min(64, frames.Count);
                for (var i = 0; i < take; i++) cameras.Add((frames[(int)((long)i * frames.Count / take)], halfWidth, halfHeight, false));
            }
            else
            {
                var viewport = view?.ActiveViewport ?? throw new InvalidOperationException("Supply Frames or activate a Rhino viewport.");
                if (!viewport.GetCameraFrame(out var frame) || !viewport.GetFrustum(out var left, out var right, out var bottom, out var top, out var near, out _))
                    throw new InvalidOperationException("Could not read the active camera.");
                if (Math.Abs(left + right) > 1e-6 * Math.Max(1, right - left) || Math.Abs(bottom + top) > 1e-6 * Math.Max(1, top - bottom))
                    throw new ArgumentException("Use centered camera Frames for a shifted/two-point perspective viewport.");
                var parallel = viewport.IsParallelProjection;
                cameras.Add((frame, (right - left) / 2 / (parallel ? 1 : near), (top - bottom) / 2 / (parallel ? 1 : near), parallel));
            }
            var colors = Estimate(_data.Samples, transform, cameras, out var fallback);
            da.SetData(0, colors.Solid); da.SetData(1, colors.Top); da.SetData(2, colors.Bottom);
            if (preview && view is not null && !ViewportCaptureSession.IsCapturing)
            {
                _previewOwner?.StopPreview();
                StartPreview(view, colors.Top, colors.Bottom);
                _previewOwner = this;
            }
            Message = "Estimated";
            da.SetData(3, $"Estimated from {colors.Count:N0} boundary-color samples across {cameras.Count} camera(s), sampling {_data.Samples.Length:N0} of {_data.Total:N0} PLY vertices. "
                + (fallback ? "Sparse boundary coverage: used visible interior colors for some views. " : "")
                + "Connect Bottom → Background and Top → Background Top for a gradient, or BG → Background for a solid. Keep splat relighting off. Missing sky/scenery is not reconstructed.");
        }
        catch (Exception ex)
        {
            Message = "Check inputs";
            da.SetData(3, "Background estimate failed: " + ex.Message);
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
        }
    }

    internal static SplatBackgroundColors Estimate(BackgroundSplat[] samples, Transform transform,
        IReadOnlyList<(Plane Frame, double HalfWidth, double HalfHeight, bool Parallel)> cameras, out bool fallback)
    {
        var result = new List<BackgroundColorSample>(); fallback = false;
        foreach (var camera in cameras)
        {
            var visible = new List<BackgroundColorSample>(); var boundary = new List<BackgroundColorSample>();
            foreach (var s in samples)
            {
                var point = new Point3d(s.X, s.Y, s.Z); point.Transform(transform);
                var delta = point - camera.Frame.Origin;
                var depth = -(delta * camera.Frame.ZAxis);
                if (depth <= 1e-8) continue;
                var x = delta * camera.Frame.XAxis / camera.HalfWidth / (camera.Parallel ? 1 : depth);
                var y = delta * camera.Frame.YAxis / camera.HalfHeight / (camera.Parallel ? 1 : depth);
                if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1 || Math.Abs(y) > 1) continue;
                var color = new BackgroundColorSample(s.Color, y, Math.Sqrt(s.Opacity));
                visible.Add(color);
                if (Math.Max(Math.Abs(x), Math.Abs(y)) >= 0.7) boundary.Add(color);
            }
            var chosen = boundary.Count >= 16 ? boundary : visible;
            if (boundary.Count < 16) fallback = true;
            // Equal total influence per camera avoids a densely sampled view dominating a batch.
            var take = Math.Min(chosen.Count, 4096);
            for (var i = 0; i < take; i++)
            {
                var s = chosen[(int)((i + 0.5) * chosen.Count / take)];
                result.Add(s with { Weight = s.Weight / take });
            }
        }
        return SplatBackground.Estimate(result);
    }

    private void StartPreview(RhinoView view, Color top, Color bottom)
    {
        var original = view.ActiveViewport.DisplayMode;
        if (original.Id == DisplayModeDescription.RaytracedId || original.DisplayAttributes.RealtimeDisplayId != Guid.Empty)
            throw new ArgumentException("Use Rendered or another raster display mode for background preview.");
        _previewView = view; _previewViewId=view.ActiveViewportID; _previewDocument = RhinoDoc.ActiveDoc; _originalMode = original;
        _previewMode = DisplayModeDescription.CopyDisplayMode(original.Id,"SP Background " + Guid.NewGuid().ToString("N"));
        if (_previewMode == Guid.Empty) throw new InvalidOperationException("Rhino could not create a background preview mode.");
        try
        {
            var mode=DisplayModeDescription.GetDisplayMode(_previewMode);
            mode.InMenu=false; mode.AllowObjectAssignment=false;
            mode.DisplayAttributes.SetFill(top,bottom);
            if(!DisplayModeDescription.UpdateDisplayMode(mode)) throw new InvalidOperationException("Rhino could not set the preview backdrop.");
            view.ActiveViewport.DisplayMode=DisplayModeDescription.GetDisplayMode(_previewMode);
            _stopRequested=false; RhinoApp.Idle+=CheckPreview;
            view.Redraw();
        }
        catch { StopPreview(); throw; }
    }

    private void CheckPreview(object? sender, EventArgs e)
    {
        if (ViewportCaptureSession.IsCapturing) return;
        if (_stopRequested || Locked || Hidden || _previewDocument?.RuntimeSerialNumber != RhinoDoc.ActiveDoc?.RuntimeSerialNumber
            || OnPingDocument() is not { Enabled: true } document || !GH_Document.EnableSolutions
            || !ReferenceEquals(global::Grasshopper.Instances.ActiveCanvas?.Document, document)
            || _previewDocument?.Views.Find(_previewViewId) is null
            || _previewView?.ActiveViewport.DisplayMode.Id != _previewMode) StopPreview();
    }

    private void StopPreview()
    {
        if (_previewMode == Guid.Empty) return;
        // Capture remembers this mode to restore afterwards. Keep it alive until
        // that session finishes, even if the component is removed in the meantime.
        if (ViewportCaptureSession.IsCapturing) { _stopRequested=true; return; }
        RhinoApp.Idle-=CheckPreview;
        if(_previewView is not null && _previewDocument?.Views.Find(_previewViewId) is not null
            && _previewView.ActiveViewport.DisplayMode.Id == _previewMode)
        {
            _previewView.ActiveViewport.DisplayMode=_originalMode;
            _previewView.Redraw();
        }
        var id=_previewMode; _previewMode=Guid.Empty; _previewView=null; _previewDocument=null; _originalMode=null;
        if(!DisplayModeDescription.DeleteDisplayMode(id)) RhinoApp.WriteLine("SP Auto Background: temporary mode cleanup will be retried at startup.");
        if (ReferenceEquals(_previewOwner, this)) _previewOwner = null;
    }
    internal static void CleanupAbandonedModes()
    {
        if (_previewOwner is not null || ViewportCaptureSession.IsCapturing) return;
        var abandoned=DisplayModeDescription.GetDisplayModes().Where(m=>m.EnglishName.StartsWith("SP Background ",StringComparison.Ordinal)
            && Guid.TryParseExact(m.EnglishName[14..],"N",out _)).ToArray();
        var ids=abandoned.Select(m=>m.Id).ToHashSet();
        foreach(var doc in RhinoDoc.OpenDocuments()) foreach(var view in doc.Views.GetViewList(ViewTypeFilter.Model))
            if(ids.Contains(view.ActiveViewport.DisplayMode.Id))
            { view.ActiveViewport.DisplayMode=DisplayModeDescription.GetDisplayMode(DisplayModeDescription.RenderedId); view.Redraw(); }
        foreach(var mode in abandoned) DisplayModeDescription.DeleteDisplayMode(mode.Id);
    }
    public override void RemovedFromDocument(GH_Document document) { StopPreview(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) StopPreview();
        base.DocumentContextChanged(document, context);
    }
}
