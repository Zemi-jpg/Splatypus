using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace RhinoSplat.Grasshopper;

public sealed class GeometryTranslatorComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("geometry-translator");

    public GeometryTranslatorComponent()
        : base("SP Geometry Translator", "Translator",
            "Copies geometry, moves it, rotates around world X, Y and Z in order, then uniformly scales it. The pivot follows every transform.",
            "Splatypus", "04 Realignment") { }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddPointParameter("Centre Point", "CenPt", "Rotation and scale pivot before movement. Moves with the geometry.", GH_ParamAccess.item, Point3d.Origin);
        p.AddGeometryParameter("Geometry", "Geo", "Geometry to copy and transform. Input geometry remains unchanged.", GH_ParamAccess.list);
        p.AddNumberParameter("Move X", "MoveX", "Translation along world X in document units.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Move Y", "MoveY", "Translation along world Y in document units.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Move Z", "MoveZ", "Translation along world Z in document units.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Rotate X", "DegreeX", "Rotation in degrees around world X through the moved pivot.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Rotate Y", "DegreeY", "Rotation in degrees around world Y through the moved pivot, after X rotation.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Rotate Z", "DegreeZ", "Rotation in degrees around world Z through the moved pivot, after Y rotation.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Scale Factor", "ScaleFactor", "Uniform scale about the moved pivot. Use a positive factor when sending Transforms to SP Align COLMAP Model.", GH_ParamAccess.item, 1);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGeometryParameter("Geometry", "Geo", "Transformed copies of the input geometry.", GH_ParamAccess.list);
        p.AddTransformParameter("Transforms", "Transforms", "Five ordered transforms: move, rotate X, rotate Y, rotate Z, scale. Connect to SP Align COLMAP Model's Xforms input.", GH_ParamAccess.list);
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        var center = Point3d.Origin;
        var input = new List<IGH_GeometricGoo>();
        if (!da.GetData(0, ref center) || !da.GetDataList(1, input)) return;
        var values = new double[] { 0, 0, 0, 0, 0, 0, 1 };
        for (var i = 0; i < values.Length; i++)
            if (!da.GetData(i + 2, ref values[i])) return;
        try
        {
            var transforms = BuildTransforms(center, values[0], values[1], values[2], values[3], values[4], values[5], values[6]);
            var geometry = TransformCopies(input, transforms);
            da.SetDataList(0, geometry);
            da.SetDataList(1, transforms);
        }
        catch (Exception exception)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, exception.Message);
        }
    }

    internal static List<Transform> BuildTransforms(Point3d center, double moveX, double moveY, double moveZ,
        double degreeX, double degreeY, double degreeZ, double scaleFactor)
    {
        if (!center.IsValid) throw new ArgumentException("CenPt must be a valid point.");
        foreach (var value in new[] { moveX, moveY, moveZ, degreeX, degreeY, degreeZ, scaleFactor })
            if (!double.IsFinite(value)) throw new ArgumentException("Movement, rotation and scale values must be finite numbers.");
        var transforms = new List<Transform>(5);
        void Append(Transform transform)
        {
            if (!transform.IsValid) throw new ArgumentException("The supplied values produce an invalid transform.");
            center.Transform(transform);
            if (!center.IsValid) throw new ArgumentException("The supplied values move the pivot outside the valid coordinate range.");
            transforms.Add(transform);
        }
        Append(Transform.Translation(moveX, moveY, moveZ));
        Append(Transform.Rotation(degreeX * (Math.PI / 180), Vector3d.XAxis, center));
        Append(Transform.Rotation(degreeY * (Math.PI / 180), Vector3d.YAxis, center));
        Append(Transform.Rotation(degreeZ * (Math.PI / 180), Vector3d.ZAxis, center));
        Append(Transform.Scale(center, scaleFactor));
        return transforms;
    }

    internal static List<IGH_GeometricGoo> TransformCopies(IEnumerable<IGH_GeometricGoo> input, IReadOnlyList<Transform> transforms)
    {
        var copies = new List<IGH_GeometricGoo>();
        foreach (var geometry in input)
        {
            if (geometry is null) continue;
            copies.Add(geometry.DuplicateGeometry() ?? throw new InvalidOperationException("Could not duplicate an input geometry."));
        }
        foreach (var transform in transforms)
            for (var i = 0; i < copies.Count; i++)
                copies[i] = copies[i].Transform(transform)
                    ?? throw new InvalidOperationException($"Could not transform geometry {i + 1}.");
        return copies;
    }

    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    public override Guid ComponentGuid => new("AC3E15DA-864A-4F49-93D4-F1F1BB4AF263");
}
