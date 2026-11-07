using Grasshopper.Kernel;
using Rhino;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class DownloadToolsComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("download-tools");

    private readonly Dictionary<int, ToolDownloadSession> _sessions = new();
    private readonly HashSet<int> _iterations = new();
    private readonly HashSet<int> _mustReset = new();
    private bool _publicationQueued;

    public DownloadToolsComponent() : base("SP Download Tools", "Download Tools",
        "Downloads and installs compatible FFmpeg, COLMAP and Brush versions in the background. Supports Windows x64 and Apple Silicon macOS.", "Splatypus", "01 Setup") { }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Tools Folder", "Folder", "Optional installation folder. Blank uses this computer's per-user tools folder (shared with earlier plugin versions). Keep installed folders in place.", GH_ParamAccess.item, "");
        p.AddBooleanParameter("COLMAP CUDA", "CUDA", "Windows: True for an NVIDIA CUDA GPU; False for CPU processing. macOS always uses CPU COLMAP. Brush requires a compatible graphics device.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Download", "Download", "Connect a Button. Downloads missing tools and repairs incomplete installs. Existing valid installs are reused.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Cancel", "Cancel", "Connect a Button to stop. Completed tool installations are retained.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "Set True before pressing Download to open a terminal with live download and installation output. Closing the terminal does not cancel installation.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("FFmpeg", "FFmpeg", "Executable path. Connect to FFmpeg Setup or Media Import. Matching ffprobe is installed beside it.", GH_ParamAccess.item);
        p.AddTextParameter("COLMAP", "COLMAP", "Executable path. Connect to the Exe input of COLMAP components.", GH_ParamAccess.item);
        p.AddTextParameter("Brush", "Brush", "Executable path. Connect to Brush Train's Exe input.", GH_ParamAccess.item);
        p.AddBooleanParameter("Ready", "Ready", "True when all three tools have passed validation.", GH_ParamAccess.item);
        p.AddTextParameter("Versions", "Versions", "Pinned versions selected for this operating system.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Download progress, validation results or errors.", GH_ParamAccess.item);
        p.AddTextParameter("Log File", "Log", "Full installation log, available after Download finishes or is cancelled. Each attempt gets a separate file in Tools Folder/logs.", GH_ParamAccess.item);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); _iterations.Clear(); _publicationQueued = false; }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        _iterations.Add(da.Iteration);
        if (!_sessions.TryGetValue(da.Iteration, out var session))
        {
            _sessions[da.Iteration] = session = new ToolDownloadSession();
            session.Changed += OnChanged;
        }
        var folder = ""; var cuda = true; var download = false; var cancel = false; var terminal = false;
        da.GetData(0, ref folder); da.GetData(1, ref cuda); da.GetData(2, ref download); da.GetData(3, ref cancel);
        if (Params.Input.Count > 4) da.GetData(4, ref terminal);
        if (_mustReset.Contains(da.Iteration)) { if (!download) _mustReset.Remove(da.Iteration); download = false; }
        var state = session.Update(new(folder, cuda, terminal), download, cancel);
        Message = state.Busy ? "Working…" : state.Result?.Ready == true ? "Ready" : state.Result?.Cancelled == true ? "Cancelled" : "Setup";
        if (state.Result is { } result)
        {
            if (result.FFmpeg.Length > 0) da.SetData(0, result.FFmpeg);
            if (result.Colmap.Length > 0) da.SetData(1, result.Colmap);
            if (result.Brush.Length > 0) da.SetData(2, result.Brush);
            da.SetData(4, result.Versions);
            if (result.LogFile.Length > 0) da.SetData(6, result.LogFile);
        }
        da.SetData(3, !state.Busy && state.Result?.Ready == true);
        da.SetData(5, state.Status);
    }
    protected override void AfterSolveInstance()
    {
        base.AfterSolveInstance();
        foreach (var key in _sessions.Keys.Where(key => !_iterations.Contains(key)).ToArray())
        { _sessions[key].Dispose(); _sessions.Remove(key); }
    }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        // Append parameters when reopening definitions saved before live terminal support.
        if (Params.Input.Count == 4)
        {
            var terminal = new global::Grasshopper.Kernel.Parameters.Param_Boolean
            { Name = "Show Terminal", NickName = "Terminal", Description = "Set True before Download to show live installation output.", Access = GH_ParamAccess.item };
            terminal.SetPersistentData(new global::Grasshopper.Kernel.Types.GH_Boolean(false));
            Params.RegisterInputParam(terminal);
        }
        if (Params.Output.Count == 6)
            Params.RegisterOutputParam(new global::Grasshopper.Kernel.Parameters.Param_String
            { Name = "Log File", NickName = "Log", Description = "Full log file from the latest installation attempt.", Access = GH_ParamAccess.item });
        Params.OnParametersChanged();
    }
    private void OnChanged() => RhinoApp.InvokeOnUiThread(new Action(Publish));
    private void Publish()
    {
        if (_publicationQueued || Locked || !_sessions.Values.Any(s => s.HasChanges)) return;
        var document = OnPingDocument();
        if (document is null) return;
        _publicationQueued = true;
        if (document.SolutionDepth == 0) ExpireSolution(false);
        document.ScheduleSolution(100, scheduled =>
        {
            _publicationQueued = false;
            if (ReferenceEquals(OnPingDocument(), scheduled) && !Locked && _sessions.Values.Any(s => s.HasChanges)) ExpireSolution(false);
        });
    }
    private void Stop()
    {
        _mustReset.UnionWith(_sessions.Keys);
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear(); _publicationQueued = false;
    }
    public override void RemovedFromDocument(GH_Document document) { Stop(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) Stop();
        base.DocumentContextChanged(document, context);
    }
    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override Guid ComponentGuid => new("DF84C5D0-635E-4F25-9465-46BC8A2A9756");
}
