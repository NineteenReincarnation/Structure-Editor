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
        Add(FrameworkCodec.PackageId);
        if(FrameworkCodec.NeedsMpDormancy(document,catalog))Add("Anno.MechProtocol");
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
    // Historical profile arguments cannot select a different output format.
    public static XDocument Xml(StructureDocument document,Catalog catalog,string profile = null)
    {
        if(catalog==null)throw new InvalidOperationException("素材尚未加载。");
        var dependencies=Resolve(document,catalog,"Framework");
        return new XDocument(new XDeclaration("1.0","utf-8",null),
            new XComment("Structure Framework v1; requires: "+string.Join(", ",dependencies.Select(s=>s.PackageId))),
            new XElement("Defs",FrameworkCodec.Export(document,catalog)));
    }
    // Historical Universal companion name used only for safe cleanup.
    internal static string MP_EnhancedName(string defName) =>
        defName.StartsWith("MP_",StringComparison.Ordinal) ? defName+"_Extended" : "MP_"+defName+"_Extended";

    public static string Bundle(StructureDocument document,Catalog catalog,string parent,string profile)
    {
        profile="Framework";
        var xml=Xml(document,catalog,profile);var dependencies=Resolve(document,catalog,profile);
        string root=Path.Combine(Path.GetFullPath(parent),document.DefName);
        string marker=Path.Combine(root,"structure-editor-package.json");
        if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()&&!File.Exists(marker))throw new InvalidOperationException("目标目录已包含其他文件，请选择其他位置："+root);
        Directory.CreateDirectory(Path.Combine(root,"About"));Directory.CreateDirectory(Path.Combine(root,"1.7","Defs","Structures"));
        var about=new XElement("ModMetaData",new XElement("name",document.Label),new XElement("author","Structure Editor"),new XElement("packageId","structureeditor."+document.DefName.ToLowerInvariant()),new XElement("supportedVersions",new XElement("li","1.7")),new XElement("description","Layout content exported by Structure Editor. Requires the listed content packages; does not include their assets."));
        if(dependencies.Count>0)
        {
            about.Add(new XElement("modDependencies",dependencies.Select(s=>new XElement("li",new XElement("packageId",s.PackageId),new XElement("displayName",s.Name)))));
            about.Add(new XElement("loadAfter",dependencies.Select(s=>new XElement("li",s.PackageId))));
        }
        new XDocument(new XDeclaration("1.0","utf-8",null),about).Save(Path.Combine(root,"About","About.xml"));
        xml.Save(Path.Combine(root,"1.7","Defs","Structures",document.DefName+".xml"));
        File.WriteAllText(marker,JsonSerializer.Serialize(new{Generator="Structure Editor",Definition=document.DefName,Profile=profile,RequiredPackages=dependencies.Select(s=>s.PackageId).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"使用说明.txt"),"将此文件夹放进 RimWorld/Mods 并启用。\n"+"依赖："+string.Join(", ",dependencies.Select(s=>s.Name))+"\n"+"开发者菜单 Structure Framework → Spawn structure，选择结构后在地图点击部署。"+"\n本内容包不会复制其他模组的 DLL 或贴图。\n");return root;
    }
}
