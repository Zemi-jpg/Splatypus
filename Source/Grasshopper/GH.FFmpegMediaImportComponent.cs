using System;
using System.Collections.Generic;
using System.Linq;
#if !MAC_WORKFLOW
using System.Windows.Forms;
#endif
using Grasshopper.Kernel;
using Rhino;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class FFmpegMediaImportComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("media-import");

    private readonly Dictionary<int, FFmpegMediaImportSession> _sessions = new();
    private readonly HashSet<int> _activeIterations = new();
    private readonly HashSet<int> _runMustReset = new();
    private bool _publicationQueued;
    private bool _resetSavedRun;
#if MAC_WORKFLOW
    private readonly HashSet<int> _refreshHeld = new();
#endif
    private readonly Dictionary<int, (FFmpegMediaImportResult Import, string Text)> _logPreviews = new();

    public FFmpegMediaImportComponent()
        : base("SP Media Import", "Media Import", "Estimates image counts and imports JPEG or PNG media in the background. Press Run to extract or Cancel to stop; Rhino remains available while processing.", "Splatypus", "02 dataset") { }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        var missing = AppendedInputs.BeforeRead(this, reader, "Image Format", "Skip Near Duplicates");
        var result = base.Read(reader);
        AppendedInputs.AfterRead(this, missing);
        _resetSavedRun = true;
        Params.Output[0].Description = "Project images folder containing the imported JPEG or PNG images.";
        Params.Output[1].Name = "Imported Images"; Params.Output[1].NickName = "Images";
        Params.Output[1].Description = "Every image written by the latest import.";
        Params.Output[5].Name = "Estimated Images";
        Params.Output[5].Description = "Approximate image count before optional duplicate removal.";
        return result;
    }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("FFmpeg Executable", "Exe", "Connect FFmpeg from SP FFmpeg Setup.", GH_ParamAccess.item);
        p.AddTextParameter("Project Root", "Root", "Connect Root from SP Project.", GH_ParamAccess.item);
        p.AddTextParameter("Media Sources", "Media", "One or more files or folders. Folders are searched recursively for media files.", GH_ParamAccess.list);
        p.AddNumberParameter("Video Frames Per Second", "FPS", "Video sampling rate. 2 fps is a good starting point for COLMAP; still image files always produce one image.", GH_ParamAccess.item, 2.0);
        p.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle. A Button is recommended. Presses while an import is running are ignored.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Replace Previous Import", "Replace", "When True, removes only JPEGs and PNGs previously created by this component before importing.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with FFmpeg's live log.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Cancel", "Cancel", "Stops the running import. A Button is recommended. Already written images remain in the project.", GH_ParamAccess.item, false);
#if MAC_WORKFLOW
        p.AddBooleanParameter("Refresh Estimate", "Refresh", "Refresh after changing files inside a source folder. Connect a Button.", GH_ParamAccess.item, false);
