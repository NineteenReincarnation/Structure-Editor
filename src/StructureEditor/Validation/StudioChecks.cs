using System.Drawing.Imaging;
using System.Xml.Linq;

namespace StructureEditor;

internal static class StudioChecks
{
    public static void Run(StudioPaths paths, bool turretExplosionsOnly = false)
    {
        int checks=0;void Need(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
        string folder=Program.TestDirectory;Directory.CreateDirectory(folder);
        using Catalog catalog=new();catalog.Load(paths);
        if (turretExplosionsOnly)
        {
            TurretExplosionChecks.Run(catalog,folder,Need);
            File.WriteAllText(Path.Combine(folder,"turret-explosion-checks.txt"),$"PASS: {checks} turret explosion checks; warnings {catalog.Warnings.Count}.");
            return;
        }
        Need(catalog.Find("Building","Wall")!=null,"Core wall catalog entry");
        Need(catalog.Items.Any(i=>i.Kind=="Pawn"&&i.Group=="动物"),"General editor loads animals beyond mechanoids");
        Need(catalog.Items.Any(i=>i.Kind=="Pawn"&&i.Group=="人类与类人单位"),"General editor loads humanlike units");
        var scyther=catalog.Textures.Get(catalog.Find("Pawn","Mech_Scyther").Texture,2);
        Need(scyther!=null,"Local split BC7 DDS decoder binding");
        StructureDocument doc=new(){DefName="SE_TestRoom",Label="测试房间"};
        doc.Items.Add(new(){Kind="Building",DefName="MechAssembler"});
        doc.Items.Add(new(){Kind="Pawn",DefName="Mech_Scyther",X=-4,Z=-2,Rotation=3,StartDormant=true});
        doc.Items.Add(new(){Kind="Terrain",DefName="MechanoidPlatform"});
        Need(doc.Validate(catalog).Count==0,"Generic document validates against installed assets");
        string json=Path.Combine(folder,"SE_TestRoom.structure.json");doc.Save(json);var loaded=StructureDocument.Load(json);
        Need(loaded.DefName==doc.DefName&&loaded.Items[1].StartDormant==true&&loaded.Items[1].Rotation==3,"Native JSON roundtrip preserves object properties");
        Need(doc.ToXml().Name.LocalName=="StructureEditor.StructureDef","Neutral XML export is not coupled to a mod");
        var legacy=doc.Copy();legacy.DefName="MP_CompatibilityRoom";string legacyPath=Path.Combine(folder,"legacy.xml");
        new XDocument(new XElement("Defs",legacy.ToXml(true))).Save(legacyPath);
        Need(StructureDocument.Load(legacyPath).Items.Count==3,"Legacy XML import/export remains compatible");
        Need(StructureStorage.ResolveTarget(legacyPath,legacy.DefName,legacy.DefName,folder,false)==Path.Combine(folder,legacy.DefName+".structure.json"),"Saving an imported XML creates a native project without overwriting the source");
        var common=catalog.Items.Where(i=>CatalogCollections.Matches(i,1,paths)).ToList();Need(common.Any(i=>i.DefName=="Wall")&&common.Any(i=>i.DefName=="Bed"),"Common assets span construction and furniture");
        var wall=catalog.Find("Building","Wall");var settings=new StudioPaths{Favorites=new(){CatalogCollections.Key(wall)},RecentAssets=new(){CatalogCollections.Key(wall)}};
        Need(CatalogCollections.Matches(wall,2,settings)&&CatalogCollections.Matches(wall,3,settings),"Favorites and recent collections are independent of source/type");
        using(StructureCanvas canvas=new(){Catalog=catalog,Document=doc})using(Bitmap image=canvas.ExportPreview())image.Save(Path.Combine(folder,"structure-preview.png"),ImageFormat.Png);
        File.WriteAllText(Path.Combine(folder,"progress.txt"),DateTime.UtcNow.ToString("O")+" Interaction checks\n");
        string performance=StudioInteractionChecks.Run(catalog,folder,Need);
        StudioUsabilityChecks.Run(catalog,folder,Need);StudioDormancyAndWallsChecks.Run(catalog,folder,Need);
        using MainForm form=new();form.BindForChecks(catalog,doc.Copy());form.Size=new(1450,900);StudioInteractionChecks.RenderWorkspace(form,Path.Combine(folder,"workspace-general.png"));
        Need(form.Text.StartsWith("Structure Editor"),"Product title is generic");
        Need(form.MainMenuStrip.Items.Count==6,"Industrial menu grouping: File/Edit/Object/View/Window/Help");
        if(typeof(AppBranding).Assembly.GetManifestResourceNames().Contains("StructureEditor.Branding.GoldenCube.png"))
            Need(AppBranding.Mark!=null&&AppBranding.Icon!=null&&AppBranding.Mark.GetPixel(0,0).A==0,"Embedded golden cube branding loads as a transparent image and valid Windows icon");
        StudioStartupChecks.Run(paths,folder,Need);
        StudioFrameworkChecks.Run(catalog,folder,Need);
        StudioExportChecks.Run(catalog,folder,Need);
        StudioOverwriteChecks.Run(catalog,Need);
        StudioPastePreviewChecks.Run(catalog,folder,Need);
        StudioWorkspaceChecks.Run(catalog,folder,Need);
        StudioOptimizationChecks.Run(folder,Need);
        TurretExplosionChecks.Run(catalog,folder,Need);
        File.WriteAllText(Path.Combine(folder,"checks.txt"),$"PASS: {checks} checks; catalog {catalog.Items.Count}; source textures {catalog.Textures.SourceCount}; decoded {catalog.Textures.DecodedCount}.\n{performance}\nWarnings: {catalog.Warnings.Count}");
        File.WriteAllLines(Path.Combine(folder,"catalog-warnings.log"),catalog.Warnings);
    }
}
