using System.Xml.Linq;

namespace StructureEditor;

internal static class PrefabCodec
{
    private static readonly string[] rotations={"None","Clockwise","Opposite","Counterclockwise"};
    public static XElement Export(StructureDocument document,Catalog catalog)
    {
        _=document.ToXml();
        if(document.Items.Any(i=>i.Kind=="Pawn"))throw new InvalidOperationException("原版 PrefabDef 不支持 PawnKind 编组；请使用框架或 MP 兼容导出，未删除任何单位。");
        if(document.Items.Any(i=>i.FrameworkModifiers?.Count>0))throw new InvalidOperationException("原版 PrefabDef 无法保存框架对象扩展，请使用框架导出。");
        if(document.Items.Count==0)throw new InvalidOperationException("结构为空。");
        Rectangle bounds=StructureDocument.Footprint(document.Items[0],catalog);
        foreach(var item in document.Items.Skip(1))bounds=Rectangle.Union(bounds,StructureDocument.Footprint(item,catalog));
        if(document.TemplateBounds.HasValue)bounds=Rectangle.Union(bounds,document.TemplateBounds.Value);
        var root=new XElement("PrefabDef",new XElement("defName",document.DefName),new XElement("label",document.Label),new XElement("description",document.Description??""),new XElement("rotations",document.PrefabRotations),new XElement("size",$"({bounds.Width},{bounds.Height})"));
        root.Add(new XComment($"Structure Editor origin: ({-bounds.X},{-bounds.Y})"));
        if(document.PrefabEdgeOnly)root.Add(new XElement("edgeOnly",true));
        var things=new XElement("things");
        foreach(var group in document.Items.Where(i=>i.Kind=="Building").GroupBy(i=>(i.DefName,i.Stuff,i.Rotation)))
        {
            var entry=new XElement(group.Key.DefName,new XElement("positions",group.Select(i=>new XElement("li",$"({i.X-bounds.X},0,{i.Z-bounds.Y})"))));
            if(!string.IsNullOrEmpty(group.Key.Stuff))entry.Add(new XElement("stuff",group.Key.Stuff));
            if(group.Key.Rotation!=0)entry.Add(new XElement("relativeRotation",rotations[group.Key.Rotation]));things.Add(entry);
        }
        if(things.HasElements)root.Add(things);
        var terrain=new XElement("terrain");
        foreach(var group in document.Items.Where(i=>i.Kind=="Terrain").GroupBy(i=>i.DefName))
            terrain.Add(new XElement(group.Key,new XElement("rects",group.Select(i=>new XElement("li",$"({i.X-bounds.X},{i.Z-bounds.Y},{i.X-bounds.X},{i.Z-bounds.Y})")))));
        if(terrain.HasElements)root.Add(terrain);return root;
    }
    public static StructureDocument Import(XElement root)
    {
        if(root.Attribute("ParentName")!=null)throw new InvalidDataException("此 Prefab 依赖 XML 继承，请先展开继承后再导入。");
        foreach(var field in root.Elements())if(!new[]{"defName","label","description","size","rotations","things","terrain","edgeOnly"}.Contains(field.Name.LocalName))throw new InvalidDataException("暂不支持原版 Prefab 字段："+field.Name.LocalName+"；未导入以避免丢失数据。");
        var size=Catalog.Numbers((string)root.Element("size"));if(size.Length!=2||size.Any(v=>v<1))throw new InvalidDataException("Prefab 缺少有效的 size。");
        var doc=new StructureDocument{DefName=(string)root.Element("defName"),Label=(string)root.Element("label")??(string)root.Element("defName"),Description=(string)root.Element("description")??"",ExportProfile="VanillaPrefab",PrefabRotations=(string)root.Element("rotations")??"North,East,South,West",PrefabEdgeOnly=(bool?)root.Element("edgeOnly")??false};
        int originX=0,originZ=0;
        var origin=root.Nodes().OfType<XComment>().FirstOrDefault(c=>c.Value.Contains("Structure Editor origin:"));
        if(origin!=null){var p=Catalog.Numbers(origin.Value);if(p.Length==2){originX=(int)p[0];originZ=(int)p[1];}}
        doc.TemplateBounds=new Rectangle(-originX,-originZ,(int)size[0],(int)size[1]);
        foreach(var(kind,list)in new[]{("Building","things"),("Terrain","terrain")})foreach(var entry in root.Element(list)?.Elements()??Enumerable.Empty<XElement>())
        {
            foreach(var field in entry.Elements())if(!new[]{"position","positions","rects","relativeRotation","stuff","chance"}.Contains(field.Name.LocalName))throw new InvalidDataException("Prefab 对象包含暂不支持的属性："+field.Name.LocalName);
            if((float?)entry.Element("chance") is float chance&&chance!=1)throw new InvalidDataException("此 Prefab 使用概率生成，当前编辑器只编辑确定布局，未导入。");
            int rotation=Array.IndexOf(rotations,(string)entry.Element("relativeRotation")??"None");if(rotation<0)throw new InvalidDataException("不支持的 relativeRotation。");
            List<Point> positions=new();
            if(entry.Element("position")!=null)positions.Add(FrameworkCodec.Position(entry.Element("position")));
            foreach(var position in entry.Element("positions")?.Elements("li")??Enumerable.Empty<XElement>())positions.Add(FrameworkCodec.Position(position));
            foreach(var rectangle in entry.Element("rects")?.Elements("li")??Enumerable.Empty<XElement>())
            {
                var p=Catalog.Numbers(rectangle.Value);if(p.Length!=4||p[2]<p[0]||p[3]<p[1]||(p[2]-p[0]+1)*(p[3]-p[1]+1)>65536)throw new InvalidDataException("无效或过大的 Prefab 矩形。");
                for(int z=(int)p[1];z<=(int)p[3];z++)for(int x=(int)p[0];x<=(int)p[2];x++)positions.Add(new(x,z));
            }
            foreach(var position in positions)doc.Items.Add(new(){Kind=kind,DefName=entry.Name.LocalName,Stuff=(string)entry.Element("stuff"),X=position.X-originX,Z=position.Y-originZ,Rotation=rotation});
        }
        return doc;
    }
}