#endif
        p.AddIntegerParameter("Image Format", "Format", "0 = JPEG (default); 1 = PNG. PNG preserves existing transparency; it does not remove backgrounds. Matching-format stills are copied with metadata intact.", GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Skip Near Duplicates", "Skip Duplicates", "Skip near-identical sampled video frames. Still images are retained. Estimate is before filtering; original frame timing is saved for playback.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Images Folder", "Images", "Project images folder containing the imported JPEG or PNG images.", GH_ParamAccess.item);
        p.AddTextParameter("Imported Images", "Images", "Every image written by the latest import.", GH_ParamAccess.list);
        p.AddIntegerParameter("Image Count", "Count", "Number of images written by the latest import.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "A short tail of FFmpeg output, safe to display in a Panel. The complete log is saved as ffmpeg-import.log in the project folder.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Import status.", GH_ParamAccess.item);
        p.AddIntegerParameter("Estimated Images", "Estimate", "Approximate image count before duplicate removal, without extracting. Empty if a video cannot be probed. Still images count as one each.", GH_ParamAccess.item);
        p.AddTextParameter("Probe Status", "Probe", "Estimate details or metadata errors. Video estimation requires ffprobe.exe beside the FFmpeg executable.", GH_ParamAccess.item);
    }

    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        _publicationQueued = false;
        _activeIterations.Clear();
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        _activeIterations.Add(da.Iteration);
        if (!_sessions.TryGetValue(da.Iteration, out var session))
        {
            _sessions[da.Iteration] = session = new FFmpegMediaImportSession();
            session.ResultAvailable += OnResultAvailable;
        }

        var executable = string.Empty; var root = string.Empty; var sources = new List<string>();
        var fps = 2.0; var run = false; var replace = false; var terminal = false; var cancel = false;
        da.GetData(0, ref executable); da.GetData(1, ref root); da.GetDataList(2, sources);
        da.GetData(3, ref fps); da.GetData(4, ref run); da.GetData(5, ref replace);
        da.GetData(6, ref terminal); da.GetData(7, ref cancel);
        if (_resetSavedRun) _runMustReset.Add(da.Iteration);
        if (_runMustReset.Contains(da.Iteration))
        {
            if (!run) _runMustReset.Remove(da.Iteration);
            run = false;
        }
#if MAC_WORKFLOW
        var refresh = false;
        da.GetData(8, ref refresh);
        if (refresh && _refreshHeld.Add(da.Iteration)) session.InvalidateEstimate();
        if (!refresh) _refreshHeld.Remove(da.Iteration);
#endif
        var format = 0; var skipDuplicates = false;
        da.GetData(Params.Input.FindIndex(p => p.Name == "Image Format"), ref format);
        da.GetData(Params.Input.FindIndex(p => p.Name == "Skip Near Duplicates"), ref skipDuplicates);
        var input = new FFmpegMediaRequest(executable, root, sources, fps, replace, terminal, (MediaImageFormat)format, skipDuplicates);

        // Update only starts work or collects results that are already complete.
        // No file traversal, ffprobe, FFmpeg or wait occurs inside a GH solution.
        // Unrelated document recomputes must not re-probe unchanged media.
        var state = session.Update(input, run, cancel);
        if (state.Estimate?.EstimatedImages is int estimatedImages)
            da.SetData(5, estimatedImages);
        da.SetData(6, state.IsEstimating ? "Estimating in background…" : state.Estimate?.Status);

        if (state.IsImporting)
        {
            Message = state.IsCancelling ? "Cancelling…" : "Importing…";
            da.SetData(4, state.IsCancelling ? "Cancelling media import…" :
                "Importing in background. You can continue using Rhino. Press Cancel to stop.");
            return;
        }
        if (state.Import is not { } latest)
        {
            Message = state.IsEstimating ? "Estimating…" : state.Estimate?.EstimatedImages is not null ? "Ready" : "Check Probe";
            da.SetData(4, "Adjust FPS to review the estimate, then press Run to import in the background.");
            return;
        }
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        da.SetData(0, latest.ImagesPath);
        da.SetDataList(1, latest.ImportedImages);
        da.SetData(2, latest.ImportedImages.Count);
        if (!_logPreviews.TryGetValue(da.Iteration, out var preview) || !ReferenceEquals(preview.Import, latest))
            _logPreviews[da.Iteration] = preview = (latest, FFmpegLogPreview.Create(latest.Log, latest.ImagesPath));
        da.SetData(3, preview.Text);
        da.SetData(4, latest.Status);
    }

    protected override void AfterSolveInstance()
    {
        base.AfterSolveInstance();
        _resetSavedRun = false;
        foreach (var iteration in _sessions.Keys.Where(key => !_activeIterations.Contains(key)).ToArray())
        {
            _sessions[iteration].Dispose();
            _sessions.Remove(iteration);
            _logPreviews.Remove(iteration);
        }
    }

    private void OnResultAvailable()
    {
        // Completion wakes the UI once. There is no repeating GH solution timer.
        RhinoApp.InvokeOnUiThread(new Action(PublishCompletedResults));
    }

    private void PublishCompletedResults()
    {
        if (_publicationQueued || Locked || !_sessions.Values.Any(session => session.HasCompletedWork)) return;
        var document = OnPingDocument();
        if (document is null) return;
        _publicationQueued = true;
        // If another solution replaces our schedule, it still sees this expired component.
        if (document.SolutionDepth == 0) ExpireSolution(false);
        document.ScheduleSolution(1, scheduledDocument =>
        {
            _publicationQueued = false;
            if (!ReferenceEquals(OnPingDocument(), scheduledDocument) || Locked) return;
            if (_sessions.Values.Any(session => session.HasCompletedWork)) ExpireSolution(false);
        });
    }

#if !MAC_WORKFLOW
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Refresh media estimate", (_, _) =>
        {
            foreach (var session in _sessions.Values) session.InvalidateEstimate();
            ExpireSolution(true);
        });
    }

#endif
    private void StopBackgroundWork()
    {
        // Returning to a document with Run still True must not restart a cancelled import.
        _runMustReset.UnionWith(_sessions.Keys);
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
        _logPreviews.Clear();
        _publicationQueued = false;
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        StopBackgroundWork();
        base.RemovedFromDocument(document);
    }

    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded)
            StopBackgroundWork();
        base.DocumentContextChanged(document, context);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("4272A6F1-F53B-4A2A-8D2A-B705C007AD6A");
}
