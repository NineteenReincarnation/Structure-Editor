using System.Reflection;

namespace StructureEditor;

internal static class StudioOverwriteChecks
{
    internal static void Run(Catalog catalog, Action<bool, string> need)
    {
        using TestForm form = new();
        StructureDocument doc = new() { Items = new()
        {
            new() { Kind = "Terrain", DefName = "Concrete" },
            new() { Kind = "Building", DefName = "Wall", Stuff = "Steel" },
            new() { Kind = "Building", DefName = "Door", Stuff = "Steel", X = 4 }
        } };
        form.BindForChecks(catalog, doc); var canvas = form.Canvas; var old = doc.Items[1];
        canvas.SetStamp(new[] { new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "BlocksGranite" } });
        need(canvas.PlaceStamp(Point.Empty) && doc.Items.Count == 3 && !doc.Items.Contains(old) && doc.Items.Single(i => i.DefName == "Wall").Stuff == "BlocksGranite", "Same building stamp replaces whole old object and applies incoming material");
        need(doc.Items.Any(i => i.Kind == "Terrain") && doc.Validate(catalog).Count == 0, "Overwrite preserves underlying terrain and a non-overlapping valid document");
        form.UndoEdit();
        need(canvas.Document.Items.Count == 3 && canvas.Document.Items.Single(i => i.DefName == "Wall").Stuff == "Steel", "One Undo restores replaced building and its original properties");
        form.RedoEdit();
        need(canvas.Document.Items.Count == 3 && canvas.Document.Items.Single(i => i.DefName == "Wall").Stuff == "BlocksGranite", "One Redo restores the complete replacement and index");
        canvas.SetStamp(new[] { new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "WoodLog" }, new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "Steel", X = 4 } });
        var unchanged = canvas.Document.Items.ToArray(); int snapshots = 0; canvas.BeforeChange += () => snapshots++;
        need(!canvas.PlaceStamp(Point.Empty) && canvas.Document.Items.SequenceEqual(unchanged) && snapshots == 0, "A different building conflict rejects whole stamp before replacing any same-type building");
        canvas.Document = new() { Items = new() { new() { Kind = "Pawn", DefName = "Mech_Scyther" } } };
        canvas.SetStamp(new[] { new PlacedItem { Kind = "Pawn", DefName = "Mech_Scyther" } });
        need(!canvas.PlaceStamp(Point.Empty) && canvas.Document.Items.Count == 1, "Same pawn types are not silently overwritten by building-only behavior");
        using (PaintCanvas paint = new() { Catalog = catalog, Document = new() { Items = new() { new() { Kind = "Building", DefName = "Wall", Stuff = "Steel" } } }, Size = new(800, 600), Brush = catalog.Find("Building", "Wall"), BrushStuff = "WoodLog", Tool = CanvasTool.Place })
        {
            int changes = 0; paint.BeforeChange += () => changes++;
            paint.Tap(Point.Empty);
            need(paint.Document.Items.Count == 1 && paint.Document.Items[0].Stuff == "WoodLog" && changes == 1, "Ordinary placement brush can replace same type with another material");
            paint.Tap(Point.Empty);
            need(changes == 1 && paint.Document.Items.Count == 1, "Repainting an identical building is a no-op rather than a duplicate undo entry");
        }
        using (StructureCanvas large = new() { Catalog = catalog, Document = new() { Items = new()
        {
            new() { Kind = "Building", DefName = "Bed", Stuff = "Steel" }, new() { Kind = "Building", DefName = "Bed", Stuff = "Steel", X = 1 }
        } } })
        {
            large.SetStamp(new[] { new PlacedItem { Kind = "Building", DefName = "Bed", Stuff = "WoodLog", Rotation = 1 } });
            need(large.PlaceStamp(Point.Empty) && large.Document.Items.Count == 1 && large.Document.Items[0].Rotation == 1, "Rotated multi-cell building replaces all touched same-type footprints as whole objects");
            need(large.CanPlace(new() { Kind = "Building", DefName = "Door", Stuff = "Steel", Z = 1 }), "Removed multi-cell footprint leaves no stale occupancy cells");
            large.Document = new(); large.SetStamp(new[] { new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "Steel" }, new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "Steel" } });
            need(!large.PlaceStamp(Point.Empty) && large.Document.Items.Count == 0, "Overlapping items inside one stamp remain invalid instead of producing duplicate buildings");
        }
        var capsule = new PlacedItem { Kind = "Building", DefName = "MechCapsule", StartDormant = true, FrameworkModifiers = new() { "Test_Modifier" } };
        canvas.Document = new() { Items = new() { capsule } }; var incoming = capsule.Copy(); incoming.StartDormant = false;
        canvas.SetStamp(new[] { incoming }); need(canvas.PlaceStamp(Point.Empty) && canvas.Document.Items.Count == 1 && canvas.Document.Items[0].StartDormant == false && canvas.Document.Items[0].FrameworkModifiers.SequenceEqual(incoming.FrameworkModifiers), "Overwrite carries incoming dormancy and framework metadata");
        canvas.Document = new();
        var group = new[] { new PlacedItem { Kind = "Building", DefName = "Bed", Stuff = "Steel" }, new PlacedItem { Kind = "Building", DefName = "Wall", Stuff = "Steel", X = 2, Z = 1 } };
        canvas.SetStamp(group);
        need(form.Key(Keys.Q) && canvas.HasStamp && canvas.Tool == CanvasTool.Place, "Q rotates paste preview counterclockwise while preserving placement mode");
        need(form.Key(Keys.E) && canvas.HasStamp && canvas.Tool == CanvasTool.Place, "E rotates paste preview clockwise instead of switching to eraser");
        need(canvas.PlaceStamp(new(10, 10)) && canvas.Document.Items.Any(i => i.DefName == "Wall" && i.X == 12 && i.Z == 11) && canvas.Document.Items.Single(i => i.DefName == "Bed").Rotation == 0, "Q followed by E restores group coordinates and object orientations");
        canvas.SetStamp(group); form.Key(Keys.Q); canvas.PlaceStamp(new(20, 20));
        need(canvas.Document.Items.Any(i => i.DefName == "Wall" && i.X == 19 && i.Z == 22) && canvas.Document.Items.Any(i => i.DefName == "Bed" && i.X == 20 && i.Z == 20 && i.Rotation == 3), "Q rotates relative group positions and rotatable object orientation by90degrees");
        canvas.SetStamp(group); need(form.Preview(Keys.E) && canvas.HasStamp && canvas.Tool == CanvasTool.Place, "KeyPreview routes E to stamp rotation for input-classified key events");
        using TextBox text = new(); form.Input = text; var beforeText = Stamp(canvas);
        form.Key(Keys.Q); form.Key(Keys.E);
        need(Stamp(canvas).SequenceEqual(beforeText), "Typing Q/E in a text field cannot rotate a pending paste");
        form.Input = null; canvas.Cancel(); form.Key(Keys.E);
        need(canvas.Tool == CanvasTool.Erase && !canvas.HasStamp, "E retains its eraser shortcut after paste cancellation");
    }
    private static (int, int, int)[] Stamp(StructureCanvas canvas) => ((List<PlacedItem>)typeof(StructureCanvas).GetField("stamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!).Select(i => (i.X, i.Z, i.Rotation)).ToArray();
    private sealed class TestForm : MainForm
    {
        public Control Input;
        protected override Control ShortcutInput => Input ?? Canvas;
        public StructureCanvas Canvas => (StructureCanvas)typeof(MainForm).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
        public bool Key(Keys key) { Message m = new(); return ProcessCmdKey(ref m, key); }
        public bool Preview(Keys key) { KeyEventArgs e = new(key); OnKeyDown(e); return e.Handled && e.SuppressKeyPress; }
        public void UndoEdit() => typeof(MainForm).GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
        public void RedoEdit() => typeof(MainForm).GetMethod("Redo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
    }
    private sealed class PaintCanvas : StructureCanvas
    {
        public void Tap(Point cell) { var p = ClientFromCell(cell); OnMouseDown(new(MouseButtons.Left, 1, p.X, p.Y, 0)); OnMouseUp(new(MouseButtons.Left, 1, p.X, p.Y, 0)); }
    }
}
