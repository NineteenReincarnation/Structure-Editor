namespace StructureEditor;

// Ordered catalog result selection. Accepts plain data only; no WinForms,
// Program.Paths, persisted settings, or mutations of the source catalog.
internal static class MP_CatalogPaletteQuery
{
    internal static CatalogItem[] Select(IEnumerable<CatalogItem> candidates,
        int scope, ICollection<string> favorites, IList<string> recentAssets)
    {
        return candidates
            .Where(item => CatalogCollections.Matches(item, scope, favorites, recentAssets))
            .OrderBy(item => scope == 3
                ? recentAssets.IndexOf(CatalogCollections.Key(item))
                : CatalogGrouping.Order(item.Group))
            .ThenBy(item => item.Pack == "Core" ? 0 : 1)
            .ThenBy(item => item.Label)
            .ToArray();
    }
}
