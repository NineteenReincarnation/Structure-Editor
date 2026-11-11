namespace StructureEditor;

// JSON settings data type; independent of the UI workspace implementation.
// Keep the original property names for persisted settings compatibility.
public sealed class WorkspaceReference
{
    public string File { get; set; }
    public string Definition { get; set; }
}
