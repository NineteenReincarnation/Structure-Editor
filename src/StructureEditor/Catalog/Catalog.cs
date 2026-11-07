using System.Formats.Tar;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StructureEditor;

public class CatalogItem
{
    public string Kind { get; set; }
    public string DefName { get; set; }
    public string Label { get; set; }
    public string Pack { get; set; }
    public string PackageId { get; set; }
    public bool IsMod { get; set; }
    public string Group { get; set; }
    public string ClusterGroup { get; set; }
    public string ClusterWikiName { get; set; }
    public bool ClusterWikiEntry { get; set; }
    public string Texture { get; set; }
    public string Icon { get; set; }
    public string GraphicClass { get; set; }
    public string TopTexture { get; set; }
    public float TopSize { get; set; } = 1;
    public float DrawX { get; set; } = 1;
    public float DrawZ { get; set; } = 1;
    public float OffsetX { get; set; }
    public float OffsetZ { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public bool Rotatable { get; set; } = true;
    public bool Linked { get; set; }
    public string LinkType { get; set; }
    public List<string> LinkFlags { get; set; } = new();
    public bool CanBeDormant { get; set; }
    public float? TurretExplosionRadius { get; set; }
    public float? ProximityTriggerRadius { get; set; }
    public float? DormantWakeRadius { get; set; }
    public List<string> MaterialCategories { get; set; } = new();
    public Color Color { get; set; } = Color.White;
    public override string ToString() => Label + "  [" + DefName + "]";
}
public class MaterialItem
{
    public string Appearance { get; set; } = "Smooth";
    public string PackageId { get; set; }
    public string DefName { get; set; }
    public string Label { get; set; }
    public List<string> Categories { get; set; } = new();
    public Color Color { get; set; } = Color.White;
    public override string ToString() => Label + " / " + DefName;
}

public class Catalog : IDisposable
{
    public List<CatalogItem> Items { get; } = new();
    public List<MaterialItem> Materials { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<CatalogSource> Sources { get; } = new();
    public Dictionary<string, XElement> StructureDefinitions { get; } = new();
    public Dictionary<string, XElement> PaletteDefinitions { get; } = new();
    public bool RuntimeSnapshotUsed { get; private set; }
    public TextureLibrary Textures { get; private set; }
    private readonly Dictionary<(string Kind, string DefName), CatalogItem> index = new();
    private readonly Dictionary<string, Color> materialColors = new();
    private readonly Dictionary<string, string> materialAppearances = new();
    private readonly Dictionary<CatalogItem, string> searchText = new();
    private readonly Dictionary<string, List<CatalogItem>> byKind = new(), byPack = new(), byGroup = new(), byClusterGroup = new();
    private readonly Dictionary<CatalogItem, IReadOnlyList<MaterialItem>> compatibleMaterials = new();
    private int indexedCount = -1, indexedMaterials = -1;
    public Color MaterialColor(string def, Color fallback)
    {
        EnsureIndexes(); return def != null && materialColors.TryGetValue(def, out var color) ? color : fallback;
    }
    private readonly Dictionary<string, XElement> raw = new(), named = new(), resolved = new();
    private readonly Dictionary<string, string> origins = new(), labels = new();
    private readonly Dictionary<string, CatalogSource> sourceOrigins = new();
    public void RebuildIndexes()
    {
        index.Clear(); materialColors.Clear(); materialAppearances.Clear(); searchText.Clear(); compatibleMaterials.Clear(); byKind.Clear(); byPack.Clear(); byGroup.Clear(); byClusterGroup.Clear();
        static void Add(Dictionary<string, List<CatalogItem>> target, string key, CatalogItem item)
        { if (key == null) return; if (!target.TryGetValue(key, out var list)) target[key] = list = new(); list.Add(item); }
        foreach (var item in Items)
        {
            index[(item.Kind, item.DefName)] = item;
            searchText[item] = string.Join(" ", item.Label, item.DefName, item.Group, item.ClusterWikiName, item.ClusterGroup, item.Pack, item.PackageId);
            Add(byKind, item.Kind, item); Add(byPack, item.Pack, item); Add(byGroup, item.Group, item); Add(byClusterGroup, item.ClusterGroup, item);
        }
        foreach (var material in Materials) if (material.DefName != null)
        {
            materialColors[material.DefName] = material.Color;
            materialAppearances[material.DefName] = material.Appearance ?? "Smooth";
        }
        indexedCount = Items.Count; indexedMaterials = Materials.Count;
    }
    private void EnsureIndexes() { if (indexedCount != Items.Count || indexedMaterials != Materials.Count) RebuildIndexes(); }
    public CatalogItem Find(string kind, string def) { EnsureIndexes(); return index.GetValueOrDefault((kind, def)); }
    public string TextureFor(CatalogItem item, string stuff = null)
    {
        if (item.GraphicClass != "Graphic_Appearances") return item.Texture;
        EnsureIndexes();
        string appearance = stuff == null ? "Smooth" : materialAppearances.GetValueOrDefault(stuff, "Smooth");
        return Textures.AppearancePath(item.Texture, appearance);
    }
    public IEnumerable<CatalogItem> Query(string kind, string pack, string group, string text, bool mechCluster = false)
    {
        EnsureIndexes(); List<CatalogItem> candidates = Items; string query = text?.Trim();
        bool Narrow(Dictionary<string, List<CatalogItem>> groups, string key)
        { if (key == null) return true; if (!groups.TryGetValue(key, out var list)) return false; if (list.Count < candidates.Count) candidates = list; return true; }
        if (!Narrow(byKind, kind) || !Narrow(byPack, pack) || !Narrow(mechCluster ? byClusterGroup : byGroup, group)) return Enumerable.Empty<CatalogItem>();
        return candidates.Where(i => (kind == null || i.Kind == kind) && (pack == null || i.Pack == pack)
            && (!mechCluster || i.ClusterGroup != null) && (group == null || (mechCluster ? i.ClusterGroup : i.Group) == group)
            && (string.IsNullOrEmpty(query) || searchText[i].Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
    public IEnumerable<MaterialItem> MaterialsFor(CatalogItem item)
    {
        EnsureIndexes(); if (item == null) return Array.Empty<MaterialItem>();
        if (!compatibleMaterials.TryGetValue(item, out var result))
        {
            var categories = new HashSet<string>(item.MaterialCategories ?? new());
            compatibleMaterials[item] = result = Array.AsReadOnly(Materials.Where(m => m.Categories?.Any(categories.Contains) == true).ToArray());
        }
        return result;
    }

    public void Load(StudioPaths paths, IProgress<string> progress = null, CancellationToken cancellation = default)
    {
        if (!Directory.Exists(Path.Combine(paths.GameRoot, "Data", "Core", "Defs"))) throw new DirectoryNotFoundException("请选择包含 Data/Core 的 RimWorld 游戏目录。");
        Textures?.Dispose(); Items.Clear(); Materials.Clear(); Sources.Clear(); Warnings.Clear(); StructureDefinitions.Clear(); PaletteDefinitions.Clear();
        raw.Clear(); named.Clear(); resolved.Clear(); origins.Clear(); sourceOrigins.Clear(); labels.Clear(); indexedCount = -1; RuntimeSnapshotUsed = false;
        progress?.Report("正在索引游戏贴图…"); Textures = new TextureLibrary();
        Sources.AddRange(ModSources.Resolve(paths, Warnings));
        HashSet<string> packages = new(Sources.Select(s => s.PackageId), StringComparer.OrdinalIgnoreCase);
        Textures.AddArchive(Path.Combine(paths.GameRoot, "Assets.zip"));
        foreach (CatalogSource source in Sources)
        {
            string folder = source.Root, pack = source.Display;
            cancellation.ThrowIfCancellationRequested(); progress?.Report("正在读取素材：" + pack + "…");
            if (pack != "Core") Textures.AddArchive(Path.Combine(folder, "Assets.zip"));
            List<string> load = new() { folder };
            string folders = Path.Combine(folder, "LoadFolders.xml");
            if (File.Exists(folders))
            {
                XElement version = XDocument.Load(folders).Root?.Element("v1.7");
                if (version != null) load = version.Elements("li").Where(e => e.Attribute("IfModActive") == null || packages.Contains((string)e.Attribute("IfModActive")))
                    .Select(e => Path.GetFullPath(Path.Combine(folder, e.Value.Trim().TrimStart('/', '\\')))).ToList();
            }
            else if (Directory.Exists(Path.Combine(folder, "1.7"))) load.Add(Path.Combine(folder, "1.7"));
            foreach (string part in load.Distinct())
            {
                Textures.AddFolder(Path.Combine(part, "Textures"));
                ReadTranslations(part);
                string defs = Path.Combine(part, "Defs");
                if (!Directory.Exists(defs)) continue;
                foreach (string file in Directory.EnumerateFiles(defs, "*.xml", SearchOption.AllDirectories))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (XElement definition in XDocument.Load(file).Root?.Elements() ?? Enumerable.Empty<XElement>())
                        {
                            string frameworkId = (string)definition.Element("defName");
                            if (frameworkId != null && definition.Name.LocalName == "StructureFramework.StructureDef") { StructureDefinitions[frameworkId] = new(definition); continue; }
                            if (frameworkId != null && definition.Name.LocalName == "StructureFramework.StructurePaletteDef") { PaletteDefinitions[frameworkId] = new(definition); continue; }
                            if (definition.Name.LocalName is not ("ThingDef" or "PawnKindDef" or "TerrainDef")) continue;
                            if (!Allowed(definition, packages)) continue;
                            XElement copy = new(definition);
                            foreach (XElement node in copy.Descendants().ToList()) if (!Allowed(node, packages)) node.Remove();
                            string type = copy.Name.LocalName;
                            string name = (string)copy.Attribute("Name");
                            string id = (string)copy.Element("defName");
                            if (name != null) named[type + ":" + name] = copy;
                            if (id != null && type is "ThingDef" or "PawnKindDef" or "TerrainDef")
                            { raw[type + ":" + id] = copy; origins[type + ":" + id] = pack; sourceOrigins[type + ":" + id] = source; }
                        }
                    }
                    catch (Exception e) { Warnings.Add(Path.GetFileName(file) + ": " + e.Message); }
                }
            }
        }
        progress?.Report("正在整理建筑、单位和地形分类…");
        foreach (var pair in raw)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                XElement def = Resolve(pair.Value, new HashSet<XElement>());
                string type = def.Name.LocalName, id = (string)def.Element("defName");
                if (type == "ThingDef" && def.Element("stuffProps") != null)
                    Materials.Add(new MaterialItem { DefName = id, Label = Label(type, id, def), PackageId = sourceOrigins.GetValueOrDefault(pair.Key)?.PackageId,
                        Appearance = (string)def.Element("stuffProps")?.Element("appearance") ?? "Smooth",
                        Categories = def.Element("stuffProps")?.Element("categories")?.Elements("li").Select(x => x.Value).ToList() ?? new(),
                        Color = ParseColor((string)def.Element("stuffProps")?.Element("color")) });
                if (type == "ThingDef" && (string)def.Element("category") == "Building") AddBuilding(def, pair.Key);
                else if (type == "TerrainDef") AddTerrain(def, pair.Key);
                else if (type == "PawnKindDef") AddPawn(def, pair.Key);
            }
            catch (Exception e) { Warnings.Add(pair.Key + ": " + e.Message); }
        }
        RuntimeSnapshotUsed = RuntimeCatalog.Apply(this,paths);
        RebuildIndexes();
        // XML inheritance trees are loading scratch data, not a runtime catalog cache.
        raw.Clear(); named.Clear(); resolved.Clear(); origins.Clear(); sourceOrigins.Clear(); labels.Clear();
    }
    private static bool Allowed(XElement e, HashSet<string> packages)
    {
        string req = (string)e.Attribute("MayRequire");
        string any = (string)e.Attribute("MayRequireAnyOf");
        return (req == null || req.Split(',').All(x => packages.Contains(x.Trim()))) && (any == null || any.Split(',').Any(x => packages.Contains(x.Trim())));
    }
    private XElement Resolve(XElement node, HashSet<XElement> chain)
    {
        string key = node.Name + ":" + ((string)node.Attribute("Name") ?? (string)node.Element("defName"));
        if (resolved.TryGetValue(key, out XElement cached)) return new XElement(cached);
        if (!chain.Add(node)) throw new InvalidDataException("循环继承");
        XElement result = new(node);
        string parent = (string)node.Attribute("ParentName");
        if (parent != null && named.TryGetValue(node.Name + ":" + parent, out XElement parentNode))
        { result = Resolve(parentNode, chain); Merge(result, node); }
        chain.Remove(node);
        resolved[key] = new XElement(result);
        return result;
    }
    private static void Merge(XElement target, XElement source)
    {
        foreach (XAttribute a in source.Attributes()) target.SetAttributeValue(a.Name, a.Value);
        foreach (XElement value in source.Elements())
        {
            XElement old = target.Element(value.Name);
            if (old == null) target.Add(new XElement(value));
            else if (!value.HasElements || string.Equals((string)value.Attribute("Inherit"), "false", StringComparison.OrdinalIgnoreCase)) old.ReplaceWith(new XElement(value));
            else if (value.Elements("li").Any()) { foreach (XElement li in value.Elements()) old.Add(new XElement(li)); }
            else Merge(old, value);
        }
    }
    private void ReadTranslations(string folder)
    {
        string loose = Path.Combine(folder, "Languages", "ChineseSimplified", "DefInjected");
        if (Directory.Exists(loose)) foreach (string f in Directory.EnumerateFiles(loose, "*.xml", SearchOption.AllDirectories))
        {
            string type = new DirectoryInfo(Path.GetDirectoryName(f)!).Name;
            if (type is not ("ThingDef" or "PawnKindDef" or "TerrainDef")) continue;
            try { ReadLabels(XDocument.Load(f), type); } catch { }
        }
        string archive = Path.Combine(folder, "Languages", "ChineseSimplified (简体中文).tar");
        if (!File.Exists(archive)) return;
        using FileStream stream = File.OpenRead(archive);
        using TarReader tar = new(stream);
        TarEntry entry;
        while ((entry = tar.GetNextEntry()) != null)
        {
            if (entry.DataStream == null || !entry.Name.Contains("DefInjected/") || !entry.Name.EndsWith(".xml")) continue;
            string type = entry.Name.Split('/')[^2];
            if (type is not ("ThingDef" or "PawnKindDef" or "TerrainDef")) continue;
            try { ReadLabels(XDocument.Load(entry.DataStream), type); } catch { }
        }
    }
    private void ReadLabels(XDocument doc, string type)
    {
        foreach (XElement e in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
            if (e.Name.LocalName.EndsWith(".label")) labels[type + ":" + e.Name.LocalName[..^6]] = e.Value;
    }
    private string Label(string type, string id, XElement def) => labels.GetValueOrDefault(type + ":" + id) ?? (string)def.Element("label") ?? id;
    private void AddBuilding(XElement def, string key)
    {
        if ((bool?)def.Element("destroyable") == false && def.Element("graphicData") == null) return;
        CatalogItem item = Make(def, "Building", key, def.Element("graphicData"));
        float[] size = Numbers((string)def.Element("size"));
        if (size.Length >= 2) { item.Width = Math.Max(1, (int)size[0]); item.Height = Math.Max(1, (int)size[1]); }
        item.Rotatable = (bool?)def.Element("rotatable") ?? true;
        item.CanBeDormant = SupportsDormancy(def);
        item.MaterialCategories = def.Element("stuffCategories")?.Elements("li").Select(e => e.Value).ToList() ?? new();
        string gun = (string)def.Element("building")?.Element("turretGunDef");
        item.Group = CatalogGrouping.Building(def);
        item.TurretExplosionRadius = ReadTurretExplosionRadius(def);
        item.ProximityTriggerRadius = ReadCompRadius(def, "CompProperties_SendSignalOnMotion", "radius");
        item.DormantWakeRadius = ReadCompRadius(def, "CompProperties_WakeUpDormant", "wakeUpCheckRadius", "wakeUpIfAnyTargetClose");
        MechClusterCatalog.Assign(item, def);
        if (gun != null && raw.TryGetValue("ThingDef:" + gun, out XElement gunNode))
        {
            XElement gd = Resolve(gunNode, new HashSet<XElement>()).Element("graphicData");
            item.TopTexture = (string)gd?.Element("texPath");
            item.TopSize = (float?)def.Element("building")?.Element("turretTopDrawSize") ?? 1;
        }
        Items.Add(item);
    }
    private static float? ReadCompRadius(XElement def, string compClass, string radiusField, string enabledField = null)
    {
        XElement comp = def.Element("comps")?.Elements("li").FirstOrDefault(c =>
            (((string)c.Attribute("Class")) ?? "").Split('.').Last() == compClass);
        if (enabledField != null && !string.Equals((string)comp?.Element(enabledField), "true", StringComparison.OrdinalIgnoreCase)) return null;
        return float.TryParse((string)comp?.Element(radiusField), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float radius) && float.IsFinite(radius) && radius > 0 ? radius : null;
    }
    internal static float? ReadTurretExplosionRadius(XElement def)
    {
        if (CatalogGrouping.Building(def) != "炮塔") return null;
        var explosive = def.Element("comps")?.Elements("li").FirstOrDefault(c =>
            ((string)c.Attribute("Class") ?? "").Split('.').Last() == "CompProperties_Explosive");
        return float.TryParse((string)explosive?.Element("explosiveRadius"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float radius) && float.IsFinite(radius) && radius >= 0 ? radius : null;
    }
    private void AddTerrain(XElement def, string key)
    {
        var item = Make(def, "Terrain", key, null); item.Group = CatalogGrouping.Terrain(def); Items.Add(item);
    }
    private void AddPawn(XElement def, string key)
    {
        string race = (string)def.Element("race");
        if (race == null || !raw.TryGetValue("ThingDef:" + race, out XElement raceNode)) return;
        XElement resolvedRace = Resolve(raceNode, new HashSet<XElement>());

        XElement graphic = def.Element("lifeStages")?.Elements("li").Select(l => l.Element("bodyGraphicData")).FirstOrDefault(g => g?.Element("texPath") != null);
        var item = Make(def, "Pawn", key, graphic); item.CanBeDormant = SupportsDormancy(resolvedRace); item.Group = CatalogGrouping.Pawn(def, resolvedRace); Items.Add(item);
    }
    private static bool SupportsDormancy(XElement def) => def.Element("comps")?.Elements("li")
        .Any(c => ((string)c.Attribute("Class") ?? "").Split('.').Last() == "CompProperties_CanBeDormant") == true;
    private CatalogItem Make(XElement def, string kind, string key, XElement graphic)
    {
        string id = (string)def.Element("defName");
        float[] size = Numbers((string)graphic?.Element("drawSize"));
        float[] offset = Numbers((string)graphic?.Element("drawOffset"));
        return new CatalogItem
        {
            Kind = kind, DefName = id, Label = Label(def.Name.LocalName, id, def), Pack = origins.GetValueOrDefault(key),
            PackageId = sourceOrigins.GetValueOrDefault(key)?.PackageId, IsMod = sourceOrigins.GetValueOrDefault(key)?.Official == false,
            Texture = (string)graphic?.Element("texPath") ?? (string)def.Element("texturePath"),
            Icon = (string)def.Element("uiIconPath"), GraphicClass = (string)graphic?.Element("graphicClass") ?? "Graphic_Single",
            DrawX = size.Length > 0 ? size[0] : 1, DrawZ = size.Length > 1 ? size[1] : size.Length > 0 ? size[0] : 1,
            OffsetX = offset.Length > 0 ? offset[0] : 0, OffsetZ = offset.Length > 2 ? offset[2] : 0,
            Linked = graphic?.Element("linkType") != null && (string)graphic.Element("linkType") != "None",
            LinkType = (string)graphic?.Element("linkType"),
            LinkFlags = graphic?.Element("linkFlags")?.Elements("li").Select(e => e.Value).ToList() ?? new(),
            Color = ParseColor((string)graphic?.Element("color") ?? (string)def.Element("color"))
        };
    }
    public static float[] Numbers(string s) => Regex.Matches(s ?? "", @"-?\d+(?:\.\d+)?").Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
    public static Color ParseColor(string s)
    {
        float[] c = Numbers(s);
        if (c.Length < 3) return Color.White;
        float scale = c.Take(3).Max() <= 1 ? 255 : 1;
        return Color.FromArgb(255, Math.Clamp((int)(c[0] * scale), 0, 255), Math.Clamp((int)(c[1] * scale), 0, 255), Math.Clamp((int)(c[2] * scale), 0, 255));
    }
    public void Dispose() => Textures?.Dispose();
}
