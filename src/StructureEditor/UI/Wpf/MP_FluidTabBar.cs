using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WI = System.Windows.Input;

namespace StructureEditor;

// The WPF tab strip sizes itself against its ACTUAL viewport, in DIPs.
// This avoids a 3rd tab being clipped by WinForms SplitContainer at 125%-200% DPI.
internal static class MP_TabLayout
{
    internal static double[] Fit(IReadOnlyList<double> desired, double available)
    {
        if (desired == null || desired.Count == 0) return Array.Empty<double>();
        double sum = desired.Sum();
        if (!double.IsFinite(available) || available <= 0 || sum <= available)
            return desired.ToArray();

        // Equal distribution keeps every tab discoverable rather than letting
        // the final one disappear behind the canvas toolbar.
        double cell = Math.Max(1, available / desired.Count);
        return Enumerable.Repeat(cell, desired.Count).ToArray();
    }
}

internal sealed class MP_FluidTabBar : WC.UserControl
{
    private sealed class Entry
    {
        internal WC.Border Host;
        internal WC.TextBlock Caption;
        internal WM.SolidColorBrush Foreground;
        internal string FullTitle;
        internal double Desired;
        internal WM.Effects.DropShadowEffect Glow;
    }

    private readonly WC.Grid panel;
    private readonly WC.StackPanel tabRow;
    private readonly WC.Border activeCapsule;
    private readonly WC.Border baseline;
    private readonly MP_LiltCursor travellingCursor;
    private readonly List<Entry> tabs = new();
    private readonly Action<int> activate;
    private int active = -1;
    private bool laidOut;
    private double lastAvailable;
    private static readonly WM.Color Muted = WM.Color.FromRgb(149, 185, 207);
    private static readonly WM.Color ActiveText = WM.Color.FromRgb(9, 22, 32);
    private static readonly WM.Color ActiveBackground = WM.Color.FromRgb(188, 228, 249);

