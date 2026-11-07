using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StructureEditor;

// A rename is planned first, including the exact exported XML files. Every
// source file is backed up before any JSON/XML/preview file is changed.
internal static class MP_StructureRenamer
{
    internal sealed record MP_FileChange(string OldPath, string NewPath, string NewContent, bool IsXml);
    internal sealed class MP_RenamePlan
    {
        internal required string OldId { get; init; }
        internal required string NewId { get; init; }
        internal required string NewLabel { get; init; }
        internal required string SourceFile { get; init; }
        internal required IReadOnlyList<MP_FileChange> Changes { get; init; }
        internal string SourceTarget => Changes.First(c => Same(c.OldPath, SourceFile)).NewPath;
        internal IReadOnlyList<string> XmlPaths => Changes.Where(c => c.IsXml).Select(c => c.OldPath).ToList();
        internal string NewPathFor(string original) => Changes.FirstOrDefault(c => Same(c.OldPath, original))?.NewPath ?? original;
    }

    private static bool Same(string a, string b) =>
        Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    internal static MP_RenamePlan Prepare(
        string sourceFile, string originalId, string requestedId, string requestedLabel,
        IEnumerable<string> discoveredXml)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("原结构文件不存在。", sourceFile);
        string oldId = originalId?.Trim() ?? "";
        string newId = requestedId?.Trim() ?? "";
        string newLabel = requestedLabel?.Trim() ?? "";
        if (oldId.Length == 0 || newLabel.Length == 0)
            throw new InvalidOperationException("结构标识和显示名称不能为空。");
        if (newId.Length > 128 || !Regex.IsMatch(newId, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new InvalidOperationException("DefName 只能使用英文字母、数字和下划线，且不能以数字开头（最长 128 个字符）。");
        if (newId != oldId && !newId.StartsWith("MP_", StringComparison.Ordinal))
            throw new InvalidOperationException("新的结构 DefName 须以 MP_ 开头。");
        if (newLabel.Length > 180)
            throw new InvalidOperationException("显示名称不能超过 180 个字符。");

        string source = Path.GetFullPath(sourceFile);
        bool jsonSource = source.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        bool xmlSource = source.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        if (!jsonSource && !xmlSource) throw new InvalidOperationException("仅支持结构 JSON 或 XML。");

        var changes = new List<MP_FileChange>();
        var xmlCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in discoveredXml ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                xmlCandidates.Add(Path.GetFullPath(path));

        if (jsonSource)
        {
            JsonNode node = JsonNode.Parse(File.ReadAllText(source)) ?? throw new InvalidDataException("JSON 文件为空。");
            if (node is not JsonObject root || root["DefName"]?.GetValue<string>() != oldId)
                throw new InvalidDataException("JSON 的 DefName 与当前选中结构不一致，请刷新列表。");
            var project = StructureDocument.Load(source);
            var prospective = project.Copy();
            prospective.DefName = newId;
            prospective.Label = newLabel;
            _ = prospective.ToXml(prospective.ExportProfile == "MechProtocol");
            root["DefName"] = newId;
            root["Label"] = newLabel;
            if (!string.IsNullOrWhiteSpace(project.XmlExportDirectory) && Directory.Exists(project.XmlExportDirectory))
            {
                // The saved output directory is authoritative, even when
                // it differs from the directory containing the JSON project.
                string oldExport = Path.Combine(project.XmlExportDirectory, oldId + ".xml");
                if (File.Exists(oldExport)) xmlCandidates.Add(Path.GetFullPath(oldExport));
            }
            string sibling = Path.Combine(Path.GetDirectoryName(source)!, oldId + ".xml");
            if (File.Exists(sibling)) xmlCandidates.Add(Path.GetFullPath(sibling));
            string target = RenameIfNamed(source, oldId + ".structure.json", newId + ".structure.json",
                oldId + ".json", newId + ".json");
            changes.Add(new MP_FileChange(source, target,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, false));
        }
        else xmlCandidates.Add(source);

        foreach (string path in xmlCandidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            XDocument xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            IEnumerable<XElement> definitions = xml.Root?.Name.LocalName == "Defs"
                ? xml.Root.Elements()
                : xml.Root is null ? Enumerable.Empty<XElement>() : new[] { xml.Root };
            XElement[] matches = definitions.Where(x => (string)x.Element("defName") == oldId).ToArray();
            if (matches.Length == 0)
            {
                // Don't touch an unrelated XML with a matching filename.
                if (Same(source, path)) throw new InvalidDataException("选中的 XML 没有原结构 DefName。");
                continue;
            }
            if (matches.Length > 1) throw new InvalidDataException("XML 含有重复的原结构标识：" + path);
            if (definitions.Any(x => x != matches[0] && (string)x.Element("defName") == newId))
                throw new InvalidOperationException("XML 已包含目标 DefName：" + path);
            matches[0].Element("defName")!.Value = newId;
            if (matches[0].Element("label") is XElement label) label.Value = newLabel;
            else matches[0].Add(new XElement("label", newLabel));
            bool singleDefinition = definitions.Count() == 1;
            string target = singleDefinition
                ? RenameIfNamed(path, oldId + ".xml", newId + ".xml")
                : path;
            string updated = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine
                + xml.ToString(SaveOptions.DisableFormatting);
            changes.Add(new MP_FileChange(path, target, updated, true));
        }

        if (!changes.Any(c => Same(c.OldPath, source)))
            throw new InvalidDataException("原结构文件未找到可重命名的定义。");

        if (newId != oldId)
        {
            // Move hand-authored preview PNG files with the source project, so
            // the library doesn't lose the image after a DefName change.
            var directories = changes.Select(c => Path.GetDirectoryName(c.OldPath)!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in directories)
                foreach (string suffix in new[] { ".png", "_preview.png", ".preview.png" })
                {
                    string before = Path.Combine(folder, oldId + suffix);
                    if (!File.Exists(before)) continue;
                    changes.Add(new MP_FileChange(before, Path.Combine(folder, newId + suffix), null, false));
                }
        }

        foreach (MP_FileChange change in changes)
        {
            if (!Same(change.OldPath, change.NewPath) && File.Exists(change.NewPath))
                throw new IOException("重命名目标已存在，拒绝覆盖：" + change.NewPath);
        }
        if (changes.GroupBy(c => c.NewPath, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new IOException("多个文件将被重命名到相同路径。");

        return new MP_RenamePlan
        {
            OldId = oldId, NewId = newId, NewLabel = newLabel,
            SourceFile = source, Changes = changes
        };
    }

    private static string RenameIfNamed(string path, string oldFile, string newFile,
        string secondOldFile = null, string secondNewFile = null)
    {
        string name = Path.GetFileName(path);
        if (name.Equals(oldFile, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetDirectoryName(path)!, newFile);
        if (secondOldFile != null && name.Equals(secondOldFile, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetDirectoryName(path)!, secondNewFile!);
        return path;
    }

    internal static string Apply(MP_RenamePlan plan)
    {
        if (plan.Changes.Count == 0) throw new InvalidOperationException("没有可更新的文件。");
        string backupDir = Path.Combine(Program.DataDirectory, "Backups",
            "Rename_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backupDir);
        var backups = new List<(MP_FileChange Change, string Backup)>();
        var tempFiles = new List<string>();
        try
        {
            for (int index = 0; index < plan.Changes.Count; index++)
            {
                MP_FileChange change = plan.Changes[index];
                if (!File.Exists(change.OldPath)) throw new FileNotFoundException("结构文件在重命名过程中消失。", change.OldPath);
                if (!Same(change.OldPath, change.NewPath) && File.Exists(change.NewPath))
                    throw new IOException("目标文件已存在：" + change.NewPath);
                string backup = Path.Combine(backupDir, index.ToString("D3") + "_" + Path.GetFileName(change.OldPath));
                File.Copy(change.OldPath, backup, false);
                backups.Add((change, backup));
            }

            foreach (MP_FileChange change in plan.Changes)
            {
                if (change.NewContent is null)
                {
                    if (!Same(change.OldPath, change.NewPath))
                        File.Move(change.OldPath, change.NewPath);
                    continue;
                }
                string temp = change.NewPath + ".rename-" + Guid.NewGuid().ToString("N") + ".writing";
                tempFiles.Add(temp);
                File.WriteAllText(temp, change.NewContent, new UTF8Encoding(false));
                File.Move(temp, change.NewPath, Same(change.OldPath, change.NewPath));
                if (!Same(change.OldPath, change.NewPath))
                    File.Delete(change.OldPath);
            }
            return backupDir;
        }
        catch (Exception original)
        {
            var failures = new List<string>();
            foreach (var (change, backup) in backups)
            {
                try
                {
                    File.Copy(backup, change.OldPath, true);
                    if (!Same(change.OldPath, change.NewPath) && File.Exists(change.NewPath))
                        File.Delete(change.NewPath);
                }
                catch (Exception restoreError) { failures.Add(restoreError.Message); }
            }
            if (failures.Count != 0)
                throw new IOException("重命名失败，且部分文件自动恢复失败；原件备份在：" + backupDir
                    + Environment.NewLine + string.Join(Environment.NewLine, failures), original);
            throw new IOException("重命名失败，已从备份恢复所有原文件：" + original.Message, original);
        }
        finally
        {
            foreach (string temp in tempFiles)
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
