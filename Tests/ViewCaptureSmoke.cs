using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Runtime.InProcess;
using RhinoSplat.Core;
using RhinoSplat.Grasshopper;
using Point = System.Drawing.Point;

internal static class ViewCaptureSmoke
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [STAThread]
    public static void Main(string[] args)
    {
        using var core = args.Contains("--in-rhino") ? null : new RhinoCore(new[] { "/NOSPLASH" }, WindowStyle.Hidden);
        using var rhino = RhinoDoc.Create(null);
        // Live renderer checks require a real painted view. A hidden Rhino
        // window suppresses paints and cannot exercise camera-dependent caches.
        ShowWindow(RhinoApp.MainWindowHandle(), 9);
        var view = rhino.Views.ActiveView ?? throw new Exception("Native Rhino view required.");
        var viewport = view.ActiveViewport;
        view.Redraw(); RhinoApp.Wait();
        using var original = new ViewportInfo(viewport);
        var originalTarget = viewport.CameraTarget; var originalSize = viewport.Size; var originalMode = viewport.DisplayMode.Id;
        var root = Path.Combine(Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.GetTempPath()), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); count++; }
        var orphan = DisplayModeDescription.CopyDisplayMode(originalMode, "SP Capture " + Guid.NewGuid().ToString("N"));
        var userMode = DisplayModeDescription.CopyDisplayMode(originalMode, "SP Capture My custom mode");
        viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(orphan);
        ViewportCaptureSession.CleanupAbandonedModes();
        Check(DisplayModeDescription.GetDisplayMode(orphan) is null
            && viewport.DisplayMode.Id == DisplayModeDescription.RenderedId,
            "Startup cleanup removes an active orphan and restores a usable built-in display mode");
        Check(DisplayModeDescription.GetDisplayMode(userMode) is not null,
            "Cleanup preserves user display modes with similar names");
        viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(originalMode);
        DisplayModeDescription.DeleteDisplayMode(userMode);
        string FreshRoot() => Path.Combine(root, "project-" + Guid.NewGuid().ToString("N"));
        void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { Check(true, message); return; } throw new Exception(message); }
        bool AdvanceFrame(ViewportCaptureSession session)
        {
            var initial = session.Count;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (session.Count == initial)
            {
                if (clock.Elapsed.TotalSeconds > 15) throw new Exception("Timed out: " + session.Phase);
                var complete = session.CaptureNext();
                if (session.Count != initial) return complete;
                Thread.Sleep(20); RhinoApp.Wait();
            }
            return session.Complete;
        }
        void Restored() => Check(viewport.CameraLocation.DistanceTo(original.CameraLocation) < 1e-8
            && (viewport.CameraDirection - original.CameraDirection).Length < 1e-8
            && viewport.CameraTarget.DistanceTo(originalTarget) < 1e-8 && viewport.Size == originalSize
            && viewport.DisplayMode.Id == originalMode, $"Original viewport, target, size and display mode restored (position delta {viewport.CameraLocation.DistanceTo(original.CameraLocation)}, direction delta {Vector3d.VectorAngle(viewport.CameraDirection, original.CameraDirection)}, target delta {viewport.CameraTarget.DistanceTo(originalTarget)}, size {viewport.Size}/{originalSize}, mode {viewport.DisplayMode.Id}/{originalMode})");
        var frames = new[] { Plane.WorldXY, new Plane(new Point3d(1, 2, 3), Vector3d.YAxis, Vector3d.ZAxis) };
        var mesh = Mesh.CreateFromBox(new BoundingBox(new Point3d(1.5, -.5, -10), new Point3d(2.5, .5, -9)), 1, 1, 1);
        rhino.Objects.AddMesh(mesh, new ObjectAttributes { ObjectColor = Color.Red, ColorSource = ObjectColorSource.ColorFromObject });
        using var conduit = new MarkerConduit { Enabled = true };
        Reject(() => new ViewportCaptureSession(rhino, view, Array.Empty<Plane>(), 360, 240, 35, "Shaded", Color.White, FreshRoot()), "Empty frames rejected before writing");
        Reject(() => new ViewportCaptureSession(rhino, view, new[] { Plane.Unset }, 360, 240, 35, "Shaded", Color.White, FreshRoot()), "Invalid planes rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 0, 240, 35, "Shaded", Color.White, FreshRoot()), "Invalid resolution rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, double.NaN, "Shaded", Color.White, FreshRoot()), "Nonfinite lens rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "missing-mode", Color.White, FreshRoot()), "Unknown mode rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Raytraced", Color.White, FreshRoot()), "Unconverged progressive mode rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.Transparent, root), "Nonopaque background rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, "relative"), "Relative destination rejected");
        Reject(() => new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, root, double.NaN), "Invalid renderer wait rejected");
        var originalModes = DisplayModeDescription.GetDisplayModes().Select(m => m.Id).OrderBy(id => id).ToArray();
        var datasets = new List<string>();
        foreach (var size in new[] { new Size(360, 240), new Size(240, 360), new Size(300, 300) })
        {
            string dataset;
            using (var session = new ViewportCaptureSession(rhino, view, frames, size.Width, size.Height, 35, "Shaded", Color.White, FreshRoot()))
            {
                dataset = session.DatasetPath;
                Check(!session.Complete && Directory.Exists(Path.Combine(dataset, "images")), "Capture writes directly into project images before completion");
                Check(!session.CaptureNext() && session.Count == 0 && view.Size == size
                    && viewport.CameraLocation.DistanceTo(frames[0].Origin) < 1e-8,
                    "First step animates and resizes the actual live view without capturing");
                Check(!viewport.DisplayMode.InMenu, "Temporary capture display mode is hidden from the viewport menu");
                Check(!session.CaptureNext() && session.Count == 0, "Capture yields while the live renderer settles");
                Check(!AdvanceFrame(session) && session.Count == 1, "Capture advances by one frame after the live wait");
                Check(AdvanceFrame(session) && session.Complete, "Batch publishes after last frame");
            }
            datasets.Add(dataset); Restored();
            using var image = new Bitmap(Path.Combine(dataset, "images", ViewportCaptureSession.ImageName(0)));
            Check(new FileInfo(Path.Combine(dataset, "images", ViewportCaptureSession.ImageName(0))).Length >= 16387,
                "Small PNGs include valid text metadata for pinned Brush's fixed-size header probe");
            Check(image.Size == size && image.GetPixel(1, 1).ToArgb() == Color.White.ToArgb(), $"PNG resolution and solid white background match requested values ({image.Size}, {image.GetPixel(1, 1)}; live draws {conduit.LiveDraws}, capture draws {conduit.CaptureDraws}, stale={conduit.StaleCamera})");
            var red = new List<Point>(); var blue = new List<Point>();
            for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R > 80 && pixel.R > pixel.G * 1.5 && pixel.R > pixel.B * 1.5) red.Add(new(x, y));
                if (pixel.B > 80 && pixel.B > pixel.R * 1.5 && pixel.B > pixel.G * 1.5) blue.Add(new(x, y));
            }
            Check(red.Count > 20, "Native Rhino mesh appears in capture");
            Check(blue.Count > 5, "Display-conduit preview appears in capture");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataset, "transforms.json")));
            var j = json.RootElement; var fx = j.GetProperty("fl_x").GetDouble(); var fy = j.GetProperty("fl_y").GetDouble();
            Console.WriteLine($"Projection {size}: fx={fx}, fy={fy}, red centre={red.Average(p => p.X):0.00},{red.Average(p => p.Y):0.00}, blue centre={blue.Average(p => p.X):0.00},{blue.Average(p => p.Y):0.00}");
            Check(Math.Abs(fx - fy) < 1e-6, "Calibration retains square pixels at requested aspect ratio");
            Check(Math.Abs(blue.Average(p => p.Y) - (size.Height / 2.0 - fy * 2 / 10)) < 3
                && Math.Abs(blue.Average(p => p.X) - size.Width / 2.0) < 3, "Saved intrinsics project a world marker into the actual captured pixels");
            var active = new ActiveDataset(dataset); active.Validate(); ActiveDataset.ValidateText(active.Text, active.Images);
            var read = VideoCameraReader.Read(active.Text);
            for (var i = 0; i < frames.Length; i++)
            {
                var pose = read[ViewportCaptureSession.ImageName(i)]; var f = frames[i];
                Check(new Point3d(pose.Centre.X, pose.Centre.Y, pose.Centre.Z).DistanceTo(f.Origin) < 1e-8
                    && new Vector3d(pose.Forward.X, pose.Forward.Y, pose.Forward.Z).IsParallelTo(-f.ZAxis) == 1
                    && new Vector3d(pose.Up.X, pose.Up.Y, pose.Up.Z).IsParallelTo(f.YAxis) == 1, "COLMAP pose roundtrip retains camera centre, direction and roll");
            }
            using var cameras = new BinaryReader(File.OpenRead(Path.Combine(active.Sparse, "cameras.bin")));
            Check(cameras.ReadUInt64() == 2 && cameras.ReadInt32() == 1 && cameras.ReadInt32() == 1
                && cameras.ReadUInt64() == (ulong)size.Width && cameras.ReadUInt64() == (ulong)size.Height
                && Math.Abs(cameras.ReadDouble() - fx) < 1e-8, "Binary camera schema agrees with text/JSON calibration");
            var prepared = BrushDatasetPreparer.Prepare(dataset);
            Check(prepared.Succeeded, "Captured dataset passes the existing SP Brush Dataset handoff: " + prepared.Status);
            Check(prepared.DatasetPath == Path.Combine(dataset, "brush", "dataset")
                && Directory.Exists(Path.Combine(prepared.DatasetPath, "images"))
                && !Directory.Exists(Path.Combine(dataset, "brush-dataset")),
                "Brush dataset is prepared directly under project brush/dataset");
            var rejectedExisting = false;
            try { using var duplicate = new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, dataset); }
            catch (IOException) { rejectedExisting = true; }
            Check(rejectedExisting && File.Exists(Path.Combine(dataset, "images", ViewportCaptureSession.ImageName(0))),
                "Existing project captures are rejected without overwriting their images");
        }
        Check(datasets.Distinct().Count() == 3, "Repeated runs preserve earlier batches in separate folders");
        using (var cancelled = new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, FreshRoot()))
        { cancelled.CaptureNext(); Check(!cancelled.Complete, "Cancelled partial batch has no published dataset"); }
        Restored();
        using (var gradient = new ViewportCaptureSession(rhino, view, new[] { Plane.WorldXY }, 240, 160, 35,
            "Rendered", Color.FromArgb(110, 85, 60), FreshRoot(), 0.1, Color.FromArgb(140, 175, 210)))
        {
            AdvanceFrame(gradient);
            using var image = new Bitmap(Path.Combine(gradient.DatasetPath, "images", ViewportCaptureSession.ImageName(0)));
            var upper = image.GetPixel(1, 3); var lower = image.GetPixel(1, image.Height - 4);
            Check(upper.B > lower.B + 80 && upper.G > lower.G + 60,
                "Rendered gradient capture retains different top and bottom colors");
        }
        Restored();
        Check(!Directory.EnumerateDirectories(root, ".sp-capture-*").Any(), "Direct-root capture creates no private staging folders");
        var failed = false;
        using (var failure = new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, FreshRoot()))
        {
            var stage = failure.DatasetPath;
            Directory.CreateDirectory(Path.Combine(stage, "images", ViewportCaptureSession.ImageName(0)));
            try { AdvanceFrame(failure); } catch (Exception) { failed = true; }
            Check(failed && !failure.Complete, "Write failure never publishes an incomplete dataset");
        }
        Restored();
        using (var rendered = new ViewportCaptureSession(rhino, view, new[] { Plane.WorldXY }, 240, 160, 35, "Rendered", Color.CornflowerBlue, FreshRoot()))
        {
            AdvanceFrame(rendered);
            using var image = new Bitmap(Path.Combine(rendered.DatasetPath, "images", ViewportCaptureSession.ImageName(0)));
            var pixel = image.GetPixel(1, 1); var expected = Color.CornflowerBlue;
            Check(Math.Abs(pixel.R - expected.R) <= 2 && Math.Abs(pixel.G - expected.G) <= 2 && Math.Abs(pixel.B - expected.B) <= 2
                && pixel.A == 255, "Default Rendered mode respects the requested background within color-pipeline rounding");
        }
        Restored();
        Check(DisplayModeDescription.GetDisplayModes().Select(m => m.Id).OrderBy(id => id).SequenceEqual(originalModes),
            "Temporary live display modes are removed after success, cancellation and failure");
        Check(conduit.LiveDraws >= 7 && conduit.CaptureDraws == 0,
            "Screen capture uses live draws without invoking the off-screen view-capture renderer");
        using (var changed = new ViewportCaptureSession(rhino, view, frames, 360, 240, 35, "Shaded", Color.White, FreshRoot()))
        {
            changed.CaptureNext(); viewport.SetCameraLocation(new Point3d(10, 20, 30), false);
            var rejected = false;
            try { changed.CaptureNext(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && changed.Count == 0, "User navigation stops capture before saving mismatched calibration");
        }
        Restored();
        using var gh = new GH_Document { Enabled = true };
        var cameraComponent = new CameraPlanesComponent(); cameraComponent.CreateAttributes(); gh.AddObject(cameraComponent, false);
        ((Param_String)cameraComponent.Params.Input[0]).PersistentData.Append(new GH_String(datasets[0]));
        gh.NewSolution(true);
        var outputFrames = cameraComponent.Params.Output[4].VolatileData.AllData(true).OfType<GH_Plane>().ToArray();
        Check(outputFrames.Length == 2 && outputFrames[0].Value.XAxis.IsParallelTo(Vector3d.XAxis) == 1
            && outputFrames[0].Value.YAxis.IsParallelTo(Vector3d.YAxis) == 1, "SP Camera Planes produces directly usable right/up/back Frames");
        var legacy = new CameraPlanesComponent(); legacy.CreateAttributes();
        legacy.Params.UnregisterOutputParameter(legacy.Params.Output[4], true); legacy.Params.OnParametersChanged();
        var ids = legacy.Params.Output.Select(p => p.InstanceGuid).ToArray();
        var chunk = new GH_LooseChunk("LegacyCameras"); legacy.Write(chunk);
        var restoredCamera = new CameraPlanesComponent(); restoredCamera.CreateAttributes(); restoredCamera.Read(chunk);
        Check(restoredCamera.Params.Output.Count == 5 && restoredCamera.Params.Output.Take(4).Select(p => p.InstanceGuid).SequenceEqual(ids), "Saved four-output Camera Planes retains existing socket IDs and appends Frames");
        var preciseModel = Path.Combine(root, "precision"); Directory.CreateDirectory(preciseModel);
        File.WriteAllText(Path.Combine(preciseModel, "images.txt"), "1 1 0 0 0 -123456789.123456 0 0 1 sample.png\n\n");
        var sourceInput = (Param_String)cameraComponent.Params.Input[0]; sourceInput.PersistentData.Clear();
        sourceInput.PersistentData.Append(new GH_String(preciseModel)); sourceInput.ExpireSolution(false); gh.NewSolution(true);
        Check(Math.Abs(((GH_Plane)cameraComponent.Params.Output[4].VolatileData.AllData(true).Single()).Value.OriginX - 123456789.123456) < 1e-8,
            "Camera frame loading preserves double precision at large coordinates");
        var capture = new ViewCaptureComponent(); capture.CreateAttributes(); gh.AddObject(capture, false);
        var oldCapture = new ViewCaptureComponent(); oldCapture.CreateAttributes();
        oldCapture.Params.UnregisterInputParameter(oldCapture.Params.Input[10], true);
        oldCapture.Params.UnregisterInputParameter(oldCapture.Params.Input[9], true); oldCapture.Params.OnParametersChanged();
        var oldInputIds = oldCapture.Params.Input.Select(p => p.InstanceGuid).ToArray();
        var oldChunk = new GH_LooseChunk("OldCapture"); oldCapture.Write(oldChunk);
        var migrated = new ViewCaptureComponent(); migrated.CreateAttributes(); migrated.Read(oldChunk);
        Check(migrated.Params.Input.Count == 11 && migrated.Params.Input.Take(9).Select(p => p.InstanceGuid).SequenceEqual(oldInputIds)
            && ((Param_Number)migrated.Params.Input[9]).PersistentData.AllData(true).OfType<GH_Number>().Single().Value == 1,
            "Saved capture components retain all existing inputs and append Wait=1 second and optional Background Top");
        var solidCapture = new ViewCaptureComponent(); solidCapture.CreateAttributes();
        solidCapture.Params.UnregisterInputParameter(solidCapture.Params.Input[10], true); solidCapture.Params.OnParametersChanged();
        var solidIds = solidCapture.Params.Input.Select(p => p.InstanceGuid).ToArray();
        var solidChunk = new GH_LooseChunk("SolidCapture"); solidCapture.Write(solidChunk);
        var solidRestored = new ViewCaptureComponent(); solidRestored.CreateAttributes(); solidRestored.Read(solidChunk);
        Check(solidRestored.Params.Input.Count == 11 && solidRestored.Params.Input.Take(10).Select(p => p.InstanceGuid).SequenceEqual(solidIds)
            && solidRestored.Params.Input[10].Optional, "Ten-input solid capture definitions retain all socket IDs when gradient support is appended");
        ((Param_String)capture.Params.Input[6]).PersistentData.Clear();
        ((Param_String)capture.Params.Input[6]).PersistentData.Append(new GH_String(Path.Combine(root, "not-started")));
        gh.NewSolution(true);
        Check(!Directory.Exists(Path.Combine(root, "not-started")), "Untriggered component does not write files or alter the viewport");
        Restored();
        var canvas = global::Grasshopper.Instances.ActiveCanvas ?? throw new Exception("Test Grasshopper canvas required.");
        var previous = canvas.Document;
        var enableSolutions = GH_Document.EnableSolutions;
        GH_Document.EnableSolutions = true;
        canvas.Document = gh;
        try
        {
            void Boolean(int index, bool value) { if (index == 7 && value) { var destination = (Param_String)capture.Params.Input[6]; destination.PersistentData.Clear(); destination.PersistentData.Append(new GH_String(FreshRoot())); destination.ExpireSolution(false); } var p = (Param_Boolean)capture.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Boolean(value)); p.ExpireSolution(false); }
            void Integer(int index, int value) { var p = (Param_Integer)capture.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); p.ExpireSolution(false); }
            var input = (Param_Plane)capture.Params.Input[0];
            foreach (var f in frames) input.PersistentData.Append(new GH_Plane(f));
            Integer(1, 240); Integer(2, 160);
            var status = () => string.Join("; ", capture.Params.Output[3].VolatileData.AllData(true));
            void PumpUntil(Func<bool> done) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (!done()) { if (clock.Elapsed.TotalSeconds > 90) throw new Exception("Timed out waiting for real UI capture dispatch: " + status()); Thread.Sleep(10); RhinoApp.Wait(); } }
            var active = typeof(ViewCaptureComponent).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Boolean(7, true); gh.NewSolution(true);
            Check(active.GetValue(capture) is ViewportCaptureSession { Count: 0 }, "Button edge starts a snapshot batch without capture during SolveInstance: " + status());
            PumpUntil(() => active.GetValue(capture) is ViewportCaptureSession { Count: 1 });
            Check(active.GetValue(capture) is ViewportCaptureSession { Count: 1 }, "Posted UI callback captures one frame and leaves UI controls available");
            Boolean(8, true); gh.NewSolution(true);
            Check(active.GetValue(capture) is null && !ViewportCaptureSession.IsCapturing, "Cancel input stops the posted capture and releases ownership");
            Restored();
            Boolean(8, false); gh.NewSolution(true);
            Check(active.GetValue(capture) is null, "Held Run does not restart after cancellation");
            Boolean(7, false); gh.NewSolution(true); Boolean(7, true); gh.NewSolution(true);
            // Changing settings mid-batch must not modify the captured snapshot.
            Integer(1, 999); gh.NewSolution(true);
            PumpUntil(() => active.GetValue(capture) is null);
            gh.NewSolution(true);
            var completed = capture.Params.Output[0].VolatileData.AllData(true).OfType<GH_String>().ToArray();
            Check(completed.Length == 2 && capture.Params.Output[1].VolatileData.DataCount == 1, "Successful posted batch publishes Images and Dataset: " + status());
            using (var image = new Bitmap(completed[0].Value)) Check(image.Width == 240, "Mid-batch edits apply only to the next run");
            Restored();
            gh.NewSolution(true); Check(active.GetValue(capture) is null, "Held True never repeats a completed batch");
            var savedCapture = new GH_LooseChunk("Capture"); capture.Write(savedCapture);
            var reopened = new ViewCaptureComponent(); reopened.CreateAttributes(); reopened.Read(savedCapture); gh.AddObject(reopened, false);
            gh.NewSolution(true);
            Check(active.GetValue(reopened) is null && !reopened.Params.Output[1].VolatileData.AllData(true).OfType<GH_String>().Any(), "Reopened saved Run=True stays idle and withholds stale dataset outputs");
            Boolean(7, false); gh.NewSolution(true); Boolean(7, true); gh.NewSolution(true);
            capture.Locked = true; PumpUntil(() => active.GetValue(capture) is null); capture.Locked = false;
            Check(active.GetValue(capture) is null, "Locking the component cancels capture and restores the view"); Restored();
            Boolean(7, false); gh.NewSolution(true);
            input.PersistentData.Clear();
            for (var i = 0; i < 12; i++) input.PersistentData.Append(new GH_Plane(new Plane(new Point3d(i * .01, 0, 0), Vector3d.XAxis, Vector3d.YAxis)));
            input.ExpireSolution(false); Integer(1, 1920); Integer(2, 1280);
            var heartbeats = 0;
            using (var heartbeat = new System.Windows.Forms.Timer { Interval = 50 })
            {
                heartbeat.Tick += (_, _) => heartbeats++;
                heartbeat.Start(); Boolean(7, true); gh.NewSolution(true);
                PumpUntil(() => active.GetValue(capture) is null); gh.NewSolution(true);
                Check(capture.Params.Output[0].VolatileData.AllData(true).OfType<GH_String>().Count() == 12,
                    "Real dispatch completes a 12-frame Rendered batch with an oversized 1920 x 1280 request: " + status());
                var firstImage = capture.Params.Output[0].VolatileData.AllData(true).OfType<GH_String>().First().Value;
                using (var image = new Bitmap(firstImage))
                {
                    Check(image.Width <= originalSize.Width && image.Height <= originalSize.Height
                        && image.Width * 2 == image.Height * 3,
                        "Oversized screen capture fits inside the original viewport and retains exact 3:2 ratio");
                    using var calibration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(firstImage))!, "transforms.json")));
                    Check(calibration.RootElement.GetProperty("w").GetInt32() == image.Width
                        && calibration.RootElement.GetProperty("h").GetInt32() == image.Height,
                        "Camera calibration records the actual screen capture dimensions");
                }
                Check(heartbeats >= 12, "UI timer continues processing between full-resolution captures");
            }
            Restored();
            Boolean(7, false); gh.NewSolution(true); Boolean(7, true); gh.NewSolution(true);
            gh.RemoveObject(capture, false);
            Check(active.GetValue(capture) is null && !ViewportCaptureSession.IsCapturing, "Removing the component releases its queued callback and capture ownership"); Restored();
        }
        finally { canvas.Document = previous; GH_Document.EnableSolutions = enableSolutions; }
        Console.WriteLine($"SUCCESS: {count} capture checks. Artifacts: {root}");
    }

    private sealed class MarkerConduit : DisplayConduit, IDisposable
    {
        private Point3d _liveLocation;
        private Vector3d _liveDirection, _liveUp;
        private Size _liveSize;
        public int CaptureDraws;
        public int LiveDraws;
        public bool StaleCamera;
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e) => e.IncludeBoundingBox(new BoundingBox(new Point3d(-1, 1, -11), new Point3d(1, 3, -9)));
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (e.Display.IsInViewCapture)
            {
                CaptureDraws++;
                StaleCamera |= e.Viewport.CameraLocation.DistanceTo(_liveLocation) > 1e-8
                    || (e.Viewport.CameraDirection - _liveDirection).Length > 1e-8
                    || (e.Viewport.CameraUp - _liveUp).Length > 1e-8 || e.Viewport.Size != _liveSize;
            }
            else if (e.Viewport.Id == RhinoDoc.ActiveDoc.Views.ActiveView.ActiveViewportID)
            {
                LiveDraws++;
                _liveLocation = e.Viewport.CameraLocation; _liveDirection = e.Viewport.CameraDirection;
                _liveUp = e.Viewport.CameraUp; _liveSize = e.Viewport.Size;
            }
            var sphere = Mesh.CreateFromSphere(new Sphere(new Point3d(0, 2, -10), .2), 16, 16);
            e.Display.DrawMeshShaded(sphere, new DisplayMaterial(Color.Blue)); sphere.Dispose();
        }
        public void Dispose() => Enabled = false;
    }
}
