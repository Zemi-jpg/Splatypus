from pathlib import Path
root=Path(__file__).resolve().parent.parent
p=root/'GH.ColmapModelAlignmentComponent.cs';s=p.read_text(encoding='utf-8-sig')
start=s.index('    private string _lastTextModelPath');end=s.index('    internal static bool TryGetTransformSequence')
s=s[:start]+'''    public ColmapModelAlignmentComponent()
        : base("Splatypus Align COLMAP Model", "Align Model", "Overwrites the active model with aligned points and camera poses, for either original or undistorted images.", "Splatypus", "04 Realignment") { }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (DatasetPorts.RemoveInputs(this, "Source Text Model", "Output Name", "Replace Existing")) _previousWrite = true;
        DatasetPorts.SingleDatasetOutput(this, "Aligned Text Model", "Aligned Sparse Model");
        DatasetPorts.RenameInput(this, 1, "Active Dataset", "Dataset", "Connect the project Root or Active Dataset. The current model is overwritten only on success.");
        Params.Input[2].Access = GH_ParamAccess.tree;
        Params.Input[2].DataMapping = GH_DataMapping.None;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        p.AddTextParameter("Active Dataset", "Dataset", "Connect Root from Project. Uses the current model, including any undistortion.", GH_ParamAccess.item);
        p.AddTransformParameter("Transforms", "Xforms", "One ordered transform sequence. Identical branches are applied once.", GH_ParamAccess.tree);
        p.AddBooleanParameter("Write", "Write", "Press to overwrite the active model with this alignment. Another press applies the transforms again to the current model.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "The same dataset after alignment. Can connect directly to Brush Dataset.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "Alignment log.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Execution status.", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        if (InPreSolve)
        {
            var exe = string.Empty; var root = string.Empty; var write = false;
            da.GetData(3, ref write);
            var edge = write && !_previousWrite; _previousWrite = write;
            if (!edge) return;
            if (!da.GetData(0, ref exe) || !da.GetData(1, ref root) || !da.GetDataTree(2, out GH_Structure<GH_Transform> tree)) return;
            if (!TryGetTransformSequence(tree, out var xforms, out var error) || !TryGetSimilarity(xforms, out var similarity, out error))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error); return; }
            Message = "Aligning…";
            TaskList.Add(Task.Run(() => ColmapModelTransformRunner.RunAsync(exe, root, similarity, CancelToken), CancelToken));
            return;
        }
        if (GetSolveResults(da, out ColmapModelTransformResult result)) _lastResult = result;
        if (_lastResult is null) { da.SetData(2, "Press Write to align the current dataset in place."); return; }
        var latest = _lastResult;
        Message = latest.Succeeded ? "Done" : latest.WasCancelled ? "Cancelled" : "Failed";
        var dataset = string.Empty;
        if (latest.Succeeded && da.GetData(1, ref dataset)) da.SetData(0, dataset);
        da.SetData(1, latest.Log); da.SetData(2, latest.Status);
    }
    public override GH_Exposure Exposure => GH_Exposure.quarternary;
    public override Guid ComponentGuid => new("BD8D1415-FE3B-4A33-A77B-6874AADC5967");

''' + s[end:]
p.write_text(s,encoding='utf-8')
p=root/'GH.ColmapImageUndistorterComponent.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('Creates an undistorted COLMAP image workspace from sparse model 0.', 'Overwrites the active images and camera model with an undistorted pair after successful processing.')
s=s.replace('    protected override void RegisterInputParams', '''    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        if (DatasetPorts.RemoveInputs(this, "Replace Existing")) _previousRun = true;
        DatasetPorts.SingleDatasetOutput(this, "Dense Workspace");
        DatasetPorts.RenameInput(this, 1, "Active Dataset", "Dataset", "Project Root or Active Dataset; replaced with corrected images and matching cameras.");
    }
    protected override void RegisterInputParams''',1)
s='\n'.join(line for line in s.splitlines() if 'p.AddBooleanParameter("Replace Existing"' not in line)+'\n'
s=s.replace('p.AddTextParameter("Project Root", "Root", "Connect the Root output from RS Project."', 'p.AddTextParameter("Active Dataset", "Dataset", "Project Root or the active dataset to overwrite."')
s=s.replace('p.AddTextParameter("Dense Workspace", "Dense", "COLMAP undistorted workspace folder."', 'p.AddTextParameter("Active Dataset", "Dataset", "The current project dataset, now undistorted."')
s=s.replace('da.GetData(3, ref replace); da.GetData(4, ref terminal);','da.GetData(3, ref terminal);')
s=s.replace('da.SetData(0, latest.DatabasePath);','if (latest.Succeeded) da.SetData(0, latest.DatabasePath);')
p.write_text(s,encoding='utf-8')
# Both previews accept the one dataset connection, while old explicit text paths still load.
for filename in ['GH.CameraPlanesComponent.cs','GH.SparsePointCloudComponent.cs']:
    p=root/filename;s=p.read_text(encoding='utf-8-sig')
    s=s.replace('using GH_IO.Serialization;', 'using GH_IO.Serialization;\nusing RhinoSplat.Core;')
    s=s.replace('"Text Model Folder", "Text", "Connect the Text output from RS COLMAP Export Model."', '"Active Dataset", "Dataset", "Connect Dataset from Splatypus Active Dataset. A legacy text model folder also works."')
    s=s.replace('var folder = hasSuppliedFolder ? suppliedFolder : _lastTextModelFolder;', 'var folder = ActiveDataset.ResolveText(hasSuppliedFolder ? suppliedFolder : _lastTextModelFolder);')
    p.write_text(s,encoding='utf-8')
# Sparse reconstruction publishes its active root rather than a separate model directory.
p=root/'GH.ColmapSparseMapperComponent.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('    protected override void RegisterInputParams', '''    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        DatasetPorts.SingleDatasetOutput(this, "Sparse Model Folder", "Sparse Folder");
    }
    protected override void RegisterInputParams''',1)
import re
s=re.sub(r'parameters.AddTextParameter\("Sparse[^\n]+', 'parameters.AddTextParameter("Active Dataset", "Dataset", "The project dataset used by optional steps and Brush.", GH_ParamAccess.item);',s,count=1)
s=s.replace('dataAccess.SetData(0, latest.DatabasePath);','if (latest.Succeeded) dataAccess.SetData(0, latest.DatabasePath);')
p.write_text(s,encoding='utf-8')
