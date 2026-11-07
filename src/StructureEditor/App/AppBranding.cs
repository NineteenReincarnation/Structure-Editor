using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace StructureEditor;

internal static class AppBranding
{
    private static Bitmap mark;
    private static Icon icon;
    public static Bitmap Mark => mark;
    public static Icon Icon => icon;

    public static void Load(TextureLibrary textures = null)
    {
        if (mark != null) return;
        using Stream embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("StructureEditor.Branding.GoldenCube.png");
        if (embedded != null) { using Bitmap image = new(embedded); mark = new Bitmap(image); }
        else if (textures?.Get("Things/Item/Special/GoldenCube") is Bitmap image) mark = new Bitmap(image);
        if (mark == null) return;
        using MemoryStream data = new(CreateIcon(mark));
        using Icon loaded = new(data);
        icon = (Icon)loaded.Clone();
    }

    public static void Prepare(string folder, string customImage = null)
    {
        if (customImage != null)
        {
            using MemoryStream bytes = new(File.ReadAllBytes(customImage));
            using Bitmap loaded = new(bytes);
            using Bitmap custom = new(loaded);
            Save(custom, folder, "Based on RimWorld Anomaly — Things/Item/Special/GoldenCube.\nLocally edited application icon: larger cube, thicker black outline.\n");
            return;
        }
        if (!Program.IsGame(Program.Paths.GameRoot)) throw new DirectoryNotFoundException("请先设置 RimWorld 游戏目录，或使用 --game 指定。");
        using TextureLibrary textures = new();
        textures.AddArchive(Path.Combine(Program.Paths.GameRoot, "Assets.zip"));
        textures.AddArchive(Path.Combine(Program.Paths.GameRoot, "Data", "Anomaly", "Assets.zip"));
        using Bitmap original = textures.Get("Things/Item/Special/GoldenCube") is Bitmap image ? new Bitmap(image) : throw new FileNotFoundException("未找到异象 DLC 的金色魔方贴图。");
        Save(original, folder, "RimWorld Anomaly — Things/Item/Special/GoldenCube\nGenerated locally from the user's installed game.\n");
    }

    private static void Save(Bitmap image, string folder, string source)
    {
        Directory.CreateDirectory(folder);
        image.Save(Path.Combine(folder, "GoldenCube.png"), ImageFormat.Png);
        File.WriteAllBytes(Path.Combine(folder, "StructureEditor.ico"), CreateIcon(image));
        File.WriteAllText(Path.Combine(folder, "SOURCE.txt"), source);
    }

    // ICO containers carry PNG frames at every Windows shell and title-bar size.
    private static byte[] CreateIcon(Bitmap source)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> frames = new();
        foreach (int size in sizes)
        {
            using Bitmap frame = new(size, size, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = (size - Math.Max(2, size / 16)) / (float)Math.Max(source.Width, source.Height);
                float w = source.Width * scale, h = source.Height * scale;
                graphics.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            }
            using MemoryStream png = new(); frame.Save(png, ImageFormat.Png); frames.Add(png.ToArray());
        }
        using MemoryStream output = new(); using BinaryWriter writer = new(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte size = sizes[i] == 256 ? (byte)0 : (byte)sizes[i];
            writer.Write(size); writer.Write(size); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (byte[] frame in frames) writer.Write(frame);
        return output.ToArray();
    }
}

public partial class MainForm
{
    private readonly PictureBox brandMark = new() { Location = new(16, 4), Size = new(48, 48), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
    private void ApplyBranding(TextureLibrary textures = null)
    {
        AppBranding.Load(textures);
        brandMark.Image = AppBranding.Mark;
        if (AppBranding.Icon != null) Icon = AppBranding.Icon;
    }
}
