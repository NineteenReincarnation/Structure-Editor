namespace StructureEditor;

// Direct visual match to the user-supplied lilt screenshots (pixel sampled):
// OLED background #080A0E, foreground/inversion #C8E6FF,
// restrained outer frame #0D1117. lilt's own default Theme is gfx.Black /
// gfx.White; this color mapping matches the actual user reference artwork.
internal static class MP_StudioTheme
{
    internal static readonly Color Window = Color.FromArgb(8, 10, 14);
    internal static readonly Color Toolbar = Color.FromArgb(8, 10, 14);
    internal static readonly Color Panel = Color.FromArgb(8, 10, 14);
    internal static readonly Color Canvas = Color.FromArgb(13, 17, 23);
    internal static readonly Color PanelRaised = Color.FromArgb(13, 17, 23);
    internal static readonly Color Input = Color.FromArgb(8, 10, 14);
    internal static readonly Color Divider = Color.FromArgb(66, 79, 92);
    internal static readonly Color Text = Color.FromArgb(200, 230, 255);
    internal static readonly Color Secondary = Color.FromArgb(141, 162, 181);
    internal static readonly Color Accent = Color.FromArgb(200, 230, 255);
    internal static readonly Color Active = Color.FromArgb(200, 230, 255);
    internal static readonly Color ActiveText = Color.FromArgb(8, 10, 14);
    internal static readonly Color Hover = Color.FromArgb(35, 45, 56);
    internal const int PanelInset = 10;
    internal const int FieldHeight = 30;
    internal const int HeaderHeight = 40;

    internal static System.Windows.Media.Color WpfColor(Color color)
        => System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);

    internal static System.Windows.Media.Color WpfOpacity(byte opacity, Color color)
        => System.Windows.Media.Color.FromArgb(opacity, color.R, color.G, color.B);

    // All brushes are mutable to support the lilt animation transitions.
    internal static System.Windows.Media.SolidColorBrush Brush(Color color)
        => new(WpfColor(color));
    internal static System.Windows.Media.SolidColorBrush Brush(byte opacity, Color color)
        => new(WpfOpacity(opacity, color));
}
