using System.Drawing;
using System.Security.Cryptography;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class QuickAlignComponent : SpComponent
{
    private static bool _sessionActive;
    private PointCloud? _cloud;
    private Point3d[] _points = Array.Empty<Point3d>();
    private Point3d[] _source = Array.Empty<Point3d>();
    private Point3d[] _target = Array.Empty<Point3d>();
    private string _cloudKey = string.Empty;
    private string _savedKey = string.Empty;
    private bool _picking;
    private bool _resetToIdentity;

    public QuickAlignComponent() : base("SP Quick Align", "Quick Align",
        "Double-click to refine the current alignment using three sparse/reference point pairs. Reset restores the original cloud and cameras.",
        "Splatypus", "04 Realignment") { }

    protected override Bitmap Icon => ComponentIcons.Get("quick-align");
    public override Guid ComponentGuid => new("21F36137-217A-4279-83E3-E51536A5EF9C");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override void CreateAttributes() => m_attributes = new QuickAlignAttributes(this);

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        // Tree access makes this one interaction per component, even with grafted inputs.
        p.AddGeometryParameter("Sparse Cloud", "Cloud", "One point cloud from SP Sparse Point Cloud. Pick directly from its viewport preview.", GH_ParamAccess.tree);
        p.AddGeometryParameter("Camera Geometry", "Cameras", "Camera planes, centres, or other geometry in the same source coordinates. Branches are preserved.", GH_ParamAccess.tree);
        p[1].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Aligned Cloud", "Cloud", "Transformed copy of the sparse preview cloud.", GH_ParamAccess.item);
        p.AddGeometryParameter("Aligned Camera Geometry", "Cameras", "Camera geometry transformed by the same similarity.", GH_ParamAccess.tree);
        p.AddTransformParameter("Transform", "Xform", "Complete alignment from the original model. Connect to Geometry Translator's Previous input or directly to SP Align COLMAP Model.", GH_ParamAccess.item);
        p.AddPointParameter("Source Points", "Source", "Three picked sparse points in original coordinates, in pair order.", GH_ParamAccess.list);
        p.AddPointParameter("Reference Points", "Reference", "Three Rhino reference points, in pair order.", GH_ParamAccess.list);
        p.AddNumberParameter("RMS Error", "Error", "Root-mean-square distance of aligned source points to reference points, in document units.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Picking instructions and alignment fit.", GH_ParamAccess.item);
    }

    protected override void BeforeSolveInstance()
    {
        _cloud = null; _points = Array.Empty<Point3d>(); _cloudKey = string.Empty;
        base.BeforeSolveInstance();
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (!da.GetDataTree(0, out GH_Structure<IGH_GeometricGoo> input)) return;
        var items = input.AllData(true).ToArray();
        if (items.Length != 1 || items[0] is not GH_PointCloud cloud || cloud.Value is null || cloud.Value.Count < 3)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect one sparse point cloud containing at least three points."); return; }
        _cloud = cloud.Value;
        _points = _cloud.GetPoints();
        if (_points.Any(p => !p.IsValid))
        { _cloud = null; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The sparse cloud contains invalid points."); return; }
        _cloudKey = Fingerprint(_points);
        var transform = Transform.Identity;
        double rms = 0;
        var aligned = _source.Length == 3 && _target.Length == 3 && _savedKey == _cloudKey;
        var reset = _resetToIdentity && _savedKey == _cloudKey;
        var status = "Double-click Quick Align, then pick sparse point 1 / Rhino reference 1, and repeat for pairs 2 and 3. Esc cancels.";
        if ((_source.Length > 0 || _resetToIdentity) && _savedKey != _cloudKey)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The input cloud changed. Pick three new pairs before writing alignment.");
            status = "Input cloud changed; double-click to pick new pairs.";
        }
        if (aligned)
        {
            if (!TryFit(_source, _target, out transform, out rms, out var error))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            status = $"Three-pair alignment. RMS error: {rms:G5} document units. Double-click to adjust from here; Reset restores the original.";
            da.SetDataList(3, _source); da.SetDataList(4, _target); da.SetData(5, rms);
            da.SetData(2, transform);
        }
        else if (reset)
        {
            da.SetData(2, transform);
            status = "Reset to original coordinates. Xform is identity. Double-click to align again.";
        }
        // Before picking, preview the originals, but withhold Xform to prevent an
        // accidental write of identity over an existing aligned dataset.
        da.SetData(0, cloud.DuplicateGeometry().Transform(transform));
        if (da.GetDataTree(1, out GH_Structure<IGH_GeometricGoo> cameras))
            da.SetDataTree(1, TransformTree(cameras, transform));
        da.SetData(6, status);
        Message = aligned ? $"RMS {rms:G3}" : reset ? "Original position" : "Double-click to pick";
    }

    internal Point3d[] PickingPoints()
    {
        var transform = Transform.Identity;
        if (_savedKey == _cloudKey && _source.Length == 3 && _target.Length == 3
            && !TryFit(_source, _target, out transform, out _, out var error))
            throw new InvalidOperationException(error);
        return _points.Select(point => { point.Transform(transform); return point; }).ToArray();
    }

    internal bool AcceptPairs(int[] indices, Point3d[] target, string key, out double rms, out string error)
    {
        rms = 0; error = "The source cloud changed or the selected pairs are invalid. Previous alignment kept.";
        if (key != _cloudKey || indices.Length != 3 || indices.Distinct().Count() != 3
            || indices.Any(index => index < 0 || index >= _points.Length)) return false;
        // Picking happens in the CURRENT alignment, but retain the selected original
        // points. Fitting these to the new targets gives the complete original-to-final
        // similarity, equivalent to composing the new fit after the previous fit.
        var source = indices.Select(index => _points[index]).ToArray();
        if (!TryFit(source, target, out _, out rms, out error)) return false;
        RecordUndoEvent("Refine quick alignment");
        _source = source; _target = target.ToArray(); _savedKey = key; _resetToIdentity = false;
        return true;
    }

    internal void ResetAlignment()
    {
        if (Locked) return;
        RecordUndoEvent("Reset quick alignment");
        _source = _target = Array.Empty<Point3d>();
        _savedKey = _cloudKey;
        _resetToIdentity = !string.IsNullOrEmpty(_cloudKey);
        ExpireSolution(true);
    }

    internal static GH_Structure<IGH_GeometricGoo> TransformTree(GH_Structure<IGH_GeometricGoo> input, Transform transform)
    {
        var output = new GH_Structure<IGH_GeometricGoo>();
        for (var b = 0; b < input.PathCount; b++)
        {
            output.EnsurePath(input.Paths[b]);
            foreach (var item in input.Branches[b])
                output.Append(item is null ? null! : item.DuplicateGeometry().Transform(transform)
                    ?? throw new InvalidOperationException("Could not transform camera geometry."), input.Paths[b]);
        }
        return output;
    }

    internal static bool TryFit(Point3d[] source, Point3d[] target, out Transform transform, out double rms, out string error)
    {
        transform = Transform.Identity;
        if (!ThreePointAlignment.TryFit(source.Select(p => new AlignmentPoint(p.X, p.Y, p.Z)).ToArray(),
            target.Select(p => new AlignmentPoint(p.X, p.Y, p.Z)).ToArray(), out var fit, out rms, out error)) return false;
        transform.M00 = fit.Scale * fit.R00; transform.M01 = fit.Scale * fit.R01; transform.M02 = fit.Scale * fit.R02;
        transform.M10 = fit.Scale * fit.R10; transform.M11 = fit.Scale * fit.R11; transform.M12 = fit.Scale * fit.R12;
        transform.M20 = fit.Scale * fit.R20; transform.M21 = fit.Scale * fit.R21; transform.M22 = fit.Scale * fit.R22;
        transform.M03 = fit.Tx; transform.M13 = fit.Ty; transform.M23 = fit.Tz;
        return transform.IsValid;
    }

    internal void PickPairs()
    {
        var document = OnPingDocument();
        var rhino = RhinoDoc.ActiveDoc;
        if (_sessionActive || Locked || _cloud is null || document is null || rhino is null
            || !document.Enabled || !GH_Document.EnableSolutions) return;
        var key = _cloudKey;
        var points = PickingPoints();
        var indices = new List<int>();
        using var snapshot = (PointCloud)_cloud.Duplicate();
        // Preserve colours and point order while displaying the last accepted fit.
        for (var i = 0; i < points.Length; i++) snapshot[i].Location = points[i];
        var preview = new QuickAlignPreview(snapshot);
        _sessionActive = _picking = true;
        try
        {
            global::Grasshopper.Instances.DocumentEditorFadeOut();
            RhinoApp.SetFocusToMainWindow();
            preview.Enabled = true;
            rhino.Views.Redraw();
            for (var pair = 0; pair < 3; pair++)
            {
                while (true)
                {
                    using var picker = new SparsePreviewPicker(points);
                    picker.SetCommandPrompt($"Quick Align: pick sparse preview point {pair + 1} of 3 (Esc cancels)");
                    var reset = pair == 0 ? picker.AddOption("Reset") : -1;
                    var result = picker.Get(true);
                    if (result == GetResult.Option && picker.OptionIndex() == reset)
                    {
                        ResetAlignment(); return;
                    }
                    if (result != GetResult.Point) return;
                    if (picker.SelectedIndex < 0)
                    { RhinoApp.WriteLine("Click a highlighted sparse point in the viewport."); continue; }
                    var point = points[picker.SelectedIndex];
                    if (preview.Source.Contains(point))
                    { RhinoApp.WriteLine("Choose a different sparse point for each pair."); continue; }
                    preview.Source.Add(point);
                    indices.Add(picker.SelectedIndex);
                    break;
                }
                rhino.Views.Redraw();
                using var reference = new GetPoint();
                reference.SetCommandPrompt($"Quick Align: snap to Rhino reference point {pair + 1} of 3 (Esc cancels)");
                if (reference.Get() != GetResult.Point) return;
                preview.Target.Add(reference.Point());
                rhino.Views.Redraw();
            }
            if (OnPingDocument() != document || RhinoDoc.ActiveDoc != rhino || _cloudKey != key || Locked) return;
            if (!AcceptPairs(indices.ToArray(), preview.Target.ToArray(), key, out var rms, out var error))
            { RhinoApp.WriteLine("Quick Align: " + error + " Previous alignment kept."); return; }
            RhinoApp.WriteLine($"Quick Align: three pairs accepted; RMS error {rms:G5} document units.");
            ExpireSolution(true);
        }
        catch (Exception error)
        { RhinoApp.WriteLine("Quick Align: " + error.Message); }
        finally
        {
            preview.Enabled = false;
            _picking = _sessionActive = false;
            global::Grasshopper.Instances.DocumentEditorFadeIn();
            rhino.Views.Redraw();
        }
    }

    public override void DrawViewportWires(IGH_PreviewArgs args) { if (!_picking) base.DrawViewportWires(args); }
    public override void DrawViewportMeshes(IGH_PreviewArgs args) { if (!_picking) base.DrawViewportMeshes(args); }

    internal static string Fingerprint(IEnumerable<Point3d> points)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[24];
        foreach (var point in points)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer, point.X);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer[8..], point.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(buffer[16..], point.Z);
            hash.AppendData(buffer);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetString("QuickAlignCloud", _savedKey);
        writer.SetBoolean("QuickAlignReset", _resetToIdentity);
        if (_source.Length == 3 && _target.Length == 3)
            for (var i = 0; i < 3; i++)
            {
                var chunk = writer.CreateChunk("QuickAlignPair", i);
                chunk.SetDouble("SX", _source[i].X); chunk.SetDouble("SY", _source[i].Y); chunk.SetDouble("SZ", _source[i].Z);
                chunk.SetDouble("TX", _target[i].X); chunk.SetDouble("TY", _target[i].Y); chunk.SetDouble("TZ", _target[i].Z);
            }
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _source = _target = Array.Empty<Point3d>();
        _resetToIdentity = reader.ItemExists("QuickAlignReset") && reader.GetBoolean("QuickAlignReset");
        _savedKey = reader.ItemExists("QuickAlignCloud") ? reader.GetString("QuickAlignCloud") : string.Empty;
        var source = new List<Point3d>(); var target = new List<Point3d>();
        for (var i = 0; i < 3; i++)
        {
            var chunk = reader.FindChunk("QuickAlignPair", i);
            if (chunk is null) break;
            source.Add(new Point3d(chunk.GetDouble("SX"), chunk.GetDouble("SY"), chunk.GetDouble("SZ")));
            target.Add(new Point3d(chunk.GetDouble("TX"), chunk.GetDouble("TY"), chunk.GetDouble("TZ")));
        }
        if (source.Count == 3) { _source = source.ToArray(); _target = target.ToArray(); }
        return base.Read(reader);
    }

    private sealed class QuickAlignAttributes : GH_ComponentAttributes
    {
        private RectangleF _resetBounds;
        public QuickAlignAttributes(QuickAlignComponent owner) : base(owner) { }
        protected override void Layout()
        {
            base.Layout();
            var bounds = Bounds;
            _resetBounds = new RectangleF(bounds.X + 2, bounds.Bottom + 2, bounds.Width - 4, 20);
            bounds.Height += 24;
            Bounds = bounds;
        }
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            using var button = GH_Capsule.CreateTextCapsule(_resetBounds, _resetBounds, GH_Palette.White, "Reset", 2, 0);
            button.Render(graphics, Selected, Owner.Locked, false);
        }
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // Grasshopper exposes a WinForms enum even on Mac. Read the value without
            // adding a Windows Forms dependency to the cross-platform plugin build.
            if (_resetBounds.Contains(e.CanvasLocation)
                && typeof(GH_CanvasMouseEvent).GetProperty("Button")?.GetValue(e)?.ToString() == "Left")
            {
                if (!_sessionActive) ((QuickAlignComponent)Owner).ResetAlignment();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_resetBounds.Contains(e.CanvasLocation)) return GH_ObjectResponse.Handled;
            if (!Bounds.Contains(e.CanvasLocation)) return base.RespondToMouseDoubleClick(sender, e);
            ((QuickAlignComponent)Owner).PickPairs();
            return GH_ObjectResponse.Handled;
        }
    }
}
