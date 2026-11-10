using System.Reflection;
using System.Text.Json;

namespace StructureEditor;

internal static class StudioUsabilityChecks
{
    public static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        need(catalog.Items.All(i => !string.IsNullOrEmpty(i.Group)), "Every catalog item must have a functional category");
        need(catalog.Find("Building", "MechAssembler").Group == "生产", "Forge nests belong to production");
        need(catalog.Find("Building", "Turret_MiniTurret").Group == "炮塔", "Native turret category missing");
        need(catalog.Find("Terrain", "MechanoidPlatform").Group == "平台与桥梁", "Platform category missing");
        var turrets = catalog.Query("Building", "Core", "炮塔", null).ToList();
        need(turrets.Count > 0 && turrets.All(i => i.Kind == "Building" && i.Pack == "Core" && i.Group == "炮塔"), "Kind/source/category filters must compose independently");
        need(catalog.Query("Building", null, "炮塔", "Turret_MiniTurret").Any(), "Text search must compose with the category filter");

        StructureDocument doc = new(); doc.Items.Add(new() { Kind = "Building", DefName = "MechAssembler" });
        using CheckCanvas canvas = new() { Catalog = catalog, Document = doc, Size = new(900, 700), Tool = CanvasTool.Select, ShowGrid = false };
        canvas.CenterView(); Point center = canvas.ClientFromCell(Point.Empty);
        need(center == new Point(450, 350), "A centered 3x3 building must be centered on the origin cell");
        var forge = catalog.Find("Building", "MechAssembler");
        need(forge.Width == 3 && forge.Height == 3 && forge.DrawX == 3 && forge.DrawZ == 3 && forge.OffsetX == 0 && forge.OffsetZ == 0, "Forge visual and occupied footprint must agree");
        canvas.Size = new(1100, 800);
        need(canvas.ClientFromCell(Point.Empty) == new Point(550, 400), "Resizing must preserve the world point at the view center");
        using (var image = canvas.Frame()) image.Save(Path.Combine(folder, "centered-forge.png"));
        canvas.Brush = catalog.Find("Terrain", "MechanoidPlatform"); canvas.PaintRectangle(new(-128, -128, 256, 256)); canvas.CenterView();
        var cornerA = canvas.ClientFromCell(new(-128, 127)); var cornerB = canvas.ClientFromCell(new(127, -128));
        need(cornerA.X > 20 && cornerA.Y > 20 && cornerB.X < canvas.Width - 20 && cornerB.Y < canvas.Height - 20, "Fit-all must include large floor layouts without the old minimum-zoom clipping");
        File.AppendAllText(Path.Combine(folder, "progress.txt"), DateTime.UtcNow.ToString("O") + " 65536 floors render\n");
        using (canvas.Frame()) { } int largeCache = canvas.TerrainCacheBuildCount;
        using (canvas.Frame()) { }
        need(canvas.TerrainCacheBuildCount == largeCache, "Fit-all of 65536 floors must keep its low-zoom chunks cached without cache thrashing");
        File.AppendAllText(Path.Combine(folder, "progress.txt"), DateTime.UtcNow.ToString("O") + " 65536 floors cached\n");
        canvas.SelectArea(new(-1, -1, 3, 3)); canvas.EditLayer = "Building"; canvas.SelectAll(); canvas.CenterSelection();
        need(canvas.ClientFromCell(Point.Empty) == new Point(550, 400), "Center-selection must ignore distant floors");
        canvas.GoToOrigin(); need(canvas.ClientFromCell(Point.Empty) == new Point(550, 400), "Go-to-origin must center (0,0)");
        canvas.EditLayer = "Pawn";
        int changes = 0; canvas.Changed += () => changes++;
        canvas.RightClick(Point.Empty);
        need(!doc.Items.Any(i => i.Kind == "Building") && doc.Items.Any(i => i.Kind == "Terrain" && i.X == 0 && i.Z == 0), "Right-click must delete a visible building even when the edit layer is Pawn, preserving its floor");
        need(changes == 1, "Right-click deletion must commit exactly one edit");
        canvas.RightClick(Point.Empty);
        need(!doc.Items.Any(i => i.X == 0 && i.Z == 0), "A second right click must delete the floor below the removed building");

