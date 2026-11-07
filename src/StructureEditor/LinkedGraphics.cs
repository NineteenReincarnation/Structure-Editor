namespace StructureEditor;

internal static class LinkedGraphics
{
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
