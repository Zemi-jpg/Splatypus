using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class ColmapPipelineComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("colmap-pipeline");
    private readonly Dictionary<int, ColmapPipelineSession> _sessions = new();
    private readonly HashSet<int> _activeIterations = new();
    private readonly HashSet<int> _runMustReset = new();
    private bool _publicationQueued;
    private bool _resetLegacyRun;

    public ColmapPipelineComponent()
        : base("SP COLMAP Pipeline", "COLMAP All", "Use either this all-in-one COLMAP workflow or the individual step-by-step components. Connect Exe and Root, then press Run. All-in-one reduces manual steps; step-by-step lets you inspect and adjust between stages. Do not run both on the same project at once. See Help for usage and tradeoffs.", "Splatypus", "03 COLMAP") { }

    protected override string HelpDescription => """
        <h3>One active dataset</h3>
        <p>Connect Exe and Root. Press Run to reconstruct the active image/model pair.
        Undistort is optional and defaults to False. When enabled, it replaces the active
        images and cameras together. Alignment can then replace the same model separately.</p>
        <p>Connect Project Root to <b>SP Active Dataset</b>, then its single Dataset
        output to <b>Brush Dataset</b>. This handoff refreshes after optional operations.
        The pipeline's Dataset output can also be used directly.</p>
        <p>Extract, Match and Sparse select reconstruction stages. Export refreshes the active
        model's text view; model-changing stages already keep that view synchronized.
        Replace allows a new sparse reconstruction to overwrite an existing model.
        Undistortion always replaces the current pair after success and clears the now-stale
        feature database; extract features again before a later reconstruction.</p>
        <p>Cancel stops processing. Failed or cancelled dataset replacements retain the previous
        active model. Run must return to False before another run. Do not train while editing
        a dataset; project operations are serialized. Run inputs are captured at job start.</p>
        """;
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
    }
    internal void MigrateDatasetPorts()
    {
        _resetLegacyRun |= DatasetPorts.SingleDatasetOutput(this, "Database", "Sparse Model Folder", "Undistorted Workspace", "Text Model Folder");
        EnsureSettings();
    }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        _resetLegacyRun = true;
        var result = base.Read(reader);
        EnsureSettings();
        return result;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        p.AddTextParameter("Project Root", "Root", "Connect Root from SP Project.", GH_ParamAccess.item);
        p.AddBooleanParameter("Run", "Run", "Starts all selected stages on a False-to-True toggle. A Button also works.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Replace Existing", "Replace", "Allow sparse reconstruction to replace the active model. Optional undistortion always updates the active pair after success.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "Open one terminal showing live output from all selected COLMAP stages.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Extract Features", "Extract", "Run feature extraction. Disable to reuse an existing database.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Match Features", "Match", "Run the selected matching method. Disable to reuse existing matches.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Reconstruct Sparse", "Sparse", "Run sparse reconstruction. Disable to reuse sparse model 0.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Undistort Images", "Undistort", "Optionally overwrite the active images and cameras with an undistorted pair.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Export Text Model", "Export", "Refresh the active model text view for geometry previews.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Show Progress", "Progress", "Publish stage progress while running. Completion is always published.", GH_ParamAccess.item, true);
        p.AddBooleanParameter("Cancel", "Cancel", "Stop the current process and skip later stages. A Button is recommended.", GH_ParamAccess.item, false);
        p.AddIntegerParameter("Matching Method", "Matching", MatchingDescription, GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Extraction Max Image Size", "Extract Size", "Longest side used for feature extraction. Default 3200. Changing extraction settings rebuilds the feature database.", GH_ParamAccess.item, 3200);
        p.AddIntegerParameter("Maximum Features", "Features", "Maximum SIFT features per image. Default 8192. Changing extraction settings rebuilds the feature database.", GH_ParamAccess.item, 8192);
    }

    internal const string MatchingDescription = "0 = Exhaustive (default); 1 = Sequential (ordered video); 2 = Spatial (positions required for every image); 3 = Auto (complete positions, then a confirmed single video, otherwise exhaustive). Video telemetry is not automatically transferred to frames.";
    private void EnsureSettings()
    {
        DatasetPorts.EnsureIntegerInput(this, "Matching Method", "Matching", MatchingDescription, 0);
        DatasetPorts.EnsureIntegerInput(this, "Extraction Max Image Size", "Extract Size", "Longest side for feature extraction. Default 3200. Changed settings rebuild the feature database.", 3200);
        DatasetPorts.EnsureIntegerInput(this, "Maximum Features", "Features", "Maximum features per image. Default 8192. Changed settings rebuild the feature database.", 8192);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "Single project dataset for optional alignment, previews and Brush Dataset.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "Zero on success; failing process code when available.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "Combined processing log.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Current stage or final outcome.", GH_ParamAccess.item);
        p.AddNumberParameter("Progress", "Progress", "Percent of selected stages completed.", GH_ParamAccess.item);
    }

    protected override void BeforeSolveInstance()
    {
        base.BeforeSolveInstance();
        _activeIterations.Clear();
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        _activeIterations.Add(da.Iteration);
        if (!_sessions.TryGetValue(da.Iteration, out var session))
        {
            _sessions[da.Iteration] = session = new ColmapPipelineSession();
            session.StateChanged += OnStateChanged;
        }
        var exe = string.Empty; var root = string.Empty;
        var run = false; var replace = false; var terminal = false; var cancel = false;
        var extract = true; var match = true; var sparse = true; var undistort = false; var export = true; var progress = true;
        da.GetData(0, ref exe); da.GetData(1, ref root); da.GetData(2, ref run);
        da.GetData(3, ref replace); da.GetData(4, ref terminal); da.GetData(5, ref extract);
        da.GetData(6, ref match); da.GetData(7, ref sparse); da.GetData(8, ref undistort);
        da.GetData(9, ref export); da.GetData(10, ref progress); da.GetData(11, ref cancel);
        var matching = 0; var imageSize = 3200; var features = 8192;
        da.GetData(12, ref matching); da.GetData(13, ref imageSize); da.GetData(14, ref features);
        if (_resetLegacyRun)
        {
            if (!run) _resetLegacyRun = false;
            run = false;
        }
        if (_runMustReset.Contains(da.Iteration))
        {
            if (!run) _runMustReset.Remove(da.Iteration);
            run = false;
        }
        var state = session.Update(new(exe, root, extract, match, sparse, undistort, export, replace, terminal,
            (ColmapMatchingMode)matching, imageSize, features), run, cancel, progress);
        if (state.IsRunning)
        {
            Message = state.IsCancelling ? "Cancelling…" : progress && state.Progress is { } step ? step.Stage : "Running…";
            da.SetData(3, state.IsCancelling ? "Cancelling COLMAP pipeline…" : progress && state.Progress is { } current
                ? $"{current.Completed}/{current.Total} complete — {current.Stage}" : "COLMAP pipeline is running in the background.");
            if (progress && state.Progress is { } value) da.SetData(4, 100.0 * value.Completed / value.Total);
            return;
        }
        if (state.Result is not { } result)
        {
            Message = "Ready";
            da.SetData(3, "Connect Exe and Root, then press Run. Undistortion is optional and disabled by default.");
            da.SetData(4, 0.0);
            return;
        }
        Message = result.Succeeded ? "Done" : result.WasCancelled ? "Cancelled" : "Failed";
        // Publish paths after the job settles, so downstream components never consume a model being rewritten.
        if (result.Succeeded) da.SetData(0, result.DatasetPath);
        if (result.ExitCode.HasValue) da.SetData(1, result.ExitCode.Value);
        da.SetData(2, result.Log); da.SetData(3, result.Status);
        da.SetData(4, 100.0 * result.Completed / result.Total);
        if (!result.Succeeded && !result.WasCancelled) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, result.Status);
    }

    private void OnStateChanged() => RhinoApp.InvokeOnUiThread(new Action(() =>
    {
        if (_publicationQueued || Locked || _sessions.Count == 0) return;
        var document = OnPingDocument();
        if (document is null) return;
        _publicationQueued = true;
        document.ScheduleSolution(25, scheduledDocument =>
        {
            _publicationQueued = false;
            if (ReferenceEquals(OnPingDocument(), scheduledDocument) && !Locked) ExpireSolution(false);
        });
    }));

    protected override void AfterSolveInstance()
    {
        base.AfterSolveInstance();
        foreach (var iteration in _sessions.Keys.Where(key => !_activeIterations.Contains(key)).ToArray())
        {
            _sessions[iteration].Dispose();
            _sessions.Remove(iteration);
        }
    }

    private void StopBackgroundWork()
    {
        _runMustReset.UnionWith(_sessions.Keys);
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();
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
    public override Guid ComponentGuid => new("A3C3D2EA-7A1C-471E-BE14-021B2459E07C");
}
