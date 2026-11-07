namespace StructureEditor.Compatibility;

internal sealed record MPClusterPreset(string Core, string Label, string Building)
{
    public override string ToString() => Label;
}

internal static class MPClusterPresets
{
    internal static readonly MPClusterPreset[] All =
    {
        new("ScytherForge", "飞螳铸巢", "MP_ScytherForgeNest"),
        new("MilitorForge", "小镰铸巢", "MP_MilitorForgeNest")
    };
    internal static string Validate(string id, string label)
    {
        string error = MP_ClusterNaming.Validate(id, label);
        if (error != null) return error;
        MP_ClusterNaming.TryParse(id, out string core, out int tier, out int variant);
        string facility = MP_ClusterNaming.CoreLabel(label, tier, variant);
        var preset = All.FirstOrDefault(p => p.Core == core || p.Label == facility);
        return preset != null && (preset.Core != core || preset.Label != facility) ? "核心名称与标识不匹配。" : null;
    }
}
