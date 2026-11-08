namespace StructureEditor;

internal static class LinkedGraphics
{

    private sealed class MP_AtlasState
    {
        internal bool IsAtlas { get; }
        internal MP_AtlasState(bool isAtlas) => IsAtlas = isAtlas;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, MP_AtlasState>
        MP_atlasCache = new();

    // Linked defs can use either a true 4x4 game atlas or an ordinary sprite.
    // Sampling a tile from the latter cuts curtains and similar buildings apart.
    internal static bool IsAtlas(Bitmap image, string texturePath)
    {
        if (image == null || image.Width < 64 || image.Height < 64) return false;
        return MP_atlasCache.GetValue(image, bmp =>
            new MP_AtlasState(DetectAtlas(bmp, texturePath))).IsAtlas;
    }

    private static bool DetectAtlas(Bitmap image, string texturePath)
    {
        string basename = Path.GetFileName((texturePath ?? "").Replace('\\', '/'));
        if (basename.Contains("Atlas", StringComparison.OrdinalIgnoreCase))
            return true;
        if (image.Width % 16 != 0 || image.Height % 16 != 0)
            return false;

        // Native 16-state atlases separate their tiles with alpha gutters.
        int transparent = 0, tested = 0, occupied = 0;
        foreach (int row in new[] { 1, 3, 5, 7 })
        {
            int y = image.Height * row / 8;
            foreach (int seam in new[] { 1, 2, 3 })
            {
                if (image.GetPixel(image.Width * seam / 4, y).A <= 16) transparent++;
                tested++;
            }
        }
        foreach (int col in new[] { 1, 3, 5, 7 })
        {
            int x = image.Width * col / 8;
            foreach (int seam in new[] { 1, 2, 3 })
            {
                if (image.GetPixel(x, image.Height * seam / 4).A <= 16) transparent++;
                tested++;
            }
        }
        foreach (int x in new[] { 1, 3, 5, 7 })
            foreach (int y in new[] { 1, 3, 5, 7 })
                if (image.GetPixel(image.Width * x / 8, image.Height * y / 8).A > 32)
                    occupied++;
        return transparent >= tested * 3 / 4 && occupied >= 5;
    }

    // MaterialAtlasPool samples the inner 3/16 of a 1/4 atlas tile: 1/32 padding on each side.
    internal static Rectangle Source(int width, int height, int mask) => new(
        mask % 4 * width / 4 + width / 32, (3 - mask / 4) * height / 4 + height / 32,
        width * 3 / 16, height * 3 / 16);

    internal static int Mask(PlacedItem item, Catalog catalog, IReadOnlyDictionary<Point, PlacedItem> cells)
    {
        CatalogItem parent = catalog.Find(item.Kind, item.DefName);
        int mask = 0;
        foreach (var (dx, dz, bit) in new[] { (0, 1, 1), (1, 0, 2), (0, -1, 4), (-1, 0, 8) })
        {
            if (Links(item, parent, catalog, cells, dx, dz)) mask |= bit;
        }
        return mask;
    }
    private static bool Links(PlacedItem item, CatalogItem parent, Catalog catalog, IReadOnlyDictionary<Point, PlacedItem> cells, int dx, int dz)
    {
        if (!cells.TryGetValue(new(item.X + dx, item.Z + dz), out var neighbor)) return false;
        CatalogItem other = catalog.Find(neighbor.Kind, neighbor.DefName);
        return parent?.LinkFlags.Count > 0
            ? other != null && parent.LinkFlags.Intersect(other.LinkFlags).Any(f => f != "MapEdge")
            : neighbor.DefName == item.DefName;
    }
    internal static int CornerMask(PlacedItem item, Catalog catalog, IReadOnlyDictionary<Point, PlacedItem> cells)
    {
        CatalogItem parent = catalog.Find(item.Kind, item.DefName);
        int mask = 0, bit = 1;
        foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
        {
            if (Links(item, parent, catalog, cells, dx, dz) && Links(item, parent, catalog, cells, dx, 0) && Links(item, parent, catalog, cells, 0, dz)) mask |= bit;
            bit <<= 1;
        }
        return mask;
    }
}
