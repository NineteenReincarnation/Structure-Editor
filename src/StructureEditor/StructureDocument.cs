using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StructureEditor.Compatibility;

namespace StructureEditor;

public class PlacedItem
{
    public string Kind { get; set; } = "Building";
    public string DefName { get; set; }
    public string Stuff { get; set; }
    public int X { get; set; }
    public int Z { get; set; }
    public int Rotation { get; set; }
    public bool? StartDormant { get; set; }
    public List<string> FrameworkModifiers { get; set; } = new();
    public PlacedItem Copy() { var copy=(PlacedItem)MemberwiseClone();copy.FrameworkModifiers=new(FrameworkModifiers ?? new());return copy; }
}

public class StructureDocument
{
    public int FormatVersion { get; set; } = 1;
    public string DefName { get; set; } = "SE_NewStructure";
    public string Label { get; set; } = "新结构";
    public string Description { get; set; } = "使用 Structure Editor 创建的结构布局。";
    public string Category { get; set; } = "General";
    public bool StartDormant { get; set; }
    public string ExportProfile { get; set; } = "Framework";
    public bool AutoExportOnSave { get; set; } = true;
    public string XmlExportDirectory { get; set; }
    public bool EnableMPCompatibility { get; set; }
    public Rectangle? TemplateBounds { get; set; }
    public string PrefabRotations { get; set; } = "North";
    public bool PrefabEdgeOnly { get; set; }
    public List<string> RequiredPackages { get; set; } = new();
    public string FrameworkPlacement { get; set; }
    public List<string> FrameworkTags { get; set; } = new();
    public List<PlacedItem> Items { get; set; } = new();
    public StructureDocument Copy() => new()
    {
        FormatVersion = FormatVersion, DefName = DefName, Label = Label, Description = Description, Category = Category, StartDormant = StartDormant, ExportProfile = ExportProfile, EnableMPCompatibility = EnableMPCompatibility, TemplateBounds = TemplateBounds, PrefabRotations = PrefabRotations, PrefabEdgeOnly = PrefabEdgeOnly,
        Items = Items.Select(i => i.Copy()).ToList(), RequiredPackages = new(RequiredPackages ?? new()), FrameworkPlacement = FrameworkPlacement, FrameworkTags = new(FrameworkTags ?? new()),
        AutoExportOnSave = AutoExportOnSave, XmlExportDirectory = XmlExportDirectory
    };
    public XElement ToXml(bool legacy = false)
    {
        if (!Regex.IsMatch(DefName ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidOperationException("标识须使用英文字母、数字和下划线，不能以数字开头。");
        if (legacy && !DefName.StartsWith("MP_")) throw new InvalidOperationException("兼容导出要求标识使用 MP_ 前缀；通用工程不受此限制。");
        if (legacy && Category == "MechCluster")
        {
            string error = MPClusterPresets.Validate(DefName, Label);
            if (error != null) throw new InvalidOperationException(error + "\n请按目标模组的机械集群命名规范设置标识与名称。");
        }
        XElement root = new(legacy ? "MechProtocol.MP_StructureDef" : "StructureEditor.StructureDef",
            new XElement("defName", DefName), new XElement("label", Label),
            new XElement("description", Description ?? ""),
            new XElement("structureCategory", Category), new XElement("startDormant", StartDormant.ToString().ToLowerInvariant()));
        foreach (string kind in new[] { "Building", "Pawn", "Terrain" })
        {
            XElement list = new(kind == "Building" ? "buildings" : kind == "Pawn" ? "pawns" : "terrain");
            foreach (PlacedItem item in Items.Where(i => i.Kind == kind))
            {
                XElement entry = new("li", new XElement(kind == "Building" ? "thingDef" : kind == "Pawn" ? "pawnKind" : "terrainDef", item.DefName),
                    new XElement("position", $"({item.X},0,{item.Z})"));
                if (kind != "Terrain") entry.Add(new XElement("rotation", item.Rotation));
                if (kind != "Terrain" && item.StartDormant.HasValue) entry.Add(new XElement("startDormant", item.StartDormant.Value.ToString().ToLowerInvariant()));
                if (kind == "Building" && !string.IsNullOrWhiteSpace(item.Stuff)) entry.Add(new XElement("stuffDef", item.Stuff));
                list.Add(entry);
            }
            root.Add(list);
        }
        return root;
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        XDocument xml = new(new XDeclaration("1.0", "utf-8", null), new XElement("Defs", ToXml()));
        if (File.Exists(path))
        {
            string backup = Path.Combine(Program.DataDirectory, "Backups");
            Directory.CreateDirectory(backup);
            File.Copy(path, Path.Combine(backup, Path.GetFileNameWithoutExtension(path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + Path.GetExtension(path)));
        }
        string temp = path + ".writing";
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); else xml.Save(temp);
        File.Move(temp, path, true);
    }
    public static StructureDocument Load(string path, Catalog catalog = null, string definitionName = null)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string json=File.ReadAllText(path);
            var loaded=JsonSerializer.Deserialize<StructureDocument>(json) ?? throw new InvalidDataException("工程文件为空。");
            if (loaded.FormatVersion != 1) throw new InvalidDataException("不支持该工程格式版本：" + loaded.FormatVersion);
            loaded.Items ??= new();
            using var properties=JsonDocument.Parse(json);
            if(!properties.RootElement.TryGetProperty(nameof(ExportProfile),out _))
                loaded.EnableMPCompatibility=loaded.StartDormant || loaded.Items.Any(i=>i.StartDormant==true);
            return loaded;
        }
        XElement root = XDocument.Load(path).Root!;
        var definitions = root.Name.LocalName == "Defs" ? root.Elements() : new[] { root };
        XElement selected = definitionName == null ? definitions.FirstOrDefault(d => (string)d.Attribute("Abstract") != "True" && d.Name.LocalName is "PrefabDef" or "StructureFramework.StructureDef" or "MechProtocol.MP_StructureDef" or "StructureEditor.StructureDef") : definitions.FirstOrDefault(d => (string)d.Element("defName") == definitionName);
        StructureDocument FromXml(StructureDocument value) { value.XmlExportDirectory = Path.GetDirectoryName(Path.GetFullPath(path)); return value; }
        if (selected?.Name.LocalName == "PrefabDef") return FromXml(PrefabCodec.Import(selected));
        if (selected?.Name.LocalName == "StructureFramework.StructureDef") return FromXml(FrameworkCodec.Import(selected, catalog, root));
        XElement def = selected;
        if (def == null || def.Name.LocalName is not ("MechProtocol.MP_StructureDef" or "StructureEditor.StructureDef")) throw new InvalidDataException("请选择结构工程或支持的结构布局 XML。");
        StructureDocument doc = new()
        {
            ExportProfile = def.Name.LocalName == "MechProtocol.MP_StructureDef" ? "MechProtocol" : "Framework", EnableMPCompatibility = def.Name.LocalName == "MechProtocol.MP_StructureDef",
            DefName = (string)def.Element("defName"), Label = (string)def.Element("label") ?? "未命名结构", Description = (string)def.Element("description") ?? "",
            Category = (string)def.Element("structureCategory") ?? "General", StartDormant = (bool?)def.Element("startDormant") ?? false
        };
        if (doc.Category == "MechCluster" && doc.DefName == MP_ClusterNaming.LegacyStarterDefName)
        { doc.DefName = MP_ClusterNaming.DefaultDefName; doc.Label = MP_ClusterNaming.Label(MP_ClusterNaming.ScytherForgeLabel, 1, 1); }
        foreach (var (list, kind, field) in new[] { ("buildings", "Building", "thingDef"), ("pawns", "Pawn", "pawnKind"), ("terrain", "Terrain", "terrainDef") })
            foreach (XElement entry in def.Element(list)?.Elements("li") ?? Enumerable.Empty<XElement>())
            {
                int[] position = Regex.Matches((string)entry.Element("position") ?? "", @"-?\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                if (position.Length != 3) throw new InvalidDataException("position 必须为 (x,0,z)。");
                int rot = (int?)entry.Element("rotation") ?? 0;
                if (rot < 0 || rot > 3) throw new InvalidDataException("旋转值必须为 0～3。");
                doc.Items.Add(new PlacedItem { Kind = kind, DefName = (string)entry.Element(field), Stuff = (string)entry.Element("stuffDef"), X = position[0], Z = position[2], Rotation = rot, StartDormant = kind == "Terrain" ? null : (bool?)entry.Element("startDormant") });
            }
        if(doc.StartDormant || doc.Items.Any(i=>i.StartDormant==true))doc.EnableMPCompatibility=true;
        return FromXml(doc);
    }
    public List<string> Validate(Catalog catalog)
    {
        List<string> issues = new();
        if (Items.Count == 0) issues.Add("结构为空。");
        HashSet<Point> buildings = new(), pawns = new(), terrain = new();
        foreach (PlacedItem item in Items)
        {
            CatalogItem entry = catalog.Find(item.Kind, item.DefName);
            if (entry == null) { issues.Add("缺少 Def：" + item.DefName); continue; }
            if (item.StartDormant.HasValue && !entry.CanBeDormant) issues.Add("对象不支持休眠：" + item.DefName);
            if (item.Kind == "Building")
            {
                if (entry.MaterialCategories.Count > 0 && !catalog.MaterialsFor(entry).Any(m => m.DefName == item.Stuff)) issues.Add("材质不兼容：" + item.DefName);
                foreach (Point cell in Footprint(item, catalog).GetCells()) if (!buildings.Add(cell)) issues.Add($"建筑重叠：({cell.X},{cell.Y})");
            }
            else if (item.Kind == "Pawn" && !pawns.Add(new Point(item.X, item.Z))) issues.Add("单位位置重复。");
            else if (item.Kind == "Terrain" && !terrain.Add(new Point(item.X, item.Z))) issues.Add("地板位置重复。");
        }
        if (pawns.Any(buildings.Contains)) issues.Add("单位与建筑占地重叠。");
        return issues.Distinct().ToList();
    }
    public static Rectangle Footprint(PlacedItem item, Catalog catalog)
    {
        CatalogItem entry = catalog.Find(item.Kind, item.DefName);
        int w = entry?.Width ?? 1, h = entry?.Height ?? 1;
        if (item.Kind != "Building") w = h = 1;
        if (item.Rotation % 2 != 0) (w, h) = (h, w);
        // Matches GenAdj.OccupiedRect's even-size orientation bias.
        int x = item.X - (w - 1) / 2, z = item.Z - (h - 1) / 2;
        if (w % 2 == 0 && item.Rotation is 2 or 3) x--;
        if (h % 2 == 0 && item.Rotation is 1 or 2) z--;
        return new Rectangle(x, z, w, h);
    }
}

public static class RectangleCells
{
    public static IEnumerable<Point> GetCells(this Rectangle rect)
    {
        for (int z = rect.Top; z < rect.Bottom; z++) for (int x = rect.Left; x < rect.Right; x++) yield return new Point(x, z);
    }
}
