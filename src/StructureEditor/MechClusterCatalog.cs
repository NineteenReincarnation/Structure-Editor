using System.Xml.Linq;

namespace StructureEditor;

internal static class MechClusterCatalog
{
    public const string SourceUrl = "https://rimworld.huijiwiki.com/wiki/%E6%9C%BA%E6%A2%B0%E9%9B%86%E7%BE%A4";
    // Wiki building-type headings; names are aliases, while graphics/DefNames come from installed 1.7.
    public static readonly string[] Groups = { "杂项", "控制器", "炮塔" };
    public static readonly IReadOnlyDictionary<string, (string Group, string Name)> Entries = new Dictionary<string, (string, string)>
    {
        ["MechCapsule"] = ("杂项", "机械舱"), ["MechAssembler"] = ("杂项", "机械装配器"),
        ["MechDropBeacon"] = ("杂项", "机械空投信标"), ["ActivatorProximity"] = ("杂项", "探测激活器"),
        ["ActivatorCountdown"] = ("杂项", "定时激活器"), ["ShieldGeneratorBullets"] = ("杂项", "机械低角护盾"),
        ["ShieldGeneratorMortar"] = ("杂项", "机械高角护盾"), ["Gloomlight"] = ("杂项", "机械节点"),
        ["UnstablePowerCell"] = ("杂项", "不稳定能量电池"),
        ["PsychicDroner"] = ("控制器", "心灵干扰器"), ["ToxicSpewer"] = ("控制器", "空气污染器"),
        ["SmokeSpewer"] = ("控制器", "烟雾产生器"), ["SunBlocker"] = ("控制器", "黑幕产生器"),
        ["PsychicSuppressor"] = ("控制器", "心灵抑制器"), ["WeatherController"] = ("控制器", "气象操纵仪"),
        ["EMIDynamo"] = ("控制器", "电磁干扰器"), ["Defoliator"] = ("控制器", "蚀叶器"),
        ["ClimateAdjuster"] = ("控制器", "气候调节器"),
        ["Turret_AutoMortar"] = ("炮塔", "自动迫击炮"), ["Turret_AutoInferno"] = ("炮塔", "自动地狱火加农炮"),
        ["Turret_AutoChargeBlaster"] = ("炮塔", "自动电荷冲击炮塔"), ["Turret_AutoMiniTurret"] = ("炮塔", "小型冲击炮塔")
    };
    public static void Assign(CatalogItem item, XElement def)
    {
        if (Entries.TryGetValue(item.DefName, out var entry))
        {
            item.ClusterGroup = entry.Group; item.ClusterWikiName = entry.Name; item.ClusterWikiEntry = true; return;
        }
        // Native incident members absent from the Wiki table are not silently presented as Wiki entries.
        bool mod = item.IsMod;
        bool tagged = def.Element("building")?.Element("buildingTags")?.Elements("li").Any(e => e.Value.StartsWith("MechCluster", StringComparison.Ordinal)) == true;
        if (!mod || (!tagged && (bool?)def.Element("isMechClusterThreat") != true)) return;
        bool controller = def.Element("comps")?.Elements().Any(e => ((string)e.Attribute("Class") ?? "").Contains("CauseGameCondition", StringComparison.OrdinalIgnoreCase)) == true;
        item.ClusterGroup = item.Group == "炮塔" ? "炮塔" : controller ? "控制器" : "杂项";
    }
}
