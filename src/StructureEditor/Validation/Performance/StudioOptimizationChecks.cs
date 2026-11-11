using System.Diagnostics;

namespace StructureEditor;

internal static class StudioOptimizationChecks
{
    public static void Run(string folder, Action<bool, string> need)
    {
        using var catalog = new Catalog();
        for (int i = 0; i < 10000; i++) catalog.Items.Add(new CatalogItem { Kind = i % 3 == 0 ? "Pawn" : "Building", DefName = "Forge_" + i, Label = "测试建筑 " + i, Pack = "Pack" + i % 8, Group = "Group" + i % 5, PackageId = "tests.pack" + i % 8 });
        catalog.Materials.Add(new() { DefName = "Steel", Categories = new() { "Metallic" }, Color = Color.Gray });
        catalog.RebuildIndexes();
        IEnumerable<CatalogItem> Legacy() => catalog.Items.Where(i => i.Kind == "Building" && i.Pack == "Pack0" && i.Group == "Group0" && (i.Label + " " + i.DefName + " " + i.Group + " " + i.ClusterWikiName + " " + i.ClusterGroup).Contains("Forge_", StringComparison.OrdinalIgnoreCase));
        IEnumerable<CatalogItem> Indexed() => catalog.Query("Building", "Pack0", "Group0", "Forge_");
        need(Legacy().SequenceEqual(Indexed()), "Narrowed indexed queries preserve the original catalog order and matches");
        need(catalog.Query(null, null, null, "tests.pack0").Count() == 1250, "Search can find player mod package IDs as well as asset labels");
        need(!catalog.Query("UnknownKind", null, null, null).Any(), "A missing indexed filter returns an empty result");
        need(catalog.MaterialColor("Missing", Color.Red) == Color.Red && catalog.MaterialColor("Missing", Color.Blue) == Color.Blue, "Missing materials must not cache another object's fallback color");
        var wall = catalog.Find("Building", "Forge_1"); wall.MaterialCategories.Add("Metallic");
        need(catalog.MaterialsFor(wall).Single().DefName == "Steel" && ReferenceEquals(catalog.MaterialsFor(wall), catalog.MaterialsFor(wall)), "Compatible material lookups reuse a stable cached result");
        catalog.Items.Add(new CatalogItem { Kind = "Building", DefName = "LateAddition", Label = "Added", Pack = "Pack0", Group = "Group0" });
        need(catalog.Find("Building", "LateAddition") != null, "Batch additions automatically invalidate catalog lookup indexes");
        _ = Legacy().Count(); _ = Indexed().Count();
        (double Ms, long Bytes) Measure(Func<IEnumerable<CatalogItem>> query)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) _ = query().Count();
            watch.Stop(); return (watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        var old = Measure(Legacy); var indexed = Measure(Indexed);
        File.WriteAllText(Path.Combine(folder, "search-performance.txt"), $"10000 assets / 1000 identical filtered queries. Legacy scan: {old.Ms:F2} ms, {old.Bytes} bytes. Indexed: {indexed.Ms:F2} ms, {indexed.Bytes} bytes. Timings are observations, not assertions.\n");
    }
}
