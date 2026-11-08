using System.Drawing.Drawing2D;

namespace StructureEditor;

// Compact, read-only composition chart and schematic footprint.
// All layout calculation happens on document changes, never in OnPaint.
// It does not render game textures or write to the JSON project.
internal sealed class MP_StructureOverview : Control
{
    private readonly List<(Rectangle Area, int Category)> items = new();
    private Rectangle footprint;
    private RectangleF MP_view = RectangleF.Empty;
    private Rectangle MP_MapBounds => new(7, 47, Math.Max(1, Width - 14),
        Math.Max(16, Height - 54));
    internal event Action<PointF> MP_NavigateRequested;

    internal void MP_SetView(RectangleF view)
    {
        if (MP_view.Equals(view)) return;
        MP_view = view;
        Invalidate();
    }
    private int buildings, pawns, terrain;
    private static readonly Color Building = Color.FromArgb(161, 177, 180);
    private static readonly Color Pawn = Color.FromArgb(214, 187, 141);
    private static readonly Color Floor = Color.FromArgb(96, 150, 149);
    private static readonly Color Frame = Color.FromArgb(53, 74, 84);
    private static readonly Color Inset = Color.FromArgb(27, 37, 45);
    private static readonly Color Muted = Color.FromArgb(158, 181, 192);

