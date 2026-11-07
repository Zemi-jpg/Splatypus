using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class ColmapImageUndistorterComponent : SpTaskCapableComponent<ColmapRunResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("undistort-images");

    private bool _previousRun;
    private ColmapRunResult? _lastResult;

    public ColmapImageUndistorterComponent()
        : base("SP COLMAP Undistort Images", "Undistort", "Overwrites the active images and camera model with an undistorted pair after successful processing.", "Splatypus", "03 COLMAP") { }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
    }
    internal void MigrateDatasetPorts()
    {
        if (DatasetPorts.RemoveInputs(this, "Replace Existing")) _previousRun = true;
        DatasetPorts.SingleDatasetOutput(this, "Dense Workspace");
        DatasetPorts.RenameInput(this, 1, "Active Dataset", "Dataset", "Project Root or Active Dataset; replaced with corrected images and matching cameras.");
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
        p.AddTextParameter("Active Dataset", "Dataset", "Project Root or the active dataset to overwrite.", GH_ParamAccess.item);
        p.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with COLMAP's live log.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "The current project dataset, now undistorted.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "COLMAP process exit code.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "COLMAP stdout and stderr.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var exe = string.Empty; var root = string.Empty; var run = false; var replace = false; var terminal = false;
            if (!da.GetData(0, ref exe) || !da.GetData(1, ref root) || !da.GetData(2, ref run)) return;
            da.GetData(3, ref terminal);
            var risingEdge = run && !_previousRun; _previousRun = run;
            if (!risingEdge) return;
            Message = "Running…";
            TaskList.Add(Task.Run(() => ColmapImageUndistorterRunner.RunAsync(exe, root, terminal, replace, CancelToken), CancelToken));
            return;
        }
        if (GetSolveResults(da, out ColmapRunResult result)) _lastResult = result;
        if (_lastResult is null) { da.SetData(3, "Complete sparse reconstruction, then toggle Run from False to True."); return; }
        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        if (latest.Succeeded) da.SetData(0, latest.DatabasePath);
        if (latest.ExitCode.HasValue) da.SetData(1, latest.ExitCode.Value);
        da.SetData(2, latest.Log); da.SetData(3, latest.Status);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("F00F5CD2-7CC2-4B0D-8F79-469BB8D4A769");
}
