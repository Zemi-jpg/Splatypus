using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class ColmapFeatureExtractionComponent : SpTaskCapableComponent<ColmapRunResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("feature-extract");

    private bool _previousRun;
    private ColmapRunResult? _lastResult;

    public ColmapFeatureExtractionComponent()
        : base("SP COLMAP Feature Extract", "Extract", "Runs COLMAP feature extraction in the background. Toggle Run from False to True to start once.", "Splatypus", "03 COLMAP")
    {
    }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        var missing = AppendedInputs.BeforeRead(this, reader, "Extraction Max Image Size", "Maximum Features");
        var result = base.Read(reader); AppendedInputs.AfterRead(this, missing);
        _previousRun = true;
        return result;
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        parameters.AddTextParameter("Project Root", "Root", "Connect the Root output from SP Project.", GH_ParamAccess.item);
        parameters.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle. Toggle False before running again.", GH_ParamAccess.item, false);
        parameters.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with COLMAP's live log.", GH_ParamAccess.item, false);
        parameters.AddIntegerParameter("Extraction Max Image Size", "Extract Size", "Longest side for feature extraction. Default 3200. Changed settings rebuild the feature database.", GH_ParamAccess.item, 3200);
        parameters.AddIntegerParameter("Maximum Features", "Features", "Maximum features per image. Default 8192. Changed settings rebuild the feature database.", GH_ParamAccess.item, 8192);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddTextParameter("Database", "Database", "COLMAP database path.", GH_ParamAccess.item);
        parameters.AddIntegerParameter("Exit Code", "Code", "COLMAP process exit code.", GH_ParamAccess.item);
        parameters.AddTextParameter("Log", "Log", "COLMAP stdout and stderr.", GH_ParamAccess.item);
        parameters.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        if (InPreSolve)
        {
            if (!TryGetInputs(dataAccess, out var executable, out var projectRoot, out var run, out var showTerminal))
                return;

            var risingEdge = run && !_previousRun;
            _previousRun = run;
            if (!risingEdge)
                return;

            Message = "Running…";
            var size = 3200; var features = 8192;
            dataAccess.GetData(4, ref size); dataAccess.GetData(5, ref features);
            TaskList.Add(Task.Run(
                () => ColmapFeatureExtractionRunner.RunAsync(executable, projectRoot, showTerminal, CancelToken, new(size, features)),
                CancelToken));
            return;
        }

        if (GetSolveResults(dataAccess, out ColmapRunResult result))
            _lastResult = result;

        if (_lastResult is null)
        {
            dataAccess.SetData(3, "Toggle Run from False to True to start feature extraction.");
            return;
        }

        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        dataAccess.SetData(0, latest.DatabasePath);
        if (latest.ExitCode.HasValue)
            dataAccess.SetData(1, latest.ExitCode.Value);
        dataAccess.SetData(2, latest.Log);
        dataAccess.SetData(3, latest.Status);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("7B3F68C6-E10E-406C-945A-C175AEE44E93");

    private static bool TryGetInputs(IGH_DataAccess dataAccess, out string executable, out string projectRoot, out bool run, out bool showTerminal)
    {
        executable = string.Empty;
        projectRoot = string.Empty;
        run = false;
        showTerminal = false;
        return dataAccess.GetData(0, ref executable)
            && dataAccess.GetData(1, ref projectRoot)
            && dataAccess.GetData(2, ref run)
            && dataAccess.GetData(3, ref showTerminal);
    }
}