    internal MP_FluidTabBar(Action<int> onActivate)
    {
        activate = onActivate;
        MP_LiltTheme.Apply(this);
        Height = 39;
        var surface = new WC.Grid
        {
            Background = new WM.SolidColorBrush(WM.Color.FromRgb(10, 15, 21)),
            ClipToBounds = true
        };
        surface.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(1, W.GridUnitType.Star) });
        surface.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(1) });

        panel = new WC.Grid
        {
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            VerticalAlignment = W.VerticalAlignment.Stretch,
            ClipToBounds = true
        };
        activeCapsule = new WC.Border
        {
            Width = 70, Height = 31, Margin = new W.Thickness(3, 0, 3, 0),
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Center,
            CornerRadius = new W.CornerRadius(0),
            // Travelling lilt edge never paints over labels on other tabs.
            Background = new WM.SolidColorBrush(WM.Colors.Transparent),
            BorderBrush = new WM.SolidColorBrush(WM.Color.FromRgb(202, 244, 249)),
            BorderThickness = new W.Thickness(.8),
            IsHitTestVisible = false
        };
        travellingCursor = new MP_LiltCursor(activeCapsule, true);
        tabRow = new WC.StackPanel
        {
            HorizontalAlignment = W.HorizontalAlignment.Left,
            Orientation = WC.Orientation.Horizontal
        };
        WC.Panel.SetZIndex(activeCapsule, 0);
        WC.Panel.SetZIndex(tabRow, 1);
        panel.Children.Add(activeCapsule);
        panel.Children.Add(tabRow);
        WC.Grid.SetRow(panel, 0);
        surface.Children.Add(panel);
        baseline = new WC.Border
        {
            Background = new WM.SolidColorBrush(WM.Color.FromRgb(52, 88, 109)),
            Height = 1
        };
        WC.Grid.SetRow(baseline, 1);
        surface.Children.Add(baseline);
        Content = surface;
        SizeChanged += (_, _) => FitToViewport();
        Loaded += (_, _) => FitToViewport();
    }

    internal void AddTab(string title)
    {
        int index = tabs.Count;
        var foreground = new WM.SolidColorBrush(Muted);
        var glow = MP_LcdVisual.Glow();
        var caption = new WC.TextBlock
        {
            Text = title,
            Foreground = foreground,
            FontFamily = MP_LiltTheme.WpfFont,
            FontSize = 16,
            FontWeight = W.FontWeights.Normal,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextAlignment = W.TextAlignment.Center,
            Effect = glow,
            Margin = new W.Thickness(6, 0, 6, 0)
        };
        double desired = Math.Max(71, title.Sum(c => c > 127 ? 16 : 9) + 20);
        var host = new WC.Border
        {
            Width = desired, Height = 35,
            Background = new WM.SolidColorBrush(WM.Color.FromArgb(0, 111, 183, 205)),
            Cursor = WI.Cursors.Hand,
            ToolTip = title,
            Child = caption
        };
        host.MouseEnter += (_, _) =>
        {
            if (index != active)
                MP_WpfMotion.Tint((WM.SolidColorBrush)host.Background,
                    WM.Color.FromArgb(52, 86, 154, 177), 120);
        };
        host.MouseLeave += (_, _) =>
        {
            if (index != active)
                MP_WpfMotion.Tint((WM.SolidColorBrush)host.Background,
                    WM.Color.FromArgb(0, 86, 154, 177), 120);
        };
        host.MouseLeftButtonDown += (_, e) =>
        {
            travellingCursor.Press();
            e.Handled = true;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            travellingCursor.Release();
            if (host.IsMouseOver) activate?.Invoke(index);
            e.Handled = true;
        };
        host.MouseLeave += (_, _) => travellingCursor.Release();
        tabRow.Children.Add(host);
        tabs.Add(new Entry
        {
            Host = host, Caption = caption, Foreground = foreground,
            Desired = desired, FullTitle = title, Glow = glow
        });
        FitToViewport();
    }

    internal void Select(int index)
    {
        if (index < 0 || index >= tabs.Count) return;
        active = index;
        for (int i = 0; i < tabs.Count; i++)
        {
            MP_WpfMotion.Tint(tabs[i].Foreground,
                i == index ? ActiveText : Muted, 145);
            MP_LcdVisual.GlowSelected(tabs[i].Glow, i == index);
            // Only the active item's own background is inverted.
            MP_WpfMotion.Tint((WM.SolidColorBrush)tabs[i].Host.Background,
                i == index ? ActiveBackground
                    : WM.Color.FromArgb(0, 111, 183, 205), 145);
        }
        Position(true);
    }

    private void FitToViewport()
    {
        if (tabs.Count == 0) return;
        double available = Math.Max(0, ActualWidth);
        if (available <= 0) return;
        // A tiny reserve avoids rounding the last button into the neighbouring
        // panel at fractional OS DPI scales.
        double usable = Math.Max(1, available - 2);
        double[] widths = MP_TabLayout.Fit(tabs.Select(t => t.Desired).ToArray(), usable);
        for (int i = 0; i < tabs.Count; i++)
        {
            Entry entry = tabs[i];
            entry.Host.Width = widths[i];
            // Narrow columns keep short aliases instead of cropped full titles.
            entry.Caption.Text = widths[i] < 69 && entry.FullTitle == "集群 CP"
                ? "CP" : entry.FullTitle;
            entry.Caption.FontSize = widths[i] < 65 ? 14 : 16;
        }
        bool resized = Math.Abs(lastAvailable - usable) > .5;
        lastAvailable = usable;
        Position(!resized);
    }

    private void Position(bool animate)
    {
        if (active < 0 || active >= tabs.Count) return;
        double x = 0;
        for (int i = 0; i < active; i++) x += tabs[i].Host.Width;
        double targetWidth = Math.Max(1, tabs[active].Host.Width - 6);
        bool smooth = animate && laidOut && MP_Motion.Enabled;
        travellingCursor.Move(x, targetWidth, immediate: !smooth);
        laidOut = true;
    }
}
