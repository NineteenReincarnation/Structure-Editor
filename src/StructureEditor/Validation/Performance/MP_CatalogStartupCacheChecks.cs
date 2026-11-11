using System.IO.Compression;
using System.Reflection;
using System.Drawing.Imaging;

namespace StructureEditor;

// A deterministic, game-installation-independent test for fast subsequent
// launches. Invoked on a Windows Actions runner before app/ is published.
internal static class MP_CatalogStartupCacheChecks
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "MP_CatalogStartupChecks", Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "FakeGame");
        string defs = Path.Combine(game, "Data", "Core", "Defs");
        string cache = Path.Combine(root, "Cache");
        string xml = Path.Combine(defs, "Walls.xml");
        Directory.CreateDirectory(defs);
        void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            void Write(string def) => File.WriteAllText(xml,
                "<Defs><ThingDef><defName>" + def + "</defName>" +
                "<category>Building</category><label>Cached Wall</label>" +
                "<graphicData><texPath>Things/Buildings/TestWall</texPath></graphicData>" +
                "</ThingDef><StructureFramework.StructureDef>" +
                "<defName>MP_CacheStructure</defName><label>Cache Structure</label>" +
                "</StructureFramework.StructureDef></Defs>");
            Write("Wall");
            StudioPaths paths = new()
            {
                GameRoot = game, AutoLoadActiveMods = false, UseRuntimeCatalog = false
            };
            using (Catalog first = new())
            {
                first.Load(paths, cacheDirectory: cache);
                Need(!first.MP_UsedWarmCache, "First catalog load must parse its XML sources.");
                Need(first.Find("Building", "Wall") != null,
                    "Cold catalog resolves test building.");
                Need(first.StructureDefinitions.ContainsKey("MP_CacheStructure"),
                    "Cold catalog loads framework structure definitions.");
            }
            Need(Directory.EnumerateFiles(cache, "*.json").Any(),
                "Cold catalog must write an atomic snapshot.");
            using (Catalog second = new())
            {
                second.Load(paths, cacheDirectory: cache);
                Need(second.MP_UsedWarmCache, "Unchanged game XML must hit the warm cache.");
                Need(second.Find("Building", "Wall")?.Label == "Cached Wall",
                    "Warm cache must preserve catalog labels.");
                Need(second.StructureDefinitions.ContainsKey("MP_CacheStructure"),
                    "Warm cache preserves referenced framework structures.");
            }
            Write("Wall_Revised");
            using (Catalog third = new())
            {
                third.Load(paths, cacheDirectory: cache);
                Need(!third.MP_UsedWarmCache,
                    "Modified game XML must invalidate the cache.");
                Need(third.Find("Building", "Wall_Revised") != null &&
                    third.Find("Building", "Wall") == null,
                    "After invalidation, new definitions replace old cached ones.");
            }
            using (Catalog fourth = new())
            {
                fourth.Load(paths, cacheDirectory: cache);
                Need(fourth.MP_UsedWarmCache,
                    "The updated catalog should be reusable on the next launch.");
            }

            // Ordinary ZIPs should never build the expensive custom offset
            // index; only the game's special Zstd compression requires it.
            string archive = Path.Combine(root, "textures.zip");
            using (Bitmap bitmap = new(2, 2))
            {
                using MemoryStream png = new();
                bitmap.Save(png, ImageFormat.Png);
                png.Position = 0;
                using ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                using Stream entry = zip.CreateEntry("Textures/Test/Preview.png")
                    .Open();
                png.CopyTo(entry);
            }
            using TextureLibrary textures = new();
            var field = typeof(TextureLibrary).GetField("offsets",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            textures.AddArchive(archive);
            int IndexCount() => (int)field.FieldType.GetProperty("Count")!
                .GetValue(field.GetValue(textures))!;
            Need(IndexCount() == 0, "Opening a ZIP must not eagerly read its entire offset index.");
            Need(textures.Get("Test/Preview") is Bitmap,
                "First texture request must still decode a standard ZIP entry.");
            Need(IndexCount() == 0,
                "Standard ZIP textures must decode without a custom offset index.");
            // Include fast, index-backed directional/folder/appearance lookup
            // and structure-preview cache validity in every offline CI run.
            MP_AssetLoadChecks.Run(root);
            MP_StructureHeaderIndexChecks.Run(root);
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
