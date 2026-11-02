using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class BrushTrainingComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("brush-train");

    private readonly Dictionary<int, BrushTrainingSession> _sessions = new();
    private readonly HashSet<int> _activeIterations = new();
    private readonly HashSet<int> _runMustReset = new();
    private readonly Dictionary<int, (BrushRunResult Result, string Text)> _logPreviews = new();
    private bool _publicationQueued;
    private bool _resetSavedRun;

    public BrushTrainingComponent()
        : base("SP Brush Train", "Brush Train", "Trains with Brush outside Grasshopper's solution so you can keep working. Press Cancel to stop.", "Splatypus", "05 Training") { }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        var missing = AppendedInputs.BeforeRead(this, reader, "Maximum Training Resolution", "Maximum Splat Count");
        var result = base.Read(reader); AppendedInputs.AfterRead(this, missing);
        _resetSavedRun = true;
        return result;
    }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Brush Executable", "Exe", "Path to the Brush executable.", GH_ParamAccess.item);
        p.AddTextParameter("Brush Dataset", "Dataset", "Connect Dataset from SP Brush Dataset.", GH_ParamAccess.item);
        p.AddTextParameter("Project Root", "Root", "Connect Root from SP Project.", GH_ParamAccess.item);
        p.AddTextParameter("Job Name", "Name", "Output subfolder name inside the project's output folder.", GH_ParamAccess.item, "brush");
        p.AddIntegerParameter("Total Steps", "Steps", "Brush training iterations.", GH_ParamAccess.item, 30000);
        p.AddBooleanParameter("Open Brush Viewer", "Viewer", "Open Brush's interactive training viewer.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Replace Existing", "Replace", "When True, deletes this job's previous exports before running.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with Brush's live log.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Rotate for Visualization", "Rotate for Visualization", "When True, creates an additional _rgs.ply rotated +90 degrees around X for RhinoGaussianSplatting. The original Brush PLY is unchanged. Both files are in Export.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Cancel", "Cancel", "Stops the current training process. Previously exported PLY files remain. A Button is recommended.", GH_ParamAccess.item, false);
        p.AddIntegerParameter("Maximum Training Resolution", "Resolution", "Longest image side loaded by Brush. Default 1920. Lower values reduce workload; larger values may require more GPU memory.", GH_ParamAccess.item, 1920);
        p.AddIntegerParameter("Maximum Splat Count", "Max Splats", "Upper bound on splats, not a target or a guaranteed memory limit. Default 10000000.", GH_ParamAccess.item, 10000000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Export Folder", "Export", "Folder containing Brush PLY exports and the log.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "Brush process exit code.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "Recent Brush output. The full log is in brush-training.log inside the Export folder.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Training status.", GH_ParamAccess.item);
    }

    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        _activeIterations.Clear();
        _publicationQueued = false;
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        _activeIterations.Add(da.Iteration);
        if (!_sessions.TryGetValue(da.Iteration, out var session))
        {
            _sessions[da.Iteration] = session = new BrushTrainingSession();
            session.ResultAvailable += OnResultAvailable;
        }
        var exe = string.Empty; var dataset = string.Empty; var root = string.Empty; var name = "brush";
        var steps = 30000; var viewer = true; var run = false; var replace = false;
        var terminal = false; var rotateForVisualization = false; var cancel = false;
        da.GetData(0, ref exe); da.GetData(1, ref dataset); da.GetData(2, ref root); da.GetData(6, ref run);
        da.GetData(3, ref name); da.GetData(4, ref steps); da.GetData(5, ref viewer); da.GetData(7, ref replace);
        da.GetData(8, ref terminal); da.GetData(9, ref rotateForVisualization); da.GetData(10, ref cancel);
        var resolution = 1920; var maxSplats = 10000000;
        da.GetData(11, ref resolution); da.GetData(12, ref maxSplats);
        if (_resetSavedRun) _runMustReset.Add(da.Iteration);
        if (_runMustReset.Contains(da.Iteration))
        {
            if (!run) _runMustReset.Remove(da.Iteration);
            run = false;
        }
        var input = new BrushTrainingRequest(exe, dataset, root, name, steps, viewer, terminal, replace, rotateForVisualization, resolution, maxSplats);
        var state = session.Update(input, run, cancel);
        if (state.IsTraining)
        {
            Message = state.IsCancelling ? "Cancelling…" : "Training…";
            da.SetData(3, state.IsCancelling ? "Cancelling Brush training…" : "Brush is training in the background. Continue working in Rhino; press Cancel to stop.");
            return;
        }
        if (state.Result is not { } latest)
        {
            Message = "Ready";
            da.SetData(3, "Press Run to start Brush training in the background.");
            return;
        }
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        da.SetData(0, latest.ExportPath);
        if (latest.ExitCode.HasValue) da.SetData(1, latest.ExitCode.Value);
        if (!_logPreviews.TryGetValue(da.Iteration, out var preview) || !ReferenceEquals(preview.Result, latest))
            _logPreviews[da.Iteration] = preview = (latest, BrushLogPreview.Create(latest.Log, latest.ExportPath));
        da.SetData(2, preview.Text); da.SetData(3, latest.Status);
    }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (Params.Input.Count > 9)
        {
            Params.Input[9].Name = "Rotate for Visualization";
            Params.Input[9].NickName = "Rotate for Visualization";
            Params.Input[9].Description = "Creates a separate _rgs.ply rotated +90 degrees around X for RhinoGaussianSplatting. The original PLY is unchanged; both files are in Export.";
        }
        // Migrate the obsolete trailing output on saved component instances.
        if (Params.Output.Count == 5 && Params.Output[4].Name == "Rhino PLY")
            Params.UnregisterOutputParameter(Params.Output[4], true);
        Params.OnParametersChanged();
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

    private void OnResultAvailable() => RhinoApp.InvokeOnUiThread(new Action(PublishCompletedResults));

    private void PublishCompletedResults()
    {
        if (_publicationQueued || Locked || !_sessions.Values.Any(session => session.HasCompletedWork)) return;
        var document = OnPingDocument();
        if (document is null) return;
        _publicationQueued = true;
        if (document.SolutionDepth == 0) ExpireSolution(false);
        document.ScheduleSolution(1, scheduledDocument =>
        {
            _publicationQueued = false;
            if (!ReferenceEquals(OnPingDocument(), scheduledDocument) || Locked) return;
            if (_sessions.Values.Any(session => session.HasCompletedWork)) ExpireSolution(false);
        });
    }

    private void StopBackgroundWork()
    {
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
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) StopBackgroundWork();
        base.DocumentContextChanged(document, context);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("B8F6F6B3-CEAF-4C45-91D1-DF718C384154");
}
