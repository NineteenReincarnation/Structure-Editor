using System.Text.Json;
using System.Xml.Linq;

namespace StructureEditor;

internal static class RuntimeCatalog
{
    internal sealed class Snapshot
    {
        public int SchemaVersion { get; set; }
        public DateTime ExportedAtUtc { get; set; }
        public List<string> ActivePackages { get; set; } = new();
        public List<Item> Items { get; set; } = new();
        public List<Material> Materials { get; set; } = new();
    }
    internal sealed class Origin { public string PackageId {get;set;} public string Name {get;set;} public string Root {get;set;} }
    internal class Item : CatalogItem
    {
        public Origin Source {get;set;}
        public string DesignationCategory {get;set;}
        public string ThingClass {get;set;}
        public List<string> CompClasses {get;set;}=new();
        public int ColorArgb {get;set;}=unchecked((int)0xffffffff);
    }
    internal sealed class Material
    {
        public string Appearance {get;set;}
        public string DefName {get;set;} public string Label {get;set;} public Origin Source {get;set;}
        public List<string> Categories {get;set;}=new(); public int ColorArgb {get;set;}=unchecked((int)0xffffffff);
    }
    public static bool Apply(Catalog catalog,StudioPaths paths)
    {
        if(!paths.AutoLoadActiveMods||!paths.UseRuntimeCatalog)return false;
        string config=string.IsNullOrEmpty(paths.ModsConfigPath)?ModSources.DefaultConfig:paths.ModsConfigPath;
        string file=Path.Combine(Path.GetDirectoryName(config)!,"StructureFramework","catalog.json");
        if(!File.Exists(file))return false;
        try
        {
            var snapshot=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file));
            if(snapshot?.SchemaVersion!=1)throw new InvalidDataException("不支持的游戏同步目录版本。");
            string Normalize(string id)=>id.EndsWith("_steam",StringComparison.OrdinalIgnoreCase)?id[..^6].ToLowerInvariant():id.ToLowerInvariant();
            var expected=ModSources.ActivePackages(paths).Select(Normalize).ToArray();
            if(!expected.SequenceEqual(snapshot.ActivePackages.Select(Normalize))) {catalog.Warnings.Add("游戏同步目录的模组列表或加载顺序已变化，使用 XML 素材；请在游戏中重新导出目录。");return false;}
            var selected=catalog.Sources.ToDictionary(s=>Normalize(s.PackageId),s=>s,StringComparer.OrdinalIgnoreCase);
            // Stage replacements by position and publish only after the whole snapshot validates.
            var items = new List<CatalogItem>(catalog.Items); var materials = new List<MaterialItem>(catalog.Materials);
            var itemPositions = new Dictionary<(string,string),int>(); var materialPositions = new Dictionary<string,int>();
            for(int i=0;i<items.Count;i++)itemPositions[(items[i].Kind,items[i].DefName)]=i;
            for(int i=0;i<materials.Count;i++)materialPositions[materials[i].DefName]=i;
            var seenItems = new HashSet<(string,string)>(); var seenMaterials = new HashSet<string>();
            foreach(var item in snapshot.Items)
            {
                if(item.Source?.PackageId==null||!selected.TryGetValue(Normalize(item.Source.PackageId),out var source))continue;
                if(string.IsNullOrWhiteSpace(item.DefName)||item.Kind is not ("Building" or "Pawn" or "Terrain")||item.Width<1||item.Height<1||!seenItems.Add((item.Kind,item.DefName)))throw new InvalidDataException("同步目录包含无效或重复素材。");
                bool replace=itemPositions.TryGetValue((item.Kind,item.DefName),out int position);var previous=replace?items[position]:null;
                item.LinkType ??= previous?.LinkType;
                item.TurretExplosionRadius ??= previous?.TurretExplosionRadius;
                item.Label??=item.DefName;item.CompClasses??=new();item.MaterialCategories??=new();item.LinkFlags??=new();
                item.PackageId=source.PackageId;item.Pack=source.Display;item.IsMod=!source.Official;item.Color=Color.FromArgb(item.ColorArgb);
                if(item.Kind=="Building")
                {
                    var xml=new XElement("ThingDef",new XElement("defName",item.DefName),new XElement("designationCategory",item.DesignationCategory??""),new XElement("thingClass",item.ThingClass??""),new XElement("comps",item.CompClasses.Select(c=>new XElement("li",new XAttribute("Class",c)))));
                    item.Group=CatalogGrouping.Building(xml);MechClusterCatalog.Assign(item,xml);
                }
                else item.Group=previous?.Group??(item.Kind=="Terrain"?"其他地形":"动物");
                if(previous!=null){item.ClusterGroup??=previous.ClusterGroup;item.ClusterWikiName??=previous.ClusterWikiName;item.ClusterWikiEntry|=previous.ClusterWikiEntry;items[position]=item;}
                else {itemPositions[(item.Kind,item.DefName)]=items.Count;items.Add(item);}
            }
            foreach(var material in snapshot.Materials)
            {
                if(material.Source?.PackageId==null||!selected.TryGetValue(Normalize(material.Source.PackageId),out var source))continue;
                if(string.IsNullOrWhiteSpace(material.DefName)||!seenMaterials.Add(material.DefName))throw new InvalidDataException("同步目录包含无效或重复材质。");
                var replacement=new MaterialItem{DefName=material.DefName,Label=material.Label??material.DefName,Categories=material.Categories??new(),Color=Color.FromArgb(material.ColorArgb),PackageId=source.PackageId,
                    Appearance=material.Appearance??(materialPositions.TryGetValue(material.DefName,out int oldPosition)?materials[oldPosition].Appearance:"Smooth")};
                if(materialPositions.TryGetValue(material.DefName,out int position))materials[position]=replacement;
                else {materialPositions[material.DefName]=materials.Count;materials.Add(replacement);}
            }
            catalog.Items.Clear();catalog.Items.AddRange(items);catalog.Materials.Clear();catalog.Materials.AddRange(materials);
            return true;
        }
        catch(Exception ex){catalog.Warnings.Add("游戏目录同步失败，继续读取 XML："+ex.Message);return false;}
    }
}
