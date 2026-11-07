using System.Xml.Linq;

namespace StructureEditor;

internal sealed class WorkspaceDocument
{
    public StructureDocument Document;
    public string File, SourceFile, SourceId, SaveDirectory;
    public string LastSavedId, LastXmlPath;
    public bool Dirty, Relocate, Prepared;
    public StructureDocument[] Undo = Array.Empty<StructureDocument>(), Redo = Array.Empty<StructureDocument>();
    public PointF ViewOrigin;
    public float ViewScale;
    public ToolStripButton Tab;
}

public partial class MainForm
{
    private readonly List<WorkspaceDocument> documents = new();
    private WorkspaceDocument activeDocument;
    private ToolStrip documentBar;
    private bool switchingDocument;

    private ToolStrip BuildDocumentBar()
    {
        documentBar = new() { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, BackColor = surface, Padding = new(6, 3, 6, 3), CanOverflow = true };
        AddTool(documentBar, "+ 新建", NewDocument);
        AddTool(documentBar, "打开…", OpenDocument);
        return documentBar;
    }

    private IEnumerable<string> StructureFolders()
    {
        var roots = (catalog?.Sources.Select(s => s.Root) ?? Enumerable.Empty<string>())
            .Concat(Program.Paths.ExtraMods).Append(Program.Paths.ModRoot);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Program.StructureLibraryFolder, OutputFolder };
        if (currentFile != null) folders.Add(Path.GetDirectoryName(Path.GetFullPath(currentFile))!);
        if (!string.IsNullOrWhiteSpace(document.XmlExportDirectory)) folders.Add(document.XmlExportDirectory);
        foreach (var session in documents)
        {
            if (session.File != null) folders.Add(Path.GetDirectoryName(Path.GetFullPath(session.File))!);
            if (!string.IsNullOrWhiteSpace(session.Document.XmlExportDirectory)) folders.Add(session.Document.XmlExportDirectory);
        }
        foreach (string file in Program.Paths.RecentDocuments.Where(File.Exists)) folders.Add(Path.GetDirectoryName(Path.GetFullPath(file))!);
        foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string defs = Path.Combine(root, "Defs", "Structures");
            if (Directory.Exists(defs)) folders.Add(defs);
            foreach (string version in Directory.EnumerateDirectories(root).Where(p => char.IsDigit(Path.GetFileName(p)[0])))
            { defs = Path.Combine(version, "Defs", "Structures"); if (Directory.Exists(defs)) folders.Add(defs); }
        }
        return folders.Where(Directory.Exists);
    }

    internal List<(string File, string Id, string Label)> LibraryDefinitions()
    {
        var result = new List<(string File, string Id, string Label)>();
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in new[] { Program.StructureLibraryFolder }.Concat(StructureFolders()).Append(Program.DefaultProjects).Append(desktop).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (string file in Directory.EnumerateFiles(folder, "*", SamePath(folder, desktop) ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) files.Add(Path.GetFullPath(file));
        foreach (string file in files)
        {
            try
            {
                if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                    if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                        || !json.RootElement.TryGetProperty("DefName", out _)
                        || !json.RootElement.TryGetProperty("Items", out _)) continue;
                    var doc = StructureDocument.Load(file);
                    result.Add((file, doc.DefName, doc.Label));
                    continue;
                }
                var root = XDocument.Load(file).Root;
                foreach (var def in (root?.Name.LocalName == "Defs" ? root.Elements()
                    : root == null ? Enumerable.Empty<XElement>() : new[] { root })
                    .Where(e => e.Name.LocalName is "MechProtocol.MP_StructureDef" or "StructureFramework.StructureDef"
                        or "StructureEditor.StructureDef" or "PrefabDef")
                    .Where(e => !string.Equals((string)e.Attribute("Abstract"), "true", StringComparison.OrdinalIgnoreCase)))
                {
                    string id = (string)def.Element("defName");
                    if (!string.IsNullOrWhiteSpace(id))
                        result.Add((file, id, (string)def.Element("label") ?? id));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Xml.XmlException or System.Text.Json.JsonException
                or InvalidDataException or ArgumentException)
            {
                // The next Refresh will retry XML files that were still being saved.
            }
        }
        return result.DistinctBy(x => (Path.GetFullPath(x.File).ToUpperInvariant(), x.Id)).OrderBy(x => x.Label).ToList();
    }

    private void EnsureWorkspaceDocument()
    {
        if (activeDocument != null) return;
        activeDocument = new() { Document = document, File = currentFile, SourceFile = currentFile, SourceId = document.DefName };
        documents.Add(activeDocument); AddDocumentTab(activeDocument);
    }
    private void AddDocumentTab(WorkspaceDocument session)
    {
        session.Tab = new() { ForeColor = text, DisplayStyle = ToolStripItemDisplayStyle.Text, AutoToolTip = false };
        session.Tab.Click += (_, _) => Safe(() => SwitchWorkspaceDocument(session));
        session.Tab.MouseUp += (_, e) => { if (e.Button == MouseButtons.Middle) Safe(() => { SwitchWorkspaceDocument(session); CloseWorkspaceDocument(); }); };
        documentBar.Items.Add(session.Tab);
        RefreshDocumentTabs();
    }
    private void CaptureWorkspaceDocument()
    {
        EnsureWorkspaceDocument();
        activeDocument.Document = document; activeDocument.File = currentFile; activeDocument.Dirty = dirty;
        activeDocument.Relocate = saveLocationChanged; activeDocument.SaveDirectory = Program.Paths.SaveDirectory;
        activeDocument.Undo = undo.ToArray(); activeDocument.Redo = redo.ToArray();
        (activeDocument.ViewOrigin, activeDocument.ViewScale) = canvas.CaptureView();
    }
    private void RefreshDocumentTabs()
    {
        foreach (var session in documents)
        {
            bool active = session == activeDocument;
            session.Tab.Text = (active ? document.Label : session.Document.Label) + ((active ? dirty : session.Dirty) ? " *" : "");
            session.Tab.Checked = active;
            session.Tab.ToolTipText = (active ? document.DefName : session.Document.DefName) + "\n" + (active ? currentFile : session.File);
        }
    }
    internal void SwitchWorkspaceDocument(WorkspaceDocument session)
    {
        namingSwitchDelay.Stop();
        if (session == activeDocument) return;
        CommitInspectorEdit(); CommitMetadata(); CaptureWorkspaceDocument(); canvas.Cancel();
        switchingDocument = true;
        try
        {
            activeDocument = session; document = session.Document; currentFile = session.File; dirty = session.Dirty;
            saveLocationChanged = session.Relocate; Program.Paths.SaveDirectory = session.SaveDirectory;
            undo.Clear(); foreach (var item in session.Undo.Reverse()) undo.Push(item);
            redo.Clear(); foreach (var item in session.Redo.Reverse()) redo.Push(item);
            canvas.Document = document; canvas.ClearSelection(); canvas.Tool = CanvasTool.Select;
            RefreshMetadata(); ShowSelection();
            if (session.ViewScale > 0) canvas.RestoreView(session.ViewOrigin, session.ViewScale); else canvas.CenterView();
            RefreshDocumentTabs();
        }
        finally { switchingDocument = false; }
        PersistWorkspace();
    }
    internal void AddWorkspaceDocument(StructureDocument opened, string path = null, bool prepared = false)
    {
        CommitInspectorEdit(); CommitMetadata(); CaptureWorkspaceDocument();
        // Replace only the untouched startup placeholder.
        if (currentFile == null && !dirty && document.Items.Count == 0 && (activeDocument.Prepared || documents.Count == 1 && document.DefName == "SE_NewStructure"))
        { documentBar.Items.Remove(activeDocument.Tab); activeDocument.Tab.Dispose(); documents.Remove(activeDocument); }
        var session = new WorkspaceDocument { Document = opened, File = path, SourceFile = path, SourceId = opened.DefName, SaveDirectory = Program.Paths.SaveDirectory, Prepared = prepared };
        documents.Add(session); AddDocumentTab(session); SwitchWorkspaceDocument(session);
        if (path != null) RememberDocument(path);
        PersistWorkspace();
    }
    internal void OpenWorkspaceFile(string path, string definition = null)
    {
        namingSwitchDelay.Stop();
        EnsureWorkspaceDocument();
        var existing = documents.FirstOrDefault(s => SamePath(s.File, path) && (definition == null || s.Document.DefName == definition));
        // An XML with multiple definitions still opens its picker unless an exact definition was requested.
        if (existing != null && (definition != null || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) { SwitchWorkspaceDocument(existing); return; }
        var opened = definition == null ? LoadChosenDefinition(path) : StructureDocument.Load(path, catalog, definition);
        if (opened == null) return;
        existing = documents.FirstOrDefault(s => SamePath(s.File, path) && s.Document.DefName == opened.DefName);
        if (existing != null) SwitchWorkspaceDocument(existing); else AddWorkspaceDocument(opened, path);
    }
    private static bool SamePath(string left, string right) => left != null && right != null && Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private void CloseWorkspaceDocument()
    {
        EnsureWorkspaceDocument(); if (!AskSave()) return;
        var closing = activeDocument; int index = documents.IndexOf(closing);
        if (documents.Count > 1) SwitchWorkspaceDocument(documents[index == 0 ? 1 : index - 1]);
        documents.Remove(closing); documentBar.Items.Remove(closing.Tab); closing.Tab.Dispose();
        if (documents.Count == 0)
        {
            activeDocument = null; document = new(); currentFile = null; dirty = false; saveLocationChanged = false;
            undo.Clear(); redo.Clear(); canvas.Cancel(); canvas.Document = document; RefreshMetadata(); EnsureWorkspaceDocument();
        }
        PersistWorkspace();
    }
    private bool AskSaveWorkspace()
    {
        EnsureWorkspaceDocument(); var original = activeDocument;
        foreach (var session in documents.ToArray())
        {
            SwitchWorkspaceDocument(session);
            if (!AskSave()) return false;
        }
        SwitchWorkspaceDocument(original); PersistWorkspace(); return true;
    }
    private void PersistWorkspace()
    {
        if (switchingDocument || activeDocument == null) return;
        CaptureWorkspaceDocument();
        Program.Paths.OpenDocuments = documents.Where(s => s.File != null).Select(s => new WorkspaceReference { File = s.File, Definition = s.SourceFile?.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) == true && SamePath(s.File, s.SourceFile) ? s.SourceId : s.Document.DefName }).ToList();
        Program.Paths.LastDocument = currentFile; Program.Paths.LastDefinition = document.DefName; Program.SavePaths();
    }
    private void RestoreWorkspace()
    {
        EnsureWorkspaceDocument(); var original = activeDocument;
        foreach (var reference in Program.Paths.OpenDocuments.ToArray())
        {
            if (!File.Exists(reference.File) || SamePath(reference.File, currentFile) && reference.Definition == document.DefName) continue;
            try { OpenWorkspaceFile(reference.File, reference.Definition); }
            catch (Exception ex) { status.Text = "工作栏恢复失败：" + Path.GetFileName(reference.File) + " · " + ex.Message; }
        }
        if (documents.Contains(original)) SwitchWorkspaceDocument(original); RefreshDocumentTabs();
    }
}

public sealed class WorkspaceReference
{
    public string File { get; set; }
    public string Definition { get; set; }
}
