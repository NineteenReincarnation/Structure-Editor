namespace StructureEditor;

// Backward-compatible JSON settings model: property names are unchanged.
public class StudioPaths
{
    public List<WorkspaceReference> OpenDocuments { get; set; } = new();
    public bool AutoLoadActiveMods { get; set; } = true;
    public bool MP_UiSoundsEnabled { get; set; } = true;
    public string ModsConfigPath { get; set; }
    public string GameRoot { get; set; }
    // Read old preferences without requiring the old mod or exposing it as a built-in dependency.
    public string ModRoot { get; set; }
    public List<string> ExtraMods { get; set; } = new();
    public string SaveDirectory { get; set; }
    public string LastDocument { get; set; }
    public string LastDefinition { get; set; }
    public bool UseRuntimeCatalog { get; set; } = true;
    public List<string> Favorites { get; set; } = new();
    public List<string> RecentAssets { get; set; } = new();
    public List<string> RecentDocuments { get; set; } = new();
    // Local-only exact paths for manual XML exports. Do not embed machine paths in shared projects.
    public Dictionary<string, string> MP_XmlExportPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
