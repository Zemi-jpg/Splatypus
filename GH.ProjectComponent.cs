using System;
using System.IO;
using Grasshopper.Kernel;
using RhinoSplat.Core;

namespace RhinoSplat.Grasshopper;

public sealed class ProjectComponent : SpComponent
{
    protected override System.Drawing.Bitmap Icon => ComponentIcons.Get("project");

    public ProjectComponent()
        : base("SP Project", "Project", "Creates and reports a reproducible SP project folder layout.", "Splatypus", "01 Setup")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager parameters)
    {
        parameters.AddTextParameter("Parent Folder", "Folder", "Folder that will contain the project.", GH_ParamAccess.item,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        parameters.AddTextParameter("Project Name", "Name", "Project folder name.", GH_ParamAccess.item, "SplatypusProject");
        parameters.AddBooleanParameter("Create", "Create", "Create the folders. Safe to leave true: creation is idempotent.", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager parameters)
    {
        parameters.AddTextParameter("Project Root", "Root", "Full project path.", GH_ParamAccess.item);
        parameters.AddTextParameter("Images", "Images", "Image input folder.", GH_ParamAccess.item);
        parameters.AddTextParameter("COLMAP", "COLMAP", "COLMAP working folder.", GH_ParamAccess.item);
        parameters.AddTextParameter("Brush", "Brush", "Brush working folder.", GH_ParamAccess.item);
        parameters.AddTextParameter("Status", "Status", "Creation status.", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess dataAccess)
    {
        var parentFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var projectName = "SplatypusProject";
        var create = false;
        if (!dataAccess.GetData(0, ref parentFolder) || !dataAccess.GetData(1, ref projectName))
            return;
        dataAccess.GetData(2, ref create);

        try
        {
            var project = RhinoSplatProject.Create(parentFolder, projectName);
            if (create)
                project.EnsureLayout();

            dataAccess.SetData(0, project.RootPath);
            dataAccess.SetData(1, project.ImagesPath);
            dataAccess.SetData(2, project.ColmapPath);
            dataAccess.SetData(3, project.BrushPath);
            dataAccess.SetData(4, create ? "Project layout created." : "Set Create to True to create the project layout.");
        }
        catch (Exception exception)
        {
            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Error,
                $"Project creation failed ({exception.GetType().Name}): {exception.Message}");
        }
    }

    public override GH_Exposure Exposure => GH_Exposure.primary;

    public override Guid ComponentGuid => new("E815BB83-F5FE-4A44-A95D-4C8DF2B7E96A");
}
