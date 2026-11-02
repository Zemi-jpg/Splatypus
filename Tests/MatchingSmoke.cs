using System.Diagnostics;
using System.Runtime.InteropServices;
using RhinoSplat.Core;

// Controlled matcher verifies the command chosen by the production launcher.
if (args.Length > 0 && args[0].EndsWith("_matcher"))
{
    Console.WriteLine("MATCHER " + args[0]);
    return;
}
if (args.Contains("-h")) { Console.WriteLine("SiftMatching.use_gpu"); return; }
var root = Path.Combine(Path.GetTempPath(), "Splatypus-Matching-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "images"));
Directory.CreateDirectory(Path.Combine(root, "colmap"));
var database = Path.Combine(root, "colmap", "database.db");
var passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
void Sql(string sql)
{
    if (Native.Open(database, out var handle) != 0) throw new Exception("Cannot create SQLite test fixture");
    try { if (Native.Exec(handle, sql, IntPtr.Zero, IntPtr.Zero, out var error) != 0) { var message = Marshal.PtrToStringUTF8(error); Native.Free(error); throw new Exception(message); } }
    finally { Native.Close(handle); }
}
void Reject(Action action, string message)
{
    try { action(); } catch (ArgumentException) { Check(true, message); return; }
    throw new Exception(message);
}
string Blob(double x, double y, double z = 0) => "X'" + Convert.ToHexString(new[] { x, y, z }.SelectMany(BitConverter.GetBytes).ToArray()) + "'";
Sql("CREATE TABLE images(image_id INTEGER PRIMARY KEY, name TEXT); CREATE TABLE pose_priors(image_id INTEGER PRIMARY KEY, position BLOB);"
    + "INSERT INTO images VALUES(1,'sp_media_0001_clip_000001.png'),(2,'sp_media_0001_clip_000002.png');");
Check(ColmapDatabase.ReadImages(database).Count == 2, "Database reader reads actual SQLite image records without another tool.");
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Exhaustive, "Auto defaults to Exhaustive when provenance and positions are absent.");
Reject(() => ColmapMatching.Resolve(ColmapMatchingMode.Spatial, root), "Explicit Spatial refuses missing image positions.");
var source = new MediaSourceRecord("clip.mp4", "sp_media_0001_clip", true) { FrameTimes = new[] { 0.0, 2.0 } };
foreach (var number in new[] { 1, 2 }) File.WriteAllText(Path.Combine(root, "images", $"{source.Prefix}_{number:D6}.png"), "fixture");
new MediaImportManifest(1, 2, new[] { source }) { SkipNearDuplicates = true }.Save(root);
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Sequential, "Auto verifies a complete PNG video sequence, including deduplicated input.");
Sql("INSERT INTO images VALUES(3,'photo.jpg');");
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Exhaustive, "Mixed video and photographs fall back to Exhaustive.");
Sql("DELETE FROM images WHERE image_id=3; INSERT INTO pose_priors VALUES(1," + Blob(47, 8) + ");");
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Sequential, "Partial GPS coverage does not silently discard the unpositioned video frame.");
Sql("INSERT INTO pose_priors VALUES(2," + Blob(47.0001, 8.0001) + ");");
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Spatial, "Complete distinct GPS coverage takes precedence in Auto.");
var executable = Path.Combine(AppContext.BaseDirectory, "RhinoSplat.Matching.SmokeTest.exe");
var spatial = await ColmapExhaustiveMatchingRunner.RunAsync(executable, root, false, default, ColmapMatchingMode.Auto);
Check(spatial.Succeeded && spatial.Log.Contains("MATCHER spatial_matcher") && spatial.Log.Contains("Verified usable positions"), "The process launcher runs Spatial and explains the Auto choice.");
Sql("UPDATE pose_priors SET position=" + Blob(47, 8) + ";");
Reject(() => ColmapMatching.Resolve(ColmapMatchingMode.Spatial, root), "Identical positions cannot select meaningful spatial pairs.");
Sql("UPDATE pose_priors SET position=" + Blob(double.NaN, 8) + " WHERE image_id=2;");
Reject(() => ColmapMatching.Resolve(ColmapMatchingMode.Spatial, root), "Non-finite position data is rejected.");
var sequential = await ColmapExhaustiveMatchingRunner.RunAsync(executable, root, false, default, ColmapMatchingMode.Sequential);
Check(sequential.Succeeded && sequential.Log.Contains("MATCHER sequential_matcher"), "Explicit Sequential reaches the actual launcher.");
Reject(() => ColmapMatching.Resolve((ColmapMatchingMode)99, root), "Unknown matching values fail clearly.");
Sql("DROP TABLE pose_priors; ALTER TABLE images ADD COLUMN prior_tx REAL; ALTER TABLE images ADD COLUMN prior_ty REAL;"
    + "UPDATE images SET prior_tx=47+image_id*0.0001, prior_ty=8+image_id*0.0001;");
Check(ColmapMatching.Resolve(ColmapMatchingMode.Auto, root).Mode == ColmapMatchingMode.Spatial, "Older COLMAP image-prior schemas are supported.");

