using Grasshopper.Kernel;
using Rhino;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class TrainingDatasetCheckComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("training-dataset-check");
    private string _root = string.Empty;
    private bool _refreshQueued;
    public TrainingDatasetCheckComponent()
        : base("SP Active Dataset", "Dataset", "One handoff for the current images and model. Refreshes after reconstruction, undistortion or alignment.", "Splatypus", "05 Training") { }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        MigrateDatasetPorts();
        ActiveDataset.Changed += OnChanged;
    }
    internal void MigrateDatasetPorts()
    {
        Name = "SP Active Dataset";
        NickName = "SP Dataset";
        DatasetPorts.SingleDatasetOutput(this, "Images", "Sparse Model", "Text Model", "Manifest");
    }
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        DatasetPorts.RestoreSavedLayout(this, reader);
        return base.Read(reader);
    }
    public override void RemovedFromDocument(GH_Document document)
    {
        ActiveDataset.Changed -= OnChanged;
        base.RemovedFromDocument(document);
    }
    private void OnChanged(string root)
    {
        if (!string.Equals(root, _root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            var document = OnPingDocument();
            if (_refreshQueued || document is null || Locked) return;
            _refreshQueued = true;
            document.ScheduleSolution(25, doc =>
            {
                _refreshQueued = false;
                if (ReferenceEquals(doc, OnPingDocument()) && !Locked) ExpireSolution(false);
            });
        }));
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Project Root", "Root", "Connect Root from SP Project.", GH_ParamAccess.item);
        p.AddBooleanParameter("Update", "Update", "Refresh manually after an external tool changes the dataset.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Active Dataset", "Dataset", "Connect this single dataset to Brush Dataset or geometry previews.", GH_ParamAccess.item);
        p.AddBooleanParameter("Valid", "Valid", "Whether the active image/model pair is complete.", GH_ParamAccess.item);
        p.AddIntegerParameter("Source Images", "Images #", "Number of image files.", GH_ParamAccess.item);
        p.AddIntegerParameter("Registered Images", "Registered #", "Number of recovered camera poses.", GH_ParamAccess.item);
        p.AddIntegerParameter("Sparse Points", "Points #", "Number of sparse points.", GH_ParamAccess.item);
        p.AddTextParameter("Status", "Status", "Validation status.", GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var root = string.Empty; var update = false;
        if (!da.GetData(0, ref root)) return;
        da.GetData(1, ref update);
        try
        {
            var dataset = new ActiveDataset(root);
            _root = dataset.Root;
            using var lease = DatasetLease.Acquire(dataset.Root);
            dataset.Validate();
            ActiveDataset.ValidateText(dataset.Text, dataset.Images);
            var sourceCount = Directory.GetFiles(dataset.Images, "*", SearchOption.AllDirectories).Length;
            var registered = ActiveDataset.ReadImagePoses(Path.Combine(dataset.Text, "images.txt")).Count();
            var points = File.ReadLines(Path.Combine(dataset.Text, "points3D.txt")).Count(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l));
            da.SetData(0, dataset.Root); da.SetData(1, true); da.SetData(2, sourceCount);
            da.SetData(3, registered); da.SetData(4, points);
            da.SetData(5, "Active dataset is ready. This same connection follows optional undistortion and alignment.");
            Message = "Ready";
        }
        catch (Exception error)
        {
            da.SetData(1, false); da.SetData(5, error.Message); Message = "Check dataset";
        }
    }
    public override GH_Exposure Exposure => GH_Exposure.primary;
    public override Guid ComponentGuid => new("F63A5E3A-C432-4D93-B7FC-5D0AF1A0D0DE");
}
