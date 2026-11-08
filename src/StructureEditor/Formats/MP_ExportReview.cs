using System.Xml.Linq;

namespace StructureEditor;

// Evaluates what this specific output can represent. Missing local mods are
// warnings; unsafe or lossy format conversion is an error.
internal static class MP_ExportReview
{
    internal sealed record Issue(bool IsError, string Message);
    internal sealed class Result
    {
        internal readonly List<Issue> Issues = new();
        internal readonly List<CatalogSource> Dependencies = new();
        internal int Errors => Issues.Count(issue => issue.IsError);
        internal int Warnings => Issues.Count(issue => !issue.IsError);
        internal bool CanExport => Errors == 0;
        internal string Summary => Errors != 0 ? $"不能导出 · {Errors} 项错误"
            : Warnings != 0 ? $"可导出 · {Warnings} 项提醒" : "导出检查通过";
        internal void Error(string message) => Add(true, message);
        internal void Warn(string message) => Add(false, message);
        private void Add(bool error, string message)
        {
            if (Issues.Any(i => i.IsError == error && i.Message == message)) return;
            Issues.Add(new(error, message));
        }
    }

    internal static Result Inspect(StructureDocument document, Catalog catalog, string profile = null)
    {
        var result = new Result();
        if (document == null || catalog == null)
        {
            result.Error("素材目录尚未加载，无法进行导出检查。");
            return result;
        }
        if (string.IsNullOrWhiteSpace(document.DefName))
            result.Error("结构标识不能为空。");
        foreach (string issue in document.Validate(catalog))
        {
            if (issue.StartsWith("缺少 Def：", StringComparison.Ordinal))
            {
                result.Warn(issue + "（本机素材不完整，需在目标游戏中检查）");
            }
            else result.Error(issue);
        }
        foreach (PlacedItem item in document.Items)
        {
            var entry = catalog.Find(item.Kind, item.DefName);
            if (entry?.IsMod == true && string.IsNullOrWhiteSpace(entry.PackageId))
                result.Warn("无法确认素材所属的 PackageId：" + item.DefName);
        }
        try
        {
            result.Dependencies.AddRange(ExportDependencies.Resolve(document, catalog, "Framework"));
            foreach (CatalogSource dependency in result.Dependencies)
            {
                if (!catalog.Sources.Any(s => s.PackageId.Equals(dependency.PackageId, StringComparison.OrdinalIgnoreCase)))
                    result.Warn("当前素材目录未加载依赖：" + dependency.PackageId);
            }
        }
        catch (Exception ex) { result.Error("解析依赖失败：" + ex.Message); }
        if (result.Errors == 0)
        {
            try { _ = ExportDependencies.Xml(document, catalog, "Framework"); }
            catch (Exception ex) { result.Error(ex.Message); }
        }
        return result;
    }
}
