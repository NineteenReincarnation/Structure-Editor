namespace StructureEditor;

// Offline contract: catalog order and scopes must stay identical after
// decoupling the palette query from WinForms and Program.Paths.
internal static class MP_CatalogPaletteQueryChecks
{
    internal static void Run()
    {
        var wall = new CatalogItem
        {
            Kind = "Building", DefName = "Wall", Label = "墙",
            Pack = "Core", Group = "防御设施"
        };
        var door = new CatalogItem
        {
            Kind = "Building", DefName = "Autodoor", Label = "自动门",
            Pack = "Royalty", Group = "防御设施"
        };
        var mod = new CatalogItem
        {
            Kind = "Building", DefName = "MP_TestWall", Label = "扩展墙",
            Pack = "MyMod", Group = "防御设施", IsMod = true
        };
        var favorites = new List<string> { CatalogCollections.Key(door) };
        var recent = new List<string>
        {
            CatalogCollections.Key(door), CatalogCollections.Key(wall)
        };
        CatalogItem[] source = [wall, mod, door];

        var favoritesOnly = MP_CatalogPaletteQuery.Select(source, 2, favorites, recent);
        if (favoritesOnly.Length != 1 || !ReferenceEquals(favoritesOnly[0], door))
            throw new InvalidOperationException("Favorite scope no longer matches.");

        var recentOnly = MP_CatalogPaletteQuery.Select(source, 3, favorites, recent);
        if (recentOnly.Length != 2 || !ReferenceEquals(recentOnly[0], door)
            || !ReferenceEquals(recentOnly[1], wall))
            throw new InvalidOperationException("Recent-asset order changed.");

        var modsOnly = MP_CatalogPaletteQuery.Select(source, 5, favorites, recent);
        var vanillaOnly = MP_CatalogPaletteQuery.Select(source, 6, favorites, recent);
        if (modsOnly.Length != 1 || !ReferenceEquals(modsOnly[0], mod)
            || vanillaOnly.Length != 2 || vanillaOnly.Contains(mod))
            throw new InvalidOperationException("Source scope filtering changed.");

        var commonOnly = MP_CatalogPaletteQuery.Select(source, 1, favorites, recent);
        if (commonOnly.Length != 2 || commonOnly.Contains(mod))
            throw new InvalidOperationException("Common-asset scope changed.");

        if (MP_CatalogPaletteQuery.Select(source, 0, favorites, recent).Length != 3)
            throw new InvalidOperationException("Unfiltered catalog lost entries.");
    }
}
