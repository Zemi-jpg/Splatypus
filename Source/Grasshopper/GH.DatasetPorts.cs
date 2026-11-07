using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GH_IO.Serialization;

namespace RhinoSplat.Grasshopper;

internal static class DatasetPorts
{
    public static void EnsureIntegerInput(GH_Component component, string name, string nickname, string description, int value)
    {
        if (component.Params.Input.Any(p => p.Name == name)) return;
        var parameter = new Param_Integer { Name = name, NickName = nickname, Description = description, Access = GH_ParamAccess.item };
        parameter.SetPersistentData(value);
        component.Params.RegisterInputParam(parameter);
        component.Params.OnParametersChanged();
    }
    // Fixed Grasshopper components otherwise read only their new port counts, truncating
    // later legacy controls or reading transform data into a text/boolean parameter.
    public static void RestoreSavedLayout(GH_Component component, GH_IReader reader)
    {
        Restore(true, "param_input");
        Restore(false, "param_output");
        component.Params.OnParametersChanged();

        void Restore(bool input, string chunkName)
        {
            if (!reader.ChunkExists(chunkName, 0)) return;
            var defaults = (input ? component.Params.Input : component.Params.Output).ToArray();
            var saved = new List<IGH_Param>();
            for (var i = 0; reader.FindChunk(chunkName, i) is { } chunk; i++)
            {
                var name = chunk.GetString("Name");
                var parameter = defaults.FirstOrDefault(p => p.Name == name && !saved.Contains(p));
                if (input && parameter is null && (name is "Project Root" or "Active Dataset"))
                    parameter = defaults.FirstOrDefault(p => p.Name is "Project Root" or "Active Dataset");
                parameter ??= name == "Replace Existing" ? new Param_Boolean() : new Param_String();
                saved.Add(parameter);
            }
            foreach (var parameter in defaults)
                if (input) component.Params.UnregisterInputParameter(parameter, true);
                else component.Params.UnregisterOutputParameter(parameter, true);
            foreach (var parameter in saved)
                if (input) component.Params.RegisterInputParam(parameter);
                else component.Params.RegisterOutputParam(parameter);
        }
    }
    // Remove retired sockets, including their wires, rather than reinterpreting an old path as a new one.
    public static bool RemoveInputs(GH_Component component, params string[] names)
    {
        var removed = false;
        foreach (var param in component.Params.Input.ToArray())
            if (names.Contains(param.Name)) { component.Params.UnregisterInputParameter(param, true); removed = true; }
        component.Params.OnParametersChanged();
        return removed;
    }
    public static bool SingleDatasetOutput(GH_Component component, params string[] retired)
    {
        var removed = false;
        foreach (var param in component.Params.Output.ToArray())
            if (retired.Contains(param.Name)) { component.Params.UnregisterOutputParameter(param, true); removed = true; }
        if (!component.Params.Output.Any(p => p.Name == "Active Dataset"))
            component.Params.RegisterOutputParam(new Param_String { Name = "Active Dataset", NickName = "Dataset",
                Description = "The project's single active dataset. Connect to Brush Dataset or geometry previews.", Access = GH_ParamAccess.item }, 0);
        component.Params.OnParametersChanged();
        return removed;
    }
    public static void RenameInput(GH_Component component, int index, string name, string nickname, string description)
    {
        var param = component.Params.Input[index];
        param.Name = name; param.NickName = nickname; param.Description = description;
        component.Params.OnParametersChanged();
    }
    public static void EnsureTextOutput(GH_Component component, string name, string nickname, string description, int index)
    {
        if (!component.Params.Output.Any(p => p.Name == name))
            component.Params.RegisterOutputParam(new Param_String { Name = name, NickName = nickname,
                Description = description, Access = GH_ParamAccess.item }, index);
        component.Params.OnParametersChanged();
    }
}
