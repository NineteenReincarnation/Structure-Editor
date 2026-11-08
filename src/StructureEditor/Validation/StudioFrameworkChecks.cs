using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace StructureEditor;

internal static class StudioFrameworkChecks
{
    public static void Run(Catalog catalog,string folder,Action<bool,string> need)
    {
        StructureDocument room=new(){DefName="SF_FrameworkRoundtrip",Label="框架测试房间"};
        room.Items.Add(new(){Kind="Building",DefName="Wall",Stuff="Steel",X=-1,Z=-1});
        room.Items.Add(new(){Kind="Building",DefName="Door",Stuff="Steel",X=1,Z=-1,Rotation=1});
        room.Items.Add(new(){Kind="Terrain",DefName="Concrete",X=0,Z=0});
        var frame=ExportDependencies.Xml(room,catalog,"Framework");string frameFile=Path.Combine(folder,room.DefName+".xml");frame.Save(frameFile);
        need(frame.Root.Element("StructureFramework.StructureDef")!=null&&!frame.ToString().Contains("MechProtocol"),"Default framework export must not depend on MP");
        var restored=StructureDocument.Load(frameFile,catalog);
        need(restored.Items.Count==3&&restored.Items[1].Rotation==1&&restored.Items[0].X==-1,"Framework XML must roundtrip exact object coordinates and rotations");
        var deps=ExportDependencies.Resolve(room,catalog,"Framework");
        need(deps.Count==1&&deps[0].PackageId==FrameworkCodec.PackageId,"A Core-only framework structure needs only the framework package");
        need(room.ExportProfile == "VanillaPrefab" && !room.EnableMPCompatibility,
            "A new JSON project defaults to unextended native PrefabDef");
        var prefab=ExportDependencies.Xml(room,catalog,"VanillaPrefab");string prefabFile=Path.Combine(folder,"SF_PrefabRoundtrip.xml");prefab.Save(prefabFile);
        var native=StructureDocument.Load(prefabFile,catalog);
        need(native.Items.Count==3&&native.Items[0].X==-1&&native.Items.First(i=>i.DefName=="Door").Rotation==1,"Prefab compatibility preserves coordinates through its origin comment");
        need(!prefab.ToString().Contains("startDormant")&&prefab.Root.Element("PrefabDef").Element("size")!=null,"Original Prefab XML uses only native placement fields");
        var unit=room.Copy();unit.Items.Add(new(){Kind="Pawn",DefName="Mech_Scyther",X=5});bool blocked=false;
        try{PrefabCodec.Export(unit,catalog);}catch(InvalidOperationException){blocked=true;}
        need(blocked&&unit.Items.Count==4,"Unsupported native pawn export must be rejected without deleting the pawn");
        var dormant = room.Copy(); dormant.StartDormant = true; blocked = false;
        try { PrefabCodec.Export(dormant,catalog); } catch(InvalidOperationException) { blocked = true; }
        need(blocked && dormant.StartDormant, "Native export must reject unsupported dormancy without discarding JSON state");
        var tagged = room.Copy(); tagged.FrameworkTags.Add("unsafe-to-drop"); blocked = false;
        try { PrefabCodec.Export(tagged,catalog); } catch(InvalidOperationException) { blocked = true; }
        need(blocked && tagged.FrameworkTags.Count == 1, "Native export must reject framework-only tags instead of silently dropping them");
        var explicitFramework = room.Copy(); explicitFramework.ExportProfile = "Framework";
        need(ExportDependencies.Xml(explicitFramework,catalog,explicitFramework.ExportProfile).Root?.Element("StructureFramework.StructureDef") != null,
            "Explicit framework projects remain framework exports");
        var sleeper=new StructureDocument{DefName="SF_Sleeper",EnableMPCompatibility=true,Items=new(){new(){Kind="Building",DefName="MechCapsule",StartDormant=true}}};
        var mp=FrameworkCodec.Export(sleeper,catalog);
        need(mp.ToString().Contains("StructureFramework.Integrations.MPDormancyModifier")&&mp.Element("requiredPackages").Value.Contains("Anno.MechProtocol"),"MP sleep states are emitted only as an explicit optional integration");
        var sleepRoundtrip=FrameworkCodec.Import(mp,catalog);
        need(sleepRoundtrip.EnableMPCompatibility&&sleepRoundtrip.Items.Single().StartDormant==true,"MP extension state survives framework import");
        using(var form=new MainForm())
        {
            var ordinary=sleeper.Copy();ordinary.EnableMPCompatibility=false;form.BindForChecks(catalog,ordinary);
            var canvas=(StructureCanvas)typeof(MainForm).GetField("canvas",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form);canvas.SelectAll();
            var state=(ComboBox)typeof(MainForm).GetField("dormancyBox",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form);
            need(!state.Enabled,"Per-object sleep controls must be disabled for a generic framework project");
        }
        string bundle=ExportDependencies.Bundle(room,catalog,Path.Combine(folder,"ContentPackages"),"Framework");
        var about=XDocument.Load(Path.Combine(bundle,"About","About.xml"));
        need(about.Root.Element("modDependencies").Value.Contains(FrameworkCodec.PackageId)&&!about.Root.Value.Contains("Anno.MechProtocol"),"A standalone Core structure package declares the framework and no MP dependency");
        var compact=XElement.Parse("<Defs><StructureFramework.StructurePaletteDef><defName>TestPalette</defName><symbols><W><thingDef>Wall</thingDef><stuffDef>Steel</stuffDef></W></symbols></StructureFramework.StructurePaletteDef><StructureFramework.StructureDef><defName>SF_Compact</defName><palette>TestPalette</palette><layout><li>WW</li></layout></StructureFramework.StructureDef></Defs>");
        var compactDoc=FrameworkCodec.Import(compact.Element("StructureFramework.StructureDef"),catalog,compact);
        need(compactDoc.Items.Count==2&&compactDoc.Items.All(i=>i.Stuff=="Steel"),"Compact palettes and symbol layouts are editable without loading game DLLs");
        string oldProject=Path.Combine(folder,"LegacySleep.structure.json");File.WriteAllText(oldProject,"{\"FormatVersion\":1,\"Items\":[{\"Kind\":\"Building\",\"DefName\":\"MechCapsule\",\"StartDormant\":true}]}");
        need(StructureDocument.Load(oldProject,catalog).EnableMPCompatibility,"Older sleep projects retain their MP compatibility when migrating to the framework");
        string multiple=Path.Combine(folder,"MultipleLegacy.xml");File.WriteAllText(multiple,"<Defs><MechProtocol.MP_StructureDef><defName>MP_First</defName></MechProtocol.MP_StructureDef><MechProtocol.MP_StructureDef><defName>MP_Second</defName></MechProtocol.MP_StructureDef></Defs>");
        need(StructureDocument.Load(multiple,catalog,"MP_Second").DefName=="MP_Second","The selected legacy definition must be imported from a multi-definition XML");
        bool unsupported=false;try{FrameworkCodec.Import(XElement.Parse("<StructureFramework.StructureDef><defName>Unknown</defName><unknownBehavior>true</unknownBehavior></StructureFramework.StructureDef>"),catalog);}catch(InvalidDataException){unsupported=true;}
        need(unsupported,"Unsupported framework fields must be rejected before editing can lose their meaning");
        string fake=Path.Combine(folder,"PlayerModFixture"),game=Path.Combine(fake,"Game"),mod=Path.Combine(game,"Mods","123456"),config=Path.Combine(fake,"Config","ModsConfig.xml");
        Directory.CreateDirectory(Path.Combine(game,"Data","Core","Defs"));Directory.CreateDirectory(Path.Combine(mod,"About"));Directory.CreateDirectory(Path.Combine(mod,"Defs"));Directory.CreateDirectory(Path.GetDirectoryName(config));
        File.WriteAllText(Path.Combine(mod,"About","About.xml"),"<ModMetaData><packageId>tests.playerbuildings</packageId><name>玩家建筑测试模组</name></ModMetaData>");
        File.WriteAllText(Path.Combine(mod,"Defs","Building.xml"),"<Defs><ThingDef><defName>Test_PlayerBuilding</defName><label>玩家建筑</label><category>Building</category><designationCategory>Production</designationCategory><size>(2,2)</size></ThingDef></Defs>");
        File.WriteAllText(config,"<ModsConfigData><activeMods><li>ludeon.rimworld</li><li>tests.playerbuildings</li></activeMods></ModsConfigData>");
        var paths=new StudioPaths{GameRoot=game,ModsConfigPath=config,AutoLoadActiveMods=true};
        using(var player=new Catalog())
        {
            player.Load(paths);var building=player.Find("Building","Test_PlayerBuilding");
            need(building?.IsMod==true&&building.PackageId=="tests.playerbuildings"&&building.Pack.Contains("玩家建筑测试模组"),"Enabled local mods must resolve by package ID and display their names");
            need(CatalogCollections.Matches(building,5,paths)&&!CatalogCollections.Matches(building,6,paths),"Player buildings belong to the independent mod collection");
        }
        string bridge=Path.Combine(Path.GetDirectoryName(config),"StructureFramework");Directory.CreateDirectory(bridge);
        File.WriteAllText(Path.Combine(bridge,"catalog.json"),JsonSerializer.Serialize(new{SchemaVersion=1,ExportedAtUtc=DateTime.UtcNow,ActivePackages=new[]{"ludeon.rimworld","tests.playerbuildings"},Items=new[]{new{Kind="Building",DefName="RuntimeOnlyBuilding",Label="代码生成建筑",Source=new{PackageId="tests.playerbuildings"},Width=3,Height=2,DesignationCategory="Production",ColorArgb=unchecked((int)0xffffffff)}},Materials=Array.Empty<object>()}));
        using(var synced=new Catalog())
        {
            synced.Load(paths);var dynamic=synced.Find("Building","RuntimeOnlyBuilding");
            need(synced.RuntimeSnapshotUsed&&dynamic?.Width==3&&dynamic.IsMod,"The game bridge must expose runtime-generated building definitions");
        }
        File.WriteAllText(Path.Combine(bridge,"catalog.json"),JsonSerializer.Serialize(new{SchemaVersion=1,ExportedAtUtc=DateTime.UtcNow,ActivePackages=new[]{"ludeon.rimworld","tests.playerbuildings"},Items=new[]{new{Kind="Building",DefName="Test_PlayerBuilding",Label="SHOULD NOT LEAK",Source=new{PackageId="tests.playerbuildings"}},new{Kind="Building",DefName="Test_PlayerBuilding",Label="Duplicate",Source=new{PackageId="tests.playerbuildings"}}},Materials=Array.Empty<object>()}));
        using(var fallback=new Catalog())
        {
            fallback.Load(paths);
            need(!fallback.RuntimeSnapshotUsed&&fallback.Find("Building","Test_PlayerBuilding").Label=="玩家建筑","A malformed runtime snapshot falls back atomically without leaking partial replacements");
        }
        File.WriteAllText(Path.Combine(bridge,"catalog.json"),JsonSerializer.Serialize(new{SchemaVersion=1,ExportedAtUtc=DateTime.UtcNow,ActivePackages=new[]{"ludeon.rimworld","tests.playerbuildings"},Items=Enumerable.Range(0,5000).Select(i=>new{Kind="Building",DefName="Runtime_Bulk_"+i,Label="批量建筑 "+i,Source=new{PackageId="tests.playerbuildings"},Width=1,Height=1}),Materials=Array.Empty<object>()}));
        using(var bulk=new Catalog())
        {
            bulk.Load(paths);need(bulk.RuntimeSnapshotUsed&&bulk.Items.Count==5001&&bulk.Find("Building","Runtime_Bulk_4999")!=null,"Large runtime catalogs merge all entries with indexed replacement");
            bulk.Load(paths);need(bulk.Items.Count==5001&&bulk.Sources.Count==2,"Reloading the same catalog replaces its resources and indexes without duplicate assets or sources");
        }
        File.WriteAllText(Path.Combine(bridge,"catalog.json"),JsonSerializer.Serialize(new{SchemaVersion=1,ActivePackages=new[]{"tests.playerbuildings","ludeon.rimworld"},Items=Array.Empty<object>(),Materials=Array.Empty<object>()}));
        using(var reordered=new Catalog()){reordered.Load(paths);need(!reordered.RuntimeSnapshotUsed&&reordered.Warnings.Any(w=>w.Contains("顺序")),"Changing mod order invalidates the runtime snapshot even when package IDs are unchanged");}
    }
}
