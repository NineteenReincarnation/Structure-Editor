using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace StructureEditor;

// A derived-data cache, never a replacement for the source XML or the user's
// editable structure JSON. Every catalog input has a path/size/mtime stamp.
// A missing or corrupt cache simply falls back to the normal XML loader.
internal static class MP_CatalogSnapshotCache
{
    private const int Schema = 4;
    private static readonly JsonSerializerOptions Json = CreateOptions();

    internal sealed class Snapshot
    {
        public int Version { get; set; }
        public string Fingerprint { get; set; }
        public List<CatalogItem> Items { get; set; } = new();
        public List<MaterialItem> Materials { get; set; } = new();
        public Dictionary<string, string> Structures { get; set; } = new();
        public Dictionary<string, string> Palettes { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool RuntimeSnapshotUsed { get; set; }
    }

    private sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => Color.FromArgb(reader.GetInt32());
        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.ToArgb());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
        options.Converters.Add(new ColorConverter());
        return options;
    }

    private static string CacheFile(StudioPaths paths, string directory)
    {
        string game = Path.GetFullPath(paths.GameRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game)))[..20];
        return Path.Combine(directory ?? Path.Combine(Program.DataDirectory, "CatalogCache"), hash + ".json");
    }

    private static void Stamp(StringBuilder output, string path)
    {
        FileInfo file = new(path);
        if (!file.Exists) return;
        output.Append(file.FullName).Append('\t')
            .Append(file.Length).Append('\t')
            .Append(file.LastWriteTimeUtc.Ticks).Append('\n');
    }

    private static void StampFiles(StringBuilder output, string folder, string pattern)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Stamp(output, file);
    }

    // Return null on inaccessible metadata; in that case loading the live XML is
    // safer than trusting a cache assembled from an incomplete source list.
    internal static string Fingerprint(StudioPaths paths, IReadOnlyList<CatalogSource> sources,
        CancellationToken cancellation = default)
    {
        try
        {
            StringBuilder fingerprint = new();
            fingerprint.Append("CatalogCache-v").Append(Schema)
                .Append('|').Append(paths.UseRuntimeCatalog)
                .Append('|').Append(paths.AutoLoadActiveMods).Append('\n');
            string config = string.IsNullOrWhiteSpace(paths.ModsConfigPath)
                ? ModSources.DefaultConfig : paths.ModsConfigPath;
            Stamp(fingerprint, config);
            string runtime = Path.Combine(Path.GetDirectoryName(config)!, "StructureFramework", "catalog.json");
            Stamp(fingerprint, runtime);
            var packages = new HashSet<string>(sources.Select(s => s.PackageId), StringComparer.OrdinalIgnoreCase);
            foreach (CatalogSource source in sources)
            {
                cancellation.ThrowIfCancellationRequested();
                fingerprint.Append(source.PackageId).Append('|').Append(source.Name)
                    .Append('|').Append(source.Root).Append('|').Append(source.Official).Append('\n');
                Stamp(fingerprint, Path.Combine(source.Root, "About", "About.xml"));
                string folderConfig = Path.Combine(source.Root, "LoadFolders.xml");
                Stamp(fingerprint, folderConfig);
                List<string> parts = new() { source.Root };
                if (File.Exists(folderConfig))
                {
                    XElement version = XDocument.Load(folderConfig).Root?.Element("v1.7");
                    if (version != null)
                        parts = version.Elements("li")
                            .Where(e => e.Attribute("IfModActive") == null ||
                                packages.Contains((string)e.Attribute("IfModActive")))
                            .Select(e => Path.GetFullPath(Path.Combine(source.Root,
                                e.Value.Trim().TrimStart('/', '\\')))).ToList();
                }
                else if (Directory.Exists(Path.Combine(source.Root, "1.7")))
                    parts.Add(Path.Combine(source.Root, "1.7"));
                foreach (string part in parts.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellation.ThrowIfCancellationRequested();
                    fingerprint.Append("PART:").Append(part).Append('\n');
                    StampFiles(fingerprint, Path.Combine(part, "Defs"), "*.xml");
                    StampFiles(fingerprint, Path.Combine(part, "Languages",
                        "ChineseSimplified", "DefInjected"), "*.xml");
                    Stamp(fingerprint, Path.Combine(part, "Languages",
                        "ChineseSimplified (简体中文).tar"));
                }
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or System.Xml.XmlException or InvalidDataException)
        {
            return null;
        }
    }

    internal static Snapshot Read(StudioPaths paths, string directory, string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint)) return null;
        try
        {
            string file = CacheFile(paths, directory);
            if (!File.Exists(file)) return null;
            using FileStream stream = File.OpenRead(file);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(stream, Json);
            return snapshot?.Version == Schema && snapshot.Fingerprint == fingerprint
                && snapshot.Items?.Count > 0 && snapshot.Materials != null
                && snapshot.Structures != null && snapshot.Palettes != null
                ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    internal static void Restore(Catalog catalog, Snapshot cached)
    {
        catalog.Items.AddRange(cached.Items);
        catalog.Materials.AddRange(cached.Materials);
        foreach (var pair in cached.Structures) catalog.StructureDefinitions[pair.Key] = XElement.Parse(pair.Value);
        foreach (var pair in cached.Palettes) catalog.PaletteDefinitions[pair.Key] = XElement.Parse(pair.Value);
        catalog.Warnings.AddRange(cached.Warnings);
        catalog.RebuildIndexes();
    }

    internal static void Write(StudioPaths paths, string directory, string fingerprint,
        Catalog catalog, int existingWarningCount, bool runtimeSnapshotUsed)
    {
        if (string.IsNullOrEmpty(fingerprint) || catalog.Items.Count == 0) return;
        string target = CacheFile(paths, directory);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Snapshot snapshot = new()
            {
                Version = Schema, Fingerprint = fingerprint,
                Items = catalog.Items,
                Materials = catalog.Materials,
                Structures = catalog.StructureDefinitions.ToDictionary(k => k.Key,
                    v => v.Value.ToString(SaveOptions.DisableFormatting)),
                Palettes = catalog.PaletteDefinitions.ToDictionary(k => k.Key,
                    v => v.Value.ToString(SaveOptions.DisableFormatting)),
                Warnings = catalog.Warnings.Skip(existingWarningCount).ToList(),
                RuntimeSnapshotUsed = runtimeSnapshotUsed
            };
            using (FileStream stream = File.Create(temp)) JsonSerializer.Serialize(stream, snapshot, Json);
            File.Move(temp, target, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or JsonException)
        {
            // Caching is opportunistic. A full catalog has already been loaded.
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }
}
