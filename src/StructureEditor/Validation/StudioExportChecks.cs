using System.Reflection;
using System.Xml.Linq;

namespace StructureEditor;

internal static class StudioExportChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        string root = Path.Combine(folder, "SaveExportChecks", Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(root);
        var originalPaths = Program.Paths;
        try
        {
            Program.Paths = new() { GameRoot = originalPaths.GameRoot, SaveDirectory = root };
            string project = Path.Combine(root, "MP_SaveExport.structure.json");
            StructureDocument doc = new() { DefName = "MP_SaveExport", Label = "自动导出检查", Items = new() { new() { Kind = "Building", DefName = "Wall", Stuff = "Steel" } } };
            doc.Save(project);
            File.WriteAllText(Path.Combine(root, doc.DefName + ".xml"), "<Defs />");
            using MainForm form = new(); form.BindForChecks(catalog, doc);
            object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            typeof(MainForm).GetField("currentFile", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, project);
            bool Save() => (bool)typeof(MainForm).GetMethod("SaveDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { false })!;
            IEnumerable<Control> Children(Control control) { foreach (Control child in control.Controls) { yield return child; foreach (Control nested in Children(child)) yield return nested; } }
            var tabs = Children(form).OfType<TabControl>().Single(t => t.TabPages.Cast<TabPage>().Any(p => p.Text == "导出"));
            var exportPage = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "导出"); tabs.SelectedTab = exportPage;
            var profile = (ComboBox)Field("profileBox"); var auto = (CheckBox)Field("autoExport");
            need(Children(exportPage).Contains(profile) && auto.Checked, "Right export page contains format selector and defaults save-time XML export on");
            need(Children(exportPage).OfType<Button>().Any(b => b.Text == "立即导出 XML") && Children(exportPage).OfType<Button>().Any(b => b.Text == "更改 XML 目录…"), "Export page exposes direct export and directory selection");
            need(Save() && File.Exists(Path.Combine(root, doc.DefName + ".xml")), "Actual SaveDocument saves project and XML together without an export dialog");
            string xmlFile = Path.Combine(root, doc.DefName + ".xml");
            need(XDocument.Load(xmlFile).Root!.Element("StructureFramework.StructureDef") != null, "Save exports selected framework XML");
            profile.SelectedIndex = 1; Save();
            need(XDocument.Load(xmlFile).Root!.Element("PrefabDef") != null, "Save follows native Prefab format selection");
            profile.SelectedIndex = 2; Save();
            need(XDocument.Load(xmlFile).Root!.Element("MechProtocol.MP_StructureDef") != null, "Save follows MechProtocol format selection");
            string previousXml = File.ReadAllText(xmlFile); auto.Checked = false;
            ((TextBox)Field("labelBox")).Text = "仅保存工程"; Save();
            need(File.ReadAllText(xmlFile) == previousXml && StructureDocument.Load(project).Label == "仅保存工程" && !StructureDocument.Load(project).AutoExportOnSave, "Disabling auto export preserves XML while saving project edits and preference");
            auto.Checked = true; Save();
            need(XDocument.Load(xmlFile).Root!.Elements().Single().Element("label")!.Value == "仅保存工程", "Re-enabling auto export updates the existing XML on next save");
            var multiple = XDocument.Load(xmlFile); multiple.Root!.Add(new XElement("MechProtocol.MP_StructureDef", new XElement("defName", "MP_KeepOther"), new XElement("label", "keep"))); multiple.Save(xmlFile);
            ((TextBox)Field("labelBox")).Text = "更新本结构"; Save();
            need(XDocument.Load(xmlFile).Root!.Elements().Count() == 2 && XDocument.Load(xmlFile).Root!.Elements().Any(e => (string)e.Element("defName") == "MP_KeepOther"), "Automatic XML updates retain other definitions in a shared file");
            var imported = StructureDocument.Load(xmlFile, catalog, doc.DefName);
            need(imported.XmlExportDirectory == root && imported.AutoExportOnSave, "Opening XML remembers its source directory for subsequent save-time export");
            var custom = doc.Copy(); custom.XmlExportDirectory = Path.Combine(root, "XmlOutput"); custom.ExportProfile = "Framework";
            var customResult = StructureStorage.SaveProject(custom, catalog, Path.Combine(root, "Custom.structure.json"));
            need(customResult.ExportError == null && File.Exists(customResult.XmlPath) && Path.GetDirectoryName(customResult.XmlPath) == custom.XmlExportDirectory, "Independent XML directory is used and created automatically");
            var loaded = StructureDocument.Load(Path.Combine(root, "Custom.structure.json"));
            need(loaded.XmlExportDirectory == custom.XmlExportDirectory && loaded.Copy().XmlExportDirectory == custom.XmlExportDirectory && loaded.Copy().AutoExportOnSave, "Export destination and toggle roundtrip JSON and undo copies");
            string moved = Path.Combine(root, "SaveAs", "Renamed.structure.json"); var ordinary = doc.Copy(); ordinary.XmlExportDirectory = null;
            need(StructureStorage.SaveProject(ordinary, catalog, moved).XmlPath == Path.Combine(Path.GetDirectoryName(moved)!, ordinary.DefName + ".xml"), "Save As follows the new project folder when no XML directory is configured");
            var invalid = custom.Copy(); invalid.ExportProfile = "VanillaPrefab"; invalid.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 4 });
            string invalidProject = Path.Combine(root, "Invalid.structure.json"), oldXml = File.ReadAllText(customResult.XmlPath);
            var failed = StructureStorage.SaveProject(invalid, catalog, invalidProject);
            need(failed.ExportError != null && StructureDocument.Load(invalidProject).Items.Count == 2 && File.ReadAllText(customResult.XmlPath) == oldXml, "Rejected export preserves JSON edits and leaves last successful XML untouched");
            string oldProject = Path.Combine(root, "Old.structure.json"); File.WriteAllText(oldProject, "{\"FormatVersion\":1,\"DefName\":\"SE_Old\",\"Items\":[]}");
            need(StructureDocument.Load(oldProject).AutoExportOnSave && StructureDocument.Load(oldProject).XmlExportDirectory == null, "Older projects default auto export on without a fixed machine-specific directory");
            form.Size = new(1450, 900); StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "export-page.png"));
            form.Size = new(1060, 720); StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "export-page-compact.png"));
        }
        finally { Program.Paths = originalPaths; }
    }
}
