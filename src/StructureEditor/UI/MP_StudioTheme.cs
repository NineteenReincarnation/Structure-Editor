namespace StructureEditor;

// Shared color and spacing vocabulary for WinForms shell, WPF libraries and
// the native canvas. The center canvas stays slightly warmer to protect
// RimWorld texture contrast; adjacent surfaces form a continuous hierarchy.
internal static class MP_StudioTheme
{
    internal static readonly Color Window = Color.FromArgb(20, 29, 37);
    internal static readonly Color Toolbar = Color.FromArgb(17, 27, 36);
    internal static readonly Color Panel = Color.FromArgb(23, 34, 43);
    internal static readonly Color PanelRaised = Color.FromArgb(29, 43, 54);
    internal static readonly Color Input = Color.FromArgb(27, 42, 53);
    internal static readonly Color Divider = Color.FromArgb(53, 78, 92);
    internal static readonly Color Text = Color.FromArgb(210, 231, 241);
    internal static readonly Color Secondary = Color.FromArgb(148, 178, 193);
    internal static readonly Color Accent = Color.FromArgb(163, 217, 235);
    internal static readonly Color Active = Color.FromArgb(177, 222, 237);
    internal static readonly Color ActiveText = Color.FromArgb(18, 35, 45);
    internal const int PanelInset = 10;
    internal const int FieldHeight = 30;
    internal const int HeaderHeight = 40;

    internal static System.Windows.Media.Color WpfColor(Color color)
        => System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);

    // Return an unfrozen brush; lilt animation changes some colors over time.
    internal static System.Windows.Media.SolidColorBrush Brush(Color color)
        => new(WpfColor(color));
}
