using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class ColmapSparseMapperComponent : SpTaskCapableComponent<ColmapRunResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("sparse-reconstruct");

    private bool _previousRun;
    private ColmapRunResult? _lastResult;

    public ColmapSparseMapperComponent()
        : base("SP COLMAP Sparse Reconstruct", "Sparse", "Estimates camera poses and creates a sparse COLMAP model. Toggle Run from False to True to start once.", "Splatypus", "03 COLMAP")
    {
    }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
    }
    internal void MigrateDatasetPorts()
    {
        DatasetPorts.SingleDatasetOutput(this, "Sparse Model Folder", "Sparse Folder");
    }
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        _previousRun = true;
        return base.Read(reader);
    }
    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        parameters.AddTextParameter("Project Root", "Root", "Connect the Root output from SP Project.", GH_ParamAccess.item);
        parameters.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle. Toggle False before running again.", GH_ParamAccess.item, false);
        parameters.AddBooleanParameter("Replace Existing", "Replace", "Allow a successful reconstruction to overwrite the active model.", GH_ParamAccess.item, false);
        parameters.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with COLMAP's live log.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddTextParameter("Active Dataset", "Dataset", "The project dataset used by optional steps and Brush.", GH_ParamAccess.item);
        parameters.AddIntegerParameter("Exit Code", "Code", "COLMAP process exit code.", GH_ParamAccess.item);
        parameters.AddTextParameter("Log", "Log", "COLMAP stdout and stderr.", GH_ParamAccess.item);
        parameters.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        if (InPreSolve)
        {
            if (!TryGetInputs(dataAccess, out var executable, out var projectRoot, out var run, out var replaceExisting, out var showTerminal))
                return;

            var risingEdge = run && !_previousRun;
            _previousRun = run;
            if (!risingEdge)
                return;

            Message = "Running…";
            TaskList.Add(Task.Run(() => ColmapSparseMapperRunner.RunAsync(executable, projectRoot, showTerminal, replaceExisting, CancelToken), CancelToken));
            return;
        }

        if (GetSolveResults(dataAccess, out ColmapRunResult result))
            _lastResult = result;

        if (_lastResult is null)
        {
            dataAccess.SetData(3, "Complete feature extraction and matching, then toggle Run from False to True.");
            return;
        }

        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        if (latest.Succeeded) dataAccess.SetData(0, latest.DatabasePath);
        if (latest.ExitCode.HasValue)
            dataAccess.SetData(1, latest.ExitCode.Value);
        dataAccess.SetData(2, latest.Log);
        dataAccess.SetData(3, latest.Status);
    }

    public override GH_Exposure Exposure => GH_Exposure.quarternary;

    public override Guid ComponentGuid => new("4596BF8E-17B0-4E17-9C84-7CC6CB9337D2");

    private static bool TryGetInputs(IGH_DataAccess dataAccess, out string executable, out string projectRoot, out bool run, out bool replaceExisting, out bool showTerminal)
    {
        executable = string.Empty;
        projectRoot = string.Empty;
        run = false;
        replaceExisting = false;
        showTerminal = false;
        return dataAccess.GetData(0, ref executable)
            && dataAccess.GetData(1, ref projectRoot)
            && dataAccess.GetData(2, ref run)
            && dataAccess.GetData(3, ref replaceExisting)
            && dataAccess.GetData(4, ref showTerminal);
    }
}
