using System.Drawing.Imaging;

namespace StructureEditor;

// Uses only temporary local PNGs/structure placeholders. No RimWorld needed.
internal static class MP_AssetLoadChecks
{
    internal static void Run(string root)
    {
        string texturesFolder = Path.Combine(root, "LookupTextures");
        string deco = Path.Combine(texturesFolder, "Props", "Decor");
        string nested = Path.Combine(deco, "Nested");
        Directory.CreateDirectory(nested);
        using (Bitmap png = new(2, 2))
        {
            png.Save(Path.Combine(deco, "Panel_Smooth.png"), ImageFormat.Png);
            png.Save(Path.Combine(nested, "Panel_Rough.png"), ImageFormat.Png);
            png.Save(Path.Combine(deco, "Panel_Rough.png"), ImageFormat.Png);
        }
        using (TextureLibrary textures = new())
        {
            textures.AddFolder(texturesFolder);
            if (textures.SourceCount != 3)
                throw new InvalidOperationException("Texture source manifest lost entries.");
            if (textures.AppearancePath("Props/Decor", "Rough") != "Props/Decor/Panel_Rough"
                || textures.AppearancePath("Props/Decor", "Unknown") != "Props/Decor/Panel_Smooth")
                throw new InvalidOperationException("Appearance selection included nested or wrong files.");
            if (textures.Get("Props/Decor") == null
                || textures.Get("Props/Decor/Panel_Rough") == null
                || textures.Get("Props") == null)
                throw new InvalidOperationException("Folder prefix texture fallback changed.");
            if (textures.AppearancePath("Props/DoesNotExist", "Smooth") != "Props/DoesNotExist")
                throw new InvalidOperationException("Missing appearance fallback changed.");
        }

        string document = Path.Combine(root, "Room.structure.json");
        File.WriteAllText(document, "{}");
        string preview = Path.Combine(root, "Room.png");
        byte[] sample = [1, 2, 3, 4];
        File.WriteAllBytes(preview, sample);
        string previewCache = Path.Combine(root, "PreviewCache");
        Directory.CreateDirectory(previewCache);
        string id = "MP_TestRoom";
        byte[] read = MP_LibraryPreviewCache.TryRead(document, id, CancellationToken.None, previewCache);
        if (!sample.SequenceEqual(read))
            throw new InvalidOperationException("Existing user preview did not hit fast-path.");

        File.Delete(preview);
        string cached = MP_LibraryPreviewCache.CachePath(document, id, previewCache);
        File.WriteAllBytes(cached, sample);
        DateTime past = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(document, past);
        File.SetLastWriteTimeUtc(cached, past.AddMinutes(1));
        read = MP_LibraryPreviewCache.TryRead(document, id, CancellationToken.None, previewCache);
        if (!sample.SequenceEqual(read))
            throw new InvalidOperationException("Valid generated preview was not reused.");
        File.SetLastWriteTimeUtc(document, past.AddMinutes(2));
        if (MP_LibraryPreviewCache.TryRead(document, id, CancellationToken.None, previewCache) != null)
            throw new InvalidOperationException("Stale previews must be regenerated.");
    }
}
