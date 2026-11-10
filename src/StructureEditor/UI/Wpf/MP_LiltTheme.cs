using System.Drawing.Text;
using W = System.Windows;
using WM = System.Windows.Media;

namespace StructureEditor;

// A local Unifont is preferred even when Windows has not installed it.
// Never download a font implicitly, and never redistribute font bytes.
internal static class MP_LiltTheme
{
    private static readonly string LocalFontDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fonts");
    private static readonly string LocalFontPath =
        Path.Combine(LocalFontDirectory, "unifont.otf");

    private static readonly Lazy<bool> Installed = new(() =>
    {
        try
        {
            using var installed = new InstalledFontCollection();
            return installed.Families.Any(f =>
                string.Equals(f.Name, "Unifont", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    });

    private static readonly Lazy<PrivateFontCollection> LocalFont = new(() =>
    {
        var fonts = new PrivateFontCollection();
        try
        {
            if (File.Exists(LocalFontPath)) fonts.AddFontFile(LocalFontPath);
        }
        catch (Exception)
        {
            // A bad optional font must not prevent the editor from starting.
        }
        return fonts;
    });

    internal static bool HasUnifont => Installed.Value || LocalFont.Value.Families.Length > 0;
    internal static string FontName =>
        Installed.Value ? "Unifont" : "Microsoft YaHei UI";

    internal static System.Drawing.Font CreateGdiFont(float points)
    {
        if (!Installed.Value && LocalFont.Value.Families.Length > 0)
            return new System.Drawing.Font(LocalFont.Value.Families[0], points);
        return new System.Drawing.Font(FontName, points);
    }

    internal static WM.FontFamily WpfFont { get; } = MakeWpfFont();

    private static WM.FontFamily MakeWpfFont()
    {
        if (File.Exists(LocalFontPath) && LocalFont.Value.Families.Length > 0)
        {
            try
            {
                return new WM.FontFamily(new Uri(
                    Path.GetFullPath(LocalFontDirectory) + Path.DirectorySeparatorChar),
                    "./#Unifont");
            }
            catch (Exception) { }
        }
        return new WM.FontFamily("Unifont, Consolas, Microsoft YaHei UI, Segoe UI");
    }

    internal static void Apply(W.FrameworkElement element)
    {
        if (element is null) return;
        if (element is System.Windows.Controls.Control control) control.FontFamily = WpfFont;
        element.SnapsToDevicePixels = true;
        element.UseLayoutRounding = true;
        WM.TextOptions.SetTextRenderingMode(element, WM.TextRenderingMode.Aliased);
        WM.TextOptions.SetTextFormattingMode(element, WM.TextFormattingMode.Display);
        WM.RenderOptions.SetEdgeMode(element, WM.EdgeMode.Aliased);
    }
}
