using System.Reflection;
using System.Xml.Linq;
using StructureEditor.Compatibility;

namespace StructureEditor;

internal static class StudioWorkspaceChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        var previousPaths = Program.Paths;
        string root = Path.Combine(folder, "WorkspaceChecks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Program.Paths = new() { SaveDirectory = root, GameRoot = previousPaths.GameRoot };
            using MainForm form = new(); form.BindForChecks(catalog, new());
            object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
            object Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
            var canvas = (StructureCanvas)Field("canvas");
            var first = new StructureDocument { DefName = "SE_First", Label = "结构一", Items = new() { new() { Kind = "Building", DefName = "Wall", Stuff = "Steel" } } };
            form.AddWorkspaceDocument(first);
            canvas.SelectAll(); canvas.MoveSelected(3, 1); canvas.ZoomAtCenter(1.4f); var firstView = canvas.CaptureView();
            var sessions = (List<WorkspaceDocument>)Field("documents"); var firstSession = sessions.Single();
            var second = new StructureDocument { DefName = "SE_Second", Label = "结构二", Items = new() { new() { Kind = "Building", DefName = "Door", Stuff = "Steel", X = 8 } } };
            form.AddWorkspaceDocument(second); canvas.SelectAll(); canvas.MoveSelected(-2, 0);
            var secondSession = sessions.Last();
            need(sessions.Count == 2 && firstSession.Dirty && firstSession.Document.Items[0].X == 3, "Opening a second document retains first unsaved changes without a save dialog");
            form.SwitchWorkspaceDocument(firstSession);
            need(canvas.CaptureView() == firstView && !canvas.HasStamp, "Switching restores each document view and clears transient paste state");
            Call("Undo"); need(canvas.Document.Items.Single().X == 0 && second.Items.Single().X == 6, "Undo acts only on active document");
            form.SwitchWorkspaceDocument(secondSession); Call("Undo");
            need(canvas.Document.Items.Single().X == 8, "Second document has an independent undo history");
            form.SwitchWorkspaceDocument(firstSession); Call("Redo");
            need(canvas.Document.Items.Single().X == 3, "Switching preserves redo history and replaced document references");
            string shared = Path.Combine(root, "Two.xml");
            new XDocument(new XElement("Defs", first.ToXml(), second.ToXml())).Save(shared);
            form.OpenWorkspaceFile(shared, first.DefName); form.OpenWorkspaceFile(shared, second.DefName); int count = sessions.Count;
            form.OpenWorkspaceFile(shared, first.DefName);
            need(sessions.Count == count && ((StructureDocument)Field("document")).DefName == first.DefName, "Repeated file and definition selects its tab while a shared XML can open two definitions");
            need(Program.Paths.OpenDocuments.Count(r => r.File == shared) == 2, "Workspace persistence records the selected definition within shared XML files");
            string occupied = Path.Combine(root, "MP_Cluster_MilitorForge_T01_V01.xml");
            var militor = new StructureDocument { DefName = "MP_Cluster_MilitorForge_T01_V01", Label = "小镰铸巢·I-A", ExportProfile = "MechProtocol" };
            new XDocument(new XElement("Defs", militor.ToXml(true))).Save(occupied);
            form.OpenWorkspaceFile(occupied, militor.DefName);
            var militorDocument = (StructureDocument)Field("document");
            var core = (ComboBox)Field("namingCore"); var tier = (ComboBox)Field("namingTier"); var variant = (NumericUpDown)Field("namingVariant");
            need(Equals(core.SelectedItem, MPClusterPresets.All[1]) && tier.SelectedIndex == 0 && variant.Value == 1, "Existing Militor cluster automatically selects its registered core and numbers");
            var originalFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
            int beforeDraft = sessions.Count;
            variant.Value = 27;
            var navigation = System.Diagnostics.Stopwatch.StartNew();
            while (((System.Windows.Forms.Timer)Field("namingSwitchDelay")).Enabled && navigation.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(2); }
            need(!((System.Windows.Forms.Timer)Field("namingSwitchDelay")).Enabled, "Selector events automatically resolve the requested document without an apply button");
            var draft = (StructureDocument)Field("document");
            need(draft.DefName == "MP_Cluster_MilitorForge_T01_V27" && draft.Label == "小镰铸巢·I-AA" && draft.Items.Count == 0 && Field("currentFile") == null && !(bool)Field("dirty"), "A missing selection opens a correctly named empty in-memory draft without marking it edited");
            need(militor.DefName == "MP_Cluster_MilitorForge_T01_V01" && sessions.Count == beforeDraft + 1, "Changing selectors navigates instead of renaming the previous structure");
            variant.Value = 28; Call("ActivateNamedCluster");
            need(sessions.Count == beforeDraft + 1 && ((StructureDocument)Field("document")).DefName.EndsWith("V28"), "Unedited empty preloads are reused when selecting another missing number");
            canvas.SetStamp(new[] { new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "Steel" } }); canvas.PlaceStamp(Point.Empty);
            variant.Value = 29; Call("ActivateNamedCluster");
            need(sessions.Count == beforeDraft + 2 && sessions.Any(d => d.Document.DefName.EndsWith("V28") && d.Dirty && d.Document.Items.Count == 1), "An edited draft retains its layout and dirty state when navigating away");
            variant.Value = 28; Call("ActivateNamedCluster");
            need(((StructureDocument)Field("document")).Items.Count == 1 && (bool)Field("dirty"), "Selecting an open draft returns to its unsaved edits");
            need(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == originalFiles.Count && originalFiles.All(p => File.ReadAllText(p.Key) == p.Value), "Browsing existing and missing selections never creates or modifies structure files");
            variant.Value = 1; Call("ActivateNamedCluster");
            need((string)Field("currentFile") == occupied && ReferenceEquals(Field("document"), militorDocument), "An already open XML wins over disk lookup and returns to the existing tab");
            need(MP_ClusterNaming.NextVariant(form.LibraryDefinitions().Select(x => x.Id), "MilitorForge", 1) == 2, "Next variant skips occupied XML definitions within the selected core and tier");
            need(MPClusterPresets.Validate("MP_Cluster_MilitorForge_T01_V01", "飞螳铸巢·I-A") != null, "Core identifiers cannot be assigned another registered facility name");
            var clone = militor.Copy(); clone.Label = "小镰铸巢·I-B"; clone.DefName = "MP_Cluster_MilitorForge_T01_V02";
            form.AddWorkspaceDocument(clone); variant.Value = 1; Call("ActivateNamedCluster");
            need(ReferenceEquals(Field("document"), militorDocument) && clone.DefName.EndsWith("V02"), "Occupied identifiers navigate to the existing structure without changing the outgoing document");
            var paired = new StructureDocument { DefName = "MP_Cluster_MilitorForge_T05_V87", Label = "小镰铸巢·V-CI", ExportProfile = "MechProtocol", XmlExportDirectory = root,
                Items = new() { new() { Kind = "Building", DefName = "Wall", Stuff = "Steel" } } };
            paired.Label = MP_ClusterNaming.Label("小镰铸巢", 5, 87);
            string pairedXml = Path.Combine(root, paired.DefName + ".xml"), pairedJson = Path.Combine(root, "renamed-project.json");
            new XDocument(new XElement("Defs", paired.ToXml(true))).Save(pairedXml); paired.Items[0].X = 9; paired.Save(pairedJson);
            tier.SelectedIndex = 4; variant.Value = 87; Call("ActivateNamedCluster");
            need((string)Field("currentFile") == pairedJson && ((StructureDocument)Field("document")).Items.Single().X == 9, "Content-based lookup prefers JSON over its exported XML even with a different filename");
            ((TextBox)Field("labelBox")).Text = paired.Label; // Commit still uses the selected document.
            need((bool)Call("SaveDocument", false) && StructureDocument.Load(pairedXml).Items.Single().X == 9, "Explicit Save updates the selected JSON and paired XML rather than another structure");
            tier.SelectedIndex = 0; variant.Value = 1; Call("ActivateNamedCluster");
            bool rejected = false;
            try { Call("CheckWorkspaceSaveTarget", shared); } catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
            need(rejected, "Saving cannot overwrite another open document path");
            var another = first.Copy(); another.DefName = "SE_Other";
            string before = File.ReadAllText(shared);
            // A one-definition target rejects a different identity before any file write.
            string single = Path.Combine(root, "One.xml"); new XDocument(new XElement("Defs", first.ToXml())).Save(single); string original = File.ReadAllText(single);
            rejected = false; try { StructureStorage.ExportXml(another, catalog, single, "Framework"); } catch (InvalidOperationException) { rejected = true; }
            need(rejected && File.ReadAllText(single) == original && File.ReadAllText(shared) == before, "Export refuses an unrelated single-definition XML without altering files");
            var references = Program.Paths.OpenDocuments.ToArray();
            using (MainForm restored = new())
            {
                restored.BindForChecks(catalog, new());
                typeof(MainForm).GetMethod("RestoreWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(restored, null);
                var restoredSessions = (List<WorkspaceDocument>)typeof(MainForm).GetField("documents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restored)!;
                var restoredActive = (WorkspaceDocument)typeof(MainForm).GetField("activeDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restored)!;
                need(restoredSessions.Count == references.Length && restoredSessions.Contains(restoredActive), "Restart restores each file and definition with an attached active tab even when last active was unsaved");
            }
            using (MainForm duplicate = new())
            {
                duplicate.BindForChecks(catalog, militor.Copy());
                rejected = false;
                try { typeof(MainForm).GetMethod("CheckWorkspaceSaveTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(duplicate, new object[] { Path.Combine(root, militor.DefName + ".structure.json") }); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
                need(rejected && File.Exists(occupied), "A new unsaved document cannot claim an occupied XML number as its own source");
            }
            form.Size = new(1450, 900);
            var inspector = ((GroupBox)Field("namingPanel")).Parent;
            while (inspector is not TabPage) inspector = inspector.Parent;
            var tabs = inspector.Parent?.Parent as MP_SectionTabs;
            need(tabs != null && tabs.SelectPage((TabPage)inspector),
                "Workspace metadata section remains selectable with the unified dark tab control");
            StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "workspace-documents.png"));
            form.Size = new(1060, 720); StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "workspace-documents-compact.png"));
        }
        finally { Program.Paths = previousPaths; }
    }
}
