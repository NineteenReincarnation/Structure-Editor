using System.Xml.Linq;

namespace StructureEditor;

internal static class CatalogGrouping
{
    // Architect/Wiki order, with frequently used specialist groups kept adjacent.
    private static readonly string[] order = { "结构", "生产", "家具", "储存", "电力", "安保", "炮塔", "医疗", "照明", "温度", "娱乐", "艺术", "飞船与运输", "仪式", "收容", "机械族设施", "遗迹与废墟", "自然岩石", "杂项", "人工地板", "平台与桥梁", "自然地表", "水域", "其他地形", "作业机械体", "近战与支援机械体", "远程战斗机械体", "重型战斗机械体", "首领机械体", "人类与类人单位", "动物" };
    public static int Order(string group) { int position = Array.IndexOf(order, group); return position < 0 ? order.Length : position; }
    public static string Building(XElement def)
    {
        string id = (string)def.Element("defName") ?? "", type = (string)def.Element("thingClass") ?? "";
        string category = (string)def.Element("designationCategory") ?? "";
        string comps = string.Join(" ", def.Element("comps")?.Elements().Select(e => (string)e.Attribute("Class") ?? "") ?? Array.Empty<string>());
        bool Has(string value) => (id + " " + type + " " + comps).Contains(value, StringComparison.OrdinalIgnoreCase);
        if (def.Element("building")?.Element("turretGunDef") != null || Has("Turret")) return "炮塔";
        if (Has("ForgeNest") || Has("Assembler")) return "生产";
        if (Has("PowerPlant") || Has("PowerBattery") || Has("PowerConduit") || category == "Power" || Has("PowerCell")) return "电力";
        if (Has("Door") || id is "Wall" or "Fence" || category == "Structure") return "结构";
        if (category == "Security" || Has("Shield") || Has("Trap") || Has("Sandbag") || Has("Barricade")) return "安保";
        if (category == "Temperature" || Has("Heater") || Has("Cooler") || id is "Vent" or "Campfire") return "温度";
        if (Has("Hospital") || Has("Medical") || Has("VitalsMonitor")) return "医疗";
        if (category == "Storage" || Has("Storage") || Has("Shelf")) return "储存";
        if (category == "Production" || Has("WorkTable") || Has("Spawner")) return "生产";
        if (Has("Glower") || Has("Lamp")) return "照明";
        if (category is "Ship" or "Gravship" || Has("Gravship") || Has("Transporter")) return "飞船与运输";
        if (category.Contains("Containment", StringComparison.OrdinalIgnoreCase) || Has("Containment")) return "收容";
        if (category is "Ideology" or "Ritual" || Has("Ritual")) return "仪式";
        if (category == "Joy") return "娱乐";
        if (category == "Art" || Has("Sculpture")) return "艺术";
        if (category == "Furniture" || Has("Bed")) return "家具";
        if (Has("Mech") || (bool?)def.Element("isMechClusterThreat") == true) return "机械族设施";
        if ((bool?)def.Element("building")?.Element("isNaturalRock") == true || Has("Mineable")) return "自然岩石";
        if (Has("Ancient") || Has("Ruined") || Has("Debris") || Has("Crater") || Has("CollapsedRocks")) return "遗迹与废墟";
        return "杂项";
    }
    public static string Terrain(XElement def)
    {
        string id = (string)def.Element("defName") ?? "";
        if (id.Contains("Bridge", StringComparison.OrdinalIgnoreCase) || id.Contains("Platform", StringComparison.OrdinalIgnoreCase)) return "平台与桥梁";
        if (def.Element("tags")?.Elements().Any(e => e.Value.Contains("Water", StringComparison.OrdinalIgnoreCase)) == true || id.Contains("Water", StringComparison.OrdinalIgnoreCase)) return "水域";
        if ((string)def.Element("designationCategory") == "Floors" || (bool?)def.Element("layerable") == true) return "人工地板";
        if ((float?)def.Element("fertility") > 0 || id.Contains("Soil") || id.Contains("Sand") || id.Contains("Gravel")) return "自然地表";
        return "其他地形";
    }
    public static string Pawn(XElement kind, XElement race)
    {
        string id = (string)kind.Element("defName") ?? "";
        if ((string)race.Element("race")?.Element("fleshType") != "Mechanoid") return (string)race.Element("race")?.Element("intelligence") == "Humanlike" ? "人类与类人单位" : "动物";
        if (id is "Mech_Diabolus" or "Mech_Warqueen" or "Mech_Apocriton") return "首领机械体";
        if (race.Element("race")?.Element("mechEnabledWorkTypes")?.HasElements == true) return "作业机械体";
        if ((float?)kind.Element("combatPower") >= 500) return "重型战斗机械体";
        if (kind.Element("weaponTags")?.HasElements == true || race.Element("verbs")?.HasElements == true) return "远程战斗机械体";
        return "近战与支援机械体";
    }
}
