using System.Text.Json;
using System.Xml.Linq;

namespace StructureEditor;

internal static class ExportDependencies
{
    public static List<CatalogSource> Resolve(StructureDocument document,Catalog catalog,string profile)
    {
        var requirements=new Dictionary<string,CatalogSource>(StringComparer.OrdinalIgnoreCase);
        void Add(string package)
        {
            if(string.IsNullOrEmpty(package)||package.Equals("ludeon.rimworld",StringComparison.OrdinalIgnoreCase)||requirements.ContainsKey(package))return;
            var source=catalog.Sources.FirstOrDefault(s=>s.PackageId.Equals(package,StringComparison.OrdinalIgnoreCase))??new CatalogSource{PackageId=package,Name=package};
            requirements.Add(package,source);
            foreach(string dependency in source.Dependencies)Add(dependency);
        }
        if(profile is "Framework" or "Universal")Add(FrameworkCodec.PackageId);
        if(profile=="MechProtocol"||((profile=="Framework"||profile=="Universal")&&document.EnableMPCompatibility))Add("Anno.MechProtocol");
        foreach(string package in document.RequiredPackages??new())Add(package);
        foreach(var item in document.Items)
        {
            var entry=catalog.Find(item.Kind,item.DefName);
            // An uninstalled source is a warning in export review, not a
            // structural XML error. Preserve the reference for the target game.
            if (entry != null) Add(entry.PackageId);
            if(!string.IsNullOrEmpty(item.Stuff))Add(catalog.Materials.FirstOrDefault(m=>m.DefName==item.Stuff)?.PackageId);
        }
        return requirements.Values.OrderBy(s=>s.PackageId).ToList();
    }
    public static XDocument Xml(StructureDocument document,Catalog catalog,string profile)
    {
        if (catalog == null) throw new InvalidOperationException("素材尚未加载。");
        var required = Resolve(document, catalog, profile);
        XElement root;
        if (profile == "Universal")
        {
            return MP_UniversalXml(document, catalog, required);
        }
        root = profile switch
        {
            "VanillaPrefab" => PrefabCodec.Export(document,catalog),
            "MechProtocol" => Legacy(document),
            "Framework" => FrameworkCodec.Export(document,catalog),
            _ => throw new InvalidOperationException("未知的 XML 导出格式：" + profile)
        };
        // When a native prefab refers to modded defs, skip the *whole* prefab
        // if those mods are absent. Bare references could produce game load
        // errors before any map generation ever uses the prefab.
        if (profile == "VanillaPrefab" && required.Count > 0)
            root.SetAttributeValue("MayRequire", string.Join(",",required.Select(x=>x.PackageId)));
        return new XDocument(new XDeclaration("1.0","utf-8",null),
            new XComment("Requires: " + string.Join(", ",required.Select(x=>x.PackageId))),
            new XElement("Defs",root));
    }

    internal static string MP_EnhancedName(string defName) =>
        defName.StartsWith("MP_", StringComparison.Ordinal) ? defName + "_Extended" : "MP_" + defName + "_Extended";

    private static XDocument MP_UniversalXml(StructureDocument document, Catalog catalog, List<CatalogSource> required)
    {
        // Native, core-only building/terrain projection. It is deliberately a
        // separate view; the original JSON and the richer extension are intact.
        StructureDocument native = document.Copy();
        native.Items = native.Items.Where(i => (i.Kind == "Building" || i.Kind == "Terrain")
            && MP_ExportReview.MP_IsCore(catalog.Find(i.Kind, i.DefName))).ToList();
        native.StartDormant = false;
        native.EnableMPCompatibility = false;
        native.FrameworkPlacement = null;
        native.FrameworkTags.Clear();
        foreach (PlacedItem item in native.Items)
        {
            item.StartDormant = null;
            item.FrameworkModifiers.Clear();
        }
        XElement defs = new("Defs");
        if (native.Items.Count > 0)
            defs.Add(PrefabCodec.Export(native, catalog));
        // LoadedModManager checks MayRequire before resolving unknown Def
        // classes. Include every necessary package so an incomplete loadout
        // safely skips the entire enhanced definition.
        XElement extended = FrameworkCodec.Export(document, catalog);
        extended.SetAttributeValue("MayRequire", string.Join(",",
            required.Select(s=>s.PackageId).Append(FrameworkCodec.PackageId)
                .Where(s=>!string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)));
        extended.Element("defName")!.Value = MP_EnhancedName(document.DefName);
        defs.Add(extended);
        return new XDocument(new XDeclaration("1.0","utf-8",null),
            new XComment("Vanilla PrefabDef plus optional conditional framework extension; missing mods skip only the enhanced definition."),
            defs);
    }

    private static XElement Legacy(StructureDocument document)
    {
        var copy=document.Copy();if(!copy.DefName.StartsWith("MP_"))copy.DefName="MP_"+copy.DefName;return copy.ToXml(true);
    }
    public static string Bundle(StructureDocument document,Catalog catalog,string parent,string profile)
    {
        var xml=Xml(document,catalog,profile);var dependencies=Resolve(document,catalog,profile);
        string root=Path.Combine(Path.GetFullPath(parent),document.DefName);
        string marker=Path.Combine(root,"structure-editor-package.json");
        if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()&&!File.Exists(marker))throw new InvalidOperationException("目标目录已包含其他文件，请选择其他位置："+root);
        Directory.CreateDirectory(Path.Combine(root,"About"));Directory.CreateDirectory(Path.Combine(root,"1.7","Defs","Structures"));
        var about=new XElement("ModMetaData",new XElement("name",document.Label),new XElement("author","Structure Editor"),new XElement("packageId","structureeditor."+document.DefName.ToLowerInvariant()),new XElement("supportedVersions",new XElement("li","1.7")),new XElement("description","Layout content exported by Structure Editor. Requires the listed content packages; does not include their assets."));
        if(dependencies.Count>0)
        {
            // In Universal mode all extra packages are optional: mandatory
            // About.xml dependencies would defeat vanilla fallback.
            if (profile != "Universal")
                about.Add(new XElement("modDependencies",dependencies.Select(s=>new XElement("li",new XElement("packageId",s.PackageId),new XElement("displayName",s.Name)))));
            about.Add(new XElement("loadAfter",dependencies.Select(s=>new XElement("li",s.PackageId))));
        }
        new XDocument(new XDeclaration("1.0","utf-8",null),about).Save(Path.Combine(root,"About","About.xml"));
        xml.Save(Path.Combine(root,"1.7","Defs","Structures",document.DefName+".xml"));
        File.WriteAllText(marker,JsonSerializer.Serialize(new{Generator="Structure Editor",Definition=document.DefName,Profile=profile,RequiredPackages=dependencies.Select(s=>s.PackageId).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"使用说明.txt"),"将此文件夹放进 RimWorld/Mods 并启用。\n"+(profile=="Universal"?"可选拓展：":"依赖：")+string.Join(", ",dependencies.Select(s=>s.Name))+"\n"+(profile=="Framework"?"开发者菜单 Structure Framework → Spawn structure，选择结构后在地图点击部署。":"原版 Prefab 开发者生成入口可以部署；自动生成仍需生成器或事件引用。")+"\n本内容包不会复制其他模组的 DLL 或贴图。\n");return root;
    }
}
