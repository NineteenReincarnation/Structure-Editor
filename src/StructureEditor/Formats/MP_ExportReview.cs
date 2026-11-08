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

    internal static bool MP_IsCore(CatalogItem item) =>
        item != null && !item.IsMod &&
        (string.Equals(item.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Pack, "Core", StringComparison.OrdinalIgnoreCase));

    internal static bool MP_CanRecommendDormancy(StructureDocument document, Catalog catalog) =>
        catalog != null && document.Items.Any(i =>
            i.Kind != "Terrain" && catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true);

    internal static Result Inspect(StructureDocument document, Catalog catalog, string profile = null)
    {
        var result = new Result();
        if (document == null || catalog == null)
        {
            result.Error("素材目录尚未加载，无法进行导出检查。");
            return result;
        }
        profile ??= document.ExportProfile;
        if (!new[] { "VanillaPrefab", "Framework", "MechProtocol", "Universal" }.Contains(profile))
            result.Error("未知导出格式：" + profile);
        if (string.IsNullOrWhiteSpace(document.DefName))
            result.Error("结构标识不能为空。");
        foreach (string issue in document.Validate(catalog))
        {
            if (issue.StartsWith("缺少 Def：", StringComparison.Ordinal))
            {
                if (profile == "VanillaPrefab") result.Error(issue + "（原版布局无法确认对象占地）");
                else result.Warn(issue + "（本机素材不完整，需在目标游戏中检查）");
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
            result.Dependencies.AddRange(ExportDependencies.Resolve(document, catalog, profile));
            foreach (CatalogSource dependency in result.Dependencies)
            {
                if (!catalog.Sources.Any(s => s.PackageId.Equals(dependency.PackageId, StringComparison.OrdinalIgnoreCase)))
                    result.Warn("当前素材目录未加载依赖：" + dependency.PackageId);
            }
        }
        catch (Exception ex) { result.Error("解析依赖失败：" + ex.Message); }
        if (profile == "Framework" && (document.StartDormant ||
            document.Items.Any(i => i.StartDormant == true)) && !document.EnableMPCompatibility)
            result.Error("已设置休眠初态，但 Framework 的 MP 休眠兼容未启用；启用拓展或清除休眠设置。");
        if (profile == "Universal")
        {
            int core = document.Items.Count(item => (item.Kind == "Building" || item.Kind == "Terrain")
                && MP_IsCore(catalog.Find(item.Kind, item.DefName)));
            int removed = document.Items.Count - core;
            if (core == 0) result.Warn("原版环境没有可生成的基础布局；只会加载满足条件的拓展定义。");
            if (removed > 0) result.Warn($"原版基础布局不包含 {removed} 个扩展素材或单位；完整内容保留在条件拓展定义中。");
            result.Warn("基础 PrefabDef 与增强版是独立定义，不会自动相互替换；地图生成入口需选择对应版本。");
            if ((document.StartDormant || document.Items.Any(i => i.StartDormant == true))
                && !document.EnableMPCompatibility)
                result.Error("已设置休眠初态，必须启用 Framework 下的 MP 休眠拓展才能无损生成通用兼容 XML。");
        }
        if (result.Errors == 0)
        {
            try { _ = ExportDependencies.Xml(document, catalog, profile); }
            catch (Exception ex) { result.Error(ex.Message); }
        }
        return result;
    }
}
