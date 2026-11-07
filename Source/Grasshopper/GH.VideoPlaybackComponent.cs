using System.Diagnostics;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class VideoPlaybackComponent : SpComponent
{
    private static WeakReference<VideoPlaybackComponent>? _owner;
    private readonly Stopwatch _clock = new();
    private VideoCameraTrack? _track;
    private double _position, _requestedTime = double.NaN, _speed = 1;
    private bool _playing, _lastPlay, _mustReset, _lastRestore;
    private int _generation;
    private RhinoView? _view;
    private uint _rhinoDocument;
    private ViewportInfo? _original;
    private Point3d _originalTarget;

    public VideoPlaybackComponent() : base("SP Video Playback", "Video Playback",
        "Plays a reconstructed video's camera track in the active Rhino viewport. Interpolates between cameras at the extraction timing.", "Splatypus", "06 Preview") { }
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("video-playback");
    public override Guid ComponentGuid => new("469127FD-A830-49A3-AEB1-3546F1AD2940");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Tracks", "Tracks", "Connect Tracks from SP Video Frames.", GH_ParamAccess.list);
        p[0].Optional = true; // Restore must remain usable after disconnecting a track.
        p.AddIntegerParameter("Video Index", "Video", "Zero-based video index in the Videos list.", GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Play", "Play", "True plays; False pauses. Toggle off/on to restart after reaching the end.", GH_ParamAccess.item, false);
        p.AddNumberParameter("Time", "Time", "Seek time in seconds. Changing this value seeks during playback or pause.", GH_ParamAccess.item, 0.0);
        p.AddNumberParameter("Speed", "Speed", "Playback speed: 1 is original extraction timing, 0.5 is half speed.", GH_ParamAccess.item, 1.0);
        p.AddBooleanParameter("Loop", "Loop", "Repeat at the end. The last-to-first transition is a cut.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Preview", "Preview", "When paused, apply the Time position to the viewport. False leaves the view alone while paused.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Smooth", "Smooth", "Softens camera position and rotation jitters, then follows a smooth curve with eased start/stop. May shift intermediate poses. False follows the original poses with straight segments.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Restore View", "Restore", "Connect a Button to stop and restore the view captured before playback. Toggle Play off before starting again.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPlaneParameter("Camera", "Camera", "Current camera plane: X right, Y up, normal backward.", GH_ParamAccess.item);
        p.AddNumberParameter("Time", "Time", "Current playback position in seconds.", GH_ParamAccess.item);
        p.AddNumberParameter("Duration", "Duration", "Extracted sequence duration in seconds.", GH_ParamAccess.item);
        p.AddTextParameter("Frame", "Frame", "Extracted frame at the current time.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Playback state and guidance.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (da.Iteration > 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Use one list of Tracks and one value per playback control. Only the first input branch is played."); return; }
        ++_generation;
        var tracks = new List<VideoCameraTrackGoo>(); var index = 0; var play = false; var time = 0.0;
        var speed = 1.0; var loop = false; var preview = false; var smooth = true; var restore = false;
        da.GetData(1, ref index); da.GetData(2, ref play); da.GetData(3, ref time); da.GetData(4, ref speed);
        da.GetData(5, ref loop); da.GetData(6, ref preview); da.GetData(7, ref smooth); da.GetData(8, ref restore);
        if (!play) _mustReset = false;
        try
        {
            if (restore && !_lastRestore)
            {
                Stop(); Restore(); _lastRestore = true;
                da.SetData(4, "View restored. Set Play to False before starting again."); Message = "Restored"; return;
            }
            _lastRestore = restore;
            if (!da.GetDataList(0, tracks) || index < 0 || index >= tracks.Count || tracks[index]?.Value is not { } track)
                throw new InvalidOperationException("Connect Tracks and select a Video Index within the Videos list.");
            if (!double.IsFinite(time) || !double.IsFinite(speed) || speed <= 0)
                throw new InvalidOperationException("Time must be finite and Speed must be finite and greater than zero.");
            var changed = !ReferenceEquals(track, _track);
            var wasPlaying = _playing;
            if (_playing && !changed) _position = VideoPlaybackTime.Advance(_position, _clock.Elapsed.TotalSeconds, _speed, track.Video.Duration, loop);
            _clock.Restart(); _speed = speed;
            if (changed || time != _requestedTime) _position = Math.Clamp(time, 0, track.Video.Duration);
            _requestedTime = time; _track = track;
            if (!play) _playing = false;
            else if (!_mustReset && (!_lastPlay || changed))
            {
                if (_position >= track.Video.Duration) _position = 0;
                _playing = true;
            }
            _lastPlay = play;
            var pose = track.Sample(_position, smooth);
            if (pose is null) throw new InvalidOperationException("This video has no registered cameras. Reconstruct it; SP Video Frames updates automatically.");
            if (_playing || wasPlaying || preview && !_mustReset)
            {
                ClaimView();
                ApplyPose(_view!, pose);
            }
            if (!loop && _position >= track.Video.Duration) _playing = false;
            da.SetData(0, new Plane(Point(pose.Centre), Vector(pose.Right), Vector(pose.Up)));
            da.SetData(1, _position); da.SetData(2, track.Video.Duration); da.SetData(3, track.FrameAt(_position));
            Message = _playing ? "Playing" : "Paused";
            da.SetData(4, $"{Message}: {Path.GetFileName(track.Video.Source)} — {_position:0.00} / {track.Video.Duration:0.00} s. {track.Keys.Count} registered cameras."
                + (_mustReset ? " Toggle Play off before restarting." : "")
                + (track.Keys.Count == 1 ? " Only one pose is available; the view is stationary." : ""));
            if (_playing) Schedule();
            else _clock.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            Stop(); Message = "Stopped";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message); da.SetData(4, ex.Message);
        }
    }

    private void Schedule()
    {
        var document = OnPingDocument();
        if (document is null) { Stop(); return; }
        var generation = _generation;
        document.ScheduleSolution(16, scheduled =>
        {
            if (generation != _generation || !_playing) return;
            if (Locked || !scheduled.Enabled || !ReferenceEquals(OnPingDocument(), scheduled)
                || !ReferenceEquals(global::Grasshopper.Instances.ActiveCanvas?.Document, scheduled)
                || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _rhinoDocument)
            { Stop(); return; }
            ExpireSolution(false);
        });
    }

    private void ClaimView()
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open a Rhino document to play a video.");
        if (_view is not null && (_rhinoDocument != doc.RuntimeSerialNumber || doc.Views.Find(_view.ActiveViewportID) is null))
            throw new InvalidOperationException("The playback view was closed or the Rhino document changed. Restore/release the view before restarting.");
        if (_owner is not null && _owner.TryGetTarget(out var other) && !ReferenceEquals(other, this))
        { other.Stop(); other.ReleaseView(); }
        _owner = new WeakReference<VideoPlaybackComponent>(this);
        if (_view is not null) return;
        var view = doc.Views.ActiveView ?? throw new InvalidOperationException("Activate a Rhino model viewport first.");
        if (view is RhinoPageView) throw new InvalidOperationException("Activate a model viewport, rather than a layout, for playback.");
        _view = view; _rhinoDocument = doc.RuntimeSerialNumber;
        _original = new ViewportInfo(view.ActiveViewport); _originalTarget = view.ActiveViewport.CameraTarget;
    }

    internal static void ApplyPose(RhinoView view, VideoCameraPose pose)
    {
        var viewport = view.ActiveViewport;
        using var projection = new ViewportInfo(viewport);
        projection.ChangeToPerspectiveProjection(RhinoMath.UnsetValue, true, 50);
        // Setting direction first can temporarily make it parallel to the previous up
        // vector (notably when the new camera looks along world Z). Bridge through an
        // up vector perpendicular to both directions, then install the final frame.
        var forward = Vector(pose.Forward);
        var bridgeUp = Vector3d.CrossProduct(projection.CameraDirection, forward);
        if (!bridgeUp.Unitize()) bridgeUp = Vector(pose.Right);
        if (!projection.SetCameraLocation(Point(pose.Centre)) || !projection.SetCameraUp(bridgeUp)
            || !projection.SetCameraDirection(forward) || !projection.SetCameraUp(Vector(pose.Up)))
            throw new InvalidOperationException("Rhino could not set the camera pose; check whether this viewport camera is locked.");
        projection.GetFrustum(out _, out _, out _, out _, out var near, out var far);
        near = Math.Max(1e-6, near); far = Math.Max(near * 1000, far);
        var height = near * Math.Tan(pose.VerticalFov / 2);
        var aspect = viewport.Size.Height > 0 ? (double)viewport.Size.Width / viewport.Size.Height : pose.Aspect;
        if (!projection.SetFrustum(-height * aspect, height * aspect, -height, height, near, far) || !viewport.SetViewProjection(projection, true))
            throw new InvalidOperationException("Rhino could not apply the camera projection.");
        view.Redraw();
    }

    private void Restore()
    {
        if (_view is not null && _original is not null && RhinoDoc.ActiveDoc?.RuntimeSerialNumber == _rhinoDocument
            && RhinoDoc.ActiveDoc.Views.Find(_view.ActiveViewportID) is not null)
        {
            _view.ActiveViewport.SetViewProjection(_original, false);
            _view.ActiveViewport.SetCameraTarget(_originalTarget, false);
            _view.Redraw();
        }
        ReleaseView();
    }

    private void ReleaseView() { _original?.Dispose(); _original = null; _view = null; }
    private void Stop() { _playing = false; _mustReset = true; _clock.Stop(); ++_generation; }
    private static Point3d Point(CameraVector p) => new(p.X, p.Y, p.Z);
    private static Vector3d Vector(CameraVector p) => new(p.X, p.Y, p.Z);
    public override bool Read(GH_IReader reader) { Stop(); ReleaseView(); return base.Read(reader); }
    public override void RemovedFromDocument(GH_Document document) { Stop(); ReleaseView(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) { Stop(); ReleaseView(); }
        base.DocumentContextChanged(document, context);
    }
}
