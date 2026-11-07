using System.Buffers;
using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;



namespace StructureEditor;

public class TextureLibrary : IDisposable
{
    private readonly object sync = new();
    private readonly ConcurrentQueue<(string Path, int Rotation)> queue = new();
    private readonly ConcurrentDictionary<(string, int), byte> pending = new();
    private int worker;
    private volatile bool disposed;
    public event Action TextureReady;
    private record Source(string File, ZipArchive Archive, string Entry, string ArchivePath = null);
    private readonly List<ZipArchive> archives = new();
    private readonly Dictionary<string, Source> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, int), Bitmap> requests = new();
    private readonly ConcurrentDictionary<(string, string), string> appearancePaths = new();
    private readonly Dictionary<ZipArchive, Dictionary<string, long>> offsets = new();
    public int SourceCount => sources.Count;
    public int DecodedCount { get { lock (sync) return cache.Values.Count(b => b != null); } }
    public List<string> Errors { get; } = new();
    private static string Key(string path)
    {
        string s = path.Replace('\\', '/');
        foreach (string prefix in new[] { "Resources/Textures/", "Textures/" })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s[prefix.Length..];
        foreach (string suffix in new[] { ".dds.split", ".dds", ".png", ".psd", ".jpg", ".jpeg" })
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) s = s[..^suffix.Length];
        return s;
    }
    public void AddArchive(string path)
    {
        if (!File.Exists(path)) return;
        appearancePaths.Clear();
        ZipArchive zip = ZipFile.OpenRead(path);
        archives.Add(zip);
        offsets[zip] = ReadOffsets(path);
        foreach (ZipArchiveEntry e in zip.Entries)
            if ((e.FullName.Contains("Textures/", StringComparison.OrdinalIgnoreCase)) && (e.Name.EndsWith(".dds.split") || e.Name.EndsWith(".dds") || e.Name.EndsWith(".png")))
                sources[Key(e.FullName)] = new Source(null, zip, e.FullName, path);
    }
    public void AddFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        appearancePaths.Clear();
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            if (new[] { ".png", ".jpg", ".jpeg", ".dds" }.Contains(Path.GetExtension(file).ToLowerInvariant()))
                sources[Key(Path.GetRelativePath(path, file))] = new Source(file, null, null);
    }
    // Graphic_Appearances selects the texture whose name ends in the stuff appearance.
    // A folder's first entry can be a MenuIcon, not a sixteen-state linked atlas.
    public string AppearancePath(string path, string appearance = "Smooth")
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var request = (path, appearance ?? "Smooth");
        if (appearancePaths.TryGetValue(request, out var ready)) return ready;
        // Sources are fixed after catalog loading; do not wait on the decoder's lock in Paint.
        string folder = Key(path).TrimEnd('/') + "/";
        string Find(string suffix) => sources.Keys.FirstOrDefault(k =>
            k.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && !k[folder.Length..].Contains('/') && k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        return appearancePaths.GetOrAdd(request, Find(request.Item2) ?? Find("Smooth") ?? path);
    }
    public Bitmap Get(string path, int rotation = 2)
    {
        lock (sync) return disposed ? null : GetCore(path, rotation);
    }
    // Painting never waits for disk access, decompression or the decoder's first JIT.
    public Bitmap Request(string path, int rotation = 2)
    {
        if (disposed || string.IsNullOrWhiteSpace(path)) return null;
        if (Monitor.TryEnter(sync))
        {
            try { if (requests.TryGetValue((path, rotation), out var ready)) return ready; }
            finally { Monitor.Exit(sync); }
        }
        if (pending.TryAdd((path, rotation), 0)) { queue.Enqueue((path, rotation)); StartWorker(); }
        return null;
    }
    private void StartWorker()
    {
        if (disposed || Interlocked.CompareExchange(ref worker, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                while (!disposed && queue.TryDequeue(out var entry))
                {
                    try { Get(entry.Path, entry.Rotation); }
                    finally { pending.TryRemove((entry.Path, entry.Rotation), out _); }
                    if (!disposed) TextureReady?.Invoke();
                }
            }
            finally { Interlocked.Exchange(ref worker, 0); if (!disposed && !queue.IsEmpty) StartWorker(); }
        });
    }
    private Bitmap GetCore(string path, int rotation)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (requests.TryGetValue((path, rotation), out var requested)) return requested;
        string key = Key(path);
        if (!sources.ContainsKey(key))
        {
            string direction = new[] { "north", "east", "south", "west" }[Math.Clamp(rotation, 0, 3)];
            if (sources.ContainsKey(key + "_" + direction)) key += "_" + direction;
            else if (rotation == 3 && sources.ContainsKey(key + "_east")) key += "_east";
            else if (sources.ContainsKey(key + "_south")) key += "_south";
            else key = sources.Keys.FirstOrDefault(k => k.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase)) ?? key;
        }
        if (cache.TryGetValue(key, out Bitmap ready)) { requests[(path, rotation)] = ready; return ready; }
        if (!sources.TryGetValue(key, out Source source)) { requests[(path, rotation)] = null; return null; }
        try
        {
            byte[] bytes;
            if (source.File != null) bytes = File.ReadAllBytes(source.File);
            else bytes = ReadArchiveEntry(source);
            Bitmap image;
            if (bytes.Length > 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S') image = DecodeDds(bytes, source.Entry?.EndsWith(".split") == true);
            else { using MemoryStream ms = new(bytes); using Bitmap original = new(ms); image = new Bitmap(original); }
            cache[key] = image;
            requests[(path, rotation)] = image;
            return image;
        }
        catch (Exception e) { Errors.Add(key + ": " + e); cache[key] = null; requests[(path, rotation)] = null; return null; }
    }
    private static Dictionary<string, long> ReadOffsets(string path)
    {
        using FileStream stream = File.OpenRead(path);
        int tailLength = (int)Math.Min(65557, stream.Length);
        byte[] tail = new byte[tailLength]; stream.Position = stream.Length - tailLength; stream.ReadExactly(tail);
        int end = -1;
        for (int i = tail.Length - 22; i >= 0; i--) if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50) { end = i; break; }
        if (end < 0) throw new InvalidDataException("ZIP central directory not found");
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 16));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(end + 10));
        if (offset == uint.MaxValue || count == ushort.MaxValue) throw new InvalidDataException("ZIP64 texture archives are not supported by this editor version.");
        stream.Position = offset;
        Dictionary<string, long> result = new(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            byte[] header = new byte[46]; stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50) throw new InvalidDataException("Invalid ZIP central entry");
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            byte[] name = new byte[nameLength]; stream.ReadExactly(name);
            result[System.Text.Encoding.UTF8.GetString(name)] = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(42));
            stream.Position += extraLength + commentLength;
        }
        return result;
    }
    private byte[] ReadArchiveEntry(Source source)
    {
        ZipArchiveEntry entry = source.Archive.GetEntry(source.Entry)!;
        using FileStream file = File.OpenRead(source.ArchivePath);
        file.Position = offsets[source.Archive][source.Entry];
        byte[] header = new byte[30]; file.ReadExactly(header);
        int method = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
        int names = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        if (method != 93)
        {
            using Stream regular = entry.Open(); using MemoryStream mem = new(); regular.CopyTo(mem); return mem.ToArray();
        }
        file.Position += names;
        byte[] compressed = new byte[checked((int)entry.CompressedLength)]; file.ReadExactly(compressed);
        using MemoryStream input = new(compressed);
        using Stream decoder = GameTextureDecoders.ZstdStream(input);
        using MemoryStream output = new(checked((int)entry.Length)); decoder.CopyTo(output);
        if (output.Length != entry.Length) throw new InvalidDataException("Texture decompression length mismatch");
        return output.ToArray();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Bitmap DecodeDds(byte[] bytes, bool split)
    {
        byte[] pooled = null;
        try
        {
            if (split)
            {
                if (!GameTextureDecoders.TryJoin(bytes, out pooled, out int length)) throw new InvalidDataException("Cannot join split DDS");
                bytes = pooled;
            }
            int h = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)), w = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
            bool dx10 = bytes.AsSpan(84, 4).SequenceEqual("DX10"u8);
            int offset = dx10 ? 148 : 128;
            int format = dx10 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(128)) : 0;
            byte[] pixels;
            string blockFormat = format switch { 71 or 72 => "Bc1", 77 or 78 => "Bc3", 80 => "Bc4", 83 => "Bc5", 98 or 99 => "Bc7", _ => null };
            if (blockFormat != null) GameTextureDecoders.DecompressBc(blockFormat, bytes.AsSpan(offset), w, h, out pixels);
            else if (format is 28 or 87)
            {
                pixels = bytes.AsSpan(offset, w * h * 4).ToArray();
                if (format == 28) for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
            else throw new InvalidDataException("Unsupported DDS format: " + format);
            // The game's DDS header stores a Vulkan channel swizzle at offset32.
            // BC4 wall atlases need R/R/R/1, while other textures can encode alpha in R.
            if (dx10) ApplyDdsSwizzle(pixels, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)));
            Bitmap result = new(w, h, PixelFormat.Format32bppArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); } finally { result.UnlockBits(data); }
            return result;
        }
        finally { if (pooled != null && pooled.Length > 0) ArrayPool<byte>.Shared.Return(pooled); }
    }
    internal static void ApplyDdsSwizzle(byte[] bgra, uint packed)
    {
        if (packed == 0) return;
        int sr = (int)(packed & 255), sg = (int)((packed >> 8) & 255), sb = (int)((packed >> 16) & 255), sa = (int)((packed >> 24) & 255);
        static byte Select(int channel, byte own, byte r, byte g, byte b, byte a) => channel switch
        { 1 => 0, 2 => 255, 3 => r, 4 => g, 5 => b, 6 => a, _ => own };
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            bgra[i] = Select(sb, b, r, g, b, a); bgra[i + 1] = Select(sg, g, r, g, b, a);
            bgra[i + 2] = Select(sr, r, r, g, b, a); bgra[i + 3] = Select(sa, a, r, g, b, a);
        }
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return; disposed = true;
            foreach (Bitmap bitmap in cache.Values) bitmap?.Dispose();
            foreach (ZipArchive zip in archives) zip.Dispose();
            queue.Clear(); pending.Clear(); TextureReady = null;
        }
    }
}
