using System.Xml.Linq;

namespace StructureEditor;

internal static class FrameworkCodec
{
    public const string PackageId = "nineteenreincarnation.structureframework";
    public static XElement Export(StructureDocument document, Catalog catalog)
    {
        _ = document.ToXml();
        var root = new XElement("StructureFramework.StructureDef", new XElement("defName",document.DefName),new XElement("label",document.Label),new XElement("description",document.Description??""),new XElement("schemaVersion",1),new XElement("category",document.Category));
        if(!string.IsNullOrEmpty(document.FrameworkPlacement))root.Add(new XElement("placement",document.FrameworkPlacement));
        if(document.FrameworkTags?.Count>0)root.Add(new XElement("tags",document.FrameworkTags.Select(t=>new XElement("li",t))));
        var requirements = ExportDependencies.Resolve(document,catalog,"Framework").Where(s=>!s.PackageId.Equals(PackageId,StringComparison.OrdinalIgnoreCase)).ToList();
        if(requirements.Count>0)root.Add(new XElement("requiredPackages",requirements.Select(s=>new XElement("li",s.PackageId))));
        foreach (var (kind,list,field) in new[]{("Building","things","thingDef"),("Pawn","pawns","pawnKind"),("Terrain","terrain","terrainDef")})
        {
            var entries = new XElement(list);
            foreach(var item in document.Items.Where(i=>i.Kind==kind))
            {
                var entry = new XElement("li",new XElement(field,item.DefName),new XElement("position",$"({item.X},0,{item.Z})"));
                if(kind!="Terrain")entry.Add(new XElement("rotation",item.Rotation));
                if(kind=="Building"&&!string.IsNullOrEmpty(item.Stuff))entry.Add(new XElement("stuffDef",item.Stuff));
                var modifiers = new XElement("modifiers");
                foreach(string xml in item.FrameworkModifiers ?? new())modifiers.Add(XElement.Parse(xml));
                if(document.EnableMPCompatibility && kind!="Terrain" && catalog.Find(kind,item.DefName)?.CanBeDormant==true)
                    modifiers.Add(new XElement("li",new XAttribute("Class","StructureFramework.Integrations.MPDormancyModifier"),new XElement("dormant",(item.StartDormant??document.StartDormant).ToString().ToLowerInvariant())));
                if(modifiers.HasElements)entry.Add(modifiers);entries.Add(entry);
            }
            if(entries.HasElements)root.Add(entries);
        }
        return root;
    }
    public static StructureDocument Import(XElement root,Catalog catalog=null,XElement fileRoot=null)
    {
        if((int?)root.Element("schemaVersion") is int version&&version!=1)throw new InvalidDataException("不支持的框架结构版本："+version);
        var document=new StructureDocument{DefName=(string)root.Element("defName"),Label=(string)root.Element("label")??(string)root.Element("defName"),Description=(string)root.Element("description")??"",Category=(string)root.Element("category")??"General",ExportProfile="Framework"};
        document.RequiredPackages=root.Element("requiredPackages")?.Elements("li").Select(e=>e.Value).ToList()??new();
        document.FrameworkPlacement=(string)root.Element("placement");document.FrameworkTags=root.Element("tags")?.Elements("li").Select(e=>e.Value).ToList()??new();
        var path = new HashSet<string>();
        XElement Find(string id,bool palette)=> (fileRoot?.Name.LocalName=="Defs" ? fileRoot.Elements(palette?"StructureFramework.StructurePaletteDef":"StructureFramework.StructureDef").FirstOrDefault(x=>(string)x.Element("defName")==id):null)
            ?? (palette?catalog?.PaletteDefinitions:catalog?.StructureDefinitions)?.GetValueOrDefault(id);
        Point Rotate(Point p,int rot)=> (rot%4) switch{1=>new(p.Y,-p.X),2=>new(-p.X,-p.Y),3=>new(-p.Y,p.X),_=>p};
        void Add(XElement entry,Point local,Point offset,int rotation)
        {
            CheckFields(entry,"id","symbol","thingDef","stuffDef","pawnKind","terrainDef","roofDef","position","rotation","stackCount","modifiers");
            if(entry.Element("id")!=null)throw new InvalidDataException("此对象含命名标识；当前编辑器不能保留标识，未导入以避免丢失数据。");
            int authoredRotation=(int?)entry.Element("rotation")??0;
            if(authoredRotation<0||authoredRotation>3)throw new InvalidDataException("旋转值必须为 0～3。");
            Point pos=Rotate(local,rotation);pos.Offset(offset);int rot=((int?)entry.Element("rotation")??0)+rotation;
            foreach(var(kind,field)in new[]{("Building","thingDef"),("Pawn","pawnKind"),("Terrain","terrainDef")})
            {
                string id=(string)entry.Element(field);if(string.IsNullOrEmpty(id))continue;
                if((int?)entry.Element("stackCount") is int stack&&stack!=1)throw new InvalidDataException("编辑器暂不支持堆叠物品数量，未导入该结构。");
                var item=new PlacedItem{Kind=kind,DefName=id,Stuff=(string)entry.Element("stuffDef"),X=pos.X,Z=pos.Y,Rotation=rot%4};
                foreach(var modifier in entry.Element("modifiers")?.Elements()??Enumerable.Empty<XElement>())
                {
                    if((string)modifier.Attribute("Class")=="StructureFramework.Integrations.MPDormancyModifier"){document.EnableMPCompatibility=true;item.StartDormant=(bool?)modifier.Element("dormant")??false;}
                    else item.FrameworkModifiers.Add(modifier.ToString(SaveOptions.DisableFormatting));
                }
                document.Items.Add(item);
            }
            if(entry.Element("roofDef")!=null)throw new InvalidDataException("此结构含屋顶层；当前编辑器未提供屋顶图层，未导入以避免丢失数据。");
        }
        void Expand(XElement def,Point offset,int rotation)
        {
            if(def.Attribute("ParentName")!=null)throw new InvalidDataException("此结构依赖 XML 继承，请先展开继承后再导入。");
            CheckFields(def,"defName","label","description","schemaVersion","category","tags","requiredPackages","placement","palette","centerLayout","layout","symbols","things","pawns","terrain","roofs","includes","markers","modExtensions");
            if((int?)def.Element("schemaVersion") is int schema&&schema!=1)throw new InvalidDataException("不支持的框架结构版本："+schema);
            string id=(string)def.Element("defName")??"";
            if(!path.Add(id))throw new InvalidDataException("循环嵌套结构："+id);
            if(def.Element("modExtensions")?.HasElements==true||def.Element("markers")?.HasElements==true)throw new InvalidDataException("此结构包含自定义扩展或命名锚点；当前编辑器不能完整编辑，未导入以避免丢失数据。");
            var symbols=new Dictionary<string,XElement>();string palette=(string)def.Element("palette");
            if(!string.IsNullOrEmpty(palette))foreach(var symbol in (Find(palette,true)??throw new InvalidDataException("缺少共享调色板："+palette)).Element("symbols")?.Elements()??Enumerable.Empty<XElement>())symbols[symbol.Name.LocalName=="li"?(string)symbol.Element("symbol"):symbol.Name.LocalName]=symbol;
            foreach(var symbol in def.Element("symbols")?.Elements()??Enumerable.Empty<XElement>())symbols[symbol.Name.LocalName=="li"?(string)symbol.Element("symbol"):symbol.Name.LocalName]=symbol;
            var rows=def.Element("layout")?.Elements("li").Select(x=>x.Value).ToList()??new();int w=rows.Count==0?0:rows.Max(r=>r.Length),h=rows.Count;
            bool center=(bool?)def.Element("centerLayout")??true;int x0=center?-(w-1)/2:0,z0=center?-(h-1)/2:0;
            for(int z=0;z<h;z++)for(int x=0;x<rows[z].Length;x++)
            {string key=rows[z][x].ToString();if(key=="."||key==" ")continue;if(!symbols.TryGetValue(key,out var symbol))throw new InvalidDataException("未定义布局符号："+key);Add(symbol,new(x0+x,z0+h-1-z),offset,rotation);}
            foreach(string list in new[]{"things","pawns","terrain","roofs"})foreach(var entry in def.Element(list)?.Elements("li")??Enumerable.Empty<XElement>())Add(entry,Position(entry.Element("position")),offset,rotation);
            foreach(var include in def.Element("includes")?.Elements("li")??Enumerable.Empty<XElement>())
            {
                CheckFields(include,"structure","position","rotation");int childRotation=(int?)include.Element("rotation")??0;
                if(childRotation<0||childRotation>3)throw new InvalidDataException("嵌套旋转值必须为 0～3。");
                string child=(string)include.Element("structure");var childDef=Find(child,false)??throw new InvalidDataException("缺少嵌套结构："+child);
                foreach(string package in childDef.Element("requiredPackages")?.Elements("li").Select(p=>p.Value)??Enumerable.Empty<string>())if(!document.RequiredPackages.Contains(package))document.RequiredPackages.Add(package);
                Point p=Rotate(Position(include.Element("position")),rotation);p.Offset(offset);Expand(childDef,p,(rotation+childRotation)%4);
            }
            path.Remove(id);
        }
        Expand(root,Point.Empty,0);return document;
    }
    internal static Point Position(XElement element)
    {
        var values=Catalog.Numbers((string)element);if(values.Length!=3||values[1]!=0||values.Any(v=>v!=Math.Truncate(v)))throw new InvalidDataException("位置应为整数 (x,0,z)。");return new((int)values[0],(int)values[2]);
    }
    private static void CheckFields(XElement element,params string[] allowed)
    {
        foreach(var field in element.Elements())if(!allowed.Contains(field.Name.LocalName))throw new InvalidDataException("编辑器暂不支持字段："+field.Name.LocalName+"；未导入以避免丢失数据。");
    }
}
