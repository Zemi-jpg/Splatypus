using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class BrushDatasetComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("brush-dataset");
    public BrushDatasetComponent()
        : base("SP Brush Dataset", "Brush Data", "Links Brush to active images and the base model, or an optional separate aligned model.", "Splatypus", "05 Training") { }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
    }
    internal void MigrateDatasetPorts()
    {
        DatasetPorts.RemoveInputs(this, "Replace Existing", "Undistorted Workspace");
        if (!Params.Input.Any(p => p.Name == "Sparse Model Override"))
            Params.RegisterInputParam(new Param_String { Name = "Sparse Model Override", NickName = "Sparse",
                Description = "Optional Aligned Sparse Model. Leave empty to use the active original or undistorted base model.", Access = GH_ParamAccess.item, Optional = true }, 2);
        Params.Input[2].Optional = true;
        DatasetPorts.RenameInput(this, 0, "Active Dataset", "Dataset", "Connect Dataset from SP Active Dataset, or the project Root.");
    }
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        return base.Read(reader);
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "Connect Dataset from SP Active Dataset, or the project Root.", GH_ParamAccess.item);
        p.AddBooleanParameter("Prepare", "Prepare", "Enable to prepare the Brush handoff. It follows later active-dataset updates automatically.", GH_ParamAccess.item, false);
        p.AddTextParameter("Sparse Model Override", "Sparse", "Optional Aligned Sparse Model. Leave empty to use the active original or undistorted base model.", GH_ParamAccess.item);
        p[2].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Brush Dataset", "Dataset", "Connect to Brush Train. Images and cameras stay linked to the active dataset.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Preparation status.", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var root = string.Empty; var prepare = false;
        if (!da.GetData(0, ref root)) return;
        da.GetData(1, ref prepare);
        if (!prepare) { da.SetData(1, "Enable Prepare to link Brush to the active dataset."); return; }
        var sparse = string.Empty;
        da.GetData(2, ref sparse);
        var result = BrushDatasetPreparer.Prepare(root, sparse);
        Message = result.Succeeded ? "Ready" : "Check Status";
        if (result.Succeeded) da.SetData(0, result.DatasetPath);
        da.SetData(1, result.Status);
    }
    public override GH_Exposure Exposure => GH_Exposure.secondary;
    public override Guid ComponentGuid => new("EABD7F8C-B45B-42A1-B6FE-F72AB4D893AA");
}
