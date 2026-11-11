using System.Xml.Linq;

namespace StructureEditor;

// All structure JSON/XML IO and safe overwrite/backup rules live here.
// No WinForms, WPF, workspace controls or global Program state.
internal static class StructureStorage
{
    internal sealed record SaveResult(string XmlPath, string ExportError);
    internal static string XmlTarget(StructureDocument document, string projectFile, string fallbackFolder) => Path.Combine(
        !string.IsNullOrWhiteSpace(document.XmlExportDirectory) ? document.XmlExportDirectory
        : projectFile?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true ? Path.GetDirectoryName(Path.GetFullPath(projectFile))! : fallbackFolder,
        document.DefName + ".xml");
    internal static void ExportXml(StructureDocument document, Catalog catalog, string path, string profile)
    {
        if (catalog == null) throw new InvalidOperationException("素材仍在加载，请稍后导出。");
        MP_ExportReview.Result review = MP_ExportReview.Inspect(document, catalog, "Framework");
        if (!review.CanExport)
            throw new InvalidOperationException(string.Join("\n", review.Issues.Where(i => i.IsError).Take(12).Select(i => i.Message)));
        var xml = ExportDependencies.Xml(document, catalog, "Framework");
        if (File.Exists(path))
        {
            XDocument existing = XDocument.Load(path);
            XElement root = existing.Root ?? throw new InvalidOperationException("输出 XML 没有根节点。");
            if (root.Name.LocalName != "Defs")
                throw new InvalidOperationException("目标文件并非 RimWorld Defs XML，不会覆盖。");
            var current = root.Elements().ToList();
            string extensionId = ExportDependencies.MP_EnhancedName(document.DefName);
            // A single unrelated definition must never be replaced by export.
            if (current.Count == 1)
            {
                string oldId = (string)current[0].Element("defName");
                if (oldId != document.DefName && oldId != extensionId)
                    throw new InvalidOperationException("目标 XML 属于另一份结构，请更换输出文件。");
            }
            var produced = xml.Root!.Elements().ToList();
            foreach (XElement item in produced)
            {
                string id = (string)item.Element("defName");
                var old = current.FirstOrDefault(e =>
                    string.Equals((string)e.Element("defName"), id, StringComparison.Ordinal));
                if (old != null) old.ReplaceWith(new XElement(item));
                else root.Add(new XElement(item));
            }
            // Only remove the editor-owned conditional companion if the user
            // switched away from Universal. Never touch other definitions.
            if (!produced.Any(e => (string)e.Element("defName") == extensionId))
                foreach (XElement leftover in root.Elements().Where(e =>
                    (string)e.Element("defName") == extensionId
                    && e.Name.LocalName == "StructureFramework.StructureDef"
                    && e.Attribute("MayRequire") != null).ToList())
                    leftover.Remove();
            xml = existing;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BackupExisting(path);
        string temp = path + ".writing";
        xml.Save(temp); File.Move(temp, path, true);
    }
    internal static SaveResult SaveProject(StructureDocument document, Catalog catalog, string path)
    {
        document.Save(path);
        if (!document.AutoExportOnSave) return new(null, null);
        string xmlPath = XmlTarget(document, path, Path.GetDirectoryName(Path.GetFullPath(path))!);
        try { ExportXml(document, catalog, xmlPath, "Framework"); return new(xmlPath, null); }
        catch (Exception ex) { return new(xmlPath, ex.Message); }
    }
    internal static void BackupExisting(string path)
    {
        if (!File.Exists(path)) return;
        string folder = Path.Combine(MP_DataDirectories.BackupDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder); File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
    }
    public static string ResolveTarget(string currentFile, string previousId, string id, string folder, bool relocate)
    {
        if (relocate || string.IsNullOrWhiteSpace(currentFile) || !currentFile.EndsWith(".json",StringComparison.OrdinalIgnoreCase)) return Path.Combine(folder,id+".structure.json");
        return previousId != null && previousId != id ? Path.Combine(Path.GetDirectoryName(currentFile)!,id+".structure.json") : currentFile;
    }
    public static void CheckDuplicate(string target,string id,string library,bool recursive=false)
    {
        if(!Directory.Exists(library))return;
        foreach(string file in Directory.EnumerateFiles(library,"*.structure.json",recursive?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly))
        {
            if(Path.GetFullPath(file).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))continue;
            try { if(StructureDocument.Load(file).DefName==id)throw new InvalidOperationException("该目录已存在相同结构标识："+Path.GetFileName(file)); }
            catch(System.Text.Json.JsonException) { }
        }
    }
}
