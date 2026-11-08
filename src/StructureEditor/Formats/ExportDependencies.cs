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
        if(profile=="Framework")Add(FrameworkCodec.PackageId);
        if(profile=="MechProtocol"||(profile=="Framework"&&document.EnableMPCompatibility))Add("Anno.MechProtocol");
        foreach(string package in document.RequiredPackages??new())Add(package);
        foreach(var item in document.Items)
        {
            var entry=catalog.Find(item.Kind,item.DefName);
            if(entry==null)throw new InvalidOperationException("缺少素材："+item.DefName);
            if(entry.IsMod&&string.IsNullOrEmpty(entry.PackageId))throw new InvalidOperationException("模组素材缺少 packageId，无法声明依赖："+entry.Pack);
            Add(entry.PackageId);
            if(!string.IsNullOrEmpty(item.Stuff))Add(catalog.Materials.FirstOrDefault(m=>m.DefName==item.Stuff)?.PackageId);
        }
        return requirements.Values.OrderBy(s=>s.PackageId).ToList();
    }
    public static XDocument Xml(StructureDocument document,Catalog catalog,string profile)
    {
        XElement root=profile switch
        {
            "VanillaPrefab"=>PrefabCodec.Export(document,catalog),
            "MechProtocol"=>Legacy(document),
            "Framework"=>FrameworkCodec.Export(document,catalog),
            _=>throw new InvalidOperationException("未知的 XML 导出格式：" + profile)
        };
        var required=Resolve(document,catalog,profile);
        return new XDocument(new XDeclaration("1.0","utf-8",null),new XComment("Requires: "+string.Join(", ",required.Select(s=>s.PackageId))),new XElement("Defs",root));
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
            about.Add(new XElement("modDependencies",dependencies.Select(s=>new XElement("li",new XElement("packageId",s.PackageId),new XElement("displayName",s.Name)))));
            about.Add(new XElement("loadAfter",dependencies.Select(s=>new XElement("li",s.PackageId))));
        }
        new XDocument(new XDeclaration("1.0","utf-8",null),about).Save(Path.Combine(root,"About","About.xml"));
        xml.Save(Path.Combine(root,"1.7","Defs","Structures",document.DefName+".xml"));
        File.WriteAllText(marker,JsonSerializer.Serialize(new{Generator="Structure Editor",Definition=document.DefName,Profile=profile,RequiredPackages=dependencies.Select(s=>s.PackageId).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"使用说明.txt"),"将此文件夹放进 RimWorld/Mods 并启用。\n依赖："+string.Join(", ",dependencies.Select(s=>s.Name))+"\n"+(profile=="Framework"?"开发者菜单 Structure Framework → Spawn structure，选择结构后在地图点击部署。":"原版 Prefab 开发者生成入口可以部署；自动生成仍需生成器或事件引用。")+"\n本内容包不会复制其他模组的 DLL 或贴图。\n");return root;
    }
}
