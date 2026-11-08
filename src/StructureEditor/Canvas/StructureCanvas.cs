using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace StructureEditor;

public partial class StructureCanvas
{
    private bool showTurretExplosionRanges;
    private bool showProximityRanges;
    private bool showShieldRanges;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowProximityRanges
    {
        get => showProximityRanges;
        set
        {
            if (showProximityRanges == value) return;
            showProximityRanges = value;
            Invalidate();
        }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowShieldRanges
    {
        get => showShieldRanges;
        set
        {
            if (showShieldRanges == value) return;
            showShieldRanges = value;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ShowTurretExplosionRanges
    {
        get => showTurretExplosionRanges;
        set { if (showTurretExplosionRanges == value) return; showTurretExplosionRanges = value; Invalidate(); }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AsyncTextures { get; set; }
    private bool exportingPreview;
    private Bitmap SceneTexture(string path, int rotation = 2) => AsyncTextures && !exportingPreview ? Catalog.Textures.Request(path, rotation) : Catalog.Textures.Get(path, rotation);
    public void RefreshTextures() { ClearTerrainCache(); ClearPastePreview(); Invalidate(); }
    private int terrainPixels = 32;
    private readonly Dictionary<Point, Bitmap> terrainCache = new();
    private readonly Queue<Point> terrainCacheOrder = new();
    private readonly Dictionary<(string Texture, int Rotation, int X, int Y, int Color, int Size), Bitmap> terrainSprites = new();
    private bool renderingTerrainChunk;
    private List<PlacedItem> solids = new();
    private bool solidDirty = true;
    public int TerrainCacheBuildCount { get; private set; }
    private void ClearTerrainCache()
    {
        foreach (var image in terrainCache.Values) image.Dispose(); terrainCache.Clear(); terrainCacheOrder.Clear();
        foreach (var image in terrainSprites.Values) image.Dispose(); terrainSprites.Clear();
    }
    private void MarkChanged(PlacedItem item)
    {
        if (item.Kind != "Terrain") { solidDirty = true; return; }
        foreach (Point p in new[] { new Point(item.X, item.Z), new(item.X + 1, item.Z), new(item.X - 1, item.Z), new(item.X, item.Z + 1), new(item.X, item.Z - 1) })
            if (terrainCache.Remove(StructureIndex.Chunk(p), out var image)) image.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None; g.InterpolationMode = InterpolationMode.Bilinear;
        if (Catalog == null || Document == null) return;
        DrawScene(g, e.ClipRectangle, drawSolids: false);
        if (ShowGrid && cellSize >= 12)
        {
            using Pen minor = new(Color.FromArgb(45, 112, 130, 137));
            int minX = (int)Math.Floor(-origin.X / cellSize) - 1, maxX = (int)Math.Ceiling((Width - origin.X) / cellSize) + 1;
            int minZ = (int)Math.Floor((origin.Y - Height) / cellSize) - 1, maxZ = (int)Math.Ceiling(origin.Y / cellSize) + 1;
            for (int x = minX; x <= maxX; x++) { float sx = Screen(x + .5f, 0).X; g.DrawLine(minor, sx, 0, sx, Height); }
            for (int z = minZ; z <= maxZ; z++) { float sy = Screen(0, z + .5f).Y; g.DrawLine(minor, 0, sy, Width, sy); }
            using Pen axis = new(Color.FromArgb(95, 136, 175, 179));
            g.DrawLine(axis, origin.X, 0, origin.X, Height); g.DrawLine(axis, 0, origin.Y, Width, origin.Y);
        }
        DrawSolids(g, e.ClipRectangle);
        if (ShowTurretExplosionRanges && VisibleLayers.Contains("Building"))
            foreach (var item in solids)
                if (!removals.Contains(item)) DrawTurretExplosionRange(g, item, e.ClipRectangle);
        if (VisibleLayers.Contains("Building"))
        {
            if (ShowProximityRanges)
                foreach (var item in solids)
                    if (!removals.Contains(item)) DrawProximityRange(g, item, e.ClipRectangle);
            if (ShowShieldRanges)
                foreach (var item in solids)
                    if (!removals.Contains(item)) DrawShieldRange(g, item, e.ClipRectangle);
        }
        if (selection.Count > 0)
        {
            using Pen highlight = new(Color.FromArgb(255, 211, 115), 2);
            if (selection.Count <= 256)
                foreach (var item in selection) { var r = ScreenRect(StructureDocument.Footprint(item, Catalog)); if (r.IntersectsWith(ClientRectangle)) g.DrawRectangle(highlight, r.X, r.Y, r.Width, r.Height); }
            else { var r = ScreenRect(BoundsFor(selection)); g.DrawRectangle(highlight, r.X, r.Y, r.Width, r.Height); }
        }
        if (boxing || rectangle)
        {
            Rectangle cells = Between(gestureStart, Grid(lastMouse)); RectangleF box = ScreenRect(cells);
            Color color = boxing ? Color.FromArgb(106, 199, 225) : Color.FromArgb(137, 218, 166);
            using SolidBrush fill = new(Color.FromArgb(42, color)); using Pen edge = new(color, 2) { DashStyle = DashStyle.Dash };
            g.FillRectangle(fill, box); g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
            TextRenderer.DrawText(g, $"{cells.Width} × {cells.Height}" + (boxing ? " 框选" : $"  ·  {cells.Width * cells.Height} 格"), Font, Point.Round(new(box.X + 5, box.Y + 5)), Color.White, Color.FromArgb(37, 53, 60));
        }
        else if (!panning && Tool is CanvasTool.Place or CanvasTool.Rectangle or CanvasTool.Erase && ClientRectangle.Contains(lastMouse))
        {
            Point p = Grid(lastMouse);
            if (Tool == CanvasTool.Erase)
            {
                var hit = At(p); var r = ScreenRect(hit == null ? new(p.X, p.Y, 1, 1) : StructureDocument.Footprint(hit, Catalog));
                using SolidBrush tint = new(Color.FromArgb(100, Color.IndianRed)); g.FillRectangle(tint, r);
            }
            else if (HasStamp)
            {
                var items = StampAt(p); bool valid = index.CanPlace(items, replaceTerrain: true, replaceSameBuildings: true);
                DrawPasteContents(g, p, e.ClipRectangle);
                if (ShowTurretExplosionRanges && VisibleLayers.Contains("Building"))
                    foreach (var item in items) DrawTurretExplosionRange(g, item, e.ClipRectangle, preview: true);
                if (VisibleLayers.Contains("Building"))
                {
                    if (ShowProximityRanges)
                        foreach (var item in items) DrawProximityRange(g, item, e.ClipRectangle, preview: true);
                    if (ShowShieldRanges)
                        foreach (var item in items) DrawShieldRange(g, item, e.ClipRectangle, preview: true);
                }
                using Pen edge = new(valid ? Color.LightGreen : Color.IndianRed, 2);
                var box = ScreenRect(BoundsFor(items)); g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                TextRenderer.DrawText(g, $"粘贴 {items.Count} 项 · Q/E 旋转", Font,
                    new Point((int)Math.Clamp(box.Left, 0, Math.Max(0, Width - 200)), (int)Math.Clamp(box.Top - Font.Height - 5, 0, Math.Max(0, Height - Font.Height))),
                    valid ? Color.LightGreen : Color.Salmon, Color.FromArgb(29, 34, 37));
            }
            else if (Brush != null && VisibleLayers.Contains(Brush.Kind))
            {
                var ghost = new PlacedItem { Kind = Brush.Kind, DefName = Brush.DefName, Stuff = BrushStuff, X = p.X, Z = p.Y, Rotation = BrushRotation };
                var r = ScreenRect(StructureDocument.Footprint(ghost, Catalog));
                using SolidBrush tint = new(Color.FromArgb(55, CanPlace(ghost) ? Color.LightGreen : Color.IndianRed));
                g.FillRectangle(tint, r); DrawItem(g, ghost, .45f);
                if (ShowTurretExplosionRanges) DrawTurretExplosionRange(g, ghost, e.ClipRectangle, preview: true);
                if (ShowProximityRanges) DrawProximityRange(g, ghost, e.ClipRectangle, preview: true);
                if (ShowShieldRanges) DrawShieldRange(g, ghost, e.ClipRectangle, preview: true);
            }
        }
    }
    private void DrawTurretExplosionRange(Graphics g, PlacedItem item, RectangleF viewport, bool preview = false)
    {
        if (item.Kind != "Building") return;
        float radius = Catalog.Find(item.Kind, item.DefName)?.TurretExplosionRadius ?? 0;
        if (!float.IsFinite(radius) || radius <= 0) return;
        // CompExplosive detonates at Thing.Position, including even-sized buildings.
        PointF center = Screen(item.X, item.Z);
        float pixels = radius * cellSize;
        RectangleF area = new(center.X - pixels, center.Y - pixels, pixels * 2, pixels * 2);
        if (!area.IntersectsWith(viewport)) return;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = preview ? Color.FromArgb(255, 210, 110) : Color.FromArgb(255, 143, 92);
        using SolidBrush fill = new(Color.FromArgb(preview ? 26 : 18, color));
        using Pen edge = new(Color.FromArgb(210, color), 1.5f) { DashStyle = preview ? DashStyle.Dash : DashStyle.Solid };
        g.FillEllipse(fill, area); g.DrawEllipse(edge, area);
        g.DrawLine(edge, center.X - 4, center.Y, center.X + 4, center.Y);
        g.DrawLine(edge, center.X, center.Y - 4, center.X, center.Y + 4);
        g.Restore(state);
        if (cellSize >= 12 && viewport.Contains(center))
            TextRenderer.DrawText(g, $"损毁爆炸 R {radius:0.##} 格", Font,
                Point.Round(new(center.X + 8, center.Y + 8)), color, Color.FromArgb(29, 34, 37));
    }
    private void DrawProximityRange(Graphics g, PlacedItem item, RectangleF viewport, bool preview = false)
    {
        if (item.Kind != "Building") return;
        CatalogItem entry = Catalog.Find(item.Kind, item.DefName);
        if (entry == null) return;
        PointF center = Screen(item.X, item.Z);
        var ranges = new (float Radius, Color Color, string Label)[]
        {
            (entry.ProximityTriggerRadius ?? 0, Color.FromArgb(81, 202, 218), "探测"),
            (entry.DormantWakeRadius ?? 0, Color.FromArgb(239, 171, 90), "唤醒")
        };
        foreach (var range in ranges)
        {
            if (!float.IsFinite(range.Radius) || range.Radius <= 0) continue;
            float pixels = range.Radius * cellSize;
            RectangleF area = new(center.X - pixels, center.Y - pixels, pixels * 2, pixels * 2);
            if (!area.IntersectsWith(viewport)) continue;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int alpha = 170;
            using SolidBrush fill = new(Color.FromArgb(preview ? 12 : 9, range.Color));
            using Pen edge = new(Color.FromArgb(alpha, range.Color), 1.6f)
            {
                DashStyle = preview ? DashStyle.Dash : DashStyle.Solid
            };
            g.FillEllipse(fill, area);
            g.DrawEllipse(edge, area);
            g.Restore(state);
            if (cellSize >= 14 && viewport.Contains(center))
                TextRenderer.DrawText(g, $"{range.Label} R {range.Radius:0.##} 格", Font,
                    new Point((int)center.X + 8, (int)center.Y + (range.Label == "探测" ? 11 : 29)),
                    range.Color, Color.FromArgb(29, 34, 37));
        }
    }
    private void DrawShieldRange(Graphics g, PlacedItem item, RectangleF viewport, bool preview = false)
    {
        if (item.Kind != "Building") return;
        CatalogItem entry = Catalog.Find(item.Kind, item.DefName);
        if (entry?.ShieldRadii == null || entry.ShieldRadii.Count == 0) return;
        PointF center = Screen(item.X, item.Z);
        Color ringColor = Color.FromArgb(133, 182, 245);
        const int pulse = 165;
        foreach (float radius in entry.ShieldRadii)
        {
            if (!float.IsFinite(radius) || radius <= 0) continue;
            float pixels = radius * cellSize;
            RectangleF area = new(center.X - pixels, center.Y - pixels, pixels * 2, pixels * 2);
            if (!area.IntersectsWith(viewport)) continue;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush tint = new(Color.FromArgb(preview ? 14 : 8, ringColor));
            using Pen ring = new(Color.FromArgb(pulse, ringColor), 1.8f)
            {
                DashStyle = preview ? DashStyle.Dash : DashStyle.Solid
            };
            g.FillEllipse(tint, area);
            g.DrawEllipse(ring, area);
            g.Restore(state);
            if (cellSize >= 13 && viewport.Contains(center))
                TextRenderer.DrawText(g, $"护盾 R {radius:0.##} 格", Font,
                    new Point((int)center.X + 8, (int)center.Y + 47),
                    ringColor, Color.FromArgb(29, 34, 37));
        }
    }
    private Bitmap TerrainChunk(Point chunk)
    {
        if (terrainCache.TryGetValue(chunk, out var ready)) return ready;
        int pixels = StructureIndex.ChunkSize * terrainPixels;
        int capacity = Math.Max(8, 32 * 1024 * 1024 / (pixels * pixels * 4));
        while (terrainCache.Count >= capacity && terrainCacheOrder.TryDequeue(out var old))
            if (terrainCache.Remove(old, out var expired)) expired.Dispose();
        Bitmap image = new(pixels, pixels, PixelFormat.Format32bppPArgb);
        using Graphics g = Graphics.FromImage(image); g.InterpolationMode = InterpolationMode.Bilinear;
        float oldSize = cellSize; PointF oldOrigin = origin;
        try
        {
            cellSize = terrainPixels;
            origin = new((-chunk.X * StructureIndex.ChunkSize + .5f) * terrainPixels, ((chunk.Y + 1) * StructureIndex.ChunkSize - .5f) * terrainPixels);
            renderingTerrainChunk = true;
            foreach (var item in index.TerrainChunks[chunk]) DrawItem(g, item);
        }
        finally { renderingTerrainChunk = false; cellSize = oldSize; origin = oldOrigin; }
        TerrainCacheBuildCount++; terrainCache[chunk] = image; terrainCacheOrder.Enqueue(chunk);
        if (terrainCacheOrder.Count > capacity * 4) { terrainCacheOrder.Clear(); foreach (Point key in terrainCache.Keys) terrainCacheOrder.Enqueue(key); }
        return image;
    }
    private void DrawScene(Graphics g, RectangleF viewport, bool drawSolids = true)
    {
        if (VisibleLayers.Contains("Terrain"))
        {
            int quality = cellSize <= 4 ? 4 : cellSize <= 8 ? 8 : cellSize <= 16 ? 16 : 32;
            if (terrainPixels != quality) { ClearTerrainCache(); terrainPixels = quality; }
            Point a = Grid(Point.Round(new(viewport.Left, viewport.Bottom))), b = Grid(Point.Round(new(viewport.Right, viewport.Top)));
            Point min = StructureIndex.Chunk(new(a.X - 1, a.Y - 1)), max = StructureIndex.Chunk(new(b.X + 1, b.Y + 1));
            for (int z = min.Y; z <= max.Y; z++) for (int x = min.X; x <= max.X; x++)
            {
                Point chunk = new(x, z); if (!index.TerrainChunks.ContainsKey(chunk)) continue;
                var dest = ScreenRect(new(x * StructureIndex.ChunkSize, z * StructureIndex.ChunkSize, StructureIndex.ChunkSize, StructureIndex.ChunkSize));
                if (dest.IntersectsWith(viewport)) g.DrawImage(TerrainChunk(chunk), dest);
            }
        }
        if (drawSolids) DrawSolids(g, viewport);
    }
    private void DrawSolids(Graphics g, RectangleF viewport)
    {
        if (solidDirty)
        {
            solids = Document.Items.Where(i => i.Kind != "Terrain" && !removals.Contains(i)).OrderBy(i => i.Kind == "Pawn" ? 1 : 0).ThenByDescending(i => i.Z).ToList();
            solidDirty = false;
        }
        foreach (var item in solids)
        {
            if (!VisibleLayers.Contains(item.Kind) || removals.Contains(item)) continue;
            var entry = Catalog.Find(item.Kind, item.DefName);
            var box = ScreenRect(StructureDocument.Footprint(item, Catalog));
            box.Inflate((Math.Max(entry?.DrawX ?? 1, entry?.TopSize ?? 1) + Math.Abs(entry?.OffsetX ?? 0)) * cellSize,
                (Math.Max(entry?.DrawZ ?? 1, entry?.TopSize ?? 1) + Math.Abs(entry?.OffsetZ ?? 0)) * cellSize);
            if (box.IntersectsWith(viewport))
            {
                DrawItem(g, item);
                if (cellSize >= 12 && entry?.CanBeDormant == true && (item.StartDormant ?? Document.StartDormant))
                {
                    var badge = ScreenRect(StructureDocument.Footprint(item, Catalog));
                    TextRenderer.DrawText(g, "Z", Font, Point.Round(new(badge.Right - 13, badge.Top)), Color.LightSkyBlue, Color.FromArgb(42, 48, 51));
                }
            }
        }
    }
    private void DrawItem(Graphics g, PlacedItem item, float alpha = 1, StructureIndex linkedIndex = null)
    {
        linkedIndex ??= index;
        CatalogItem entry = Catalog.Find(item.Kind, item.DefName);
        Rectangle rect = StructureDocument.Footprint(item, Catalog);
        PointF center = Screen(rect.Left + (rect.Width - 1) / 2f, rect.Top + (rect.Height - 1) / 2f);
        if (entry == null)
        {
            // Unknown defs are marked by a compact graphic instead of a large
            // DefName overlay that looks like the sprite has turned into text.
            RectangleF unknown = ScreenRect(rect);
            using Pen bad = new(Color.FromArgb(216, 117, 105), Math.Max(1f, cellSize / 12f));
            using SolidBrush tint = new(Color.FromArgb(90, 139, 68, 64));
            g.FillRectangle(tint, unknown);
            g.DrawRectangle(bad, unknown.X, unknown.Y, unknown.Width, unknown.Height);
            if (unknown.Width > 9 && unknown.Height > 9)
            {
                g.DrawLine(bad, unknown.Left + 3, unknown.Top + 3, unknown.Right - 3, unknown.Bottom - 3);
                g.DrawLine(bad, unknown.Right - 3, unknown.Top + 3, unknown.Left + 3, unknown.Bottom - 3);
            }
            return;
        }
        string texturePath = Catalog.TextureFor(entry, item.Stuff);
        Bitmap texture = SceneTexture(texturePath, item.Rotation);
        Color color = item.Stuff == null ? entry.Color : Catalog.MaterialColor(item.Stuff, entry.Color);
        if (texture == null)
        {
            // Request() returns null while a texture is being decoded in the
            // background. Never swap the mechanoid for an oversized text label:
            // leave a quiet geometric silhouette until the sprite arrives.
            RectangleF footprint = ScreenRect(rect);
            using SolidBrush fallback = new(Color.FromArgb(
                Math.Clamp((int)(alpha * 170), 0, 255), color));
            using Pen outline = new(Color.FromArgb(
                Math.Clamp((int)(alpha * 150), 0, 255), 24, 34, 39), 1);
            g.FillRectangle(fallback, footprint);
            if (footprint.Width >= 3 && footprint.Height >= 3)
                g.DrawRectangle(outline, footprint.X, footprint.Y,
                    footprint.Width, footprint.Height);
            return;
        }
        Rectangle source = new(0, 0, texture.Width, texture.Height);
        if (entry.Linked && LinkedGraphics.IsAtlas(texture, texturePath))
        {
            var cells = item.Kind == "Terrain" ? linkedIndex.Terrain : linkedIndex.Solid;
            source = LinkedGraphics.Source(texture.Width, texture.Height, LinkedGraphics.Mask(item, Catalog, cells));
        }
        float w = item.Kind == "Terrain" ? 1 : entry.DrawX, h = item.Kind == "Terrain" ? 1 : entry.DrawZ;
        if (renderingTerrainChunk && item.Kind == "Terrain" && alpha == 1)
        {
            var key = (texturePath, item.Rotation, source.X, source.Y, color.ToArgb(), terrainPixels);
            if (!terrainSprites.TryGetValue(key, out Bitmap tile))
            {
                tile = new(terrainPixels, terrainPixels, PixelFormat.Format32bppPArgb);
                using Graphics tileGraphics = Graphics.FromImage(tile); tileGraphics.InterpolationMode = InterpolationMode.Bilinear;
                DrawTinted(tileGraphics, texture, new(0, 0, terrainPixels, terrainPixels), source, color, 1);
                terrainSprites[key] = tile;
            }
            PointF topLeft = Screen(item.X - .5f, item.Z + .5f);
            g.DrawImageUnscaled(tile, Point.Round(topLeft)); return;
        }
        GraphicsState state = g.Save();
        g.TranslateTransform(center.X + entry.OffsetX * cellSize, center.Y - entry.OffsetZ * cellSize);
        if (entry.GraphicClass != "Graphic_Multi" && entry.Rotatable && !entry.Linked) g.RotateTransform(item.Rotation * 90);
        DrawTinted(g, texture, new(-w * cellSize / 2, -h * cellSize / 2, w * cellSize, h * cellSize), source, color, alpha); g.Restore(state);
        if (entry.Linked && entry.LinkType == "CornerFiller")
        {
            // Native Graphic_LinkedCornerFiller covers only fully surrounded corners.
            int corners = LinkedGraphics.CornerMask(item, Catalog, linkedIndex.Solid), bit = 1;
            Color sample = texture.GetPixel(source.X + source.Width / 2, source.Y + (int)(source.Height * .4f));
            using SolidBrush fill = new(Color.FromArgb((int)(sample.A * alpha), sample.R * color.R / 255, sample.G * color.G / 255, sample.B * color.B / 255));
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
            {
                if ((corners & bit) != 0)
                    g.FillRectangle(fill, center.X + (dx * .25f - .25f) * cellSize, center.Y - (dz * .25f + .09f + .25f) * cellSize, .5f * cellSize, .5f * cellSize);
                bit <<= 1;
            }
        }
        if (!string.IsNullOrEmpty(entry.TopTexture) && entry.TopSize > 0)
        {
            Bitmap top = SceneTexture(entry.TopTexture);
            if (top != null)
            {
                state = g.Save(); g.TranslateTransform(center.X, center.Y); g.RotateTransform(item.Rotation * 90);
                float size = entry.TopSize * cellSize;
                DrawTinted(g, top, new(-size / 2, -size / 2, size, size), new(0, 0, top.Width, top.Height), Color.White, alpha); g.Restore(state);
            }
        }
    }
    public static void DrawTinted(Graphics g, Bitmap image, RectangleF dest, Rectangle source, Color color, float alpha)
    {
        if (color.ToArgb() == Color.White.ToArgb() && alpha == 1) { g.DrawImage(image, dest, source, GraphicsUnit.Pixel); return; }
        using ImageAttributes attr = new();
        attr.SetColorMatrix(new ColorMatrix(new[] { new[] { color.R / 255f, 0f, 0f, 0f, 0f }, new[] { 0f, color.G / 255f, 0f, 0f, 0f }, new[] { 0f, 0f, color.B / 255f, 0f, 0f }, new[] { 0f, 0f, 0f, alpha, 0f }, new[] { 0f, 0f, 0f, 0f, 1f } }));
        g.DrawImage(image, Rectangle.Round(dest), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attr);
    }
    public Bitmap ExportPreview(int maxDimension = 2048)
    {
        if (Document.Items.Count == 0) throw new InvalidOperationException("结构为空。");
        Rectangle bounds = BoundsFor(Document.Items); bounds.Inflate(3, 3);
        if (bounds.Width > 512 || bounds.Height > 512) throw new InvalidOperationException("结构范围超过 512 格，无法导出整张预览；请缩小结构范围。");
        int size = Math.Clamp(maxDimension / Math.Max(bounds.Width, bounds.Height), 2, 64);
        Bitmap output = new(bounds.Width * size, bounds.Height * size);
        using Graphics g = Graphics.FromImage(output); g.Clear(Color.FromArgb(42, 48, 51)); g.InterpolationMode = InterpolationMode.Bilinear;
        float savedSize = cellSize; PointF savedOrigin = origin;
        try { exportingPreview = true; ClearTerrainCache(); cellSize = size; origin = new((-bounds.Left + .5f) * size, (bounds.Bottom - .5f) * size); DrawScene(g, new(0, 0, output.Width, output.Height)); }
        finally { exportingPreview = false; cellSize = savedSize; origin = savedOrigin; ClearTerrainCache(); }
        return output;
    }
    protected override void Dispose(bool disposing) { if (disposing) { ClearTerrainCache(); ClearPastePreview(); } base.Dispose(disposing); }
}
