using System.Drawing;
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

internal static class AutoBackgroundRhinoSmoke
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var core = args.Contains("--in-rhino") ? null : new RhinoCore(new[] { "/NOSPLASH" }, WindowStyle.Hidden);
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        var sky = Color.FromArgb(140,175,210); var ground = Color.FromArgb(110,85,60);
        var splats = new List<BackgroundSplat>();
        for (var i=0;i<40;i++)
        {
            splats.Add(new((i/40.0-0.5)*16,8,-10,sky,1));
            splats.Add(new((i/40.0-0.5)*16,-8,-10,ground,1));
        }
        for (var i=0;i<1000;i++) splats.Add(new(0,0,-10,Color.Red,1));
        splats.Add(new(0,0,10,Color.White,1)); splats.Add(new(1000,0,-10,Color.White,1));
        var cameras = new[] { (Plane.WorldXY,1.0,1.0,false) };
        var colors = AutoBackgroundComponent.Estimate(splats.ToArray(),Transform.Identity,cameras,out var fallback);
        Check(colors.Top==sky && colors.Bottom==ground && colors.Count==80 && !fallback,
            "Camera boundaries exclude a dominant red foreground, offscreen points and points behind the camera");
        var moved = splats.Select(s=>s with { X=s.X+100 }).ToArray();
        var transformed = AutoBackgroundComponent.Estimate(moved,Transform.Translation(-100,0,0),cameras,out _);
        Check(transformed.Top==sky && transformed.Bottom==ground,"Alignment transform matches moved viewport splats");
        var interior = AutoBackgroundComponent.Estimate(new[]{new BackgroundSplat(0,0,-10,ground,1)},Transform.Identity,cameras,out fallback);
        Check(fallback && interior.Solid==ground,"Sparse boundary coverage falls back to visible colors");
        var inRhino=args.Contains("--in-rhino");
        using var ownedDocument = inRhino ? null : RhinoDoc.Create(null);
        var rhino = ownedDocument ?? RhinoDoc.ActiveDoc ?? throw new Exception("Rhino document required.");
        var previousView=rhino.Views.ActiveView;
        var view = inRhino ? rhino.Views.Add("SP Background Verification", DefinedViewportProjection.Perspective, new Rectangle(80,80,480,320), true) : previousView;
        var cameraOrigin=inRhino ? new Point3d(1e9,1e9,1e9) : Point3d.Origin;
        var viewport = view.ActiveViewport;
        viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.RenderedId);
        using (var projection = new ViewportInfo(viewport))
        {
            projection.ChangeToPerspectiveProjection(RhinoMath.UnsetValue,true,35);
            projection.FrustumAspect=1.5; projection.Camera35mmLensLength=35;
            projection.SetCameraLocation(cameraOrigin); projection.SetCameraDirection(-Vector3d.ZAxis); projection.SetCameraUp(Vector3d.YAxis);
            viewport.SetViewProjection(projection,false);
        }
        RhinoApp.RunScript("_Grasshopper",false);
        var canvas = global::Grasshopper.Instances.ActiveCanvas ?? throw new Exception("Grasshopper canvas required.");
        var previous = canvas.Document;
        var editor=global::Grasshopper.Instances.DocumentEditor;
        var editorVisible=editor.Visible;
        var enabled=GH_Document.EnableSolutions; GH_Document.EnableSolutions=true;
        using var document = new GH_Document { Enabled=true }; canvas.Document=document;
        var folder=Path.Combine(Path.GetTempPath(),"SP-auto-background-rhino-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            // Use points on actual Rhino frustum edges for integration with the lens setup.
            viewport.GetFrustum(out var left,out var right,out var lower,out var upper,out var near,out _);
            var ply=Path.Combine(folder,"fixture.ply");
            var lines=new List<string>{"ply","format ascii 1.0","element vertex 80","property float x","property float y","property float z","property uchar red","property uchar green","property uchar blue","end_header"};
            for(var i=0;i<40;i++)
            {
                var x=(i/40.0-0.5)*(right-left)/near*10*0.8;
                lines.Add(FormattableString.Invariant($"{x+cameraOrigin.X} {upper/near*10*0.9+cameraOrigin.Y} {cameraOrigin.Z-10} {sky.R} {sky.G} {sky.B}"));
                lines.Add(FormattableString.Invariant($"{x+cameraOrigin.X} {lower/near*10*0.9+cameraOrigin.Y} {cameraOrigin.Z-10} {ground.R} {ground.G} {ground.B}"));
            }
            File.WriteAllLines(ply,lines);
            var component=new AutoBackgroundComponent(); component.CreateAttributes(); document.AddObject(component,false);
            ((Param_String)component.Params.Input[0]).PersistentData.Append(new GH_String(ply));
            ((Param_Plane)component.Params.Input[1]).PersistentData.Append(new GH_Plane(new Plane(cameraOrigin,Vector3d.XAxis,Vector3d.YAxis)));
            var preview=(Param_Boolean)component.Params.Input[6]; preview.PersistentData.Clear(); preview.PersistentData.Append(new GH_Boolean(true));
            document.NewSolution(true);
            var status=component.Params.Output[3].VolatileData.AllData(true).OfType<GH_String>().Single().Value;
            Check(component.Params.Output[1].VolatileData.AllData(true).OfType<GH_Colour>().Single().Value==sky
                && component.Params.Output[2].VolatileData.AllData(true).OfType<GH_Colour>().Single().Value==ground,
                "Grasshopper component produces camera-aligned gradient outputs: "+status);
            Check(component.Icon_24x24.Width==24,"New component icon is embedded");
            viewport.DisplayMode.DisplayAttributes.GetFill(out var actualTop,out var actualBottom,out _,out _);
            Check(actualTop.ToArgb()==sky.ToArgb() && actualBottom.ToArgb()==ground.ToArgb()
                && viewport.DisplayMode.DisplayAttributes.FillMode==DisplayPipelineAttributes.FrameBufferFillMode.Gradient2Color,
                "Live Rendered preview applies the estimated two-color gradient");
            Check(viewport.DisplayMode.DisplayAttributes.LightingScheme==DisplayModeDescription.GetDisplayMode(DisplayModeDescription.RenderedId).DisplayAttributes.LightingScheme
                && !viewport.DisplayMode.InMenu,"Preview copies Rendered lighting and keeps its temporary mode out of menus");
            if(args.Contains("--capture"))
            {
                editor.Hide(); RhinoApp.SetFocusToMainWindow(rhino); rhino.Views.ActiveView=view;
                using var session=new ViewportCaptureSession(rhino,view,new[]{new Plane(cameraOrigin,Vector3d.XAxis,Vector3d.YAxis)},240,160,35,"Rendered",ground,folder,0.1,sky);
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(!session.Complete)
                {
                    if(clock.Elapsed.TotalSeconds>30) throw new Exception("Timed out capturing gradient: "+session.Phase);
                    session.CaptureNext(); Thread.Sleep(20); RhinoApp.Wait();
                }
                using var bitmap=new Bitmap(Path.Combine(session.DatasetPath,"images",ViewportCaptureSession.ImageName(0)));
                Check(bitmap.Size==session.CaptureSize && bitmap.GetPixel(1,3).B>bitmap.GetPixel(1,bitmap.Height-4).B+80,
                    "ScreenCaptureToFile exports a calibrated PNG with the estimated top/bottom gradient");
                Console.WriteLine("Gradient PNG: "+Path.Combine(session.DatasetPath,"images",ViewportCaptureSession.ImageName(0)));
            }
            preview.PersistentData.Clear(); preview.PersistentData.Append(new GH_Boolean(false)); preview.ExpireSolution(false); document.NewSolution(false);
            Check(viewport.DisplayMode.Id==DisplayModeDescription.RenderedId,"Disabling preview restores the original backdrop and display mode");
            var old=new ViewCaptureComponent(); old.CreateAttributes(); old.Params.UnregisterInputParameter(old.Params.Input[10],true); old.Params.OnParametersChanged();
            var ids=old.Params.Input.Select(p=>p.InstanceGuid).ToArray(); var chunk=new GH_LooseChunk("LegacyCapture"); old.Write(chunk);
            var restored=new ViewCaptureComponent(); restored.CreateAttributes(); restored.Read(chunk);
            Check(restored.Params.Input.Count==11 && restored.Params.Input.Take(10).Select(p=>p.InstanceGuid).SequenceEqual(ids) && restored.Params.Input[10].Optional,
                "Existing capture definitions retain all wires and append optional gradient input");
        }
        finally
        {
            foreach(var component in document.Objects.OfType<AutoBackgroundComponent>().ToArray()) document.RemoveObject(component,false);
            canvas.Document=previous; GH_Document.EnableSolutions=enabled;
            if(inRhino) { view.Close(); rhino.Views.ActiveView=previousView; }
            if(editorVisible) editor.Show();
        }
        Console.WriteLine($"{checks} checks passed. Fixtures: {folder}");
    }
}
