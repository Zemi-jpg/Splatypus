using Grasshopper.Kernel;
using GH_IO.Serialization;
using System.Text.RegularExpressions;

namespace RhinoSplat.Grasshopper;

internal static class AppendedInputs
{
    // Let Grasshopper deserialize exactly the saved socket count, then append new defaults.
    // Existing wires and persistent values retain their original indices.
    public static IGH_Param[] BeforeRead(GH_Component component, GH_IReader reader, params string[] names)
    {
        var additions = component.Params.Input.Where(p => names.Contains(p.Name)).ToArray();
        if (!reader.ChunkExists("param_input", 0)) return Array.Empty<IGH_Param>();
        var savedNames = new HashSet<string>();
        for (var i = 0; reader.FindChunk("param_input", i) is { } chunk; i++) savedNames.Add(chunk.GetString("Name"));
        var missing = additions.Where(p => !savedNames.Contains(p.Name)).ToArray();
        foreach (var parameter in missing) component.Params.UnregisterInputParameter(parameter, true);
        component.Params.OnParametersChanged();
        return missing;
    }
    public static void AfterRead(GH_Component component, IEnumerable<IGH_Param> missing)
    {
        foreach (var parameter in missing) component.Params.RegisterInputParam(parameter);
        component.Params.OnParametersChanged();
    }
}

// Refresh saved display metadata as well as newly placed/searchable components.
internal static class SpBranding
{
    public static string Labels(string text) => Regex.Replace(text, @"\b(?:RS|Splatypus|RhinoSplat)(?=\s)", "SP");
    public static void Restore(GH_Component component, string name, string nickname, string description, Dictionary<IGH_Param, string> descriptions)
    {
        component.Name = name;
        component.Description = description;
        var saved = Labels(component.NickName);
        component.NickName = saved == nickname[3..] ? nickname : saved;
        foreach (var parameter in component.Params.Input.Concat(component.Params.Output))
        {
            parameter.Name = Labels(parameter.Name);
            parameter.NickName = Labels(parameter.NickName);
            parameter.Description = Labels(descriptions.GetValueOrDefault(parameter, parameter.Description)).Replace("rhinosplat_*.ply", "sp_*.ply");
        }
    }
}

public abstract class SpComponent : GH_Component
{
    protected SpComponent(string name, string nickname, string description, string category, string subcategory)
        : base(name, "SP " + nickname, description, category, subcategory) { }
    public override bool Read(GH_IReader reader)
    {
        var name = Name; var nickname = NickName; var description = Description;
        var descriptions = Params.Input.Concat(Params.Output).ToDictionary(p => p, p => p.Description);
        var result = base.Read(reader);
        SpBranding.Restore(this, name, nickname, description, descriptions);
        return result;
    }
}

public abstract class SpTaskCapableComponent<T> : GH_TaskCapableComponent<T>
{
    protected SpTaskCapableComponent(string name, string nickname, string description, string category, string subcategory)
        : base(name, "SP " + nickname, description, category, subcategory) { }
    public override bool Read(GH_IReader reader)
    {
        var name = Name; var nickname = NickName; var description = Description;
        var descriptions = Params.Input.Concat(Params.Output).ToDictionary(p => p, p => p.Description);
        var result = base.Read(reader);
        SpBranding.Restore(this, name, nickname, description, descriptions);
        return result;
    }
}
