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
            var tabs = Children(form).OfType<MP_SectionTabs>().Single(t => Children(t).OfType<TabPage>().Any(p => p.Text == "工程"));
            var exportPage = Children(tabs).OfType<TabPage>().Single(p => p.Text == "工程");
            need(tabs.SelectPage(exportPage), "Merged project/export tab remains accessible with the dark tab bar");
            var profile = (ComboBox)Field("profileBox"); var auto = (CheckBox)Field("autoExport");
            need(Children(exportPage).Contains(profile) && auto.Checked && profile.SelectedIndex == 1,
                "New projects default to the original PrefabDef XML and automatic save-time export");
            need(Children(exportPage).OfType<Button>().Any(b => b.Text == "立即导出 XML")
                && Children(exportPage).OfType<Button>().Any(b => b.Text == "更改 XML 目录…")
                && Children(exportPage).OfType<Button>().Any(b => b.Text.StartsWith("更多导出方式")),
                "Export page groups direct export and advanced export choices");
            need(Children(exportPage).OfType<CheckBox>().Any(c => c.Text.Contains("MechProtocol")),
                "Export page embeds optional mod compatibility settings");
            need(Save() && File.Exists(Path.Combine(root, doc.DefName + ".xml")), "Actual SaveDocument saves project and XML together without an export dialog");
            string xmlFile = Path.Combine(root, doc.DefName + ".xml");
            var nativeXml = XDocument.Load(xmlFile);
            need(nativeXml.Root!.Elements().Single().Name.LocalName == "PrefabDef",
                "Default save emits an original RimWorld PrefabDef, not a custom class");
            var vanilla = nativeXml.Root.Elements().Single();
            need(vanilla.Elements().All(e => new[] { "defName", "label", "description", "rotations", "size", "edgeOnly", "things", "terrain" }.Contains(e.Name.LocalName))
                && vanilla.Element("things")?.Element("Wall")?.Element("positions") != null,
                "Default XML uses only vanilla PrefabDef fields and native thing placement fields");
            profile.SelectedIndex = 0; Save();
            need(XDocument.Load(xmlFile).Root!.Element("StructureFramework.StructureDef") != null,
                "Explicit Structure Framework selection remains supported");
            profile.SelectedIndex = 1; Save();
            need(XDocument.Load(xmlFile).Root!.Element("PrefabDef") != null,
                "Explicit native PrefabDef selection remains supported");
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
            var universal = doc.Copy(); universal.ExportProfile = "Universal";
            universal.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 11, Z = 11 });
            var dualXml = ExportDependencies.Xml(universal, catalog, "Universal");
            need(dualXml.Root!.Elements().Count() == 2
                && dualXml.Root.Elements().First().Name.LocalName == "PrefabDef"
                && dualXml.Root.Elements().Skip(1).Single().Name.LocalName == "StructureFramework.StructureDef"
                && ((string)dualXml.Root.Elements().Last().Attribute("MayRequire"))?.Contains(FrameworkCodec.PackageId) == true,
                "Universal XML exposes a native prefab and defers framework class parsing by MayRequire");
            need(dualXml.Root.Element("PrefabDef")!.Element("things")!.Element("Wall") != null
                && dualXml.Root.Element("PrefabDef")!.Element("pawns") == null
                && dualXml.Root.Element("StructureFramework.StructureDef")!.Element("pawns") != null,
                "Universal vanilla fallback keeps compatible buildings while conditional enhancement preserves pawn layout");
            var universalReview = MP_ExportReview.Inspect(universal,catalog,"Universal");
            need(universalReview.CanExport && universalReview.Warnings > 0,
                "Partial native fallback generates a warning instead of deleting JSON pawn data");
            var missingPackage = doc.Copy();
            missingPackage.RequiredPackages.Add("test.package.not.loaded");
            var missingReview = MP_ExportReview.Inspect(missingPackage,catalog,"VanillaPrefab");
            need(missingReview.CanExport && missingReview.Warnings > 0
                && ExportDependencies.Xml(missingPackage,catalog,"VanillaPrefab").Root!.Element("PrefabDef")!.Attribute("MayRequire") != null,
                "Unavailable dependencies warn rather than block and are guarded by native MayRequire");
            string combinedPath = Path.Combine(root,"Combined.xml");
            var coreOnly = doc.Copy(); coreOnly.ExportProfile = "Universal";
            StructureStorage.ExportXml(coreOnly,catalog,combinedPath,"Universal");
            var combined = XDocument.Load(combinedPath);
            need(combined.Root!.Elements().Count() == 2 && combined.Root.Elements().Any(x =>
                    (string)x.Element("defName") == ExportDependencies.MP_EnhancedName(coreOnly.DefName)),
                "Dual export writes a single native file with its guarded companion");
            combined.Root.Add(new XElement("PrefabDef",new XElement("defName","MP_Unrelated")));
            combined.Save(combinedPath);
            StructureStorage.ExportXml(coreOnly,catalog,combinedPath,"VanillaPrefab");
            var downgraded = XDocument.Load(combinedPath);
            need(downgraded.Root!.Elements().Count() == 2
                && downgraded.Root.Elements().Any(x => (string)x.Element("defName") == "MP_Unrelated")
                && !downgraded.Root.Elements().Any(x =>
                    (string)x.Element("defName") == ExportDependencies.MP_EnhancedName(coreOnly.DefName)),
                "Switching back to native removes only the editor-owned conditional companion, preserving unrelated defs");
            var invalid = custom.Copy(); invalid.ExportProfile = "VanillaPrefab"; invalid.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 4 });
            string invalidProject = Path.Combine(root, "Invalid.structure.json"), oldXml = File.ReadAllText(customResult.XmlPath);
            var failed = StructureStorage.SaveProject(invalid, catalog, invalidProject);
            need(failed.ExportError != null && StructureDocument.Load(invalidProject).Items.Count == 2 && File.ReadAllText(customResult.XmlPath) == oldXml, "Rejected export preserves JSON edits and leaves last successful XML untouched");
            string oldProject = Path.Combine(root, "Old.structure.json"); File.WriteAllText(oldProject, "{\"FormatVersion\":1,\"DefName\":\"SE_Old\",\"Items\":[]}");
            need(StructureDocument.Load(oldProject).AutoExportOnSave
                && StructureDocument.Load(oldProject).XmlExportDirectory == null
                && StructureDocument.Load(oldProject).ExportProfile == "Framework",
                "Historical JSON projects without a profile retain their previous Framework output");
            form.Size = new(1450, 900); StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "export-page.png"));
            form.Size = new(1060, 720); StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "export-page-compact.png"));
        }
        finally { Program.Paths = originalPaths; }
    }
}
