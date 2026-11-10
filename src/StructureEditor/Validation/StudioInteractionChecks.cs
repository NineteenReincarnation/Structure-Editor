using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;

namespace StructureEditor;

internal static class StudioInteractionChecks
{
    public static string Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        var floor = catalog.Find("Terrain", "MechanoidPlatform");
        using TestCanvas canvas = new() { Catalog = catalog, Document = new(), Size = new(1000, 700), Brush = floor };
        canvas.CenterView();
        int before = 0, changed = 0, selectionEvents = 0;
        canvas.BeforeChange += () => before++; canvas.Changed += () => changed++; canvas.SelectionChanged += () => selectionEvents++;
        Stopwatch watch = Stopwatch.StartNew();
        canvas.PaintRectangle(new(-50, -50, 100, 100));
        long fillMs = watch.ElapsedMilliseconds;
        need(canvas.Document.Items.Count == 10000, "Rectangle fill should place exactly 10000 cells");
        need(before == 1 && changed == 1 && selectionEvents == 1, "A 10000-cell fill must have one undo and one inspector refresh");
        canvas.PaintRectangle(new(-50, -50, 100, 100));
        need(before == 1 && changed == 1, "Painting identical terrain must not create an undo entry");
        using (var frame = canvas.Frame()) frame.Save(Path.Combine(folder, "terrain-10000.png"), ImageFormat.Png);
        int builds = canvas.TerrainCacheBuildCount;
        watch.Restart();
        for (int i = 0; i < 20; i++) using (canvas.Frame()) { }
        double frameMs = watch.Elapsed.TotalMilliseconds / 20;
        need(canvas.TerrainCacheBuildCount == builds, "Unchanged frames must reuse every terrain chunk");
        var otherFloor = catalog.Items.First(i => i.Kind == "Terrain" && i.DefName != floor.DefName);
        canvas.Brush = otherFloor; canvas.PaintRectangle(new(0, 0, 1, 1));
        using (canvas.Frame()) { }
        need(canvas.Document.Items.Count == 10000, "Terrain replacement must not duplicate a cell");
        need(canvas.TerrainCacheBuildCount - builds <= 4, "A cell edit must invalidate only local terrain chunks");
        watch.Restart(); var largeCopy = canvas.Document.Copy(); long copyMs = watch.ElapsedMilliseconds;
        need(largeCopy.Items.Count == 10000 && !ReferenceEquals(largeCopy.Items[0], canvas.Document.Items[0]), "Undo snapshots must deep-copy items");
        canvas.SelectArea(new(-4, -4, 8, 8));
        need(canvas.Selection.Count == 64, "Box selection should include all 64 cells");
        canvas.MoveSelected(100, 0);
        need(canvas.Selection.All(i => i.X >= 96 && i.X <= 103), "Group movement must preserve offsets");
        var positions = canvas.Selection.Select(i => (i.X, i.Z, i.Rotation)).OrderBy(p => p.X).ThenBy(p => p.Z).ToArray();
        for (int r = 0; r < 4; r++) canvas.Rotate();
        need(canvas.Selection.Select(i => (i.X, i.Z, i.Rotation)).OrderBy(p => p.X).ThenBy(p => p.Z).SequenceEqual(positions), "Four group rotations should restore exact coordinates");
        var copied = canvas.CopySelection(); canvas.SetStamp(copied);
        need(canvas.PlaceStamp(new(200, 200)) && canvas.Selection.Count == 64 && canvas.Document.Items.Count == 10064, "Pasting must preserve the entire selected group");
        int beforeBlocked = before;
        need(!canvas.PlaceStamp(new(200, 200)) || canvas.Document.Items.Count == 10064, "Repeated terrain stamp should replace floors instead of duplicating them");
        canvas.DeleteSelected(); need(canvas.Document.Items.Count == 10000, "Group delete should delete all pasted cells");
        canvas.VisibleLayers.Remove("Terrain"); canvas.SelectAll(); need(canvas.Selection.Count == 0, "Hidden layers must be excluded from selection"); canvas.VisibleLayers.Add("Terrain");