        string output = Path.Combine(folder, "CustomSaveDirectory"), imported = Path.Combine(folder, "Imported", "custom.structure.json");
        need(StructureStorage.ResolveTarget(imported, "MP_A", "MP_A", output, false) == imported, "Save must keep an external opened file at its actual path");
        need(StructureStorage.ResolveTarget(null, null, "MP_A", output, false) == Path.Combine(output, "MP_A.structure.json"), "A new document must honor its configured output directory");
        need(StructureStorage.ResolveTarget(imported, "MP_A", "MP_A", output, true) == Path.Combine(output, "MP_A.structure.json"), "Changing the save directory must relocate the next save");
        need(StructureStorage.ResolveTarget(imported, "MP_A", "MP_B", output, false) == Path.Combine(Path.GetDirectoryName(imported)!, "MP_B.structure.json"), "Renaming a Def must create a new file beside the current one");
        var settings = new StudioPaths { SaveDirectory = output, ExtraMods = new() { "extra mod" } };
        var restored = JsonSerializer.Deserialize<StudioPaths>(JsonSerializer.Serialize(settings));
        need(restored.SaveDirectory == output && restored.ExtraMods.SequenceEqual(settings.ExtraMods), "Save-directory preferences must roundtrip without losing other settings");

        using MainForm form = new(); form.BindForChecks(catalog, new StructureDocument());
        var groupBox = (ComboBox)typeof(MainForm).GetField("groupBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var palette = (ListBox)typeof(MainForm).GetField("palette", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        groupBox.SelectedItem = "炮塔";
        need(palette.Items.Count > 0 && palette.Items.Cast<CatalogItem>().All(i => i.Group == "炮塔"), "Category selection must actually filter the palette");
        List<Control> controls = new();
        void Collect(Control parent) { foreach (Control child in parent.Controls) { controls.Add(child); Collect(child); } }
        Collect(form);
        var buttons = controls.OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>()).ToList();
        var documentBar = (ToolStrip)typeof(MainForm).GetField("documentBar",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var documentHost = (Panel)typeof(MainForm).GetField("MP_documentHost",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var materialPicker = (ComboBox)typeof(MainForm).GetField("materialBox",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        need(materialPicker.DrawMode == DrawMode.OwnerDrawFixed && materialPicker.ItemHeight >= 30,
            "Material choices should show a material texture or swatch beside the localized name");
        need(buttons.Count(b => b.ToolTipText?.StartsWith("新建结构") == true) == 1
            && buttons.Count(b => b.ToolTipText?.StartsWith("打开结构") == true) == 1
            && !documentBar.Items.Cast<ToolStripItem>().Any(
                b => b.Text.Contains("新建") || b.Text.Contains("打开")),
            "New and Open remain accessible once each, before edit layers, even in compact layout");
        need(buttons.Any(b => b.Text == "保存")
            && !buttons.Any(b => b.Text is "保存位置" or "导出预览" or "选择" or "放置" or "矩形" or "擦除"),
            "Top bars do not duplicate editor tools or secondary project/export actions");
        typeof(MainForm).GetMethod("MP_CenterDocumentBar",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (documentHost.ClientSize.Width > 0)
            need(Math.Abs(documentBar.Left - (documentHost.Width - documentBar.Width) / 2) <= 1,
                "Open document tabs are centered within their reserved symmetric header area");
        bool InObjectPage(Control c)
        {
            for (Control parent = c.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is MP_PageSurface page) return page.Title == "对象";
                if (parent is TabPage legacyPage) return legacyPage.Text == "对象";
            }
            return false;
        }
        need(!controls.OfType<NumericUpDown>().Any(InObjectPage) && !controls.OfType<Label>().Any(c => c.Text.Contains("双击吸取")), "Object properties must not contain coordinate editors or generic shortcut/help paragraphs");
        need(MechClusterCatalog.Entries.Count == 22 && MechClusterCatalog.Entries.All(e => catalog.Find("Building", e.Key)?.ClusterWikiEntry == true && catalog.Find("Building", e.Key).ClusterGroup == e.Value.Group), "All 22 Wiki building types must map to installed game Defs");
        var wikiItems = catalog.Query(null, null, null, null, true).Where(i => i.ClusterWikiEntry).ToList();
        need(wikiItems.Count(i => i.ClusterGroup == "杂项") == 9 && wikiItems.Count(i => i.ClusterGroup == "控制器") == 9 && wikiItems.Count(i => i.ClusterGroup == "炮塔") == 4, "Wiki taxonomy must preserve its 9 miscellaneous, 9 controller and 4 turret entries");
        need(catalog.Find("Building", "Wall")?.ClusterGroup == "防御设施",
            "Steel walls must be available in the mech-cluster defensive palette");
        if (catalog.Find("Building", "Barricade") is CatalogItem barricade)
            need(barricade.ClusterGroup == "防御设施",
                "Native barricades must be available in the mech-cluster defensive palette");
        need(catalog.Find("Building", "Turret_MiniTurret").ClusterGroup == null, "An ordinary player turret must not leak into the Wiki cluster list");
        need(catalog.Query(null, null, "杂项", "机械装配器", true).Any(i => i.DefName == "MechAssembler"), "Wiki aliases must be searchable when local game translations differ");
        var royalControllers = catalog.Query(null, "Royalty", "控制器", null, true).ToList();
        need(royalControllers.Count > 0 && royalControllers.All(i => i.ClusterGroup == "控制器" && i.Pack == "Royalty"), "Source filtering must compose with the Wiki category");
        var scopeBox = (ComboBox)typeof(MainForm).GetField("scopeBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var kindBox = (ComboBox)typeof(MainForm).GetField("kindBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        scopeBox.SelectedIndex = 4;
        need(groupBox.Items.Cast<string>().Skip(1).SequenceEqual(MechClusterCatalog.Groups) && !kindBox.Enabled, "The cluster scope includes defensive structures and the 22 dedicated Royalty devices");
        need(palette.Items.Count >= 22 && palette.Items.Cast<CatalogItem>().All(i => i.ClusterGroup != null), "The independent cluster scope must exclude unrelated material");
        groupBox.SelectedItem = "控制器";
        need(palette.Items.Cast<CatalogItem>().Count(i => i.ClusterWikiEntry) == 9 && palette.Items.Cast<CatalogItem>().All(i => i.ClusterGroup == "控制器"), "Wiki heading selection must filter the live catalog");
        scopeBox.SelectedIndex = 0;
        need(kindBox.Enabled && groupBox.Items.Contains("生产"), "Returning to all materials must restore the regular usage categories");
        File.WriteAllLines(Path.Combine(folder, "wiki-cluster-catalog.txt"), new[] { MechClusterCatalog.SourceUrl }.Concat(catalog.Query(null, null, null, null, true).OrderBy(i => i.ClusterGroup).ThenBy(i => i.DefName).Select(i => $"{i.ClusterGroup} | {i.DefName} | {i.ClusterWikiName ?? i.Label} | {(i.ClusterWikiEntry ? "Wiki" : "模组扩展")} | {i.Pack}")));
        StructureDocument DeleteFixture() => new() { Items = new()
        {
            new() { Kind = "Terrain", DefName = "MechanoidPlatform", X = 0 },
            new() { Kind = "Terrain", DefName = "MechanoidPlatform", X = 1 },
            new() { Kind = "Terrain", DefName = "MechanoidPlatform", X = 6 }
        } };
        foreach (Keys key in new[] { Keys.X, Keys.Back, Keys.Delete, Keys.Shift | Keys.X, Keys.Shift | Keys.Back })
        {
            using KeyCheckForm keyForm = new(); keyForm.BindForChecks(catalog, DeleteFixture());
            var selectedCanvas = keyForm.GetCanvas(); selectedCanvas.SelectArea(new(0, 0, 2, 1));
            need(keyForm.Command(key) && selectedCanvas.Document.Items.Count == 1 && selectedCanvas.Document.Items[0].X == 6, key + " must delete the selected group and preserve unselected objects");
            keyForm.Command(Keys.Control | Keys.Z);
            need(selectedCanvas.Document.Items.Count == 3, key + " must undo the complete group in one action");
            keyForm.Command(Keys.Control | Keys.Y);
            need(selectedCanvas.Document.Items.Count == 1, key + " group deletion must redo correctly");
        }
        using (CheckCanvas rightCanvas = new() { Catalog = catalog, Document = DeleteFixture(), Size = new(900, 600) })
        {
            rightCanvas.SelectArea(new(0, 0, 2, 1)); int snapshots = 0, edits = 0;
            rightCanvas.BeforeChange += () => snapshots++; rightCanvas.Changed += () => edits++;
            rightCanvas.RightClick(new(6, 0));
            need(rightCanvas.Document.Items.Count == 1 && rightCanvas.Document.Items[0].X == 6 && rightCanvas.Selection.Count == 0, "Right click with a group selected must delete that group, even when the pointer is on an unselected object");
            need(snapshots == 1 && edits == 1, "Right-click group deletion must remain a single undo action");
        }
        File.WriteAllLines(Path.Combine(folder, "catalog-groups.txt"), catalog.Items.GroupBy(i => i.Kind + " / " + i.Group).OrderBy(g => g.Key).Select(g => g.Key + ": " + g.Count()));
        using (KeyCheckForm shortcutForm = new())
        using (ComboBox closedPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList })
        using (TextBox textInput = new())
        {
            shortcutForm.BindForChecks(catalog, DeleteFixture()); shortcutForm.InputForChecks = closedPicker;
            foreach (var (key, mode) in new[] { (Keys.B, CanvasTool.Place), (Keys.V, CanvasTool.Select), (Keys.M, CanvasTool.Rectangle), (Keys.E, CanvasTool.Erase), (Keys.H, CanvasTool.Hand), (Keys.Z, CanvasTool.Zoom) })
            {
                need(shortcutForm.Command(key) && shortcutForm.GetCanvas().Tool == mode, "Closed sidebar pickers must not swallow mode shortcut " + key);
                need(shortcutForm.PreviewKey(key) && shortcutForm.GetCanvas().Tool == mode, "KeyPreview must route input-classified mode key " + key);
            }
            shortcutForm.InputForChecks = textInput; shortcutForm.GetCanvas().Tool = CanvasTool.Select;
            need(!shortcutForm.PreviewKey(Keys.B) && shortcutForm.GetCanvas().Tool == CanvasTool.Select, "Typing in search/name fields must not change canvas mode");
        }
        IDataObject savedClipboard = Clipboard.GetDataObject();
        try
        {
            using KeyCheckForm textForm = new(); textForm.BindForChecks(catalog, DeleteFixture());
            var textCanvas = textForm.GetCanvas(); textCanvas.SelectAll();
            TextBox Field(string name) => (TextBox)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(textForm)!;
            var identifier = Field("idBox"); textForm.InputForChecks = identifier;
            _ = identifier.Handle;
            identifier.Text = "SE_CopySource"; identifier.Select(3, 10);
            Clipboard.SetText("sentinel"); textForm.Command(Keys.Control | Keys.C);
            need(Clipboard.GetText() == "CopySource", "Ctrl+C copies selected identifier text through the real menu command path");
            textForm.Command(Keys.Control | Keys.X);
            need(identifier.Text == "SE_" && Clipboard.GetText() == "CopySource" && textCanvas.Document.Items.Count == 3, "Ctrl+X cuts identifier text without deleting selected scene objects; text=" + identifier.Text + " selection=" + identifier.SelectionLength + " objects=" + textCanvas.Document.Items.Count);
            Clipboard.SetText("NewStructure"); textForm.Command(Keys.Control | Keys.V);
            need(identifier.Text == "SE_NewStructure" && !textCanvas.HasStamp, "Ctrl+V pastes identifier text without creating an object stamp or moving focus");
            identifier.Text = "SE_UndoSource"; identifier.ClearUndo(); identifier.SelectAll();
            Clipboard.SetText("SE_Pasted"); textForm.Command(Keys.Control | Keys.V); textForm.Command(Keys.Control | Keys.Z);
            need(identifier.Text == "SE_UndoSource" && textCanvas.Document.Items.Count == 3, "Ctrl+Z undoes the text paste rather than undoing the scene; text=" + identifier.Text);
            identifier.Text = "SE_Alternate"; textForm.Command(Keys.Control | Keys.A); textForm.Command(Keys.Control | Keys.Insert);
            need(identifier.SelectionLength == identifier.TextLength && Clipboard.GetText() == identifier.Text, "Text select-all and Ctrl+Insert stay inside the input");
            Clipboard.SetText("SE_ShiftInsert"); textForm.Command(Keys.Shift | Keys.Insert);
            need(identifier.Text == "SE_ShiftInsert", "Shift+Insert pastes into the identifier field");
            foreach (string name in new[] { "labelBox", "search" })
            {
                var field = Field(name); textForm.InputForChecks = field; _ = field.Handle; field.Text = "旧文本"; field.SelectAll();
                Clipboard.SetText("墙体布局"); textForm.Command(Keys.Control | Keys.V); field.SelectAll(); textForm.Command(Keys.Control | Keys.C);
                need(field.Text == "墙体布局" && Clipboard.GetText() == "墙体布局", "Native Chinese text clipboard shortcuts also work for " + name);
            }
            using TextBox readOnly = new() { ReadOnly = true, Text = "read-only-path" }; readOnly.SelectAll(); textForm.InputForChecks = readOnly;
            textForm.Command(Keys.Control | Keys.C); textForm.Command(Keys.Control | Keys.V);
            need(Clipboard.GetText() == "read-only-path" && readOnly.Text == "read-only-path" && textCanvas.Document.Items.Count == 3, "Read-only paths remain copyable and cannot trigger scene paste");
            textForm.InputForChecks = textCanvas; textForm.Command(Keys.Control | Keys.C); textForm.Command(Keys.Control | Keys.V);
            need(textCanvas.HasStamp && textCanvas.Document.Items.Count == 3, "Canvas focus retains existing object copy/paste behavior");
        }
        finally { if (savedClipboard != null) Clipboard.SetDataObject(savedClipboard, true); else Clipboard.Clear(); }
        using (CheckCanvas pickupCanvas = new() { Catalog = catalog, Document = new StructureDocument(), Size = new(900, 600), Tool = CanvasTool.Select })
        {
            var original = new PlacedItem { Kind = "Pawn", DefName = "Mech_Scyther", Rotation = 3, StartDormant = true };
            pickupCanvas.Document.Items.Add(original); pickupCanvas.Document = pickupCanvas.Document; pickupCanvas.CenterView();
            pickupCanvas.DoubleTap(Point.Empty);
            need(pickupCanvas.Tool == CanvasTool.Place && pickupCanvas.Brush.DefName == original.DefName && pickupCanvas.Selection.Count == 0, "Double-click must absorb the object and switch to placement");
            need(pickupCanvas.BrushRotation == 3 && pickupCanvas.BrushDormant == true && pickupCanvas.Document.Items.Count == 1, "Double-click preserves direction and dormancy without duplicating objects");
            pickupCanvas.Tap(new(4, 0));
            need(pickupCanvas.Document.Items.Count == 2 && pickupCanvas.Document.Items.Last().Rotation == 3 && pickupCanvas.Document.Items.Last().StartDormant == true, "Next click places the absorbed object with its saved properties");
            pickupCanvas.Document = new(); pickupCanvas.Document.Items.Add(new() { Kind = "Building", DefName = "Barricade", Stuff = "Steel" }); pickupCanvas.Document = pickupCanvas.Document;
            pickupCanvas.Tool = CanvasTool.Select; pickupCanvas.DoubleTap(Point.Empty);
            need(pickupCanvas.Brush.DefName == "Barricade" && pickupCanvas.BrushStuff == "Steel", "Double-click absorbs the building material");
            pickupCanvas.Document = new(); pickupCanvas.Brush = catalog.Find("Building", "MechCapsule");
            need(pickupCanvas.BrushDormant == false, "New mech capsules default awake like all other palette objects");
            pickupCanvas.Tap(Point.Empty);
            need(pickupCanvas.Document.Items.Single().StartDormant == false, "Placed capsule stores its awake default explicitly in the document");
            pickupCanvas.BrushDormant = true; pickupCanvas.Tap(new(4, 0));
            need(pickupCanvas.Document.Items.Last().StartDormant == true, "An operator can manually set capsule dormancy before placement");
            pickupCanvas.Brush = catalog.Find("Pawn", "Mech_Scyther");
            need(pickupCanvas.BrushDormant == false, "Selecting another palette asset resets its default to awake");
        }
    }
    private sealed class KeyCheckForm : MainForm
    {
        public Control InputForChecks;
        protected override Control ShortcutInput => InputForChecks ?? base.ShortcutInput;
        public bool PreviewKey(Keys key) { KeyEventArgs e = new(key); OnKeyDown(e); return e.Handled && e.SuppressKeyPress; }
        public StructureCanvas GetCanvas() => (StructureCanvas)typeof(MainForm).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
        public bool Command(Keys key) { Message message = new(); return ProcessCmdKey(ref message, key); }
    }
    private sealed class CheckCanvas : StructureCanvas
    {
        protected override Keys InputModifiers => Keys.None;
        public void Tap(Point cell) { Point p = ClientFromCell(cell); OnMouseDown(new(MouseButtons.Left, 1, p.X, p.Y, 0)); OnMouseUp(new(MouseButtons.Left, 1, p.X, p.Y, 0)); }
        public void DoubleTap(Point cell) { Tap(cell); Point p = ClientFromCell(cell); OnMouseDown(new(MouseButtons.Left, 2, p.X, p.Y, 0)); OnMouseUp(new(MouseButtons.Left, 2, p.X, p.Y, 0)); }
        public void RightClick(Point cell) { Point p = ClientFromCell(cell); OnMouseDown(new(MouseButtons.Right, 1, p.X, p.Y, 0)); OnMouseUp(new(MouseButtons.Right, 1, p.X, p.Y, 0)); }
        public Bitmap Frame() { Bitmap bitmap = new(Width, Height); using var g = Graphics.FromImage(bitmap); g.Clear(BackColor); OnPaint(new(g, ClientRectangle)); return bitmap; }
    }
}
