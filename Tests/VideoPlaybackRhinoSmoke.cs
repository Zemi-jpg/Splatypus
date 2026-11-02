using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoSplat.Core;
using RhinoSplat.Grasshopper;

internal static class VideoPlaybackRhinoSmoke
{
    [STAThread]
    public static void Main(string[] args)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        var root = Path.Combine(Path.GetTempPath(), "Splatypus-video-rhino-" + Guid.NewGuid().ToString("N"));
        var enabled = GH_Document.EnableSolutions;
        GH_Document.EnableSolutions = true;
        using var document = new GH_Document { Enabled = true };
        var canvas = global::Grasshopper.Instances.ActiveCanvas ?? throw new Exception("Test Grasshopper canvas required.");
        var previousDocument = canvas.Document;
        canvas.Document = document;
        var rhino = RhinoDoc.ActiveDoc ?? throw new Exception("Test Rhino document required.");
        var view = rhino.Views.ActiveView ?? throw new Exception("Test Rhino view required.");
        using var original = new ViewportInfo(view.ActiveViewport);
        var target = view.ActiveViewport.CameraTarget;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images"));
            var model = Path.Combine(root, "colmap", "sparse-text"); Directory.CreateDirectory(model);
            var sources = new[] { new MediaSourceRecord("first.mp4", "sp_media_0001_first", true), new MediaSourceRecord("second.mov", "sp_media_0002_second", true), new MediaSourceRecord("photo.jpg", "sp_media_0003_photo", false) };
            new MediaImportManifest(1, 2, sources).Save(root);
            var imageText = "";
            for (var i = 0; i < 3; i++)
            {
                var name = $"sp_media_0001_first_{i + 1:D6}.jpg";
                File.WriteAllText(Path.Combine(root, "images", name), "fixture");
                imageText += $"{i + 1} 1 0 0 0 {-i * 10} 0 0 1 {name}\n\n";
            }
            File.WriteAllText(Path.Combine(root, "images", "sp_media_0003_photo.jpg"), "fixture");
            File.WriteAllText(Path.Combine(model, "cameras.txt"), "1 PINHOLE 800 600 400 300 400 300\n");
            File.WriteAllText(Path.Combine(model, "images.txt"), imageText);
            var frames = new VideoFramesComponent(); frames.CreateAttributes(); document.AddObject(frames, false);
            ((Param_String)frames.Params.Input[0]).PersistentData.Append(new GH_String(root));
            document.NewSolution(true);
            var output = frames.Params.Output[0].VolatileData;
            Check(output.PathCount == 2 && output.DataCount == 3, "Frame output has two branches including the empty video; still omitted");
            Check(frames.Params.Output[4].VolatileData.AllData(true).OfType<VideoCameraTrackGoo>().Count() == 2, "Both video tracks use native Grasshopper goo");
            var player = new VideoPlaybackComponent(); player.CreateAttributes(); document.AddObject(player, false);
            player.Params.Input[0].AddSource(frames.Params.Output[4]);
            void Number(int input, double value) { var p = (Param_Number)player.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Number(value)); p.ExpireSolution(false); }
            void Boolean(int input, bool value) { var p = (Param_Boolean)player.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Boolean(value)); p.ExpireSolution(false); }
            void Integer(int input, int value) { var p = (Param_Integer)player.Params.Input[input]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Integer(value)); p.ExpireSolution(false); }
            document.NewSolution(true);
            Check(view.ActiveViewport.CameraLocation.DistanceTo(original.CameraLocation) < 1e-8, "Connecting playback does not change the viewport");
            Check(player.Params.Output[0].VolatileData.AllData(true).Single() is GH_Plane, "Track connection publishes a camera plane");
            Number(3, 0.25); Boolean(6, true); Boolean(7, false); document.NewSolution(true);
            Check(view.ActiveViewport.CameraLocation.DistanceTo(new Point3d(5, 0, 0)) < 1e-8, "Paused preview seeks to interpolated camera position; actual="
                + view.ActiveViewport.CameraLocation + "; status=" + string.Join(";", player.Params.Output[4].VolatileData.AllData(true))
                + "; warnings=" + string.Join(";", player.RuntimeMessages(GH_RuntimeMessageLevel.Warning)));
            Check(Vector3d.VectorAngle(view.ActiveViewport.CameraDirection, Vector3d.ZAxis) < 1e-8, "Viewport looks along COLMAP positive Z");
            Check(Vector3d.VectorAngle(view.ActiveViewport.CameraUp, -Vector3d.YAxis) < 1e-8, "Viewport camera up uses negative image Y");
            using (var projection = new ViewportInfo(view.ActiveViewport))
            {
                projection.GetFrustum(out _, out _, out _, out var top, out var near, out _);
                Check(Math.Abs(top / near - 1) < 1e-6 && !projection.IsParallelProjection, "Camera intrinsics become a perspective frustum with correct vertical field of view");
            }
            Boolean(6, false); Boolean(2, true); document.NewSolution(true);
            var started = ((GH_Number)player.Params.Output[1].VolatileData.AllData(true).Single()).Value;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 160) { Thread.Sleep(10); RhinoApp.Wait(); }
            var later = ((GH_Number)player.Params.Output[1].VolatileData.AllData(true).Single()).Value;
            Check(later > started, "Scheduled playback advances on the Rhino UI thread without an external Timer");
            Boolean(2, false); document.NewSolution(true);
            var paused = ((GH_Number)player.Params.Output[1].VolatileData.AllData(true).Single()).Value;
            Thread.Sleep(50); player.ExpireSolution(false); document.NewSolution(false);
            Check(((GH_Number)player.Params.Output[1].VolatileData.AllData(true).Single()).Value == paused, "Pause holds playback time");
            Boolean(8, true); document.NewSolution(true);
            Check(view.ActiveViewport.CameraLocation.DistanceTo(original.CameraLocation) < 1e-8
                && view.ActiveViewport.CameraTarget.DistanceTo(target) < 1e-8, "Restore recovers original camera and target");
            Boolean(8, false); Integer(1, 1); document.NewSolution(true);
            Check(player.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("no registered cameras")), "Selecting empty video reports why it cannot play");
            Integer(1, 99); document.NewSolution(true);
            Check(player.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Video Index")), "Invalid video index is handled without changing view");
            var saved = new GH_LooseChunk("Playback"); Check(player.Write(saved), "Playback serializes");
            var restored = new VideoPlaybackComponent(); restored.CreateAttributes(); restored.Read(saved);
            Check((bool)typeof(VideoPlaybackComponent).GetField("_mustReset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restored)!, "Reopened playback requires Play reset");
            void Pump(int milliseconds)
            {
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < milliseconds) { Thread.Sleep(10); RhinoApp.Wait(); }
            }
            var aligned = Path.Combine(root, "colmap", "aligned model", "text"); Directory.CreateDirectory(aligned);
            File.Copy(Path.Combine(model, "cameras.txt"), Path.Combine(aligned, "cameras.txt"));
            File.WriteAllText(Path.Combine(aligned, "images.txt"), imageText.Replace(" 0 0 0 1 ", " -100 0 0 1 "));
            File.WriteAllText(Path.Combine(aligned, VideoModelSelection.RevisionFile), VideoModelSelection.Revision(root));
            Pump(1300);
            var selected = ((VideoCameraTrackGoo)frames.Params.Output[4].VolatileData.AllData(true).First()).Value;
            Check(Math.Abs(selected.Keys[0].Pose.Centre.X - 100) < 1e-8, "Saved alignment is selected and reloaded automatically without Refresh");
            File.WriteAllText(Path.Combine(model, "images.txt"), imageText.Replace(" 0 0 0 1 ", " -200 0 0 1 "));
            Pump(1300);
            selected = ((VideoCameraTrackGoo)frames.Params.Output[4].VolatileData.AllData(true).First()).Value;
            Check(Math.Abs(selected.Keys[0].Pose.Centre.X - 100) < 1e-8, "Existing aligned model remains selected when base metadata changes");
            Check(frames.Params.Input.Count == 3 && frames.Params.Input[2].Name == "Model" && frames.Params.Input[2].Optional, "Model override is optional alongside Dataset and Media");
            var modelInput = (Param_String)frames.Params.Input[2];
            modelInput.PersistentData.Append(new GH_String(model)); modelInput.ExpireSolution(false); document.NewSolution(false);
            selected = ((VideoCameraTrackGoo)frames.Params.Output[4].VolatileData.AllData(true).First()).Value;
            Check(Math.Abs(selected.Keys[0].Pose.Centre.X - 200) < 1e-8, "Model input overrides automatic aligned selection in Grasshopper");
            modelInput.PersistentData.Clear(); modelInput.ExpireSolution(false); document.NewSolution(false);
            selected = ((VideoCameraTrackGoo)frames.Params.Output[4].VolatileData.AllData(true).First()).Value;
            Check(Math.Abs(selected.Keys[0].Pose.Centre.X - 100) < 1e-8, "Clearing Model restores automatic aligned selection");
            var oldFrames = new VideoFramesComponent(); oldFrames.CreateAttributes();
            while (oldFrames.Params.Input.Count > 1) oldFrames.Params.UnregisterInputParameter(oldFrames.Params.Input[1], true);
            var oldModel = new Param_String { Name = "Text Model" };
            oldModel.PersistentData.Append(new GH_String(model)); oldFrames.Params.RegisterInputParam(oldModel);
            var oldMedia = new Param_String { Name = "Legacy Videos", Access = GH_ParamAccess.list };
            oldMedia.PersistentData.Append(new GH_String("old mixed media folder"));
            oldFrames.Params.RegisterInputParam(oldMedia);
            oldFrames.Params.RegisterInputParam(new Param_Number { Name = "Legacy FPS" });
            oldFrames.Params.RegisterInputParam(new Param_Boolean { Name = "Refresh" });
            var legacyChunk = new GH_LooseChunk("OldFrames"); oldFrames.Write(legacyChunk);
            var migrated = new VideoFramesComponent(); migrated.CreateAttributes(); migrated.Read(legacyChunk); document.AddObject(migrated, false);
            Check(migrated.Params.Input.Count == 3 && migrated.Params.Input[1].Name == "Media"
                && migrated.Params.Input[1].InstanceGuid == oldMedia.InstanceGuid
                && ((Param_String)migrated.Params.Input[1]).PersistentData.AllData(true).OfType<GH_String>().Single().Value == "old mixed media folder",
                "Saved component retains media data while removing FPS and Refresh");
            Check(migrated.Params.Input[2].Name == "Model" && migrated.Params.Input[2].InstanceGuid == oldModel.InstanceGuid
                && ((Param_String)migrated.Params.Input[2]).PersistentData.AllData(true).OfType<GH_String>().Single().Value == model,
                "Older Text Model input migrates to Model without losing its identity or saved path");
            File.WriteAllText(Path.Combine(aligned, "cameras.txt"), "1 UNKNOWN 800 600 300 400 300\n");
            Pump(1300);
            Check(frames.Params.Output[0].VolatileData.DataCount == 3 && frames.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Unsupported")), "Frame groups remain available when camera model is unsupported");
            var olderRoot = Path.Combine(root, "older"); var originalMedia = Path.Combine(root, "original-media");
            Directory.CreateDirectory(Path.Combine(olderRoot, "images")); Directory.CreateDirectory(originalMedia);
            File.WriteAllText(Path.Combine(olderRoot, "images", "sp_media_0001_clip_000001.jpg"), "frame");
            File.WriteAllText(Path.Combine(originalMedia, "photo.jpg"), "still");
            var older = new VideoFramesComponent(); older.CreateAttributes(); document.AddObject(older, false);
            ((Param_String)older.Params.Input[0]).PersistentData.Append(new GH_String(olderRoot));
            ((Param_String)older.Params.Input[1]).PersistentData.Append(new GH_String(originalMedia));
            document.NewSolution(true);
            Check(!older.Params.Output[4].VolatileData.AllData(true).Any(), "Mixed media source folder initially contains no videos");
            File.WriteAllText(Path.Combine(originalMedia, "clip.mp4"), "video");
            Pump(1300);
            Check(older.Params.Output[4].VolatileData.AllData(true).OfType<VideoCameraTrackGoo>().Count() == 1,
                "Adding a video to a mixed source folder is discovered without a Refresh input");
            document.RemoveObject(older, false);
            Check(!(bool)typeof(VideoFramesComponent).GetField("_subscribed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(older)!,
                "Removing Video Frames releases automatic update subscriptions");
            Console.WriteLine($"SUCCESS: {checks} native Rhino video checks.");
        }
        finally
        {
            view.ActiveViewport.SetViewProjection(original, false); view.ActiveViewport.SetCameraTarget(target, false); view.Redraw();
            canvas.Document = previousDocument;
            GH_Document.EnableSolutions = enabled;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