        StructureDocument group = new();
        group.Items.Add(new() { Kind = "Building", DefName = "MechAssembler" });
        group.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 4 });
        group.Items.Add(new() { Kind = "Pawn", DefName = "Mech_Scyther", X = 5 });
        canvas.Document = group; canvas.Tool = CanvasTool.Place;
        canvas.Modifiers = Keys.Shift; canvas.Down(new(3, -1)); canvas.MoveTo(new(6, 1)); canvas.Up(new(6, 1)); canvas.Modifiers = Keys.None;
        need(canvas.Selection.Count == 2 && canvas.Tool == CanvasTool.Select, "Shift-drag in paint mode must box-select instead of painting");
        canvas.Modifiers = Keys.Control; canvas.Down(new(4, 0)); canvas.Up(new(4, 0));
        need(canvas.Selection.Count == 1, "Ctrl-click should remove an existing selection");
        canvas.Down(new(4, 0)); canvas.Up(new(4, 0)); canvas.Modifiers = Keys.None;
        need(canvas.Selection.Count == 2, "Ctrl-click should add to a selection");
        int blockedBefore = before;
        canvas.MoveSelected(-4, 0);
        need(group.Items[1].X == 4 && group.Items[2].X == 5 && before == blockedBefore, "A blocked group move must be atomic and create no undo entry");
        int dragBefore = before, dragChanged = changed;
        canvas.Down(new(4, 0)); canvas.MoveTo(new(6, 2)); canvas.MoveTo(new(7, 3)); canvas.Up(new(7, 3));
        need(group.Items[1].X == 7 && group.Items[1].Z == 3 && group.Items[2].X == 8, "Dragging a group must preserve relative positions");
        need(before == dragBefore + 1 && changed == dragChanged + 1, "An entire group drag must be one undo action");
        canvas.Modifiers = Keys.Shift; canvas.Down(new(-1, -1)); canvas.Up(new(1, 1)); canvas.Modifiers = Keys.None;
        need(canvas.Selection.Count == 3, "Shift-box must add to the existing selection");
        canvas.EditLayer = "Pawn"; canvas.SelectAll(); need(canvas.Selection.Count == 2, "Edit layer should filter select-all");
        canvas.ApplyProperties(30, 20, 1, null);
        need(canvas.Selected.X == 30 && canvas.Selected.Z == 20 && canvas.Selection.All(i => i.Rotation == 1), "Batch inspector editing must move/rotate around the selected anchor");
        canvas.EditLayer = null;

        canvas.Document = new(); canvas.Brush = floor; canvas.Tool = CanvasTool.Place;
        int lineBefore = before, lineChanged = changed;
        canvas.Down(new(-10, 0)); canvas.MoveTo(new(10, 0)); canvas.Up(new(10, 0));
        need(canvas.Document.Items.Count == 21 && Enumerable.Range(-10, 21).All(x => canvas.Document.Items.Any(i => i.X == x)), "Fast drag painting must interpolate skipped cells");
        need(before == lineBefore + 1 && changed == lineChanged + 1, "A freehand stroke must have one undo action");
        canvas.Down(new(-10, 0), MouseButtons.Right); canvas.MoveTo(new(10, 0)); canvas.Up(new(10, 0), MouseButtons.Right);
        need(canvas.Document.Items.Count == 0, "Right drag should continuously erase interpolated cells");
        canvas.Tool = CanvasTool.Rectangle; canvas.Down(new(-5, -5)); canvas.MoveTo(new(4, 4)); canvas.Up(new(4, 4));
        need(canvas.Document.Items.Count == 100, "Rectangle gesture must fill the inclusive 10-by-10 area");
        canvas.Document.Items.Add(new() { Kind = "Building", DefName = "MechAssembler" });
        canvas.Document = canvas.Document; canvas.EditLayer = "Terrain"; canvas.Tool = CanvasTool.Erase;
        canvas.Down(new(0, 0)); canvas.Up(new(0, 0));
        need(canvas.Document.Items.Count == 100 && canvas.Document.Items.Any(i => i.Kind == "Building"), "Terrain-layer erasing must preserve a building above it");

        using MainForm form = new();
        form.BindForChecks(catalog, group.Copy());
        var field = typeof(MainForm).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var formCanvas = (StructureCanvas)field.GetValue(form)!;
        formCanvas.Brush = floor; formCanvas.Tool = CanvasTool.Rectangle; int originalCount = formCanvas.Document.Items.Count;
        formCanvas.PaintRectangle(new(-20, -20, 10, 10));
        typeof(MainForm).GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        need(formCanvas.Document.Items.Count == originalCount, "Undo must restore the entire rectangle in one action");
        typeof(MainForm).GetMethod("Redo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        need(formCanvas.Document.Items.Count == originalCount + 100, "Redo must restore the entire rectangle and rebuild the index");
        form.Size = new(1450, 900); form.PerformLayout();
        File.AppendAllText(Path.Combine(folder, "progress.txt"), DateTime.UtcNow.ToString("O") + " Wide/compact UI render\n");
        RenderWorkspace(form, Path.Combine(folder, "workspace-wide.png"));
        form.Size = new(1050, 720); form.PerformLayout();
        RenderWorkspace(form, Path.Combine(folder, "workspace-compact.png"));
        need(formCanvas.Width >= 250 && formCanvas.Height >= 350, "Compact layout must retain a usable canvas");
        return $"10000-cell rectangle {fillMs} ms; warmed viewport {frameMs:F2} ms/frame; deep snapshot {copyMs} ms; cached visible chunks {builds}.";
    }
    // A hidden Form's DrawToBitmap skips child windows. Compose their own renderings without showing it.
    internal static void RenderWorkspace(Form form, string path)
    {
        void Prepare(Control control) { _ = control.Handle; foreach (Control child in control.Controls.Cast<Control>().ToArray()) Prepare(child); }
        Prepare(form);
        void Layout(Control control) { control.PerformLayout(); foreach (Control child in control.Controls) Layout(child); control.PerformLayout(); }
        Layout(form); Layout(form);
        using Bitmap output = new(form.ClientSize.Width, form.ClientSize.Height);
        using Graphics graphics = Graphics.FromImage(output); graphics.Clear(form.BackColor);
        void Children(Control parent, Point offset, Rectangle clip)
        {
            foreach (Control child in parent.Controls.Cast<Control>().Reverse())
            {
                if (parent is TabControl tabs && child != tabs.SelectedTab) continue;
                if (child is MP_PageSurface page && !page.MP_IsActive) continue;
                if (child is SplitterPanel panel && panel.Parent is SplitContainer split && (panel == split.Panel1 ? split.Panel1Collapsed : split.Panel2Collapsed)) continue;
                if (child.Width <= 0 || child.Height <= 0) continue;
                Point position = new(offset.X + child.Left, offset.Y + child.Top);
                Rectangle bounds = new(position, child.Size), intersection = Rectangle.Intersect(clip, bounds);
                if (intersection.IsEmpty) continue;
                using Bitmap piece = new(child.Width, child.Height);
                if (child is ComboBox combo)
                {
                    // Native owner-drawn combo windows do not send WM_DRAWITEM to an invisible Form.
                    using Graphics comboGraphics = Graphics.FromImage(piece); comboGraphics.Clear(Color.FromArgb(49, 58, 64));
                    TextRenderer.DrawText(comboGraphics, combo.Text, combo.Font, new Rectangle(5, 0, combo.Width - 25, combo.Height), combo.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(comboGraphics, "⌄", combo.Font, new Rectangle(combo.Width - 23, 0, 20, combo.Height), combo.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
                else child.DrawToBitmap(piece, new(Point.Empty, piece.Size));
                var state = graphics.Save(); graphics.SetClip(intersection); graphics.DrawImageUnscaled(piece, position); graphics.Restore(state);
                Children(child, position, intersection);
            }
        }
        Children(form, Point.Empty, new(Point.Empty, output.Size)); output.Save(path, ImageFormat.Png);
    }
    private sealed class TestCanvas : StructureCanvas
    {
        public Keys Modifiers;
        protected override Keys InputModifiers => Modifiers;
        public void Down(Point cell, MouseButtons button = MouseButtons.Left) { Point p = ClientFromCell(cell); OnMouseDown(new(button, 1, p.X, p.Y, 0)); }
        public void MoveTo(Point cell) { Point p = ClientFromCell(cell); OnMouseMove(new(MouseButtons.None, 0, p.X, p.Y, 0)); }
        public void Up(Point cell, MouseButtons button = MouseButtons.Left) { Point p = ClientFromCell(cell); OnMouseUp(new(button, 1, p.X, p.Y, 0)); }
        public Bitmap Frame() { Bitmap image = new(Width, Height); using Graphics g = Graphics.FromImage(image); g.Clear(BackColor); OnPaint(new(g, ClientRectangle)); return image; }
    }
}
