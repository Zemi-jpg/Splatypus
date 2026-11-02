using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GH_IO.Serialization;
using RhinoSplat.Core;
using Grasshopper.Kernel;
using Rhino.Geometry;

#nullable enable

namespace RhinoSplat.Grasshopper;

public sealed class CameraPlanesComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("camera-planes");

    private const string CachedFolderKey = "LastTextModelFolder";
    private string _lastTextModelFolder = string.Empty;

    public CameraPlanesComponent()
        : base("SP Camera Planes", "Cameras", "Loads COLMAP camera positions from images.txt and previews them as oriented image planes.", "Splatypus", "04 Realignment")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("Active Dataset", "Dataset", "Connect Dataset from SP Active Dataset. A legacy text model folder also works.", GH_ParamAccess.item);
        parameters.AddNumberParameter("Plane Height", "Size", "Viewport height of each camera image plane, in Rhino document units.", GH_ParamAccess.item, 1.0);
        parameters.AddIntegerParameter("Maximum Cameras", "Max", "Preview limit; use 0 to show every registered camera.", GH_ParamAccess.item, 0);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddBrepParameter("Camera Planes", "Planes", "Oriented image planes at the COLMAP camera positions.", GH_ParamAccess.list);
        parameters.AddPointParameter("Camera Centres", "Centres", "COLMAP camera centres.", GH_ParamAccess.list);
        parameters.AddTextParameter("Image Names", "Names", "Registered image names in the same order as the planes.", GH_ParamAccess.list);
        parameters.AddTextParameter("Status", "Status", "Load status.", GH_ParamAccess.item);
        parameters.AddPlaneParameter("Frames", "Frames", "Camera frames for SP View Capture: origin at camera, X right, Y up, looking along negative Z. Order matches Image Names. Preview surfaces remain on Planes.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        var suppliedFolder = string.Empty;
        var height = 1.0;
        var maximum = 0;
        var hasSuppliedFolder = dataAccess.GetData(0, ref suppliedFolder) && !string.IsNullOrWhiteSpace(suppliedFolder);
        if (!hasSuppliedFolder && string.IsNullOrWhiteSpace(_lastTextModelFolder))
        {
            dataAccess.SetData(3, "Connect Dataset from SP Active Dataset.");
            return;
        }
        var folder = ActiveDataset.ResolveText(hasSuppliedFolder ? suppliedFolder : _lastTextModelFolder);
        dataAccess.GetData(1, ref height);
        dataAccess.GetData(2, ref maximum);

        if (height <= 0 || maximum < 0)
        {
            dataAccess.SetData(3, "Plane Height must be greater than zero and Maximum Cameras must be zero or greater.");
            return;
        }

        var path = Path.Combine(folder, "images.txt");
        if (!File.Exists(path))
        {
            dataAccess.SetData(3, $"images.txt was not found: {path}");
            return;
        }
        _lastTextModelFolder = folder;

        var planes = new List<Brep>();
        var centres = new List<Point3d>();
        var names = new List<string>();
        var frames = new List<Plane>();
        const double aspectRatio = 1.5;

        foreach (var fields in ActiveDataset.ReadImagePoses(path))
        {
            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;
            if (!TryParsePose(fields, out var rotationWorldToCamera, out var translationWorldToCamera))
                continue;

            var r = rotationWorldToCamera.Normalized();
            var cameraToWorld = new CameraRotation(r.W, -r.X, -r.Y, -r.Z);
            var centreVector = cameraToWorld.Rotate(translationWorldToCamera * -1);
            var xAxis = cameraToWorld.Rotate(new CameraVector(1, 0, 0));
            var yAxis = cameraToWorld.Rotate(new CameraVector(0, 1, 0));
            var centre = new Point3d(centreVector.X, centreVector.Y, centreVector.Z);
            var plane = new Plane(
                centre,
                new Vector3d(xAxis.X, xAxis.Y, xAxis.Z),
                new Vector3d(yAxis.X, yAxis.Y, yAxis.Z));

            var surface = new PlaneSurface(
                plane,
                new Interval(-height * aspectRatio / 2.0, height * aspectRatio / 2.0),
                new Interval(-height / 2.0, height / 2.0));
            planes.Add(surface.ToBrep());
            // COLMAP image Y points down; Rhino camera frames use Y up and Z back.
            frames.Add(new Plane(centre, plane.XAxis, -plane.YAxis));
            centres.Add(centre);
            names.Add(fields[9]);

            if (maximum > 0 && planes.Count >= maximum)
                break;
        }

        dataAccess.SetDataList(0, planes);
        dataAccess.SetDataList(1, centres);
        dataAccess.SetDataList(2, names);
        dataAccess.SetDataList(4, frames);
        var restored = hasSuppliedFolder ? string.Empty : " Restored the saved Text Model Folder.";
        dataAccess.SetData(3, (planes.Count == 0 ? "No valid registered camera poses were found." : $"Loaded {planes.Count:N0} camera planes from COLMAP.") + restored);
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetString(CachedFolderKey, _lastTextModelFolder);
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        _lastTextModelFolder = reader.ItemExists(CachedFolderKey) ? reader.GetString(CachedFolderKey) : string.Empty;
        // Deserialize the old four-output layout before appending Frames, preserving
        // existing output identities and wires in saved definitions.
        var frames = Params.Output.First(p => p.Name == "Frames");
        var legacy = reader.ChunkExists("param_output", 0) && !reader.ChunkExists("param_output", 4);
        if (legacy) { Params.UnregisterOutputParameter(frames, true); Params.OnParametersChanged(); }
        var result = base.Read(reader);
        if (legacy) { Params.RegisterOutputParam(frames); Params.OnParametersChanged(); }
        return result;
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("F1B709B9-464D-4B6B-9A0F-D7166984D258");

    private static bool TryParsePose(string[] fields, out CameraRotation rotation, out CameraVector translation)
    {
        rotation = default; translation = default;
        var values = new double[7];
        for (var i = 0; i < values.Length; i++)
            if (!double.TryParse(fields[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i])) return false;
        var lengthSquared = values.Take(4).Sum(v => v * v);
        if (!double.IsFinite(lengthSquared) || lengthSquared < 1e-30) return false;
        rotation = new CameraRotation(values[0], values[1], values[2], values[3]);
        translation = new CameraVector(values[4], values[5], values[6]);
        return true;
    }
}
