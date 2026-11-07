using System.Drawing;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace RhinoSplat.Grasshopper;

public sealed class ViewCaptureComponent : SpComponent
{
    private ViewportCaptureSession? _session;
    private bool _lastRun, _busy;
    private System.Threading.Timer? _timer;
    private int _generation;
    private string? _pendingStop;
    private string _status = "Connect camera Frames and press Run. Hide camera/guide previews and pause video playback before capturing.";
    private string _dataset = "";
    private string[] _images = Array.Empty<string>();
    private double _progress;

    public ViewCaptureComponent() : base("SP View Capture", "View Capture",
        "Captures the Rhino viewport at camera planes, saving PNG images and known cameras for splat retraining.", "Splatypus", "06 Preview") { }
    protected override Bitmap Icon => ComponentIcons.Get("view-capture");
    public override Guid ComponentGuid => new("811916F3-D3BB-4378-A98B-1DA3C218D5D4");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPlaneParameter("Frames", "Frames", "Connect Frames from SP Camera Planes, or supply camera planes: origin at camera, X right, Y up, looking along negative Z. Flatten multiple branches into one list.", GH_ParamAccess.list);
        p[0].Optional = true;
        p.AddIntegerParameter("Width", "Width", "Maximum capture width. Default 1920. Screen capture fits this aspect ratio inside the current viewport; Status reports the actual pixel size.", GH_ParamAccess.item, 1920);
        p.AddIntegerParameter("Height", "Height", "Maximum capture height. Default 1280 (3:2). Oversized requests are reduced proportionally to fit the visible viewport.", GH_ParamAccess.item, 1280);
        p.AddTextParameter("Display Mode", "Mode", "Installed Rhino display mode name or ID. Default Rendered. Progressive/Raytraced modes are unsupported.", GH_ParamAccess.item, "Rendered");
        p.AddNumberParameter("Lens", "Lens", "35 mm equivalent focal length in mm, shared by every frame. Default 35. Planes contain no original lens calibration.", GH_ParamAccess.item, 35.0);
        p.AddColourParameter("Background", "BG", "Opaque solid background, or gradient bottom when Background Top is connected. Connect BG or Bottom from SP Auto Background. Default white; grids, axes and Rhino selection highlights are excluded.", GH_ParamAccess.item, Color.White);
        p.AddTextParameter("Root", "Root", "Destination project root. Captures go directly into images, known cameras into colmap, and the training dataset into brush/dataset. Use a fresh project root; existing images/models are never overwritten.", GH_ParamAccess.item,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Splatypus", "Captures"));
        p.AddBooleanParameter("Run", "Run", "Connect a Button. Animates the live Rhino camera, waits, then saves each image; held True runs only once. Input edits apply to the next run.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Cancel", "Cancel", "Cancel capture and restore the viewport. Already saved images remain under Root/images; incomplete dataset outputs are withheld.", GH_ParamAccess.item, false);
        p.AddNumberParameter("Wait", "Wait", "Seconds to let the live renderer update after each camera move (0.1–60). Default 1; increase for heavy Gaussian splat scenes. Keep the capture viewport active and do not navigate during capture.", GH_ParamAccess.item, 1.0);
        p.AddColourParameter("Background Top", "Top", "Optional opaque gradient top. Connect Top from SP Auto Background and Bottom to Background. Leave unconnected for the existing solid fill.", GH_ParamAccess.item);
        p[10].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Images", "Images", "Completed PNG paths in the same order as Frames. Available only after the entire batch succeeds.", GH_ParamAccess.list);
        p.AddTextParameter("Active Dataset", "Dataset", "Completed image/camera dataset. Connect to SP Brush Dataset; no camera reconstruction is needed.", GH_ParamAccess.item);
        p.AddNumberParameter("Progress", "%", "Capture progress from 0 to 100.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Capture status and guidance.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (da.Iteration > 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Flatten Frames and supply one value per capture control. Only the first branch is captured."); return; }
        var run = false; var cancel = false;
        da.GetData(7, ref run); da.GetData(8, ref cancel);
        var start = run && !_lastRun; _lastRun = run;
        if (cancel) Stop("Capture cancelled; saved images remain under Root/images. Use a fresh Root for the next run.");
        else if (start && _session is null)
        {
            _dataset = ""; _images = Array.Empty<string>(); _progress = 0;
            try
            {
                var frames = new List<Plane>(); var width = 1920; var height = 1280; var mode = "Rendered"; var lens = 35.0;
                var background = Color.White; var folder = ""; var wait = 1.0;
                da.GetDataList(0, frames); da.GetData(1, ref width); da.GetData(2, ref height); da.GetData(3, ref mode);
                da.GetData(4, ref lens); da.GetData(5, ref background); da.GetData(6, ref folder);
                da.GetData(9, ref wait);
                var top = Color.Empty;
                Color? backgroundTop = da.GetData(10, ref top) ? top : null;
                var document = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document to capture views.");
                var view = document.Views.ActiveView ?? throw new InvalidOperationException("Activate a Rhino model viewport first.");
                _session = new ViewportCaptureSession(document, view, frames, width, height, lens, mode, background, folder, wait, backgroundTop);
                _status = $"Capturing 0 / {_session.Total} images at {_session.CaptureSize.Width} x {_session.CaptureSize.Height} pixels (requested {width} x {height}). Input edits apply to the next run.";
                QueueCapture();
            }
            catch (Exception ex) { _status = "Capture could not start: " + ex.Message; AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _status); }
        }
        Message = _session is not null ? $"{_progress:0}%" : _dataset.Length > 0 ? "Captured" : "Ready";
        da.SetDataList(0, _images);
        if (_dataset.Length > 0) da.SetData(1, _dataset);
        da.SetData(2, _progress); da.SetData(3, _status);
    }

    private void QueueCapture()
    {
        var generation = _generation;
        // Post from a worker after yielding to mouse-up/paint events. Capturing
        // inside Rhino's Idle dispatch can re-enter display/plugin idle handlers.
        // Only dispatch runs on the worker: all Rhino operations stay on the UI thread.
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            if (generation != _generation || _session is null) return;
            OnCaptureTick();
        })), null, 250, System.Threading.Timeout.Infinite);
    }

    private void OnCaptureTick()
    {
        if (_busy || _session is null) return;
        _busy = true;
        try
        {
            if (_pendingStop is not null && !_session.IsSaving) { Stop(_pendingStop, false); return; }
            var gh = OnPingDocument();
            if (!_session.IsSaving && (Locked || gh is null || !gh.Enabled || !GH_Document.EnableSolutions
                || !ReferenceEquals(global::Grasshopper.Instances.ActiveCanvas?.Document, gh)))
            { Stop("Capture stopped because the Grasshopper document is inactive or its solver is disabled.", false); return; }
            var complete = _session.CaptureNext();
            if (_pendingStop is not null && !_session.IsSaving) { Stop(_pendingStop, false); return; }
            if (complete)
            {
                var session = _session;
                var dataset = session.DatasetPath;
                var images = Enumerable.Range(0, session.Total).Select(i => Path.Combine(dataset, "images", ViewportCaptureSession.ImageName(i))).ToArray();
                Stop($"Captured {images.Length:N0} PNGs at {session.CaptureSize.Width} x {session.CaptureSize.Height} pixels and camera calibration: {dataset}. Connect Dataset to SP Brush Dataset for retraining.", false);
                _dataset = dataset; _images = images; _progress = 100;
            }
            else
            {
                _progress = 100.0 * _session.Count / _session.Total;
                _status = $"Captured {_session.Count:N0} / {_session.Total:N0} at {_session.CaptureSize.Width} x {_session.CaptureSize.Height} pixels. {_session.Phase}. Input edits apply to the next run.";
            }
        }
        catch (Exception ex) { Stop(_pendingStop ?? "Capture failed: " + ex.Message, false); }
        finally
        {
            _busy = false; Refresh();
            if (_session is not null) QueueCapture();
        }
    }

    private void Refresh() => OnPingDocument()?.ScheduleSolution(1, d => { if (ReferenceEquals(OnPingDocument(), d)) ExpireSolution(false); });
    private void Stop(string status, bool deferIfBusy = true)
    {
        // Rhino commands may pump UI events. Defer re-entrant cancellation until
        // the command returns, before restoring its view or deleting its files.
        if (deferIfBusy && (_busy || _session?.IsSaving == true)) { _pendingStop = status; return; }
        _pendingStop = null;
        ++_generation;
        _timer?.Dispose(); _timer = null;
        var session = _session; _session = null;
        try { session?.Dispose(); _status = status; }
        catch (Exception ex) { _status = status + " View/staging cleanup: " + ex.Message; }
    }
    public override bool Read(GH_IReader reader)
    {
        Stop("Connect camera Frames and press Run. Reset Run to False before starting a saved capture.");
        _dataset = ""; _images = Array.Empty<string>(); _progress = 0; _lastRun = true;
        var missing = AppendedInputs.BeforeRead(this, reader, "Wait", "Background Top");
        var result = base.Read(reader);
        AppendedInputs.AfterRead(this, missing);
        Params.Input[6].Name = "Root";
        Params.Input[6].NickName = "Root";
        return result;
    }
    public override void RemovedFromDocument(GH_Document document) { Stop("Capture stopped."); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) Stop("Capture stopped because the document closed.");
        base.DocumentContextChanged(document, context);
    }
}
