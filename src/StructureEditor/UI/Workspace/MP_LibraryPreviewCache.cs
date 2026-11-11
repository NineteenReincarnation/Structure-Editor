using System.Security.Cryptography;
using System.Text;

namespace StructureEditor;

// Fast-path for already available structure previews. It only reads files;
// first-time rendering stays in the existing dedicated STA worker.
internal static class MP_LibraryPreviewCache
{
    internal static string CachePath(string input, string id, string cacheDirectory = null)
    {
        string file = Path.GetFullPath(input);
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(file + "|" + id)))[..24];
        return Path.Combine(cacheDirectory ??
            Path.Combine(MP_DataDirectories.DataDirectory, "PreviewCache"), hash + ".png");
    }

    internal static string ExistingPath(string input, string id, string cacheDirectory = null)
    {
        string file = Path.GetFullPath(input);
        string basename = Path.GetFileNameWithoutExtension(file);
        if (basename.EndsWith(".structure", StringComparison.OrdinalIgnoreCase))
            basename = basename[..^".structure".Length];
        string folder = Path.GetDirectoryName(file)!;
        // User-authored preview files always override generated thumbnails.
        foreach (string candidate in new[]
        {
            Path.Combine(folder, basename + ".png"),
            Path.Combine(folder, basename + "_preview.png"),
            Path.Combine(folder, basename + ".preview.png")
        })
            if (File.Exists(candidate)) return candidate;

        string generated = CachePath(file, id, cacheDirectory);
        return File.Exists(generated)
            && File.GetLastWriteTimeUtc(generated) >= File.GetLastWriteTimeUtc(file)
            ? generated : null;
    }

    internal static byte[] TryRead(string input, string id,
        CancellationToken cancellation, string cacheDirectory = null)
    {
        cancellation.ThrowIfCancellationRequested();
        string existing = ExistingPath(input, id, cacheDirectory);
        if (existing == null) return null;
        cancellation.ThrowIfCancellationRequested();
        return File.ReadAllBytes(existing);
    }
}
