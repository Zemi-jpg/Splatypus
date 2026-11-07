using System;
using System.Globalization;
using System.IO;
using GH_IO.Serialization;
using RhinoSplat.Core;
using Grasshopper.Kernel;
using Rhino.Geometry;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class SparsePointCloudComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("sparse-point-cloud");

    private const string CachedFolderKey = "LastTextModelFolder";
    private string _lastTextModelFolder = string.Empty;

    public SparsePointCloudComponent()
        : base("SP Sparse Point Cloud", "Sparse Cloud", "Loads COLMAP points3D.txt as a Rhino point cloud for viewport preview.", "Splatypus", "04 Realignment")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("Active Dataset", "Dataset", "Connect Dataset from SP Active Dataset. A legacy text model folder also works.", GH_ParamAccess.item);
        parameters.AddIntegerParameter("Maximum Points", "Max", "Preview limit; use 0 to load every sparse point.", GH_ParamAccess.item, 100000);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddGeometryParameter("Point Cloud", "Cloud", "Sparse COLMAP points for Rhino preview.", GH_ParamAccess.item);
        parameters.AddIntegerParameter("Loaded Points", "Count", "Number of sparse points loaded.", GH_ParamAccess.item);
        parameters.AddTextParameter("Status", "Status", "Load status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        var suppliedFolder = string.Empty;
        var maximum = 100000;
        var hasSuppliedFolder = dataAccess.GetData(0, ref suppliedFolder) && !string.IsNullOrWhiteSpace(suppliedFolder);
        if (!hasSuppliedFolder && string.IsNullOrWhiteSpace(_lastTextModelFolder))
        {
            dataAccess.SetData(2, "Connect Dataset from SP Active Dataset.");
            return;
        }
        var folder = ActiveDataset.ResolveText(hasSuppliedFolder ? suppliedFolder : _lastTextModelFolder);
        dataAccess.GetData(1, ref maximum);

        if (maximum < 0)
        {
            dataAccess.SetData(2, "Maximum Points must be zero or greater.");
            return;
        }

        var path = Path.Combine(folder, "points3D.txt");
        if (!File.Exists(path))
        {
            dataAccess.SetData(2, $"points3D.txt was not found: {path}");
            return;
        }
        _lastTextModelFolder = folder;

        var cloud = new PointCloud();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4
                || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                continue;

            cloud.Add(new Point3d(x, y, z));
            if (maximum > 0 && cloud.Count >= maximum)
                break;
        }

        dataAccess.SetData(0, cloud);
        dataAccess.SetData(1, cloud.Count);
        var restored = hasSuppliedFolder ? string.Empty : " Restored the saved Text Model Folder.";
        dataAccess.SetData(2, (cloud.Count == 0 ? "No valid sparse points were found." : $"Loaded {cloud.Count:N0} sparse points from COLMAP.") + restored);
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetString(CachedFolderKey, _lastTextModelFolder);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _lastTextModelFolder = reader.ItemExists(CachedFolderKey) ? reader.GetString(CachedFolderKey) : string.Empty;
        return base.Read(reader);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("47D06C42-0A08-4CEB-833A-47A1FD5B1471");
}
