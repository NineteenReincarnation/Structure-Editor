using System.Xml.Linq;

namespace StructureEditor;

// Regression checks for the JSON -> exported XML rename link, without
// depending on UI screenshots or replacing unrelated definitions.
internal static class MP_StructureRenameChecks
{
    internal static void Run(string testsDirectory, Action<bool, string> need)
    {
        string root = Path.Combine(testsDirectory, "rename-fixtures-" + Guid.NewGuid().ToString("N")[..8]);
        string projects = Path.Combine(root, "projects");
        string exports = Path.Combine(root, "exports");
        Directory.CreateDirectory(projects);
        Directory.CreateDirectory(exports);
        try
        {
            const string oldId = "MP_RenameFixture_A";
            const string newId = "MP_RenameFixture_B";
            string originalJson = Path.Combine(projects, oldId + ".structure.json");
            string originalXml = Path.Combine(exports, oldId + ".xml");
            string multiXml = Path.Combine(exports, "shared.xml");
            string originalPng = Path.Combine(projects, oldId + "_preview.png");
            new StructureDocument
            {
                DefName = oldId, Label = "原名称",
                XmlExportDirectory = exports
            }.Save(originalJson);
            static XElement Def(string id, string label) => new("StructureFramework.StructureDef",
                new XElement("defName", id), new XElement("label", label));
            new XDocument(new XElement("Defs", Def(oldId, "原名称"))).Save(originalXml);
            new XDocument(new XElement("Defs", Def(oldId, "原名称"),
                Def("MP_Unrelated", "不应修改"))).Save(multiXml);
            File.WriteAllBytes(originalPng, new byte[] { 1, 2, 3 });

            var plan = MP_StructureRenamer.Prepare(originalJson, oldId, newId,
                "新名称", new[] { multiXml });
            need(plan.XmlPaths.Count == 2, "Rename tracks configured output XML and selected multi-definition XML");
            MP_StructureRenamer.Apply(plan);

            string nextJson = Path.Combine(projects, newId + ".structure.json");
            string nextXml = Path.Combine(exports, newId + ".xml");
            need(File.Exists(nextJson) && !File.Exists(originalJson) &&
                StructureDocument.Load(nextJson).DefName == newId, "JSON project filename and DefName change together");
            need(File.Exists(nextXml) && !File.Exists(originalXml) &&
                (string)XDocument.Load(nextXml).Root!.Elements().Single().Element("label") == "新名称",
                "Exported XML filename and label follow the JSON rename");
            var shared = XDocument.Load(multiXml).Root!.Elements().ToArray();
            need(shared.Length == 2 && (string)shared[0].Element("defName") == newId &&
                (string)shared[1].Element("defName") == "MP_Unrelated",
                "Multi-definition XML keeps unrelated Defs intact");
            need(File.Exists(Path.Combine(projects, newId + "_preview.png")) && !File.Exists(originalPng),
                "Hand-authored preview PNG follows a structure rename");

            var labelOnly = MP_StructureRenamer.Prepare(nextJson, newId, newId,
                "仅更新显示名", new[] { multiXml });
            MP_StructureRenamer.Apply(labelOnly);
            need(StructureDocument.Load(nextJson).Label == "仅更新显示名" &&
                (string)XDocument.Load(nextXml).Root!.Elements().Single().Element("label") == "仅更新显示名",
                "Label-only rename also tracks and updates exported XML");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