if (args.Length == 2)
{
    var colmap = args[0]; var ffmpeg = args[1];
    var liveRoot = Path.Combine(root, "live"); var media = Path.Combine(root, "media"); Directory.CreateDirectory(media);
    async Task Run(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await stdout; var errors = await stderr;
        if (process.ExitCode != 0) throw new Exception(errors);
    }
    await Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=128x128:r=4:d=1", "-c:v", "mpeg4", Path.Combine(media, "clip.mp4"));
    var imported = await FFmpegMediaImporter.RunAsync(ffmpeg, liveRoot, new[] { media }, 4, false, false, default, MediaImageFormat.Png, true);
    Check(imported.Succeeded, "Real FFmpeg prepares a PNG dataset with duplicate removal.");
    var extract = await ColmapFeatureExtractionRunner.RunAsync(colmap, liveRoot, false, default, new(128, 512));
    Check(extract.Succeeded && ColmapDatabase.ReadImages(Path.Combine(liveRoot, "colmap", "database.db")).Count == imported.ImportedImages.Count, "Pinned COLMAP extracts PNG features using the selected limits.");
    var matched = await ColmapExhaustiveMatchingRunner.RunAsync(colmap, liveRoot, false, default, ColmapMatchingMode.Auto);
    Check(matched.Succeeded && matched.Log.Contains("Matching: Sequential"), "Pinned COLMAP runs real Sequential matching chosen by Auto.");
    var liveDatabase = Path.Combine(liveRoot, "colmap", "database.db");
    var previous = database; database = liveDatabase;
    Sql("INSERT OR REPLACE INTO pose_priors(image_id,position,coordinate_system) VALUES(1," + Blob(47, 8) + ",0),(2," + Blob(47.0001, 8.0001) + ",0),(3," + Blob(47.0002, 8.0002) + ",0),(4," + Blob(47.0003, 8.0003) + ",0);");
    database = previous;
    matched = await ColmapExhaustiveMatchingRunner.RunAsync(colmap, liveRoot, false, default, ColmapMatchingMode.Spatial);
    Check(matched.Succeeded && matched.Log.Contains("Matching: Spatial"), "Pinned COLMAP runs Spatial using per-image priors in its real database.");

    // A real EXIF fixture exercises original JPEG -> import -> COLMAP GPS parsing -> Auto.
    var photo = Path.Combine(media, "gps1.jpg");
    await Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=128x128", "-frames:v", "1", photo);
    var pixels = File.ReadAllBytes(photo);
    byte[] GpsPhoto(uint seconds)
    {
        using var metadata = new MemoryStream(); using var writer = new BinaryWriter(metadata);
        writer.Write(new byte[] { (byte)'I', (byte)'I', 42, 0 }); writer.Write(8u);
        writer.Write((ushort)1); writer.Write((ushort)0x8825); writer.Write((ushort)4); writer.Write(1u); writer.Write(26u); writer.Write(0u);
        writer.Write((ushort)6);
        void Entry(ushort tag, ushort type, uint count, uint value) { writer.Write(tag); writer.Write(type); writer.Write(count); writer.Write(value); }
        Entry(1, 2, 2, (uint)'N'); Entry(2, 5, 3, 104); Entry(3, 2, 2, (uint)'E'); Entry(4, 5, 3, 128);
        Entry(5, 1, 1, 0); Entry(6, 5, 1, 152); writer.Write(0u);
        foreach (var value in new uint[] { 47, 1, 0, 1, seconds, 10000, 8, 1, 0, 1, seconds, 10000, 500, 1 }) writer.Write(value);
        var exif = new byte[] { 69, 120, 105, 102, 0, 0 }.Concat(metadata.ToArray()).ToArray();
        var length = exif.Length + 2;
        return pixels.Take(2).Concat(new byte[] { 255, 225, (byte)(length >> 8), (byte)length }).Concat(exif).Concat(pixels.Skip(2)).ToArray();
    }
    File.WriteAllBytes(photo, GpsPhoto(0)); var photo2 = Path.Combine(media, "gps2.jpg"); File.WriteAllBytes(photo2, GpsPhoto(3600));
    var gpsRoot = Path.Combine(root, "GPS photographs");
    imported = await FFmpegMediaImporter.RunAsync(ffmpeg, gpsRoot, new[] { photo, photo2 }, 2, false, false, default);
    Check(imported.Succeeded && File.ReadAllBytes(imported.ImportedImages[0]).SequenceEqual(File.ReadAllBytes(photo)), "JPEG import retains original EXIF metadata byte-for-byte.");
    extract = await ColmapFeatureExtractionRunner.RunAsync(colmap, gpsRoot, false, default, new(128, 512));
    Check(extract.Succeeded && ColmapDatabase.ReadImages(Path.Combine(gpsRoot, "colmap", "database.db")).All(image => image.HasPosition), "Pinned COLMAP reads preserved EXIF GPS coordinates from imported photographs.");
    matched = await ColmapExhaustiveMatchingRunner.RunAsync(colmap, gpsRoot, false, default, ColmapMatchingMode.Auto);
    Check(matched.Succeeded && matched.Log.Contains("Matching: Spatial"), "Auto chooses real Spatial matching for geotagged JPEG photographs.");
}
Console.WriteLine($"{passed} matching checks passed. Fixtures: {root}");

static class Native
{
    [DllImport("winsqlite3", EntryPoint = "sqlite3_open", CallingConvention = CallingConvention.Cdecl)] internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr handle);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)] internal static extern int Exec(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr state, out IntPtr error);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)] internal static extern int Close(IntPtr handle);
    [DllImport("winsqlite3", EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(IntPtr value);
}