    internal MP_StructureOverview()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 146;
        BackColor = Color.FromArgb(33, 43, 52);
    }

    internal void SetDocument(StructureDocument document, Catalog catalog)
    {
        items.Clear();
        buildings = pawns = terrain = 0;
        footprint = Rectangle.Empty;
        bool hasBounds = false;
        if (document != null)
        {
            foreach (PlacedItem item in document.Items)
            {
                int category = item.Kind switch
                {
                    "Building" => 0,
                    "Pawn" => 1,
                    "Terrain" => 2,
                    _ => -1
                };
                if (category < 0) continue;
                if (category == 0) buildings++;
                else if (category == 1) pawns++;
                else terrain++;
                Rectangle area = catalog == null
                    ? new Rectangle(item.X, item.Z, 1, 1)
                    : StructureDocument.Footprint(item, catalog);
                if (area.Width <= 0 || area.Height <= 0) continue;
                items.Add((area, category));
                footprint = hasBounds ? Rectangle.Union(footprint, area) : area;
                hasBounds = true;
            }
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        if (Width < 75 || Height < 65) return;
        int total = buildings + pawns + terrain;
        int usable = Math.Max(1, Width - 14);
        int top = 5;

        // Three-color legend displays raw object counts; no false percentage
        // claims about occupied cells, as buildings can occupy multiple cells.
        string[] names = { "建筑", "单位", "地板" };
        int[] counts = { buildings, pawns, terrain };
        Color[] colors = { Building, Pawn, Floor };
        for (int i = 0; i < 3; i++)
        {
            int x = 7 + i * usable / 3;
            using (SolidBrush swatch = new(colors[i]))
                g.FillEllipse(swatch, x, top + 6, 7, 7);
            Rectangle label = new(x + 11, top, Math.Max(12, usable / 3 - 12), 21);
            TextRenderer.DrawText(g, names[i] + " " + counts[i], Font, label,
                Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
        }

        Rectangle track = new(7, top + 27, usable, 7);
        using (SolidBrush bg = new(Inset)) g.FillRectangle(bg, track);
        if (total > 0)
        {
            int x = track.Left;
            int lastSegment = terrain > 0 ? 2 : pawns > 0 ? 1 : 0;
            for (int i = 0; i < 3; i++)
            {
                if (counts[i] == 0) continue;
                int width = i == lastSegment ? track.Right - x :
                    (int)Math.Round(track.Width * (counts[i] / (double)total));
                width = Math.Min(Math.Max(1, width), track.Right - x);
                if (width <= 0) break;
                using SolidBrush brush = new(colors[i]);
                g.FillRectangle(brush, x, track.Y, width, track.Height);
                x += width;
            }
        }

        Rectangle map = MP_MapBounds;
        using (SolidBrush fill = new(Inset)) g.FillRectangle(fill, map);
        using (Pen border = new(Frame)) g.DrawRectangle(border,
            map.X, map.Y, map.Width - 1, map.Height - 1);

        if (items.Count == 0)
        {
            TextRenderer.DrawText(g, "暂无布局", Font, map, Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        float scale = Math.Min((map.Width - 18f) / Math.Max(1, footprint.Width),
            (map.Height - 19f) / Math.Max(1, footprint.Height));
        if (scale <= 0) return;
        float originX = map.X + (map.Width - footprint.Width * scale) / 2f;
        float originY = map.Y + (map.Height - footprint.Height * scale) / 2f;

        GraphicsState clipped = g.Save();
        g.SetClip(new Rectangle(map.X + 2, map.Y + 2, map.Width - 4, map.Height - 4));
        g.SmoothingMode = SmoothingMode.None;
        // Terrain is the base, buildings cover terrain, pawns sit above both.
        DrawLayer(g, 2, originX, originY, scale);
        DrawLayer(g, 0, originX, originY, scale);
        DrawLayer(g, 1, originX, originY, scale);
        // The pale frame is the currently visible canvas, not a separate
        // selection: moving or zooming the canvas updates it in real time.
        if (!MP_view.IsEmpty)
        {
            RectangleF view = new(
                originX + (MP_view.Left - footprint.Left) * scale,
                originY + (footprint.Bottom - MP_view.Bottom) * scale,
                MP_view.Width * scale, MP_view.Height * scale);
            RectangleF clippedView = RectangleF.Intersect(view,
                new RectangleF(map.X + 2, map.Y + 2, map.Width - 4, map.Height - 4));
            if (clippedView.Width >= 1 && clippedView.Height >= 1)
            {
                using SolidBrush overlay = new(Color.FromArgb(36, 146, 205, 224));
                using Pen outline = new(Color.FromArgb(216, 164, 219, 234), 1.25f);
                g.FillRectangle(overlay, clippedView);
                g.DrawRectangle(outline, clippedView.X, clippedView.Y,
                    clippedView.Width, clippedView.Height);
            }
        }
        g.Restore(clipped);
        Rectangle dimensions = new(map.Right - 106, map.Bottom - 19, 99, 16);
        using (SolidBrush labelBg = new(Inset)) g.FillRectangle(labelBg, dimensions);
        TextRenderer.DrawText(g, footprint.Width + " × " + footprint.Height + " 格",
            Font, dimensions, Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = items.Count > 0 && MP_MapBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Rectangle map = MP_MapBounds;
        if (e.Button != MouseButtons.Left || items.Count == 0 || !map.Contains(e.Location))
            return;
        float scale = Math.Min((map.Width - 18f) / Math.Max(1, footprint.Width),
            (map.Height - 19f) / Math.Max(1, footprint.Height));
        if (scale <= 0) return;
        float originX = map.X + (map.Width - footprint.Width * scale) / 2f;
        float originY = map.Y + (map.Height - footprint.Height * scale) / 2f;
        // The schematic uses screen Y downward, while structure Z increases upward.
        MP_NavigateRequested?.Invoke(new PointF(
            Math.Clamp(footprint.Left + (e.X - originX) / scale, footprint.Left, footprint.Right),
            Math.Clamp(footprint.Bottom - (e.Y - originY) / scale, footprint.Top, footprint.Bottom)));
    }

    private void DrawLayer(Graphics g, int category, float x, float y, float scale)
    {
        Color color = category switch { 0 => Building, 1 => Pawn, _ => Floor };
        using SolidBrush brush = new(color);
        foreach (var item in items)
        {
            if (item.Category != category) continue;
            Rectangle area = item.Area;
            float sx = x + (area.Left - footprint.Left) * scale;
            float sy = y + (footprint.Bottom - area.Bottom) * scale;
            float width = Math.Max(1f, area.Width * scale);
            float height = Math.Max(1f, area.Height * scale);
            g.FillRectangle(brush, sx, sy, width, height);
        }
    }
}
