from pathlib import Path
import re
root=Path(__file__).resolve().parent.parent
p=root/'GH.ColmapPipelineComponent.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('    private bool _publicationQueued;', '    private bool _publicationQueued;\n    private bool _resetLegacyRun;')
start=s.index('    protected override string HelpDescription');end=s.index('    protected override void RegisterInputParams',start)
s=s[:start]+'''    protected override string HelpDescription => """
        <h3>One active dataset</h3>
        <p>Connect Exe and Root. Press Run to reconstruct the active image/model pair.
        Undistort is optional and defaults to False. When enabled, it replaces the active
        images and cameras together. Alignment can then replace the same model separately.</p>
        <p>Connect Project Root to <b>Splatypus Active Dataset</b>, then its single Dataset
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
        _resetLegacyRun = DatasetPorts.SingleDatasetOutput(this, "Database", "Sparse Model Folder", "Undistorted Workspace", "Text Model Folder");
    }

''' + s[end:]
s=s.replace('Replace outputs only for enabled Sparse, Undistort and Export stages. The database is reused, as in the individual components.', 'Allow sparse reconstruction to replace the active model. Optional undistortion always updates the active pair after success.')
s=s.replace('"Create the undistorted image workspace in colmap/dense.", GH_ParamAccess.item, true', '"Optionally overwrite the active images and cameras with an undistorted pair.", GH_ParamAccess.item, false')
s=s.replace('"Export sparse model 0 as text in colmap/sparse-text."','"Refresh the active model text view for geometry previews."')
start=s.index('    protected override void RegisterOutputParams');end=s.index('    protected override void BeforeSolveInstance',start)
s=s[:start]+'''    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "Single project dataset for optional alignment, previews and Brush Dataset.", GH_ParamAccess.item);
        p.AddIntegerParameter("Exit Code", "Code", "Zero on success; failing process code when available.", GH_ParamAccess.item);
        p.AddTextParameter("Log", "Log", "Combined processing log.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Current stage or final outcome.", GH_ParamAccess.item);
        p.AddNumberParameter("Progress", "Progress", "Percent of selected stages completed.", GH_ParamAccess.item);
    }

''' + s[end:]
s=s.replace('var undistort = true', 'var undistort = false').replace('var sparse = true; var undistort = true','var sparse = true; var undistort = false')
s=s.replace('        if (_runMustReset.Contains', '''        if (_resetLegacyRun)
        {
            if (!run) _resetLegacyRun = false;
            run = false;
        }
        if (_runMustReset.Contains''',1)
s=s.replace('da.SetData(6,','da.SetData(3,').replace('da.SetData(7,','da.SetData(4,')
s=s.replace('All stages are enabled by default.', 'Undistortion is optional and disabled by default.')
s=s.replace('        da.SetData(0, result.DatabasePath); da.SetData(1, result.SparsePath);\n        da.SetData(2, result.DensePath); da.SetData(3, result.TextPath);','        if (result.Succeeded) da.SetData(0, result.DatasetPath);')
s=s.replace('da.SetData(4, result.ExitCode.Value)','da.SetData(1, result.ExitCode.Value)').replace('da.SetData(5, result.Log)','da.SetData(2, result.Log)')
p.write_text(s,encoding='utf-8')
p=root/'GH.ColmapModelExportComponent.cs';s=p.read_text(encoding='utf-8-sig')
s=s.replace('    protected override void RegisterInputParams', '''    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        DatasetPorts.SingleDatasetOutput(this, "Text Model Folder");
    }
    protected override void RegisterInputParams''',1)
s=s.replace('"Text Model Folder", "Text", "Folder containing cameras.txt, images.txt, and points3D.txt."','"Active Dataset", "Dataset", "Active dataset with refreshed text model for geometry previews."')
s=s.replace('dataAccess.SetData(0, latest.DatabasePath);','if (latest.Succeeded) { var root = string.Empty; if (dataAccess.GetData(1, ref root)) dataAccess.SetData(0, root); }')
p.write_text(s,encoding='utf-8')
# Compile-linked component tests need the schema migration helper too.
for f in ['Tests/RhinoSplat.AlignmentInput.SmokeTest.csproj','Tests/RhinoSplat.GeometryTranslator.SmokeTest.csproj']:
    p=root/f;s=p.read_text(encoding='utf-8-sig')
    s=s.replace('<Compile Include="AlignmentInputSmoke.cs" />','<Compile Include="AlignmentInputSmoke.cs" />\n    <Compile Include="../GH.DatasetPorts.cs" />') if 'AlignmentInputSmoke' in s else s.replace('<Compile Include="GeometryTranslatorSmoke.cs" />','<Compile Include="GeometryTranslatorSmoke.cs" />\n    <Compile Include="../GH.DatasetPorts.cs" />')
    p.write_text(s,encoding='utf-8')
