using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace StructureEditor;

internal static class TurretExplosionChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        foreach (var (name, radius) in new[] { ("Turret_AutoMiniTurret", 3.9f), ("Turret_AutoInferno", 5.9f), ("Turret_AutoChargeBlaster", 5.9f), ("Turret_AutoMortar", 4.9f) })
            need(catalog.Find("Building", name)?.TurretExplosionRadius == radius, name + " uses its own destruction explosion radius");
        need(catalog.Find("Building", "Wall").TurretExplosionRadius == null && catalog.Find("Building", "BurningPowerCell").TurretExplosionRadius == null,
            "The turret overlay excludes non-turret explosive buildings");
        XElement Def(string radius) => new("ThingDef", new XElement("building", new XElement("turretGunDef", "TestGun")),
            new XElement("comps", new XElement("li", new XAttribute("Class", "RimWorld.CompProperties_Explosive"), new XElement("explosiveRadius", radius))));
        foreach (string invalid in new[] { "NaN", "Infinity", "-1", "bad" })
            need(Catalog.ReadTurretExplosionRadius(Def(invalid)) == null, "Invalid explosion radius is ignored: " + invalid);
        need(Catalog.ReadTurretExplosionRadius(Def("0")) == 0, "Explicit zero radius is preserved");
        string fixture = Path.Combine(folder, "ExplosionCatalogFixture"), game = Path.Combine(fixture, "Game"), config = Path.Combine(fixture, "Config", "ModsConfig.xml");
        Directory.CreateDirectory(Path.Combine(game, "Data", "Core", "Defs")); Directory.CreateDirectory(Path.GetDirectoryName(config));
        File.WriteAllText(Path.Combine(game, "Data", "Core", "Defs", "Turret.xml"),
            "<Defs><ThingDef Name='TestTurretBase' Abstract='True'><category>Building</category><building><turretGunDef>TestGun</turretGunDef></building><comps><li Class='CompProperties_Explosive'><explosiveRadius>7.25</explosiveRadius></li></comps></ThingDef><ThingDef ParentName='TestTurretBase'><defName>TestTurret</defName></ThingDef></Defs>");
        File.WriteAllText(config, "<ModsConfigData><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        var paths = new StudioPaths { GameRoot = game, ModsConfigPath = config, AutoLoadActiveMods = true, UseRuntimeCatalog = false };
        using (Catalog inherited = new()) { inherited.Load(paths); need(inherited.Find("Building", "TestTurret")?.TurretExplosionRadius == 7.25f, "Inherited explosion radius is resolved from the parent XML"); }
        paths.UseRuntimeCatalog = true;
        string bridge = Path.Combine(Path.GetDirectoryName(config), "StructureFramework"); Directory.CreateDirectory(bridge);
        var runtimeItem = new Dictionary<string, object> { ["Kind"] = "Building", ["DefName"] = "TestTurret", ["ThingClass"] = "Building_TurretGun", ["Width"] = 1, ["Height"] = 1, ["Source"] = new { PackageId = "ludeon.rimworld" } };
        void Snapshot() => File.WriteAllText(Path.Combine(bridge, "catalog.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, ActivePackages = new[] { "ludeon.rimworld" }, Items = new[] { runtimeItem } }));
        Snapshot();
        using (Catalog legacy = new()) { legacy.Load(paths); need(legacy.RuntimeSnapshotUsed && legacy.Find("Building", "TestTurret")?.TurretExplosionRadius == 7.25f, "Legacy runtime snapshots retain the XML explosion radius"); }
        runtimeItem["TurretExplosionRadius"] = 0f; Snapshot();
        using (Catalog zero = new()) { zero.Load(paths); need(zero.Find("Building", "TestTurret")?.TurretExplosionRadius == 0, "Explicit runtime zero disables the explosion radius without XML fallback"); }

        StructureDocument doc = new(); doc.Items.Add(new() { Kind = "Building", DefName = "Turret_AutoMiniTurret" });
        using CheckCanvas canvas = new() { Catalog = catalog, Document = doc, Size = new(900, 700), Tool = CanvasTool.Select, ShowGrid = false };
        canvas.GoToOrigin();
        int changes = 0; canvas.BeforeChange += () => changes++; canvas.Changed += () => changes++;
        using Bitmap off = canvas.Frame();
        need(!canvas.ShowTurretExplosionRanges, "Explosion overlay defaults to off");
        canvas.ShowTurretExplosionRanges = true;
        using Bitmap on = canvas.Frame(); on.Save(Path.Combine(folder, "turret-explosion-ranges.png"), ImageFormat.Png);
        using Bitmap exportedOn = canvas.ExportPreview(); canvas.ShowTurretExplosionRanges = false;
        using (Bitmap exportedOff = canvas.ExportPreview()) need(Same(exportedOn, exportedOff), "View overlays do not change exported PNG previews");
        canvas.ShowTurretExplosionRanges = true;
        Point inside = canvas.ClientFromCell(new(3, 0)), outside = canvas.ClientFromCell(new(5, 0));
        need(off.GetPixel(inside.X, inside.Y) != on.GetPixel(inside.X, inside.Y), "Enabled overlay shades cells inside the blast radius");
        need(off.GetPixel(outside.X, outside.Y) == on.GetPixel(outside.X, outside.Y), "Overlay does not shade outside the radius");
        canvas.ShowTurretExplosionRanges = false;
        using (Bitmap restored = canvas.Frame()) need(Same(off, restored), "Turning the overlay off restores the original rendering");
        canvas.ShowTurretExplosionRanges = true; canvas.VisibleLayers.Remove("Building");
        using (Bitmap hidden = canvas.Frame())
        {
            canvas.ShowTurretExplosionRanges = false;
            using Bitmap hiddenOff = canvas.Frame(); need(Same(hidden, hiddenOff), "Hidden building layers hide their blast radii");
        }
        canvas.VisibleLayers.Add("Building"); canvas.Document = new(); canvas.Brush = catalog.Find("Building", "Turret_AutoInferno");
        canvas.Tool = CanvasTool.Place; canvas.MoveTo(new(0, 0));
        using Bitmap ghostOff = canvas.Frame(); canvas.ShowTurretExplosionRanges = true;
        using (Bitmap ghostOn = canvas.Frame()) need(!Same(ghostOff, ghostOn), "Placement ghost shows the turret explosion radius");
        canvas.SetStamp(doc.Items); canvas.ShowTurretExplosionRanges = false;
        using Bitmap stampOff = canvas.Frame(); canvas.ShowTurretExplosionRanges = true;
        using (Bitmap stampOn = canvas.Frame()) need(!Same(stampOff, stampOn), "Pasted turret previews show their explosion radii");
        need(changes == 0 && doc.Items.Count == 1, "View toggles never change layout or create undo transactions");

        using MainForm form = new(); form.BindForChecks(catalog, doc); form.Size = new(1500, 900);
        var field = typeof(MainForm).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var formCanvas = (StructureCanvas)field.GetValue(form)!;
        var menu = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().SelectMany(m => m.DropDownItems.OfType<ToolStripMenuItem>()).Single(m => m.Name == "TurretExplosionRanges");
        ToolStripButton button = null;
        void Find(Control c)
        {
            if (c is ToolStrip strip) button ??= strip.Items.OfType<ToolStripButton>().FirstOrDefault(b => b.Name == "TurretExplosionRanges");
            foreach (Control child in c.Controls) Find(child);
        }
        Find(form); need(button != null && !button.Checked && !menu.Checked, "Toolbar and View menu expose the same default-off switch");
        button.PerformClick(); need(formCanvas.ShowTurretExplosionRanges && menu.Checked && button.Checked, "Toolbar toggle synchronizes the menu and canvas");
        formCanvas.Tool = CanvasTool.Select; formCanvas.GoToOrigin();
        StudioInteractionChecks.RenderWorkspace(form, Path.Combine(folder, "workspace-turret-explosion.png"));
        menu.PerformClick(); need(!formCanvas.ShowTurretExplosionRanges && !button.Checked && !menu.Checked, "View menu toggle synchronizes the toolbar and canvas");
    }
    private static bool Same(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
    private sealed class CheckCanvas : StructureCanvas
    {
        internal void MoveTo(Point cell) { Point p = ClientFromCell(cell); OnMouseMove(new(MouseButtons.None, 0, p.X, p.Y, 0)); }
        internal Bitmap Frame() { Bitmap frame = new(Width, Height); using Graphics g = Graphics.FromImage(frame); g.Clear(BackColor); OnPaint(new(g, ClientRectangle)); return frame; }
    }
}
