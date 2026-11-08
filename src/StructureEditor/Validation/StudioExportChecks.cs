using System.Reflection;
using System.Xml.Linq;

namespace StructureEditor;

internal static class StudioExportChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        string root=Path.Combine(folder,"SaveExportChecks",Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var originalPaths=Program.Paths;
        try
        {
            Program.Paths=new(){GameRoot=originalPaths.GameRoot,SaveDirectory=root};
            string project=Path.Combine(root,"MP_SaveExport.structure.json");
            StructureDocument doc=new()
            {
                DefName="MP_SaveExport",Label="统一导出验证",
                Items=new(){new(){Kind="Building",DefName="Wall",Stuff="Steel"}}
            };
            doc.Save(project);
            string xmlFile=Path.Combine(root,doc.DefName+".xml");
            new XDocument(new XElement("Defs",
                new XElement("PrefabDef",new XElement("defName",doc.DefName)),
                new XElement("MechProtocol.MP_StructureDef",new XElement("defName","MP_KeepOther")))).Save(xmlFile);
            using MainForm form=new();
            form.BindForChecks(catalog,doc);
            object Field(string name)=>typeof(MainForm).GetField(
                name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            typeof(MainForm).GetField("currentFile",BindingFlags.Instance|BindingFlags.NonPublic)!
                .SetValue(form,project);
            bool Save()=>(bool)typeof(MainForm).GetMethod("SaveDocument",
                BindingFlags.Instance|BindingFlags.NonPublic)!
                .Invoke(form,new object[]{false})!;
            IEnumerable<Control> Children(Control rootControl)
            {
                foreach(Control child in rootControl.Controls)
                {
                    yield return child;
                    foreach(Control descendant in Children(child))yield return descendant;
                }
            }
            var tabs=Children(form).OfType<MP_SectionTabs>().Single(t=>
                Children(t).OfType<TabPage>().Any(p=>p.Text=="工程"));
            var projectPage=Children(tabs).OfType<TabPage>().Single(p=>p.Text=="工程");
            need(tabs.SelectPage(projectPage),"Unified project tab remains available");
            need(Children(projectPage).OfType<Label>().Any(l=>l.Text.Contains("Structure Framework"))
                && !Children(projectPage).OfType<ComboBox>().Any(c=>c.Items.Cast<object>()
                    .Any(item=>(item?.ToString()??"").Contains("PrefabDef"))),
                "Project panel exposes one standard and no format switcher");
            var auto=(CheckBox)Field("autoExport");
            need(auto.Checked&&Save()&&File.Exists(xmlFile),
                "New projects save JSON and canonical XML together");
            var written=XDocument.Load(xmlFile);
            need(written.Root!.Elements().Count()==2
                && written.Root.Element("StructureFramework.StructureDef")!=null
                && written.Root.Element("PrefabDef")==null
                && written.Root.Elements().Any(e=>(string)e.Element("defName")=="MP_KeepOther"),
                "Legacy XML is replaced in-place without touching unrelated definitions");
            need(StructureDocument.Load(project).ExportProfile=="Framework",
                "Saved project uses Framework");
            string before=File.ReadAllText(xmlFile);
            auto.Checked=false;
            ((TextBox)Field("labelBox")).Text="只保存 JSON";
            Save();
            need(File.ReadAllText(xmlFile)==before
                && !StructureDocument.Load(project).AutoExportOnSave,
                "Disabling auto export preserves last valid XML");
            auto.Checked=true;Save();
            need(XDocument.Load(xmlFile).Root!.Element("StructureFramework.StructureDef")!
                .Element("label")!.Value=="只保存 JSON",
                "Re-enabling auto export updates the canonical definition");
            var custom=doc.Copy();
            custom.XmlExportDirectory=Path.Combine(root,"XmlOutput");
            var res=StructureStorage.SaveProject(custom,catalog,Path.Combine(root,"Custom.structure.json"));
            need(res.ExportError==null && File.Exists(res.XmlPath)
                && Path.GetDirectoryName(res.XmlPath)==custom.XmlExportDirectory,
                "Independent XML destination works with the single standard");
            var legacy=doc.Copy();
            legacy.ExportProfile="MechProtocol";
            legacy.Items.Add(new(){Kind="Pawn",DefName="Mech_Scyther",X=4});
            foreach(string profile in new[]{"VanillaPrefab","MechProtocol","Universal","Framework"})
            {
                var output=ExportDependencies.Xml(legacy,catalog,profile);
                need(output.Root!.Elements().Count()==1
                    && output.Root.Element("StructureFramework.StructureDef")!.Element("pawns")!=null,
                    "Historical profile "+profile+" exports a single full Framework structure");
            }
            var missing=doc.Copy();missing.RequiredPackages.Add("test.package.not.loaded");
            var review=MP_ExportReview.Inspect(missing,catalog,"VanillaPrefab");
            need(review.CanExport&&review.Warnings>0,
                "Missing optional local assets warn without deleting project content");
            string oldProject=Path.Combine(root,"Old.structure.json");
            File.WriteAllText(oldProject,"{\"FormatVersion\":1,\"DefName\":\"MP_Old\",\"ExportProfile\":\"Universal\",\"Items\":[]}");
            need(StructureDocument.Load(oldProject).ExportProfile=="Framework",
                "Old saved export profiles normalize to Framework on load");
            string pref=Path.Combine(root,"Old.xml");
            new XDocument(new XElement("Defs",PrefabCodec.Export(doc,catalog))).Save(pref);
            need(StructureDocument.Load(pref,catalog).ExportProfile=="Framework",
                "Historical Prefab XML can still be imported for migration");
            form.Size=new(1450,900);
            StudioInteractionChecks.RenderWorkspace(form,Path.Combine(folder,"export-page.png"));
            form.Size=new(1060,720);
            StudioInteractionChecks.RenderWorkspace(form,Path.Combine(folder,"export-page-compact.png"));
        }
        finally {Program.Paths=originalPaths;}
    }
}
