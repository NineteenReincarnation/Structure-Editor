using System.Reflection;

namespace StructureEditor;

internal static class StudioDormancyAndWallsChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        var wall = catalog.Find("Building", "Wall");
        need(wall.GraphicClass == "Graphic_Appearances" && wall.Linked, "Core walls require a linked appearance atlas, not a folder's menu icon");
        foreach (var (stuff, appearance) in new[] { ("Steel", "Smooth"), ("WoodLog", "Planks"), ("BlocksGranite", "Bricks") })
        {
            string texturePath = catalog.TextureFor(wall, stuff);
            need(texturePath.EndsWith("Wall_Atlas_" + appearance, StringComparison.OrdinalIgnoreCase), "Wall selects native material appearance for " + stuff);
            var atlas = catalog.Textures.Get(texturePath);
            need(atlas != null && atlas.Width == atlas.Height && atlas.Width >= 128, "Wall resolves an actual16-state atlas for " + stuff + "; " + texturePath + " " + atlas?.Size + (atlas == null ? string.Join("\n", catalog.Textures.Errors) : ""));
            StructureDocument walls = new() { Category = "General" };
            for (int mask = 0; mask < 16; mask++)
            {
                Point p = new(mask % 4 * 5, mask / 4 * 5);
                walls.Items.Add(new() { Kind = "Building", DefName = "Wall", Stuff = stuff, X = p.X, Z = p.Y });
                foreach (var (dx, dz, bit) in new[] { (0, 1, 1), (1, 0, 2), (0, -1, 4), (-1, 0, 8) })
                    if ((mask & bit) != 0) walls.Items.Add(new() { Kind = "Building", DefName = "Wall", Stuff = stuff, X = p.X + dx, Z = p.Y + dz });
            }
            using StructureCanvas wallCanvas = new() { Catalog = catalog, Document = walls };
            using Bitmap image = wallCanvas.ExportPreview(); image.Save(Path.Combine(folder, "walls16-" + stuff + ".png"));
            if (stuff == "Steel") atlas.Save(Path.Combine(folder, "wall-atlas.png"));
        }
        var barricade = catalog.Find("Building", "Barricade");
        var block = new StructureDocument();
        for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) block.Items.Add(new() { Kind = "Building", DefName = "Wall", Stuff = "Steel", X = x, Z = z });
        var blockCells = block.Items.ToDictionary(i => new Point(i.X, i.Z));
        need(LinkedGraphics.CornerMask(blockCells[new(1, 1)], catalog, blockCells) == 15, "Solid wall blocks fill all four native corners");
        blockCells.Remove(new(2, 2));
        need(LinkedGraphics.CornerMask(blockCells[new(1, 1)], catalog, blockCells) == 11, "A missing diagonal must leave its corner open");
        using (StructureCanvas canvas = new() { Catalog = catalog, Document = block })
        using (Bitmap image = canvas.ExportPreview()) image.Save(Path.Combine(folder, "walls-solid-block.png"));
        byte[] swizzleSample = { 5, 10, 80, 140 };
        TextureLibrary.ApplyDdsSwizzle(swizzleSample, 0x02030303);
        need(swizzleSample.SequenceEqual(new byte[] { 80, 80, 80, 255 }), "BC4's nativeR/R/R/1 mapping produces opaque gray instead of red");
        need(barricade.Linked && barricade.LinkFlags.Contains("Barricades"), "Barricades retain native link flags");
        need(catalog.Find("Pawn", "Mech_Scyther").CanBeDormant && !barricade.CanBeDormant, "Dormancy capability comes from the native race/building comps");
        var center = new PlacedItem { Kind = "Building", DefName = "Barricade", Stuff = "Steel" };
        var neighbors = new Dictionary<Point, PlacedItem>();
        foreach (var (x, z, bit) in new[] { (0, 1, 1), (1, 0, 2), (0, -1, 4), (-1, 0, 8) })
        {
            neighbors[new(x, z)] = new() { Kind = "Building", DefName = "Barricade", X = x, Z = z };
            need((LinkedGraphics.Mask(center, catalog, neighbors) & bit) != 0, "Cardinal connection bit " + bit);
        }
        need(LinkedGraphics.Mask(center, catalog, neighbors) == 15, "Cross junction uses all four connections");
        neighbors[new(0, 1)] = new() { Kind = "Building", DefName = "Sandbags" };
        need(LinkedGraphics.Mask(center, catalog, neighbors) == 14, "Different native link groups do not connect");
        for (int mask = 0; mask < 16; mask++)
        {
            Rectangle tile = LinkedGraphics.Source(1024, 1024, mask);
            need(tile.Width == 192 && tile.Height == 192 && tile.X % 256 == 32 && tile.Y % 256 == 32, "Atlas tile excludes game padding: " + mask);
        }
        StructureDocument preview = new() { Category = "General" };
        foreach (var p in new[] { new Point(-4, -1), new(-4, 0), new(-4, 1), new(-1, 0), new(0, 0), new(1, 0), new(1, 1), new(4, 0), new(4, 1), new(4, -1), new(3, 0), new(5, 0) })
            preview.Items.Add(new() { Kind = "Building", DefName = "Barricade", Stuff = "Steel", X = p.X, Z = p.Y });
        using (StructureCanvas canvas = new() { Catalog = catalog, Document = preview })
        using (Bitmap image = canvas.ExportPreview()) image.Save(Path.Combine(folder, "linked-walls.png"));

        StructureDocument doc = new() { DefName = "MP_DormancyVerification", Category = "General", StartDormant = true, ExportProfile = "MechProtocol", EnableMPCompatibility = true };
        doc.Items.Add(new() { Kind = "Building", DefName = "Barricade", Stuff = "Steel", X = -3 });
        doc.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", StartDormant = true });
        doc.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 2, StartDormant = false });
        doc.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 4 });
        string path = Path.Combine(folder, "MP_DormancyVerification.xml"); doc.Save(path);
        var loaded = StructureDocument.Load(path);
        need(loaded.Items.Where(i => i.Kind == "Pawn").Select(i => i.StartDormant).SequenceEqual(new bool?[] { true, false, null }), "XML roundtrip preserves asleep, awake and inherited states");
        need(loaded.Validate(catalog).Count == 0 && loaded.Copy().Items[2].StartDormant == false, "Validation and undo snapshots preserve explicit awake override");
        using MainForm form = new(); form.BindForChecks(catalog, loaded);
        object Get(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
        var editor = (StructureCanvas)Get("canvas"); editor.SelectArea(new(-3, 0, 8, 1));
        var setting = (ComboBox)Get("dormancyBox");
        need(setting.Enabled && setting.SelectedIndex == 3, "Mixed multi-selection displays keep-individual-settings");
        setting.SelectedIndex = 1;
        need(loaded.Items.Where(i => i.Kind == "Pawn").All(i => i.StartDormant == true), "Changing dormancy applies immediately without a second Apply click");
        need(loaded.ToXml().Element("pawns")!.Elements("li").All(i => (bool?)i.Element("startDormant") == true), "Immediate dormancy changes are included in saved XML");
        need(loaded.Items.Where(i => i.Kind == "Pawn").All(i => i.StartDormant == true) && loaded.Items[0].StartDormant == null, "Batch dormancy edits only supported objects");
        typeof(MainForm).GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        need(editor.Document.Items.Where(i => i.Kind == "Pawn").Select(i => i.StartDormant).SequenceEqual(new bool?[] { true, false, null }), "A batch dormancy edit undoes in one operation");
        editor.SelectArea(new(2, 0, 1, 1)); setting.SelectedIndex = 0;
        need(editor.Selected.StartDormant == null, "An object can return to inherited structure default");
        editor.SelectArea(new(-3, 0, 1, 1)); need(!setting.Enabled, "Unsupported walls cannot be configured as dormant");
        editor.SelectArea(new(0, 0, 1, 1));
        need(editor.CopySelection().Single().StartDormant == true, "Copy/paste retains per-object dormancy");
        var rotation = (ComboBox)Get("rotationBox"); rotation.SelectedIndex = 1;
        need(editor.Selected.Rotation == 1, "Changing the direction selector immediately rotates the selected object");
        typeof(MainForm).GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        need(editor.Document.Items.Single(i => i.Kind == "Pawn" && i.X == 0).Rotation == 0, "Immediate direction edits undo normally");
        editor.SelectArea(new(-3, 0, 1, 1)); var material = (ComboBox)Get("materialBox");
        var chosen = material.Items.Cast<MaterialItem>().First(m => m.DefName != "Steel"); material.SelectedItem = chosen;
        need(editor.Selected.Stuff == chosen.DefName, "Changing material immediately updates compatible selected buildings");
        typeof(MainForm).GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        need(editor.Document.Items.Single(i => i.Kind == "Building").Stuff == "Steel", "Immediate material edits undo normally");
        need(typeof(MainForm).GetField("xBox", BindingFlags.Instance | BindingFlags.NonPublic) == null
            && typeof(MainForm).GetField("zBox", BindingFlags.Instance | BindingFlags.NonPublic) == null, "Inspector no longer contains numeric coordinate editors");
        editor.SelectArea(new(0, 0, 1, 1));
        StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "dormancy-inspector.png"));
    }
}
