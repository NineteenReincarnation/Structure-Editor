using System.Text.Json;
using System.Xml.Linq;

namespace StructureEditor;

// Structure library listings need only DefName/Label, not full placed-item data.
// Cache each file's small header until its size or modification time changes.
internal sealed class MP_StructureHeaderIndex
{
    private sealed record Entry(long Length, DateTime ModifiedUtc,
        IReadOnlyList<(string Id, string Label)> Headers);
    private readonly Dictionary<string, Entry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    internal int ParseCount { get; private set; }

    internal IReadOnlyList<(string Id, string Label)> Read(string path)
    {
        FileInfo file = new(path);
        if (!file.Exists) return Array.Empty<(string Id, string Label)>();
        if (entries.TryGetValue(file.FullName, out Entry cached)
            && cached.Length == file.Length
            && cached.ModifiedUtc == file.LastWriteTimeUtc)
            return cached.Headers;

        // An incomplete XML/JSON file must not be cached: a later refresh
        // retries it after the editor or another program finishes saving.
        IReadOnlyList<(string Id, string Label)> headers = Parse(file.FullName);
        ParseCount++;
        entries[file.FullName] = new Entry(file.Length, file.LastWriteTimeUtc, headers);
        return headers;
    }

    internal void Prune(IEnumerable<string> activeFiles)
    {
        HashSet<string> live = new(activeFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string path in entries.Keys.Where(path => !live.Contains(path)).ToArray())
            entries.Remove(path);
    }

    private static IReadOnlyList<(string Id, string Label)> Parse(string file)
    {
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Most saved projects are UTF-8. Retain File.ReadAllText's
            // BOM-aware behavior for legacy UTF-16/UTF-32 JSON documents.
            using FileStream input = File.OpenRead(file);
            Span<byte> header = stackalloc byte[4];
            int count = input.Read(header);
            input.Position = 0;
            bool unicode = count >= 2 &&
                ((header[0] == 0xFF && header[1] == 0xFE)
                || (header[0] == 0xFE && header[1] == 0xFF)
                || (count >= 4 && header[0] == 0 && header[1] == 0
                    && header[2] == 0xFE && header[3] == 0xFF));
            using JsonDocument json = unicode
                ? JsonDocument.Parse(File.ReadAllText(file))
                : JsonDocument.Parse(input);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("DefName", out JsonElement idElement)
                || !json.RootElement.TryGetProperty("Items", out _)
                || idElement.ValueKind != JsonValueKind.String)
                return Array.Empty<(string Id, string Label)>();
            string id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
                return Array.Empty<(string Id, string Label)>();
            string label = json.RootElement.TryGetProperty("Label", out JsonElement labelElement)
                && labelElement.ValueKind == JsonValueKind.String
                ? labelElement.GetString() : null;
            return [(id, string.IsNullOrWhiteSpace(label) ? id : label)];
        }
        XElement root = XDocument.Load(file).Root;
        var definitions = root?.Name.LocalName == "Defs" ? root.Elements()
            : root == null ? Enumerable.Empty<XElement>() : new[] { root };
        return definitions
            .Where(e => e.Name.LocalName is "MechProtocol.MP_StructureDef"
                or "StructureFramework.StructureDef"
                or "StructureEditor.StructureDef" or "PrefabDef")
            .Where(e => !string.Equals((string)e.Attribute("Abstract"), "true",
                StringComparison.OrdinalIgnoreCase))
            .Select(e => ((string)e.Element("defName"), (string)e.Element("label")))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Item1))
            .Select(pair => (Id: pair.Item1, Label: pair.Item2 ?? pair.Item1))
            .ToArray();
    }
}
