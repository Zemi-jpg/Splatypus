using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using RhinoSplat.Core;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class ColmapExhaustiveMatchingComponent : SpTaskCapableComponent<ColmapRunResult>
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("exhaustive-match");

    private bool _previousRun;
    private ColmapRunResult? _lastResult;

    public ColmapExhaustiveMatchingComponent()
        : base("SP COLMAP Exhaustive Match", "Match", "Matches every image pair in the COLMAP database. Toggle Run from False to True to start once.", "Splatypus", "03 COLMAP")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        parameters.AddTextParameter("Project Root", "Root", "Connect the Root output from SP Project.", GH_ParamAccess.item);
        parameters.AddBooleanParameter("Run", "Run", "Starts on a False-to-True toggle. Toggle False before running again.", GH_ParamAccess.item, false);
        parameters.AddBooleanParameter("Show Terminal", "Terminal", "When True, opens a terminal window with COLMAP's live log.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddTextParameter("Database", "Database", "COLMAP database path used for matching.", GH_ParamAccess.item);
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
            TaskList.Add(Task.Run(() => ColmapExhaustiveMatchingRunner.RunAsync(executable, projectRoot, showTerminal, CancelToken), CancelToken));
            return;
        }

        if (GetSolveResults(dataAccess, out ColmapRunResult result))
            _lastResult = result;

        if (_lastResult is null)
        {
            dataAccess.SetData(3, "Complete feature extraction, then toggle Run from False to True.");
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

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override Guid ComponentGuid => new("4D443761-8A03-42C9-B42C-9BEC9427A0AA");

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
