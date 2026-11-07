using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace StructureEditor;

// Native caption theme plus a restrained frosted-glass treatment for toolbar
// surfaces. Unsupported DWM attributes simply keep the dark fallback colors.
internal static class MP_WindowGlass
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void Apply(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            static int ColorRef(Color color) => ColorTranslator.ToWin32(color);
            void Set(int attribute, int value)
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            Set(20, 1); // DWMWA_USE_IMMERSIVE_DARK_MODE
            Set(19, 1); // older Windows 10 builds
            Set(35, ColorRef(Color.FromArgb(22, 31, 39))); // caption
            Set(36, ColorRef(Color.FromArgb(226, 239, 243))); // caption text
            Set(34, ColorRef(Color.FromArgb(68, 92, 104))); // window border
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                Set(38, 3); // DWMSBT_TRANSIENTWINDOW (Desktop Acrylic)
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}

internal sealed class MP_GlassLayout : TableLayoutPanel
{
    public MP_GlassLayout()
    {
        BackColor = Color.FromArgb(29, 40, 48);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var fill = new LinearGradientBrush(bounds,
            Color.FromArgb(62, 81, 95), Color.FromArgb(27, 38, 48),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(fill, bounds);
        using var reflection = new Pen(Color.FromArgb(92, 135, 153), 1);
        using var border = new Pen(Color.FromArgb(53, 76, 86), 1);
        e.Graphics.DrawLine(reflection, 0, 0, bounds.Right, 0);
        e.Graphics.DrawLine(border, 0, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }
}
