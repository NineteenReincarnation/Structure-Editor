namespace StructureEditor;

// Cell lookups serve painting, hit testing and linked graphics without scanning the document.
internal sealed class StructureIndex
{
    public const int ChunkSize = 16;
    public readonly Dictionary<Point, PlacedItem> Solid = new(), Terrain = new();
    public readonly Dictionary<Point, HashSet<PlacedItem>> TerrainChunks = new();
    private readonly Catalog catalog;
    public StructureIndex(Catalog catalog) => this.catalog = catalog;
    public static Point Chunk(Point cell) => new((int)Math.Floor(cell.X / (double)ChunkSize), (int)Math.Floor(cell.Y / (double)ChunkSize));
    public void Add(PlacedItem item)
    {
        if (item.Kind == "Terrain")
        {
            Point cell = new(item.X, item.Z), chunk = Chunk(cell);
            Terrain[cell] = item;
            if (!TerrainChunks.TryGetValue(chunk, out var items)) TerrainChunks[chunk] = items = new();
            items.Add(item);
        }
        else foreach (Point cell in StructureDocument.Footprint(item, catalog).GetCells()) Solid[cell] = item;
    }
    public void Remove(PlacedItem item)
    {
        if (item.Kind == "Terrain")
        {
            Point cell = new(item.X, item.Z), chunk = Chunk(cell);
            if (Terrain.GetValueOrDefault(cell) == item) Terrain.Remove(cell);
            if (TerrainChunks.TryGetValue(chunk, out var items)) { items.Remove(item); if (items.Count == 0) TerrainChunks.Remove(chunk); }
        }
        else foreach (Point cell in StructureDocument.Footprint(item, catalog).GetCells())
            if (Solid.GetValueOrDefault(cell) == item) Solid.Remove(cell);
    }
    public PlacedItem Hit(Point cell, string layer)
    {
        if (layer != "Terrain" && Solid.TryGetValue(cell, out var solid) && (layer == null || solid.Kind == layer)) return solid;
        return layer == null || layer == "Terrain" ? Terrain.GetValueOrDefault(cell) : null;
    }
    private static bool SameBuilding(PlacedItem candidate, PlacedItem other) => candidate.Kind == "Building" && other.Kind == "Building" && candidate.DefName == other.DefName;
    public HashSet<PlacedItem> ReplacedBuildings(IEnumerable<PlacedItem> candidates)
    {
        var replaced = new HashSet<PlacedItem>();
        foreach (var item in candidates)
            if (item.Kind == "Building")
                foreach (Point cell in StructureDocument.Footprint(item, catalog).GetCells())
                    if (Solid.TryGetValue(cell, out var old) && SameBuilding(item, old)) replaced.Add(old);
        return replaced;
    }
    public bool CanPlace(IEnumerable<PlacedItem> candidates, HashSet<PlacedItem> ignored = null, bool replaceTerrain = false, bool replaceSameBuildings = false)
    {
        var replaced = replaceSameBuildings ? ReplacedBuildings(candidates) : null;
        HashSet<Point> occupied = new(), floors = new();
        foreach (PlacedItem item in candidates)
        {
            if (item.Kind == "Terrain")
            {
                Point p = new(item.X, item.Z);
                if (!floors.Add(p)) return false;
                if (!replaceTerrain && Terrain.TryGetValue(p, out var floor) && ignored?.Contains(floor) != true) return false;
            }
            else foreach (Point p in StructureDocument.Footprint(item, catalog).GetCells())
            {
                if (!occupied.Add(p)) return false;
                if (Solid.TryGetValue(p, out var other) && ignored?.Contains(other) != true && replaced?.Contains(other) != true) return false;
            }
        }
        return true;
    }
}
