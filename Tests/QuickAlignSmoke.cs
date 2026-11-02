using System.Globalization;
using System.Reflection;
using RhinoSplat.Core;

var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}
AlignmentPoint Apply(ColmapSimilarityTransform t, AlignmentPoint p)
{
    var q = t.Apply(p.X, p.Y, p.Z); return new(q.X, q.Y, q.Z);
}
double Distance(AlignmentPoint a, AlignmentPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
var source = new[] { new AlignmentPoint(1, 2, 3), new AlignmentPoint(4, -2, 5), new AlignmentPoint(-3, 1, 8) };
var random = new Random(1931);
ColmapSimilarityTransform RandomTransform()
{
    var q = Enumerable.Range(0, 4).Select(_ => random.NextDouble() - .5).ToArray();
    var norm = Math.Sqrt(q.Sum(x => x * x));
    var w = q[0] / norm; var x = q[1] / norm; var y = q[2] / norm; var z = q[3] / norm;
    return new(Math.Pow(10, random.NextDouble() * 8 - 4),
        1 - 2 * (y*y + z*z), 2 * (x*y - w*z), 2 * (x*z + w*y),
        2 * (x*y + w*z), 1 - 2 * (x*x + z*z), 2 * (y*z - w*x),
        2 * (x*z - w*y), 2 * (y*z + w*x), 1 - 2 * (x*x + y*y),
        random.NextDouble() * 1000, random.NextDouble() * 1000, random.NextDouble() * 1000);
}
for (var trial = 0; trial < 250; trial++)
{
    var expected = RandomTransform();
    var target = source.Select(p => Apply(expected, p)).ToArray();
    Check(ThreePointAlignment.TryFit(source, target, out var actual, out var rms, out var error), error);
    Check(rms < 1e-8, "Exact similarity residual");
    // Off-plane points exercise the recovered handedness and all rotation axes.
    var probe = new AlignmentPoint(11, -7, -3);
    Check(Distance(Apply(expected, probe), Apply(actual, probe)) < 1e-7, "Full transform including off-plane camera centre");
    Check(Math.Abs(actual.Scale / expected.Scale - 1) < 1e-8, "Uniform scale");
    foreach (var order in new[] { new[] { 2, 0, 1 }, new[] { 1, 0, 2 } })
    {
        Check(ThreePointAlignment.TryFit(order.Select(i => source[i]).ToArray(), order.Select(i => target[i]).ToArray(), out var reordered, out _, out error), error);
        Check(Distance(Apply(actual, probe), Apply(reordered, probe)) < 1e-7, "Paired permutations preserve fit");
    }
}
Check(ThreePointAlignment.TryFit(source, source, out var same, out _, out _)
    && Distance(Apply(same, new(4, 7, 9)), new(4, 7, 9)) < 1e-12, "Identity");
var noisy = source.Select(p => Apply(new(2, 0,-1,0, 1,0,0, 0,0,1, 10,20,30), p)).ToArray();
noisy[2] += new AlignmentPoint(.2, -.15, .1);
Check(ThreePointAlignment.TryFit(source, noisy, out var fit, out var residual, out _)
    && residual > .001 && residual < .2, "Noisy references report residual");
var determinant = fit.R00 * (fit.R11 * fit.R22 - fit.R12 * fit.R21)
    - fit.R01 * (fit.R10 * fit.R22 - fit.R12 * fit.R20) + fit.R02 * (fit.R10 * fit.R21 - fit.R11 * fit.R20);
Check(Math.Abs(determinant - 1) < 1e-12, "Proper rotation, no reflection");
var residualCentroid = source.Select((p, i) => Apply(fit, p) - noisy[i]).Aggregate(new AlignmentPoint(), (a, b) => a + b);
Check(Distance(residualCentroid, new()) < 1e-12, "Least-squares translation aligns centroids");
// Independent optimality check: random small rotations, translations and scales cannot improve the fitted error.
for (var i = 0; i < 1000; i++)
{
    var theta = (random.NextDouble() - .5) * .1;
    var c = Math.Cos(theta); var s = Math.Sin(theta);
    var scale = 1 + (random.NextDouble() - .5) * .02;
    var offset = new AlignmentPoint(random.NextDouble() * .02 - .01, random.NextDouble() * .02 - .01, random.NextDouble() * .02 - .01);
    double squared = 0;
    for (var j = 0; j < 3; j++)
    {
        var p = Apply(fit, source[j]);
        var perturbed = new AlignmentPoint(c * p.X - s * p.Y, s * p.X + c * p.Y, p.Z) * scale + offset;
        squared += Math.Pow(Distance(perturbed, noisy[j]), 2);
    }
    Check(Math.Sqrt(squared / 3) >= residual - 1e-10, "No perturbation improves best fit");
}
foreach (var invalid in new[] {
    Array.Empty<AlignmentPoint>(), new[] { source[0], source[0], source[2] },
    new[] { new AlignmentPoint(0,0,0), new AlignmentPoint(1,0,0), new AlignmentPoint(2,0,0) },
    new[] { new AlignmentPoint(0,0,0), new AlignmentPoint(1,0,0), new AlignmentPoint(2,1e-9,0) },
    new[] { source[0], new AlignmentPoint(double.NaN,0,0), source[2] },
    new[] { source[0], new AlignmentPoint(double.PositiveInfinity,0,0), source[2] } })
{
    Check(!ThreePointAlignment.TryFit(invalid, source, out _, out _, out _), "Invalid source rejected");
    Check(!ThreePointAlignment.TryFit(source, invalid, out _, out _, out _), "Invalid reference rejected");
}
var huge = source.Select(p => p + new AlignmentPoint(1e9, -1e9, 1e9)).ToArray();
Check(ThreePointAlignment.TryFit(huge, source, out var shifted, out var shiftedError, out _) && shiftedError < 1e-5, "Large coordinate offset");

// Exercise the actual COLMAP point/pose writers with the quick fit, including rotations.
var directory = Path.Combine(Path.GetTempPath(), "Splatypus-QuickAlign-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var poses = Path.Combine(directory, "images.txt"); var points = Path.Combine(directory, "points3D.txt");
File.WriteAllText(poses, "7 1 0 0 0 -1 -2 -3 4 image.jpg\n15 20 9\n");
File.WriteAllText(points, "9 1 2 3 255 128 0 0.2 7 0\n");
var posesOut = Path.Combine(directory, "images-out.txt"); var pointsOut = Path.Combine(directory, "points-out.txt");
typeof(ColmapModelTransformRunner).GetMethod("TransformImages", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { poses, posesOut, fit });
typeof(ColmapModelTransformRunner).GetMethod("TransformPoints", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { points, pointsOut, fit });
var pose = File.ReadAllLines(posesOut)[0].Split(' ');
var data = File.ReadAllLines(pointsOut)[0].Split(' ');
double Value(string text) => double.Parse(text, CultureInfo.InvariantCulture);
var alignedPoint = new AlignmentPoint(Value(data[1]), Value(data[2]), Value(data[3]));
var qw = Value(pose[1]); var qx = Value(pose[2]); var qy = Value(pose[3]); var qz = Value(pose[4]);
var tx = Value(pose[5]); var ty = Value(pose[6]); var tz = Value(pose[7]);
var cameraCenter = new AlignmentPoint(
    -((1 - 2*(qy*qy+qz*qz))*tx + 2*(qx*qy+qw*qz)*ty + 2*(qx*qz-qw*qy)*tz),
    -(2*(qx*qy-qw*qz)*tx + (1-2*(qx*qx+qz*qz))*ty + 2*(qy*qz+qw*qx)*tz),
    -(2*(qx*qz+qw*qy)*tx + 2*(qy*qz-qw*qx)*ty + (1-2*(qx*qx+qy*qy))*tz));
Check(Distance(cameraCenter, alignedPoint) < 1e-10, "COLMAP camera centre and sparse point share quick alignment");
Check(Distance(alignedPoint, Apply(fit, source[0])) < 1e-10, "Written points agree with fit");
Check(File.ReadAllLines(posesOut)[1] == "15 20 9" && data.Skip(4).SequenceEqual(new[] { "255", "128", "0", "0.2", "7", "0" }), "Observations, colours, errors and tracks preserved");
Console.WriteLine($"PASS: {checks} quick alignment checks, including 250 exact similarities, noisy fit, degeneracy and COLMAP camera/point agreement.");
