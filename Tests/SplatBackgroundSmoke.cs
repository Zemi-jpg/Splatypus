using System.Buffers.Binary;
using System.Drawing;
using System.Text;
using RhinoSplat.Core;

var root = Path.Combine(Path.GetTempPath(), "SP-background-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message)
{
    try { action(); } catch (Exception e) when (e is IOException or ArgumentException or FormatException) { Check(true, message); return; }
    throw new Exception(message);
}
string Rgb(string name, string body, int count) { var path = Path.Combine(root, name); File.WriteAllText(path, $"ply\nformat ascii 1.0\nelement vertex {count}\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n" + body); return path; }
var rgb = Rgb("rgb.ply", "0 1 2 120 150 180\n3 4 5 70 80 90\n", 2);
var parsed = SplatBackground.Read(rgb, 100);
Check(parsed.Total == 2 && parsed.Samples.Length == 2 && parsed.Samples[0].Color == Color.FromArgb(120,150,180) && parsed.Samples[1].Z == 5, "ASCII RGB colors and stored coordinates are preserved");
foreach (var big in new[] { false, true })
{
    var path = Path.Combine(root, big ? "big.ply" : "brush.ply");
    using (var writer = new BinaryWriter(File.Create(path)))
    {
        writer.Write(Encoding.ASCII.GetBytes($"ply\nformat binary_{(big ? "big" : "little")}_endian 1.0\ncomment test\nelement vertex 3\nproperty uchar ignored\nproperty float f_dc_0\nproperty double x\nproperty float y\nproperty float z\nproperty float opacity\nproperty float f_dc_1\nproperty float f_dc_2\nelement face 0\nproperty list uchar int vertex_indices\nend_header\n"));
        void Float(float n) { var bits=BitConverter.SingleToInt32Bits(n); writer.Write(big ? BinaryPrimitives.ReverseEndianness(bits) : bits); }
        void Double(double n) { var bits=BitConverter.DoubleToInt64Bits(n); writer.Write(big ? BinaryPrimitives.ReverseEndianness(bits) : bits); }
        for(var i=0;i<3;i++) { writer.Write((byte)7); Float(0); Double(123456789.25); Float(2); Float(-3); Float(i==0?-100:0); Float(1); Float(-1); }
    }
    var data = SplatBackground.Read(path,100);
    Check(data.Total==3 && data.Samples.Length==2 && data.Samples.All(s=>s.X==123456789.25 && s.Color.R==128 && s.Color.G==199 && s.Color.B==56 && s.Opacity==0.5), $"{(big?"Big":"Little")}-endian mixed scalar types, SH DC conversion and logit opacity work");
    var bytes=File.ReadAllBytes(path); File.WriteAllBytes(path,bytes[..^1]);
    Reject(()=>SplatBackground.Read(path,100), "Truncated binary body rejected before sampling");
}
var many=Rgb("many.ply",string.Concat(Enumerable.Range(0,10000).Select(i=>$"{i} 0 0 100 120 140\n")),10000);
var bounded=SplatBackground.Read(many,123);
Check(bounded.Samples.Length==123 && bounded.Samples.First().X<50 && bounded.Samples.Last().X>9950, "Sampling stays bounded and covers the entire file");
Reject(()=>SplatBackground.Read(many,0),"Invalid sample cap rejected");
var invalid=Rgb("invalid.ply","NaN 0 0 1 2 3\n",1);
Reject(()=>SplatBackground.Read(invalid,100),"All non-finite splats produce an actionable failure");
File.WriteAllText(invalid,"ply\nformat binary_little_endian 1.0\nelement vertex 1000000000\nproperty float x\nend_header\n");
Reject(()=>SplatBackground.Read(invalid,100),"Corrupt huge vertex declarations are rejected");
var sky=Color.FromArgb(140,170,200); var ground=Color.FromArgb(95,85,70);
var colors=Enumerable.Range(0,100).Select(i=>new BackgroundColorSample(sky,0.8,1))
    .Concat(Enumerable.Range(0,80).Select(i=>new BackgroundColorSample(ground,-0.8,1)))
    .Concat(Enumerable.Range(0,3).Select(i=>new BackgroundColorSample(Color.White,0.8,0.1))).ToArray();
var estimate=SplatBackground.Estimate(colors);
Check(estimate.Top==sky && estimate.Bottom==ground && estimate.Solid==sky,"Sky/ground gradient retains distinct colors and rejects bright outliers");
var one=SplatBackground.Estimate(new[]{new BackgroundColorSample(ground,0,1)});
Check(one.Top==ground && one.Bottom==ground && one.Solid==ground,"Sparse coverage falls back to a stable solid color");
Reject(()=>SplatBackground.Estimate(Array.Empty<BackgroundColorSample>()),"No visible colors fails instead of returning a fabricated background");
Console.WriteLine($"{checks} checks passed. Fixtures: {root}");
