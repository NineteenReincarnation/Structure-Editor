using System.Drawing.Text;
using W = System.Windows;
using WM = System.Windows.Media;

namespace StructureEditor;

// Fonts are provided by the OS. Prefer the user's installed Unifont,
// keep East Asian text readable when it isn't installed.
internal static class MP_LiltTheme
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var installed = new InstalledFontCollection();
            return installed.Families.Any(f =>
                string.Equals(f.Name, "Unifont", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    });

    internal static bool HasUnifont => Available.Value;
    internal static string FontName => HasUnifont ? "Unifont" : "Microsoft YaHei UI";
    internal static WM.FontFamily WpfFont { get; } =
        new("Unifont, Microsoft YaHei UI, Segoe UI");

    internal static void Apply(W.FrameworkElement element)
    {
        if (element is null) return;
        if (element is System.Windows.Controls.Control control) control.FontFamily = WpfFont;
        if (HasUnifont)
            WM.TextOptions.SetTextRenderingMode(element, WM.TextRenderingMode.Aliased);
        WM.TextOptions.SetTextFormattingMode(element, WM.TextFormattingMode.Display);
    }
}
