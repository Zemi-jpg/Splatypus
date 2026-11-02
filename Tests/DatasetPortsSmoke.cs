using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using RhinoSplat.Grasshopper;

var passed = 0;
void Check(bool okay, string message)
{
    if (!okay) { Console.Error.WriteLine("FAIL " + message); Environment.Exit(1); }
    Console.WriteLine("PASS " + message); passed++;
}
void AddLegacyInput(GH_Component component, string name, int index)
    => component.Params.RegisterInputParam(name == "Replace Existing"
        ? new Param_Boolean { Name = name, NickName = name, Access = GH_ParamAccess.item }
        : new Param_String { Name = name, NickName = name, Access = GH_ParamAccess.item }, index);
void LegacyOutputs(GH_Component component, params string[] names)
{
    component.Params.UnregisterOutputParameter(component.Params.Output[0], true);
    for (var i = 0; i < names.Length; i++)
        component.Params.RegisterOutputParam(new Param_String { Name = names[i], NickName = names[i], Access = GH_ParamAccess.item }, i);
}
void Test(Func<GH_Component> make, Action<GH_Component> legacy, string[] inputs, string[] outputs)
{
    var old = make(); old.CreateAttributes(); legacy(old); old.Params.OnParametersChanged();
    old.Name = old.Name.Replace("SP ", "RS ");
    old.NickName = old.NickName.Replace("SP ", "RS ");
    old.Params.Input[0].Description = "Connect output from RS Project.";
    ((Param_String)old.Params.Input.First(p => p.Name is "Project Root" or "Active Dataset")).PersistentData.Append(new GH_String(@"C:\fixture\dataset"));
    var archive = new GH_LooseChunk("component");
    Check(old.Write(archive), old.Name + " serializes a legacy-shaped component.");
    var restored = make(); restored.CreateAttributes();
    Check(restored.Read(archive), restored.Name + " reads the saved parameter schema.");
    Check(restored.Name.StartsWith("SP ") && restored.NickName.StartsWith("SP ")
        && !restored.Params.Input[0].Description.Contains("RS "), "Saved component names, nicknames and tooltips adopt SP branding.");
    restored.GetType().GetMethod("MigrateDatasetPorts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(restored, null);
    Check(restored.Params.Input.Select(p => p.Name).SequenceEqual(inputs)
        && restored.Params.Output.Select(p => p.Name).SequenceEqual(outputs), restored.Name + " migrates to one Dataset socket and the expected controls. Inputs: "
        + string.Join(", ", restored.Params.Input.Select(p => p.Name)) + "; Outputs: " + string.Join(", ", restored.Params.Output.Select(p => p.Name)));
    var root = (Param_String)restored.Params.Input.First(p => p.Name is "Project Root" or "Active Dataset");
    Check(((GH_String)root.PersistentData.AllData(true).Single()).Value == @"C:\fixture\dataset", restored.Name + " preserves the saved project root.");
    var roundtrip = new GH_LooseChunk("current"); restored.Write(roundtrip);
    var current = make(); current.CreateAttributes(); current.Read(roundtrip);
    current.GetType().GetMethod("MigrateDatasetPorts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(current, null);
    Check(current.Params.Input.Select(p => p.GetType()).SequenceEqual(restored.Params.Input.Select(p => p.GetType()))
        && current.Params.Output.Select(p => p.Name).SequenceEqual(outputs), restored.Name + " reopens the new layout without changing parameter types.");
    var latch = current.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .FirstOrDefault(f => f.Name is "_previousWrite" or "_previousRun" or "_resetLegacyRun");
    if (latch is not null) Check((bool)latch.GetValue(current)!, restored.Name + " cannot rerun an overwrite merely by reopening the definition.");
}
Test(() => new BrushDatasetComponent(), c => {
    c.Params.Input[0].Name = "Project Root";
    AddLegacyInput(c, "Replace Existing", 3); AddLegacyInput(c, "Undistorted Workspace", 4);
}, new[] { "Active Dataset", "Prepare", "Sparse Model Override" }, new[] { "Brush Dataset", "Status" });
Test(() => new ColmapModelAlignmentComponent(), c => {
    c.Params.Input[1].Name = "Project Root";
    AddLegacyInput(c, "Source Text Model", 2); AddLegacyInput(c, "Output Name", 4); AddLegacyInput(c, "Replace Existing", 6);
}, new[] { "COLMAP Executable", "Active Dataset", "Transforms", "Write" }, new[] { "Aligned Text Model", "Aligned Sparse Model", "Log", "Status" });
Test(() => new ColmapModelAlignmentComponent(), c => {
    c.Params.UnregisterOutputParameter(c.Params.Output[1], true);
    LegacyOutputs(c, "Active Dataset");
}, new[] { "COLMAP Executable", "Active Dataset", "Transforms", "Write" }, new[] { "Aligned Text Model", "Aligned Sparse Model", "Log", "Status" });
Test(() => new BrushDatasetComponent(), c => c.Params.UnregisterInputParameter(c.Params.Input[2], true),
    new[] { "Active Dataset", "Prepare", "Sparse Model Override" }, new[] { "Brush Dataset", "Status" });
Test(() => new ColmapImageUndistorterComponent(), c => {
    c.Params.Input[1].Name = "Project Root"; AddLegacyInput(c, "Replace Existing", 3); LegacyOutputs(c, "Dense Workspace");
}, new[] { "COLMAP Executable", "Active Dataset", "Run", "Show Terminal" }, new[] { "Active Dataset", "Exit Code", "Log", "Status" });
Test(() => new ColmapModelExportComponent(), c => {
    AddLegacyInput(c, "Replace Existing", 3); LegacyOutputs(c, "Text Model Folder");
}, new[] { "COLMAP Executable", "Project Root", "Run", "Show Terminal" }, new[] { "Active Dataset", "Exit Code", "Log", "Status" });
Test(() => new ColmapSparseMapperComponent(), c => LegacyOutputs(c, "Sparse Model Folder"),
    new[] { "COLMAP Executable", "Project Root", "Run", "Replace Existing", "Show Terminal" }, new[] { "Active Dataset", "Exit Code", "Log", "Status" });
Test(() => new ColmapPipelineComponent(), c => LegacyOutputs(c, "Database", "Sparse Model Folder", "Undistorted Workspace", "Text Model Folder"),
    new[] { "COLMAP Executable", "Project Root", "Run", "Replace Existing", "Show Terminal", "Extract Features", "Match Features", "Reconstruct Sparse", "Undistort Images", "Export Text Model", "Show Progress", "Cancel", "Matching Method", "Extraction Max Image Size", "Maximum Features" },
    new[] { "Active Dataset", "Exit Code", "Log", "Status", "Progress" });
Test(() => new TrainingDatasetCheckComponent(), c => LegacyOutputs(c, "Images", "Sparse Model", "Text Model", "Manifest"),
    new[] { "Project Root", "Update" }, new[] { "Active Dataset", "Valid", "Source Images", "Registered Images", "Sparse Points", "Status" });
Console.WriteLine($"{passed} component serialization/migration checks passed.");

void Appended(Func<GH_Component> make, int oldCount, string[] settings)
{
    var old = make(); old.CreateAttributes();
    while (old.Params.Input.Count > oldCount) old.Params.UnregisterInputParameter(old.Params.Input[^1], true);
    var oldParameters = old.Params.Input.Select(p => (p.Name, p.GetType())).ToArray();
    ((Param_String)old.Params.Input[0]).SetPersistentData("original executable path");
    var saved = new GH_LooseChunk("legacy"); old.Write(saved);
    var restored = make(); restored.CreateAttributes();
    Check(restored.Read(saved), restored.Name + " reads a 0.1.0 component with fewer settings.");
    Check(restored.Params.Input.Take(oldCount).Select(p => (p.Name, p.GetType())).SequenceEqual(oldParameters), "Original socket order and types are preserved for " + restored.Name);
    Check(restored.Params.Input.Skip(oldCount).Select(p => p.Name).SequenceEqual(settings), "New optional settings are appended to " + restored.Name);
    Check(((Param_String)restored.Params.Input[0]).PersistentData.AllData(true).Cast<GH_String>().Single().Value == "original executable path", "Old persistent values survive in " + restored.Name);
    foreach (var param in restored.Params.Input.Skip(oldCount))
    {
        if (param is Param_Integer integer) { integer.PersistentData.Clear(); integer.SetPersistentData(123); }
        if (param is Param_Boolean boolean) { boolean.PersistentData.Clear(); boolean.SetPersistentData(true); }
    }
    var currentArchive = new GH_LooseChunk("current"); restored.Write(currentArchive);
    var current = make(); current.CreateAttributes(); current.Read(currentArchive);
    Check(current.Params.Input.Select(p => (p.Name, p.GetType())).SequenceEqual(restored.Params.Input.Select(p => (p.Name, p.GetType()))), "Updated settings roundtrip without duplicate sockets for " + restored.Name);
    Check(current.Params.Input.Skip(oldCount).OfType<Param_Integer>().All(p => ((GH_Integer)p.PersistentData.AllData(true).Single()).Value == 123)
        && current.Params.Input.Skip(oldCount).OfType<Param_Boolean>().All(p => ((GH_Boolean)p.PersistentData.AllData(true).Single()).Value), "Edited optional values survive reopening for " + restored.Name);
    Check(current.ComponentGuid == old.ComponentGuid, "Component identity remains stable for " + restored.Name);
}
Appended(() => new FFmpegMediaImportComponent(), 8, new[] { "Image Format", "Skip Near Duplicates" });
Appended(() => new BrushTrainingComponent(), 11, new[] { "Maximum Training Resolution", "Maximum Splat Count" });
Appended(() => new ColmapFeatureExtractionComponent(), 4, new[] { "Extraction Max Image Size", "Maximum Features" });
Appended(() => new ColmapExhaustiveMatchingComponent(), 4, new[] { "Matching Method" });
Appended(() => new ColmapPipelineComponent(), 12, new[] { "Matching Method", "Extraction Max Image Size", "Maximum Features" });
Console.WriteLine($"{passed} total component migration checks passed.");
