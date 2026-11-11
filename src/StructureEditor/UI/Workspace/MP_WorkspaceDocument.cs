namespace StructureEditor;

// One open editor tab's persistent session data.
// Kept separate from view composition and document-tab event wiring.
internal sealed class WorkspaceDocument
{
    public StructureDocument Document;
    public string File, SourceFile, SourceId, SaveDirectory;
    public string LastSavedId, LastXmlPath;
    public bool Dirty, Relocate, Prepared;
    public StructureDocument[] Undo = Array.Empty<StructureDocument>(), Redo = Array.Empty<StructureDocument>();
    public PointF ViewOrigin;
    public float ViewScale;
    public MP_DocumentTabButton Tab;
}
