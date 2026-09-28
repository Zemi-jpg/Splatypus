using Grasshopper.Kernel;
using GH_IO.Serialization;
using System.Text.RegularExpressions;

namespace RhinoSplat.Grasshopper;

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
