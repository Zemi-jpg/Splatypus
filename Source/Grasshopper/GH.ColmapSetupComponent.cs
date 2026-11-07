using System;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class ColmapSetupComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("colmap-setup");

    public ColmapSetupComponent()
        : base("SP COLMAP Setup", "COLMAP", "Validates COLMAP and prepares its project paths without launching reconstruction.", "Splatypus", "03 COLMAP")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("COLMAP Executable", "Exe", "Full path to the COLMAP executable.", GH_ParamAccess.item);
        parameters.AddTextParameter("Project Root", "Root", "Connect the Root output from SP Project.", GH_ParamAccess.item);
        parameters.AddBooleanParameter("Prepare", "Prepare", "Create the COLMAP working folder when True.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddBooleanParameter("Valid", "Valid", "Whether COLMAP can be used.", GH_ParamAccess.item);
        parameters.AddTextParameter("Database", "Database", "Expected COLMAP database path.", GH_ParamAccess.item);
        parameters.AddTextParameter("Feature Command", "Command", "Preview of the command to be run in the next milestone.", GH_ParamAccess.item);
        parameters.AddTextParameter("Status", "Status", "Validation/preparation status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        var executable = string.Empty;
        var projectRoot = string.Empty;
        var prepare = false;
        if (!dataAccess.GetData(0, ref executable) || !dataAccess.GetData(1, ref projectRoot))
            return;
        dataAccess.GetData(2, ref prepare);

        var validation = ToolValidation.Colmap(executable);
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            dataAccess.SetData(0, false);
            dataAccess.SetData(3, "Connect Project Root from SP Project.");
            return;
        }

        var root = Path.GetFullPath(projectRoot);
        var colmapDirectory = Path.Combine(root, "colmap");
        var database = Path.Combine(colmapDirectory, "database.db");
        var images = Path.Combine(root, "images");

        if (prepare)
            Directory.CreateDirectory(colmapDirectory);

        var featureCommand = validation.IsValid
            ? FormatCommand(validation.ExecutablePath, "feature_extractor", "--database_path", database, "--image_path", images)
            : string.Empty;

        dataAccess.SetData(0, validation.IsValid);
        dataAccess.SetData(1, database);
        dataAccess.SetData(2, featureCommand);
        dataAccess.SetData(3, validation.IsValid
            ? prepare ? "COLMAP is ready; working folder prepared." : "COLMAP is ready; set Prepare to True to create its working folder."
            : validation.Message);
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("A1D44733-5B96-4F43-998A-50FBD8C4D374");

    private static string FormatCommand(string executable, params string[] arguments) =>
        Quote(executable) + " " + string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';
}
