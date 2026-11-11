namespace StructureEditor;

// Offline cache correctness checks: metadata refresh must preserve the
// original JSON/XML file discovery semantics and retry incomplete writes.
internal static class MP_StructureHeaderIndexChecks
{
    internal static void Run(string root)
    {
        var index = new MP_StructureHeaderIndex();
        string path = Path.Combine(root, "Indexed.structure.json");
        DateTime original = DateTime.UtcNow.AddMinutes(-8);
        File.WriteAllText(path,
            "{\"DefName\":\"MP_Room_A\",\"Label\":\"One\",\"Items\":[{\"Kind\":\"Building\"}]}");
        File.SetLastWriteTimeUtc(path, original);
        var first = index.Read(path);
        var repeated = index.Read(path);
        if (first.Count != 1 || first[0].Id != "MP_Room_A"
            || !ReferenceEquals(first, repeated) || index.ParseCount != 1)
            throw new InvalidOperationException("Unchanged structures must reuse cached headers.");

        File.WriteAllText(path,
            "{\"DefName\":\"MP_Room_B\",\"Label\":\"Two\",\"Items\":[{\"Kind\":\"Building\"}]}");
        File.SetLastWriteTimeUtc(path, original.AddMinutes(1));
        if (index.Read(path).Single().Id != "MP_Room_B" || index.ParseCount != 2)
            throw new InvalidOperationException("Modified structure header was not reloaded.");

        string xml = Path.Combine(root, "Indexed.xml");
        File.WriteAllText(xml, "<Defs>" +
            "<StructureFramework.StructureDef><defName>MP_One</defName><label>First</label></StructureFramework.StructureDef>" +
            "<PrefabDef Abstract=\"true\"><defName>MP_Excluded</defName></PrefabDef>" +
            "<PrefabDef><defName>MP_Two</defName></PrefabDef></Defs>");
        var xmlHeaders = index.Read(xml);
        if (xmlHeaders.Count != 2 || xmlHeaders[0].Label != "First"
            || xmlHeaders[1].Id != "MP_Two" || xmlHeaders[1].Label != "MP_Two")
            throw new InvalidOperationException("XML definition selection changed.");

        // Old projects may be saved as BOM-marked UTF-16 JSON.
        string unicodePath = Path.Combine(root, "Unicode.structure.json");
        File.WriteAllText(unicodePath,
            "{\"DefName\":\"MP_Unicode\",\"Label\":\"Legacy\",\"Items\":[]}",
            System.Text.Encoding.Unicode);
        if (index.Read(unicodePath).Single().Id != "MP_Unicode")
            throw new InvalidOperationException("Legacy UTF-16 structure indexing changed.");

        // Failed parsing must not poison the cache.
        File.WriteAllText(path, "{incomplete");
        File.SetLastWriteTimeUtc(path, original.AddMinutes(2));
        bool failed = false;
        try { index.Read(path); }
        catch (System.Text.Json.JsonException) { failed = true; }
        if (!failed) throw new InvalidOperationException("Incomplete JSON was accepted.");
        File.WriteAllText(path, "{\"DefName\":\"MP_Recovered\",\"Items\":[]}");
        File.SetLastWriteTimeUtc(path, original.AddMinutes(3));
        if (index.Read(path).Single().Id != "MP_Recovered")
            throw new InvalidOperationException("Failed structure header did not recover.");

        index.Prune([path]);
        if (index.Read(xml).Count != 2)
            throw new InvalidOperationException("Pruned structure must reparse on rediscovery.");
    }
}
