using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class ColmapModelAnalyzerComponent : SpTaskCapableComponent<ColmapRunResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("model-report");

    private bool _previousRun;
    private ColmapRunResult? _lastResult;

    public ColmapModelAnalyzerComponent()
        : base("SP COLMAP Model Report", "Report", "Runs COLMAP model_analyzer on sparse model 0 to report reconstruction quality.", "Splatypus", "03 COLMAP") { }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        DatasetPorts.SingleDatasetOutput(this, "Sparse Model");
    }
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        _previousRun = true;
        return base.Read(reader);
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        p.AddTextParameter("Project Root", "Root", "Connect the Root output from SP Project.", GH_ParamAccess.item);
        p.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with COLMAP's live log.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "The active dataset that was analyzed.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "COLMAP process exit code.", GH_ParamAccess.item);
        p.AddTextParameter("Report", "Report", "COLMAP model_analyzer output.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var exe = string.Empty; var root = string.Empty; var run = false; var terminal = false;
            if (!da.GetData(0, ref exe) || !da.GetData(1, ref root) || !da.GetData(2, ref run)) return;
            da.GetData(3, ref terminal);
            var risingEdge = run && !_previousRun; _previousRun = run;
            if (!risingEdge) return;
            Message = "Running…";
            TaskList.Add(Task.Run(() => ColmapModelAnalyzerRunner.RunAsync(exe, root, terminal, CancelToken), CancelToken));
            return;
        }
        if (GetSolveResults(da, out ColmapRunResult result)) _lastResult = result;
        if (_lastResult is null) { da.SetData(3, "Toggle Run from False to True to analyze sparse model 0."); return; }
        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        if (latest.Succeeded) da.SetData(0, System.IO.Path.GetFullPath(System.IO.Path.Combine(latest.DatabasePath, "..", "..", "..")));
        if (latest.ExitCode.HasValue) da.SetData(1, latest.ExitCode.Value);
        da.SetData(2, latest.Log); da.SetData(3, latest.Status);
    }

    public override GH_Exposure Exposure => GH_Exposure.hidden;

    public override Guid ComponentGuid => new("9F790274-119B-4E4A-BDF2-9B3FC9FE93D9");
}
