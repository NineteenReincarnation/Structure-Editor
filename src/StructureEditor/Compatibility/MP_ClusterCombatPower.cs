namespace StructureEditor.Compatibility;

// Cluster CP rules are independent from WinForms: both the library and
// the structure inspector use the same eligibility and cost calculations.
internal static class MP_ClusterCombatPower
{
    internal sealed record Row(CatalogItem Item, int Count)
    {
        internal float Total => Count * Item.ClusterCombatPower.GetValueOrDefault();
    }

    internal static bool Eligible(CatalogItem item) =>
        item?.Kind == "Building" &&
        !string.IsNullOrEmpty(item.ClusterGroup) &&
        item.ClusterGroup != "防御设施" &&
        item.DefName is not ("Wall" or "Barricade");

    internal static CatalogItem[] Search(Catalog catalog, string search)
    {
        if (catalog == null) return Array.Empty<CatalogItem>();
        search = search?.Trim() ?? "";
        return catalog.Items
            .Where(Eligible)
            .Where(i => search.Length == 0 ||
                (i.Label?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                i.DefName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (i.ClusterWikiName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            .OrderByDescending(i => i.ClusterCombatPower.HasValue)
            .ThenByDescending(i => i.ClusterCombatPower.GetValueOrDefault())
            .ThenBy(i => i.Label, StringComparer.CurrentCulture)
            .ToArray();
    }

    internal static Row[] Calculate(Catalog catalog, StructureDocument document)
    {
        if (catalog == null || document?.Items == null) return Array.Empty<Row>();
        return document.Items
            .Where(i => i.Kind == "Building" && !string.IsNullOrEmpty(i.DefName))
            .GroupBy(i => i.DefName, StringComparer.Ordinal)
            .Select(group => new Row(catalog.Find("Building", group.Key), group.Count()))
            .Where(line => Eligible(line.Item) &&
                line.Item.ClusterCombatPower.GetValueOrDefault() > 0)
            .OrderByDescending(line => line.Total)
            .ThenBy(line => line.Item.Label, StringComparer.CurrentCulture)
            .ToArray();
    }

    internal static float Total(IEnumerable<Row> rows) => rows.Sum(row => row.Total);
}
