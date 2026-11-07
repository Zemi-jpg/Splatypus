using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSplat.Core;
using GH_IO.Serialization;
using Grasshopper.Kernel.Parameters;
using Rhino;

namespace RhinoSplat.Grasshopper;

public sealed class VideoCameraTrackGoo : GH_Goo<VideoCameraTrack>
{
    public VideoCameraTrackGoo() { }
    public VideoCameraTrackGoo(VideoCameraTrack value) { Value = value; }
    public override bool IsValid => Value is not null;
    public override string TypeName => "Video Camera Track";
    public override string TypeDescription => "Ordered extracted video frames and reconstructed camera poses.";
    public override IGH_Goo Duplicate() => new VideoCameraTrackGoo(Value);
    public override string ToString() => Value is null ? "Empty video track" : $"{Path.GetFileName(Value.Video.Source)}: {Value.Video.Frames.Count} frames, {Value.Keys.Count} cameras";
}

public sealed class VideoFramesComponent : SpComponent
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedRoots = new(StringComparer.OrdinalIgnoreCase);
    private System.Threading.Timer? _debounce;
    private bool _subscribed;
    private int _watchGeneration;
    private string[] _observedRoots = Array.Empty<string>();
    public VideoFramesComponent() : base("SP Video Frames", "Video Frames",
        "Groups extracted frames by their source video and builds camera tracks for Rhino playback. Still photographs are excluded.", "Splatypus", "06 Preview") { }
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("video-frames");
    public override Guid ComponentGuid => new("F924BC93-2AB2-418F-97E3-D7EEAB3DF166");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "Project root containing images and colmap. Connect Dataset from SP Active Dataset.", GH_ParamAccess.item);
        p.AddTextParameter("Media", "Media", "Optional original media files or folders for older imports. Mixed photographs and videos are accepted; videos are found automatically. New imports use their saved media record.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddTextParameter("Model", "Model", "Optional COLMAP text model folder or project root. Overrides automatic aligned-model selection. Leave empty to prefer an existing aligned model under COLMAP, then the base model.", GH_ParamAccess.item);
        p[2].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Frames", "Frames", "Extracted JPEG or PNG paths: one branch per video, in numeric frame order. No still photos.", GH_ParamAccess.tree);
        p.AddTextParameter("Videos", "Videos", "Original video paths, in branch/index order.", GH_ParamAccess.list);
        p.AddNumberParameter("Times", "Times", "Seconds from the extracted sequence start, matching Frames.", GH_ParamAccess.tree);
        p.AddBooleanParameter("Registered", "Registered", "True when the corresponding frame has a reconstructed camera pose.", GH_ParamAccess.tree);
        p.AddGenericParameter("Tracks", "Tracks", "Connect directly to SP Video Playback. Each track includes its original timing and available camera poses.", GH_ParamAccess.list);
        p.AddTextParameter("Status", "Status", "Frame counts, registration and import compatibility information.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var root = ""; var media = new List<string>(); var modelOverride = "";
        if (!da.GetData(0, ref root)) return;
        da.GetDataList(1, media);
        da.GetData(2, ref modelOverride);
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            Watch(root);
            if (!File.Exists(Path.Combine(root, MediaImportManifest.FileName)))
                foreach (var path in media.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    var full = Path.GetFullPath(path.Trim());
                    var folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
                    if (folder is not null) Watch(folder, true);
                }
            // Imports/alignment publish a complete snapshot while holding this lease.
            // File notifications during a write are retried when the writer completes.
            using var lease = DatasetLease.Acquire(root);
            var videos = VideoFrameReader.Read(root, media, out var warning);
            var model = VideoModelSelection.Resolve(root, modelOverride, out var modelStatus);
            if (!string.IsNullOrWhiteSpace(modelOverride))
            {
                var supplied = Path.GetFullPath(modelOverride.Trim());
                Watch(Directory.Exists(Path.Combine(supplied, "colmap")) ? supplied : Path.GetDirectoryName(model) ?? model, modelFolder: true);
            }
            IReadOnlyDictionary<string, VideoCameraPose> poses = new Dictionary<string, VideoCameraPose>();
            if (File.Exists(Path.Combine(model, "images.txt")) && File.Exists(Path.Combine(model, "cameras.txt")))
            {
                try { poses = VideoCameraReader.Read(model); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException)
                { warning += " Camera model could not be read: " + ex.Message; }
            }
            else warning += " No text camera model yet; frame lists are available, but playback needs a reconstructed model.";
            var frames = new GH_Structure<GH_String>();
            var times = new GH_Structure<GH_Number>();
            var registered = new GH_Structure<GH_Boolean>();
            var tracks = new List<VideoCameraTrackGoo>();
            for (var i = 0; i < videos.Count; i++)
            {
                var branch = new GH_Path(da.ParameterTargetPath(0).AppendElement(i));
                var video = videos[i];
                var track = VideoCameraReader.Track(video, Path.Combine(root, "images"), poses);
                var registeredTimes = track.Keys.Select(k => k.Time).ToHashSet();
                frames.EnsurePath(branch); times.EnsurePath(branch); registered.EnsurePath(branch);
                foreach (var frame in video.Frames)
                {
                    frames.Append(new GH_String(frame.Path), branch);
                    times.Append(new GH_Number(frame.Time), branch);
                    registered.Append(new GH_Boolean(registeredTimes.Contains(frame.Time)), branch);
                }
                tracks.Add(new VideoCameraTrackGoo(track));
            }
            da.SetDataTree(0, frames); da.SetDataList(1, videos.Select(v => v.Source));
            da.SetDataTree(2, times); da.SetDataTree(3, registered); da.SetDataList(4, tracks);
            var count = videos.Sum(v => v.Frames.Count); var keys = tracks.Sum(t => t.Value.Keys.Count);
            var gaps = count - keys;
            if (gaps > 0) warning += $" {gaps:N0} frame(s) have no camera pose; playback interpolates across gaps and holds the nearest pose at the ends.";
            if (!string.IsNullOrWhiteSpace(warning)) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning.Trim());
            Message = $"{videos.Count} videos";
            da.SetData(5, $"{videos.Count} video(s), {count:N0} frames, {keys:N0} registered cameras. {modelStatus} " + warning.Trim());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Message = "Check dataset";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); da.SetData(5, ex.Message);
        }
    }

    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); _usedRoots.Clear(); }
    protected override void AfterSolveInstance()
    {
        base.AfterSolveInstance();
        foreach (var root in _watchers.Keys.Where(r => !_usedRoots.Contains(r)).ToArray())
        { _watchers[root].Dispose(); _watchers.Remove(root); }
        _observedRoots = _watchers.Keys.ToArray();
    }

    private void Watch(string root, bool sourceMedia = false, bool modelFolder = false)
    {
        var key = sourceMedia ? "media:" + root : modelFolder ? "model:" + root : root;
        _usedRoots.Add(key);
        if (_watchers.ContainsKey(key) || !Directory.Exists(root)) return;
        var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        void Changed(object sender, FileSystemEventArgs e)
        {
            if (modelFolder)
            {
                if (Path.GetFileName(e.FullPath) is "images.txt" or "cameras.txt" || !Path.HasExtension(e.FullPath)) QueueUpdate();
                return;
            }
            if (sourceMedia)
            {
                if (VideoFrameReader.IsSourceMedia(e.FullPath) || !Path.HasExtension(e.FullPath)) QueueUpdate();
                return;
            }
            var relative = Path.GetRelativePath(root, e.FullPath).Replace('\\', '/');
            if (relative == MediaImportManifest.FileName || relative == "images" || relative.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("colmap/", StringComparison.OrdinalIgnoreCase)
                    && (relative.Split('/').Skip(1).Any(VideoModelSelection.IsAlignedFolder)
                        || Path.GetFileName(relative) is "images.txt" or "cameras.txt")
                || relative == "colmap/sparse-text" || relative.StartsWith("colmap/sparse-text/", StringComparison.OrdinalIgnoreCase)
                || relative == "colmap/sparse" || relative.StartsWith("colmap/sparse/", StringComparison.OrdinalIgnoreCase)) QueueUpdate();
        }
        watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed;
        watcher.Renamed += (s, e) => { Changed(s, e); Changed(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath)!, Path.GetFileName(e.OldFullPath))); };
        watcher.Error += (_, _) => QueueUpdate();
        _watchers.Add(key, watcher); watcher.EnableRaisingEvents = true;
    }

    private void DatasetChanged(string root)
    {
        if (_observedRoots.Contains(root, StringComparer.OrdinalIgnoreCase)) QueueUpdate();
    }
    private void QueueUpdate()
    {
        // Debounce notifications from background imports; only the UI thread touches GH.
        lock (_watchers)
        {
            if (!_subscribed) return;
            var generation = _watchGeneration;
            _debounce ??= new System.Threading.Timer(_ => RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                if (!_subscribed || generation != _watchGeneration || Locked) return;
                var document = OnPingDocument();
                if (document is null || !document.Enabled) return;
                if (document.SolutionDepth == 0) ExpireSolution(false);
                document.ScheduleSolution(1, scheduled =>
                {
                    if (_subscribed && generation == _watchGeneration && !Locked && ReferenceEquals(OnPingDocument(), scheduled)) ExpireSolution(false);
                });
            })), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(400, Timeout.Infinite);
        }
    }

    private void StopWatching()
    {
        lock (_watchers)
        {
            _subscribed = false; ++_watchGeneration;
            ActiveDataset.Changed -= DatasetChanged;
            _debounce?.Dispose(); _debounce = null;
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _observedRoots = Array.Empty<string>();
        }
    }

    public override bool Read(GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        return base.Read(reader);
    }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        DatasetPorts.RemoveInputs(this, "Legacy FPS", "Refresh");
        var model = Params.Input.FirstOrDefault(p => p.Name is "Text Model" or "Model");
        if (model is not null) Params.UnregisterInputParameter(model, false);
        var media = Params.Input.FirstOrDefault(p => p.Name is "Legacy Videos" or "Media");
        if (media is null) { media = new Param_String(); Params.RegisterInputParam(media); }
        media.Name = "Media"; media.NickName = "Media"; media.Access = GH_ParamAccess.list; media.Optional = true;
        media.Description = "Optional mixed media files or folders for older imports. Videos are selected automatically; new imports use their saved media record.";
        model ??= new Param_String();
        model.Name = "Model"; model.NickName = "Model"; model.Access = GH_ParamAccess.item; model.Optional = true;
        model.Description = "Optional COLMAP text model folder or project root. Overrides automatic aligned-model selection. Leave empty to prefer an existing aligned model under COLMAP, then the base model.";
        Params.RegisterInputParam(model);
        Params.OnParametersChanged();
        if (!_subscribed) { ActiveDataset.Changed += DatasetChanged; _subscribed = true; }
    }
    public override void RemovedFromDocument(GH_Document document) { StopWatching(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) StopWatching();
        else if (!_subscribed) { ActiveDataset.Changed += DatasetChanged; _subscribed = true; ExpireSolution(false); }
        base.DocumentContextChanged(document, context);
    }
}
