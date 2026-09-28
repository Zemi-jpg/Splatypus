using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using RhinoSplat.Grasshopper;

var passed = 0;
void Check(bool condition, string text)
{
    if (!condition) throw new InvalidOperationException(text);
    Console.WriteLine("PASS " + text); passed++;
}
Transform Move(double x, double y, double z)
{
    var t = Transform.Identity; t.M03 = x; t.M13 = y; t.M23 = z; return t;
}
Transform Scale(double scale)
{
    var t = Transform.Identity; t.M00 = t.M11 = t.M22 = scale; return t;
}
GH_Structure<GH_Transform> Tree(params Transform[][] branches)
{
    var tree = new GH_Structure<GH_Transform>();
    for (var i = 0; i < branches.Length; i++)
        tree.AppendRange(branches[i].Select(t => new GH_Transform(t)), new GH_Path(i));
    return tree;
}
var steps = new[] { Move(2.42, 78.26, 34.58), Transform.Identity, Transform.Identity, Transform.Identity, Scale(16.23) };
var duplicateBranches = Tree(steps, steps);
Check(ColmapModelAlignmentComponent.TryGetTransformSequence(duplicateBranches, out var sequence, out var error)
    && sequence.Count == 5, "Identical geometry branches produce one five-transform alignment. " + error);
Check(ColmapModelAlignmentComponent.TryGetSimilarity(sequence, out var actual, out error)
    && Math.Abs(actual.Scale - 16.23) < 1e-10, "Scale is applied once, not squared. " + error);
var point = new Point3d(1, 2, 3);
foreach (var step in steps) point.Transform(step);
var aligned = actual.Apply(1, 2, 3);
Check(point.DistanceTo(new Point3d(aligned.X, aligned.Y, aligned.Z)) < 1e-9,
    "The alignment conversion agrees with sequential geometry transformation.");
var oldSequence = steps.Concat(steps).ToArray();
Check(ColmapModelAlignmentComponent.TryGetSimilarity(oldSequence, out var oldResult, out error)
    && Math.Abs(oldResult.Scale - 16.23 * 16.23) < 1e-9,
    "Regression reproduces the previous squared-scale error from concatenating branches.");
Check(ColmapModelAlignmentComponent.TryGetTransformSequence(Tree(new[] { Scale(2), Scale(2) }), out sequence, out error)
    && sequence.Count == 2 && ColmapModelAlignmentComponent.TryGetSimilarity(sequence, out actual, out error)
    && Math.Abs(actual.Scale - 4) < 1e-10, "Repeated transforms within one branch remain intentional and are not deduplicated.");
Check(!ColmapModelAlignmentComponent.TryGetTransformSequence(Tree(steps, new[] { Scale(3) }), out sequence, out error)
    && error.Contains("different transform sequences"), "Conflicting geometry branches fail instead of silently compounding alignments.");
Check(!ColmapModelAlignmentComponent.TryGetTransformSequence(Tree(), out sequence, out error), "Empty transforms are rejected.");
Check(ColmapModelAlignmentComponent.TryGetTransformSequence(Tree(steps), out sequence, out error)
    && sequence.SequenceEqual(steps), "Single-branch order remains unchanged.");
// Reproduce the screenshot's rotation angles and scale with a non-origin pivot.
Transform Rotation(int axis, double degrees, Point3d pivot)
{
    var radians = degrees * Math.PI / 180;
    var c = Math.Cos(radians); var s = Math.Sin(radians);
    var r = Transform.Identity;
    if (axis == 0) { r.M11 = c; r.M12 = -s; r.M21 = s; r.M22 = c; }
    if (axis == 1) { r.M00 = c; r.M02 = s; r.M20 = -s; r.M22 = c; }
    if (axis == 2) { r.M00 = c; r.M01 = -s; r.M10 = s; r.M11 = c; }
    return Move(pivot.X, pivot.Y, pivot.Z) * r * Move(-pivot.X, -pivot.Y, -pivot.Z);
}
var pivot = new Point3d(3, 5, 7);
var move = Move(2.42, 78.26, 34.58);
pivot.Transform(move);
var screenshotSteps = new[] { move, Rotation(0, 216, pivot), Rotation(1, 357, pivot), Rotation(2, 306, pivot),
    Move(pivot.X, pivot.Y, pivot.Z) * Scale(16.23) * Move(-pivot.X, -pivot.Y, -pivot.Z) };
Check(ColmapModelAlignmentComponent.TryGetTransformSequence(Tree(screenshotSteps, screenshotSteps), out sequence, out error)
    && ColmapModelAlignmentComponent.TryGetSimilarity(sequence, out actual, out error),
    "The screenshot's move, rotations and scale accept two identical branches.");
var maximumError = 0.0;
for (var i = 0; i < 30; i++)
{
    var p = new Point3d(i - 12, i * 0.31, i * i * 0.14);
    var result = actual.Apply(p.X, p.Y, p.Z);
    foreach (var transform in screenshotSteps) p.Transform(transform);
    maximumError = Math.Max(maximumError, p.DistanceTo(new Point3d(result.X, result.Y, result.Z)));
}
Check(maximumError < 1e-9, $"Aligned points match sequential preview transforms with rotations (maximum error {maximumError:G4}).");
Console.WriteLine($"All {passed} alignment input checks passed.");
