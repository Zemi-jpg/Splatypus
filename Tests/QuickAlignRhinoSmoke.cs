using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Parameters;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Runtime.InProcess;
using RhinoSplat.Grasshopper;

internal static class QuickAlignRhinoSmoke
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var core = args.Contains("--in-rhino") ? null : new RhinoCore(new[] { "/NOSPLASH" }, WindowStyle.Hidden);
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message); checks++;
        }
        var source = new[] { new Point3d(0,0,0), new Point3d(10,0,0), new Point3d(0,10,0) };
        var target = new[] { new Point3d(30,40,50), new Point3d(30,60,50), new Point3d(10,40,50) };
        Check(QuickAlignComponent.TryFit(source, target, out var transform, out var rms, out var error) && rms < 1e-10, "Rhino transform matches three pairs: " + error);
        Check(ColmapModelAlignmentComponent.TryGetSimilarity(new[] { transform }, out var fit, out error) && Math.Abs(fit.Scale - 2) < 1e-10,
            "Quick transform accepted by existing COLMAP writer: " + error);
        using var cloud = new PointCloud(source);
        cloud[0].Color = System.Drawing.Color.Red;
        using var camera = new PlaneSurface(new Plane(new Point3d(1,2,3), Vector3d.ZAxis), new Interval(-1,1), new Interval(-1,1)).ToBrep();
        var tree = new GH_Structure<IGH_GeometricGoo>();
        tree.Append(new GH_PointCloud(cloud), new GH_Path(2, 4));
        tree.Append(new GH_Brep(camera), new GH_Path(9));
        tree.Append(new GH_Point(new Point3d(1,2,3)), new GH_Path(9));
        tree.EnsurePath(new GH_Path(10));
        var transformed = QuickAlignComponent.TransformTree(tree, transform);
        Check(transformed.Paths.Select(p => p.ToString()).SequenceEqual(tree.Paths.Select(p => p.ToString())), "Geometry tree paths and empty branches preserved");
        Check(((GH_PointCloud)transformed.Branches[0][0]).Value[0].Location.DistanceTo(target[0]) < 1e-10
            && ((GH_PointCloud)transformed.Branches[0][0]).Value[0].Color.ToArgb() == System.Drawing.Color.Red.ToArgb(), "Cloud positions and colours preserved through copy");
        Check(((GH_Point)transformed.Branches[1][1]).Value.DistanceTo(new Point3d(26,42,56)) < 1e-10, "Camera centre follows same transform");
        Check(((GH_Brep)transformed.Branches[1][0]).Value.IsValid && camera.Vertices[0].Location.Z == 3 && cloud[0].Location == source[0], "Camera and sparse input geometry unchanged");
        Check(QuickAlignComponent.Fingerprint(source) != QuickAlignComponent.Fingerprint(source.Reverse()), "Reordered cloud invalidates saved picks");
        Check(QuickAlignComponent.Fingerprint(source) != QuickAlignComponent.Fingerprint(target), "Changed coordinates invalidate saved picks");
        using var document = new GH_Document();
        using var solver = new SolverScope();
        document.Enabled = true;
        var component = new QuickAlignComponent();
        component.CreateAttributes();
        document.AddObject(component, false);
        void Set(string name, object value) => typeof(QuickAlignComponent).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(component, value);
        Set("_source", source); Set("_target", target); Set("_savedKey", QuickAlignComponent.Fingerprint(source));
        var saved = new GH_LooseChunk("QuickAlign");
        Check(component.Write(saved), "Alignment serializes");
        var restored = new QuickAlignComponent(); restored.CreateAttributes();
        Check(restored.Read(saved), "Alignment restores");
        var restoredPoints = (Point3d[])typeof(QuickAlignComponent).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(restored)!;
        Check(restoredPoints.SequenceEqual(source), "Saved pair order and coordinates restored");

        var input = (Param_Geometry)component.Params.Input[0];
        input.PersistentData.Append(new GH_PointCloud(cloud));
        document.NewSolution(true);
        Check(component.Params.Output[2].VolatileDataCount == 1, "Completed pairs publish one transform. "
            + string.Join("; ", component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var previewCloud = (GH_PointCloud)component.Params.Output[0].VolatileData.AllData(true).Single();
        Check(previewCloud.Value[1].Location.DistanceTo(target[1]) < 1e-10, "Component publishes aligned sparse preview");
        input.PersistentData.Clear(); input.PersistentData.Append(new GH_PointCloud(new PointCloud(target)));
        input.ExpireSolution(false); document.NewSolution(true);
        // Grasshopper may retain a null placeholder in an unwritten output branch.
        // Verify there is no usable transform rather than counting placeholders.
        Check(!component.Params.Output[2].VolatileData.AllData(true).Any(), "Changed input withholds stale transform");
        Check(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count > 0, "Changed input explains why repicking is needed");
        input.PersistentData.Clear(); input.PersistentData.Append(new GH_PointCloud(cloud));
        Set("_source", Array.Empty<Point3d>()); Set("_target", Array.Empty<Point3d>()); Set("_savedKey", string.Empty);
        input.ExpireSolution(false); document.NewSolution(true);
        Check(!component.Params.Output[2].VolatileData.AllData(true).Any() && component.Params.Output[0].VolatileDataCount == 1,
            "Unaligned component previews source without publishing identity for writing");

        var key = QuickAlignComponent.Fingerprint(source);
        Check(component.AcceptPairs(new[] { 0, 1, 2 }, target, key, out rms, out error), "First alignment accepted");
        component.ExpireSolution(false); document.NewSolution(false);
        Check(component.PickingPoints().Zip(target).All(p => p.First.DistanceTo(p.Second) < 1e-9),
            "Reopening picker uses previous aligned locations, not original points");
        var delta = Transform.Translation(-12, 7, 5) * Transform.Rotation(0.37, Vector3d.XAxis, Point3d.Origin)
            * Transform.Scale(Point3d.Origin, 0.7);
        var indices = new[] { 2, 0, 1 };
        var nextTargets = indices.Select(i => { var point = target[i]; point.Transform(delta); return point; }).ToArray();
        Check(component.AcceptPairs(indices, nextTargets, key, out rms, out error), "Second alignment accepts points selected from current preview");
        component.ExpireSolution(false); document.NewSolution(false);
        var total = ((GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single()).Value;
        var expectedTotal = delta * transform;
        var witnesses = source.Concat(new[] { new Point3d(2, 3, 9), new Point3d(-4, 6, -8) });
        Check(witnesses.All(p => { var expected = p; expected.Transform(expectedTotal); p.Transform(total); return p.DistanceTo(expected) < 1e-8; }),
            "Second Xform accumulates rotation, translation and scale from both passes (including off-plane points)");
        Check(component.PickingPoints().Zip(source).All(pair => { var expected = pair.Second; expected.Transform(total); return pair.First.DistanceTo(expected) < 1e-9; }),
            "Next picking session starts from accumulated result");
        Check(!component.AcceptPairs(indices, new[] { Point3d.Origin, Point3d.Origin, Point3d.Origin }, key, out _, out _),
            "Degenerate refinement rejected");
        Check(!component.AcceptPairs(indices, nextTargets, "changed cloud", out _, out _), "Changed-cloud refinement rejected");
        component.ExpireSolution(false); document.NewSolution(false);
        Check(((GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single()).Value == total,
            "Failed refinements retain the previous complete transform");
        var refinedSave = new GH_LooseChunk("refined"); component.Write(refinedSave);
        var reopened = new QuickAlignComponent(); reopened.CreateAttributes(); reopened.Read(refinedSave);
        using var reopenedDocument = new GH_Document { Enabled = true };
        reopenedDocument.AddObject(reopened, false); reopenedDocument.NewSolution(true);
        Check(((GH_Transform)reopened.Params.Output[2].VolatileData.AllData(true).Single()).Value == total,
            "Accumulated alignment survives document serialization");

        // Real component wiring: already-aligned geometry and the complete Xform.
        var cameraInput = (Param_Geometry)component.Params.Input[1];
        cameraInput.PersistentData.Append(new GH_Point(new Point3d(2, 3, 9)), new GH_Path(4));
        var translator = new GeometryTranslatorComponent(); translator.CreateAttributes(); document.AddObject(translator, false);
        translator.Params.Input[1].AddSource(component.Params.Output[0]);
        translator.Params.Input[1].AddSource(component.Params.Output[1]);
        translator.Params.Input[9].AddSource(component.Params.Output[2]);
        void SetNumber(int index, double value)
        {
            var parameter = (Param_Number)translator.Params.Input[index];
            parameter.PersistentData.Clear(); parameter.SetPersistentData(value);
        }
        SetNumber(2, 5.0); SetNumber(5, 18.0); SetNumber(8, 0.96);
        document.NewSolution(true);
        var combinedTree = (GH_Structure<GH_Transform>)translator.Params.Output[1].VolatileData;
        Check(ColmapModelAlignmentComponent.TryGetTransformSequence(combinedTree, out var sequence, out error) && sequence.Count == 6,
            "Translator emits one complete six-step sequence per geometry branch; writer deduplicates branches: " + error);
        Check(sequence[0] == total && ColmapModelAlignmentComponent.TryGetSimilarity(sequence, out fit, out error),
            "Quick Align precedes fine adjustment and is accepted by COLMAP writer");
        var local = GeometryTranslatorComponent.BuildTransforms(Point3d.Origin, 5, 0, 0, 18, 0, 0, 0.96);
        var expectedCamera = new Point3d(2, 3, 9); expectedCamera.Transform(total);
        foreach (var step in local) expectedCamera.Transform(step);
        var cameraOutput = translator.Params.Output[0].VolatileData.AllData(true).OfType<GH_Point>().Single().Value;
        Check(cameraOutput.DistanceTo(expectedCamera) < 1e-8, "Translator does not apply previous alignment twice to camera preview");
        ColmapModelAlignmentComponent.TryGetSimilarity(sequence, out fit, out _);
        var writtenCamera = fit.Apply(2, 3, 9);
        Check(cameraOutput.DistanceTo(new Point3d(writtenCamera.X, writtenCamera.Y, writtenCamera.Z)) < 1e-8,
            "Saved COLMAP transform agrees with final camera preview");
        var finalCloud = translator.Params.Output[0].VolatileData.AllData(true).OfType<GH_PointCloud>().Single().Value;
        Check(source.Select((point, i) => { var written = fit.Apply(point.X, point.Y, point.Z);
            return finalCloud[i].Location.DistanceTo(new Point3d(written.X, written.Y, written.Z)) < 1e-8; }).All(ok => ok),
            "Saved COLMAP transform agrees with final sparse cloud preview");

        component.Attributes.PerformLayout();
        var resetBounds = (System.Drawing.RectangleF)component.Attributes.GetType()
            .GetField("_resetBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component.Attributes)!;
        Check(resetBounds.Width > 20 && resetBounds.Height >= 20 && component.Attributes.Bounds.Contains(resetBounds),
            "Reset button occupies a visible area inside component bounds");
        var resetLocation = new System.Drawing.PointF(resetBounds.X + resetBounds.Width / 2, resetBounds.Y + 10);
        var resetClick = new Grasshopper.GUI.GH_CanvasMouseEvent(System.Drawing.Point.Empty, resetLocation,
            System.Windows.Forms.MouseButtons.Left, 1, 0);
        Check(component.Attributes.RespondToMouseDown(null!, resetClick) == Grasshopper.GUI.Canvas.GH_ObjectResponse.Handled,
            "Left-click on visible Reset button is handled");
        Check(((GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single()).Value == Transform.Identity,
            "Explicit Reset publishes identity for downstream writing");
        Check(component.PickingPoints().SequenceEqual(source), "Reset restores original picking coordinates");
        Check(((GH_PointCloud)component.Params.Output[0].VolatileData.AllData(true).Single()).Value.GetPoints().SequenceEqual(source),
            "Reset restores original cloud preview");
        Check(document.Undo(), "Reset records an Undo event");
        document.NewSolution(true);
        Check(((GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single()).Value == total,
            "Undo Reset restores accumulated alignment");
        Check(document.Redo(), "Reset supports Redo");
        document.NewSolution(true);
        var resetSave = new GH_LooseChunk("reset"); component.Write(resetSave);
        var resetReopened = new QuickAlignComponent(); resetReopened.CreateAttributes(); resetReopened.Read(resetSave);
        using var resetDocument = new GH_Document { Enabled = true };
        resetDocument.AddObject(resetReopened, false); resetDocument.NewSolution(true);
        Check(((GH_Transform)resetReopened.Params.Output[2].VolatileData.AllData(true).Single()).Value == Transform.Identity,
            "Explicit Reset state survives reopening");
        Check(translator.Params.Output[1].VolatileData.AllData(true).Cast<GH_Transform>().First().Value == Transform.Identity,
            "Reset propagates to combined transform output");
        input.PersistentData.Clear(); input.PersistentData.Append(new GH_PointCloud(new PointCloud(target)));
        input.ExpireSolution(false);
        document.NewSolution(true);
        Check(!translator.Params.Output[1].VolatileData.AllData(true).Any()
            && translator.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0,
            "Missing previous alignment cannot silently produce a partial fine-only save");
        translator.Params.Input[9].RemoveAllSources();
        translator.ExpireSolution(false); document.NewSolution(false);
        Check(ColmapModelAlignmentComponent.TryGetTransformSequence((GH_Structure<GH_Transform>)translator.Params.Output[1].VolatileData,
            out var localOnly, out _) && localOnly.Count == 5,
            "Unconnected Previous preserves original five-transform workflow");

        var legacyTranslator = new GeometryTranslatorComponent(); legacyTranslator.CreateAttributes();
        legacyTranslator.Params.UnregisterInputParameter(legacyTranslator.Params.Input[9], true);
        ((Param_Number)legacyTranslator.Params.Input[2]).PersistentData.Clear();
        ((Param_Number)legacyTranslator.Params.Input[2]).SetPersistentData(12.5);
        var oldIds = legacyTranslator.Params.Input.Select(p => p.InstanceGuid).ToArray();
        var legacySave = new GH_LooseChunk("legacy translator"); legacyTranslator.Write(legacySave);
        var migratedTranslator = new GeometryTranslatorComponent(); migratedTranslator.CreateAttributes();
        Check(migratedTranslator.Read(legacySave) && migratedTranslator.Params.Input.Count == 10,
            "Legacy Translator gains one optional Previous input");
        Check(migratedTranslator.Params.Input.Take(9).Select(p => p.InstanceGuid).SequenceEqual(oldIds)
            && ((Param_Number)migratedTranslator.Params.Input[2]).PersistentData.AllData(true).Cast<GH_Number>().Single().Value == 12.5,
            "Translator migration preserves original socket identities and values");
        input.PersistentData.Clear(); input.PersistentData.Append(new GH_PointCloud(cloud));
        Set("_source", Array.Empty<Point3d>()); Set("_target", Array.Empty<Point3d>()); Set("_savedKey", string.Empty); Set("_resetToIdentity", false);
        document.NewSolution(true);

        // Native pick-frustum regression in both orthographic and perspective views.
        using var rhino = RhinoDoc.Create(null);
        var view = rhino.Views.ActiveView;
        foreach (var projection in new[] { DefinedViewportProjection.Top, DefinedViewportProjection.Perspective })
        {
            view.ActiveViewport.SetProjection(projection, projection.ToString(), false);
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(new Point3d(-5,-5,-5), new Point3d(15,15,5)));
            rhino.Views.Redraw();
            var screen = view.ActiveViewport.WorldToClient(source[0]);
            var cursor = new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
            Check(SparsePreviewPicker.HitTest(view.ActiveViewport, cursor, source) == 0, projection + " picks exact cloud index without baked geometry");
            Check(SparsePreviewPicker.HitTest(view.ActiveViewport, new System.Drawing.Point(-1000,-1000), source) == -1, projection + " rejects empty-space click");
        }
        if (args.Contains("--interactive"))
        {
            foreach (var point in target) rhino.Objects.AddPoint(point);
            view.ActiveViewport.SetProjection(DefinedViewportProjection.Top, "Top", false);
            view.ActiveViewport.ZoomBoundingBox(new BoundingBox(new Point3d(-5,-5,0), new Point3d(40,65,55)));
            rhino.Views.Redraw();
            component.PickPairs();
            Check(component.Params.Output[2].VolatileData.AllData(true).Count() == 1, "Interactive three-pair selection completed (do not cancel before all three pairs)");
            var picked = (GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single();
            var p = source[0]; p.Transform(picked.Value);
            Check(p.DistanceTo(target[0]) < 1e-8, "Interactive point pairs recover expected alignment");
            component.PickPairs(); // Cancel this replacement with Esc.
            Check(((GH_Transform)component.Params.Output[2].VolatileData.AllData(true).Single()).Value == picked.Value,
                "Cancelling interactive replacement preserves completed alignment");
        }
        Console.WriteLine($"All {checks} Rhino quick alignment checks passed.");
    }

    private sealed class SolverScope : IDisposable
    {
        private readonly bool _wasEnabled = GH_Document.EnableSolutions;
        public SolverScope() => GH_Document.EnableSolutions = true;
        public void Dispose() => GH_Document.EnableSolutions = _wasEnabled;
    }
}
