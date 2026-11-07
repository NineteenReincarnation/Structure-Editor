using System.Diagnostics;
using System.Reflection;

namespace StructureEditor;

internal static class StudioPastePreviewChecks
{
    internal static void Run(Catalog catalog, string folder, Action<bool, string> need)
    {
        using PreviewCanvas canvas = new() { Catalog = catalog, Document = new(), Size = new(1000, 800), ShowGrid = false };
        canvas.CenterView(); canvas.ZoomAtCenter(.25f);
        var copied = new List<PlacedItem>();
        for (int z = 0; z < 40; z++) for (int x = 0; x < 50; x++)
            copied.Add(new() { Kind = "Building", DefName = "Wall", Stuff = "Steel", X = x, Z = z });
        canvas.SetStamp(copied); canvas.Hover(new(-25, -20));
        int Painted(Bitmap image, IEnumerable<Point> cells) => cells.Count(c => { Point pixel = canvas.ClientFromCell(c); return image.GetPixel(pixel.X, pixel.Y).ToArgb() != canvas.BackColor.ToArgb(); });
        var samples = new[] { new Point(-20, -15), new(-12, -8), new(0, 0), new(10, 8), new(20, 15) };
        var watch = Stopwatch.StartNew();
        using (Bitmap frame = canvas.Frame())
        {
            need(Painted(frame, samples) == samples.Length, "2000-item paste renders real contents at multiple points inside the border instead of suppressing copies above128items");
            frame.Save(Path.Combine(folder, "paste-preview-2000.png"));
        }
        watch.Stop(); long initialMs = watch.ElapsedMilliseconds;
        int Builds() => (int)typeof(StructureCanvas).GetField("pastePreviewBuildCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
        int built = Builds(); watch.Restart();
        for (int i = 0; i < 5; i++) { canvas.Hover(new(-25 + i, -20)); using var frame = canvas.Frame(); }
        watch.Stop(); double movedMs = watch.Elapsed.TotalMilliseconds / 5;
        need(Builds() == built, "Moving2000-item paste reuses the rendered preview without redrawing every object");
        need(canvas.Document.Items.Count == 0 && canvas.HasStamp, "Rendering a large preview does not place or alter document objects");
        canvas.Rotate(-1); canvas.Hover(new(20, -25));
        using (Bitmap frame = canvas.Frame())
        {
            need(Painted(frame, new[] { new Point(10, -15), new(0, 0), new(-10, 15) }) == 3, "Rotated large paste updates visible contents and linked-wall geometry");
            frame.Save(Path.Combine(folder, "paste-preview-rotated.png"));
        }
        canvas.ZoomAtCenter(4); canvas.Hover(Point.Empty);
        watch.Restart();
        using (Bitmap frame = canvas.Frame())
        {
            need(Painted(frame, new[] { new Point(-1, 1), new(-3, 3), new(-5, 5) }) == 3, "Zoomed preview still draws the visible portion when most copied objects lie offscreen");
            frame.Save(Path.Combine(folder, "paste-preview-zoomed.png"));
        }
        watch.Stop(); long zoomedMs = watch.ElapsedMilliseconds;
        int oldBuilds = Builds(); canvas.RefreshTextures(); using (canvas.Frame()) { }
        need(Builds() == oldBuilds + 1, "Asynchronously refreshed textures invalidate and rebuild the paste image");
        canvas.Cancel();
        using (Bitmap frame = canvas.Frame()) need(Painted(frame, new[] { new Point(-1, 1), new(-3, 3), new(-5, 5) }) == 0, "Cancel removes the large preview without modifying the document");
        canvas.ZoomAtCenter(2); canvas.SetStamp(copied); canvas.Hover(Point.Empty);
        using (Bitmap frame = canvas.Frame()) need(Painted(frame, new[] { new Point(1, 1), new(2, 2), new(3, 3) }) == 3, "Oversized preview uses visible-object rendering rather than hiding its contents at the bitmap limit");
        var room = new List<PlacedItem>();
        void Wall(int x, int z) => room.Add(new() { Kind = "Building", DefName = "Wall", Stuff = "Steel", X = x, Z = z });
        for (int x = -25; x <= 25; x++) { Wall(x, -20); Wall(x, 20); }
        for (int z = -19; z <= 19; z++) { Wall(-25, z); Wall(25, z); }
        for (int x = -24; x <= 24; x++) if (Math.Abs(x) > 1) Wall(x, 0);
        room.Add(new() { Kind = "Building", DefName = "Door", Stuff = "Steel" });
        room.Add(new() { Kind = "Building", DefName = "MechAssembler", Z = 10 });
        foreach (int x in new[] { -15, -10, 10, 15 }) room.Add(new() { Kind = "Building", DefName = "Bed", Stuff = "WoodLog", X = x, Z = -10, Rotation = 1 });
        foreach (int x in new[] { -12, 12 }) room.Add(new() { Kind = "Building", DefName = "Turret_AutoMiniTurret", X = x, Z = 10 });
        for (int z = -12; z < -8; z++) for (int x = -2; x < 2; x++) room.Add(new() { Kind = "Terrain", DefName = "Concrete", X = x, Z = z });
        canvas.ZoomAtCenter(1, actualSize: true); canvas.ZoomAtCenter(.25f); canvas.SetStamp(room); canvas.Hover(Point.Empty);
        using (Bitmap frame = canvas.Frame())
        {
            need(room.Count > 128 && Painted(frame, new[] { new Point(-25, 5), new(10, 0), new(0, -10) }) == 3 && Painted(frame, new[] { new Point(-10, -5) }) == 0, "Large mixed room shows walls and interior details while empty room space stays transparent");
            frame.Save(Path.Combine(folder, "paste-preview-room.png"));
        }
        canvas.Rotate(-1);
        using (Bitmap frame = canvas.Frame()) frame.Save(Path.Combine(folder, "paste-preview-room-rotated.png"));
        File.WriteAllText(Path.Combine(folder, "paste-preview-performance.txt"), $"2000 copied walls. First overview render: {initialMs}ms; warmed moving preview: {movedMs:F2}ms/frame; zoomed render: {zoomedMs}ms. Timings are observations, not assertions.\n");
    }
    private sealed class PreviewCanvas : StructureCanvas
    {
        public void Hover(Point cell) { var p = ClientFromCell(cell); OnMouseMove(new(MouseButtons.None, 0, p.X, p.Y, 0)); }
        public Bitmap Frame() { Bitmap image = new(Width, Height); using var g = Graphics.FromImage(image); g.Clear(BackColor); OnPaint(new(g, ClientRectangle)); return image; }
    }
}
