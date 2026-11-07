using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class ColmapMeshReconstructionComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("mesh-reconstruct");
    private readonly Dictionary<int, ColmapMeshSession> _sessions = new();
    private readonly Dictionary<int, (PlyMeshData Data, Mesh Mesh)> _previews = new();
    private readonly HashSet<int> _activeIterations = new();
    private readonly HashSet<int> _runMustReset = new();
    private bool _publicationQueued, _loaded;

    public ColmapMeshReconstructionComponent()
        : base("SP COLMAP Mesh Reconstruction", "Mesh", "Reconstruct a surface with native COLMAP, then reduce its face count for Rhino. Dense methods require CUDA; sparse Delaunay requires CGAL. Runs in the background with cancellation.", "Splatypus", "03 COLMAP") { }

    protected override string HelpDescription => """
        <h3>Mesh reconstruction</h3>
        <p>Connect Exe and the project Dataset after sparse reconstruction, then press Run.
        Method 0 runs undistortion, CUDA stereo, fusion and Poisson meshing in a separate workspace.
        Method 1 uses dense Delaunay instead. Method 2 meshes sparse points directly without CUDA;
        it is less detailed and requires a COLMAP build with CGAL.</p>
        <p>Target Faces defaults to 100,000. Rhino's reducer aims for that count while preserving
        appearance; the actual count is reported. Zero disables reduction. Higher Accuracy takes longer.
        Max Image Size and Poisson Depth control reconstruction cost and detail, independently of the final target.
        Higher Poisson Trim removes more low-density surface; lower it if the mesh is empty or has holes.</p>
        <p>Enable Refine Only and press Run again to reduce the saved raw mesh without rerunning COLMAP.
        Only Target Faces and Accuracy apply in this mode. The raw mesh is always preserved.
        New reconstruction replaces this project's previous mesh workspace only after success.</p>
        <p>The Mesh output is ready to preview or bake. Mesh File is the reduced PLY; Raw Mesh File
        retains full detail. Coordinates match the input model, without automatic scaling or rotation.
        Connect Align Model's Aligned Sparse Model to the optional override to mesh in aligned coordinates.
        The active images and cameras are preserved. Cancel stops the active job. Reset Run to False
        before another run; reopening a saved True does not start a job.</p>
        """;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("COLMAP Executable", "Exe", "Full path from COLMAP Setup. Not used by Refine Only.", GH_ParamAccess.item);
        p[0].Optional = true;
        p.AddTextParameter("Active Dataset", "Dataset", "Project Root or Dataset after sparse reconstruction.", GH_ParamAccess.item);
        p.AddBooleanParameter("Run", "Run", "Start on a False-to-True toggle or Button.", GH_ParamAccess.item, false);
        p.AddIntegerParameter("Method", "Method", "0 = Dense Poisson (CUDA); 1 = Dense Delaunay (CUDA + CGAL); 2 = Sparse Delaunay (CGAL, less detail).", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Target Faces", "Faces", "Desired triangle count after Rhino reduction. Default 100,000; 0 keeps full detail. Actual result can exceed the target to preserve appearance.", GH_ParamAccess.item, 100000);
        p.AddIntegerParameter("Max Image Size", "ImageSize", "Longest image side for dense reconstruction, 320–8192 pixels. Lower values reduce GPU memory and detail.", GH_ParamAccess.item, 1600);
        p.AddIntegerParameter("Poisson Depth", "Depth", "Poisson octree depth, 6–14. Default 10; higher values require more memory and retain finer detail. Ignored by Delaunay.", GH_ParamAccess.item, 10);
        p.AddNumberParameter("Poisson Trim", "Trim", "Nonnegative density threshold; higher removes more low-density surface. Ignored by Delaunay.", GH_ParamAccess.item, 10);
        p.AddIntegerParameter("Reduction Accuracy", "Accuracy", "1–10; higher values improve reduction accuracy and take longer.", GH_ParamAccess.item, 5);
        p.AddBooleanParameter("Refine Only", "Refine", "Reduce the saved raw mesh again; skips all reconstruction and ignores source/model settings.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Show Terminal", "Terminal", "Open a terminal with live COLMAP output.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Cancel", "Cancel", "Cancel the current job, keeping previously saved meshes.", GH_ParamAccess.item, false);
        p.AddTextParameter("Sparse Model Override", "Model", "Optional aligned binary model. Leave empty to use the active base model.", GH_ParamAccess.item);
        p[12].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddMeshParameter("Mesh", "Mesh", "Reduced surface ready for preview or baking, in the input model's coordinates.", GH_ParamAccess.item);
        p.AddTextParameter("Mesh File", "File", "Reduced PLY saved in colmap/mesh/mesh.ply.", GH_ParamAccess.item);
        p.AddTextParameter("Raw Mesh File", "Raw", "Full-detail COLMAP mesh, retained for Refine Only.", GH_ParamAccess.item);
        p.AddIntegerParameter("Original Faces", "Before", "Triangle count before reduction.", GH_ParamAccess.item);
        p.AddIntegerParameter("Final Faces", "After", "Actual triangle count after reduction.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Current stage or result; reports if the requested count could not be reached.", GH_ParamAccess.item);
        p.AddNumberParameter("Progress", "Progress", "Completed stages as a percentage; not a time estimate.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "COLMAP and mesh processing output. Live log: colmap/mesh-reconstruction.log.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "Zero on success; failing COLMAP process code when available.", GH_ParamAccess.item);
    }

    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        _loaded = true;
        return base.Read(reader);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); _activeIterations.Clear(); }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var iteration = da.Iteration;
        _activeIterations.Add(iteration);
        if (!_sessions.TryGetValue(iteration, out var session))
        {
            _sessions[iteration] = session = new ColmapMeshSession(ColmapMeshGeometry.Reduce);
            session.StateChanged += OnStateChanged;
            if (_loaded) _runMustReset.Add(iteration);
        }
        var exe = ""; var root = ""; var model = "";
        var run = false; var refine = false; var terminal = false; var cancel = false;
        var method = 0; var faces = 100000; var size = 1600; var depth = 10; var accuracy = 5; var trim = 10.0;
        da.GetData(0, ref exe); da.GetData(1, ref root); da.GetData(2, ref run); da.GetData(3, ref method);
        da.GetData(4, ref faces); da.GetData(5, ref size); da.GetData(6, ref depth); da.GetData(7, ref trim);
        da.GetData(8, ref accuracy); da.GetData(9, ref refine); da.GetData(10, ref terminal);
        da.GetData(11, ref cancel); da.GetData(12, ref model);
        if (_runMustReset.Contains(iteration))
        {
            if (!run) _runMustReset.Remove(iteration);
            run = false;
        }
        var state = session.Update(new(exe, root, (ColmapMeshMethod)method, faces, size, depth, trim, accuracy, refine, terminal, model), run, cancel);
        if (state.IsRunning)
        {
            _previews.Remove(iteration);
            Message = state.IsCancelling ? "Cancelling…" : state.Progress?.Stage ?? "Starting…";
            da.SetData(5, Message);
            if (state.Progress is { } progress) da.SetData(6, 100.0 * progress.Completed / progress.Total);
            return;
        }
        if (state.Result is not { } result)
        {
            Message = "Ready"; da.SetData(5, "Connect Dataset and Exe, then press Run. Dense methods require CUDA."); da.SetData(6, 0.0); return;
        }
        Message = result.Succeeded ? "Done" : result.WasCancelled ? "Cancelled" : "Failed";
        da.SetData(5, result.Status); da.SetData(7, result.Log);
        if (result.ExitCode.HasValue) da.SetData(8, result.ExitCode.Value);
        da.SetData(6, result.Succeeded ? 100.0 : state.Progress is { } last ? 100.0 * last.Completed / last.Total : 0);
        if (result.Succeeded && result.Mesh is { } data)
        {
            da.SetData(1, result.MeshPath); da.SetData(2, result.RawMeshPath);
            da.SetData(3, result.OriginalFaces); da.SetData(4, result.FinalFaces);
            try
            {
                if (!_previews.TryGetValue(iteration, out var preview) || !ReferenceEquals(preview.Data, data))
                    _previews[iteration] = preview = (data, ColmapMeshGeometry.ToRhino(data));
                da.SetData(0, preview.Mesh);
            }
            catch (Exception error) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh file saved, but Rhino preview failed: " + error.Message); }
        }
        else if (!result.WasCancelled) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, result.Status);
    }

    private void OnStateChanged() => RhinoApp.InvokeOnUiThread(new Action(() =>
    {
        if (_publicationQueued || Locked || _sessions.Count == 0) return;
        var document = OnPingDocument();
        if (document is null) return;
        _publicationQueued = true;
        document.ScheduleSolution(25, scheduled =>
        {
            _publicationQueued = false;
            if (ReferenceEquals(OnPingDocument(), scheduled) && !Locked) ExpireSolution(false);
        });
    }));
    protected override void AfterSolveInstance()
    {
        base.AfterSolveInstance();
        foreach (var iteration in _sessions.Keys.Where(key => !_activeIterations.Contains(key)).ToArray())
        {
            _sessions[iteration].Dispose(); _sessions.Remove(iteration); _previews.Remove(iteration);
        }
    }
    private void StopBackgroundWork()
    {
        _runMustReset.UnionWith(_sessions.Keys);
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear(); _previews.Clear(); _publicationQueued = false;
    }
    public override void RemovedFromDocument(GH_Document document) { StopBackgroundWork(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) StopBackgroundWork();
        base.DocumentContextChanged(document, context);
    }
    public override GH_Exposure Exposure => GH_Exposure.senary;
    public override Guid ComponentGuid => new("765B17B4-163E-4D2D-91BF-3AF28F83A819");
}
