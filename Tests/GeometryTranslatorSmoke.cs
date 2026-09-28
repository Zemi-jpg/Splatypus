using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSplat.Grasshopper;

using var rhinoCore = args.Contains("--in-rhino") ? null : new Rhino.Runtime.InProcess.RhinoCore();
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
    passed++;
}
bool Near(Point3d a, Point3d b) => a.DistanceTo(b) < 1e-9;
Point3d Apply(Point3d point, IEnumerable<Transform> transforms)
{
    foreach (var transform in transforms) point.Transform(transform);
    return point;
}
var pivot = new Point3d(1, 2, 3);
var point = new Point3d(2, 4, 6);
var transforms = GeometryTranslatorComponent.BuildTransforms(pivot, 10, 20, 30, 90, 90, 90, 2);
Check(transforms.Count == 5, "Output contains five ordered transforms.");
Check(Near(Apply(pivot, transforms), new Point3d(11, 22, 33)), "Pivot moves with translation and stays fixed during rotations and scale.");
// Offset (1,2,3) -> Rx (1,-3,2) -> Ry (2,-3,-1) -> Rz (3,2,-1) -> scale (6,4,-2).
Check(Near(Apply(point, transforms), new Point3d(17, 26, 31)), "World-axis X, Y, Z rotations and final scaling match an independently calculated point.");
Check(Near(Apply(point, transforms.Take(1)), new Point3d(12, 24, 36)), "Move is the first transform.");
Check(Near(Apply(point, transforms.Take(2)), new Point3d(12, 19, 35)), "X rotation uses the translated centre.");
var combined = Transform.Identity;
foreach (var transform in transforms) combined = transform * combined;
var combinedPoint = point;
combinedPoint.Transform(combined);
Check(Near(combinedPoint, Apply(point, transforms)), "Output list composes in RS Align COLMAP Model's application order.");
var source = new GH_Point(point);
var copy = GeometryTranslatorComponent.TransformCopies(new IGH_GeometricGoo[] { source, null! }, transforms);
Check(copy.Count == 1 && Near(((GH_Point)copy[0]).Value, new Point3d(17, 26, 31)), "Geometry copies use the same transforms and omit null entries.");
Check(Near(source.Value, point) && !ReferenceEquals(source, copy[0]), "Source geometry remains unchanged.");
var cloud = new PointCloud(new[] { point, pivot });
var curve = new LineCurve(pivot, point);
var brep = new PlaneSurface(new Plane(pivot, Vector3d.ZAxis), new Interval(0, 1), new Interval(0, 1)).ToBrep();
var mixedCopies = GeometryTranslatorComponent.TransformCopies(new IGH_GeometricGoo[]
    { new GH_PointCloud(cloud), new GH_Curve(curve), new GH_Brep(brep) }, transforms);
Check(Near(((GH_PointCloud)mixedCopies[0]).Value[0].Location, new Point3d(17, 26, 31))
    && Near(cloud[0].Location, point), "Sparse point clouds transform without changing their source.");
Check(Near(((GH_Curve)mixedCopies[1]).Value.PointAtEnd, new Point3d(17, 26, 31))
    && Near(curve.PointAtEnd, point), "Curves transform without changing their source.");
Check(((GH_Brep)mixedCopies[2]).Value.IsValid && Near(brep.Vertices[0].Location, pivot),
    "Camera-plane Breps remain valid and their original vertices are preserved.");
var identity = GeometryTranslatorComponent.BuildTransforms(pivot, 0, 0, 0, 0, 0, 0, 1);
Check(Near(Apply(point, identity), point), "Default controls preserve geometry.");
var negative = GeometryTranslatorComponent.BuildTransforms(pivot, 0, 0, 0, 0, 0, -90, 1);
Check(Near(Apply(new Point3d(2, 2, 3), negative), new Point3d(1, 1, 3)), "Angles are degrees and negative rotations work.");
foreach (var invalid in new[] { double.NaN, double.PositiveInfinity })
{
    var rejected = false;
    try { GeometryTranslatorComponent.BuildTransforms(pivot, invalid, 0, 0, 0, 0, 0, 1); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Reject invalid numeric input: " + invalid);
}
Console.WriteLine($"All {passed} geometry translator checks passed.");
