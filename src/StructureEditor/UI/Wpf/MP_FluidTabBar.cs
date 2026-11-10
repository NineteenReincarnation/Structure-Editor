using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WI = System.Windows.Input;

namespace StructureEditor;

// Animated visual navigation inspired by the video's translating cyan selection
// capsule. Selection semantics stay immediate; only visuals interpolate.
internal sealed class MP_FluidTabBar : WC.UserControl
{
    private sealed class Entry
    {
        internal WC.Border Border;
        internal WC.TextBlock Label;
        internal WM.SolidColorBrush Foreground;
    }

    private readonly WC.Grid canvas;
    private readonly WC.StackPanel items;
    private readonly WC.Border slider;
    private readonly WM.TranslateTransform slide = new();
    private readonly List<Entry> entries = new();
    private readonly Action<int> activate;
    private int active = -1;
    private bool layoutReady;
    private static readonly WM.Color Muted = WM.Color.FromRgb(164, 191, 202);
    private static readonly WM.Color Bright = WM.Color.FromRgb(229, 248, 250);
    private static readonly WM.Color Dark = WM.Color.FromRgb(20, 49, 61);
    private static readonly WM.Color Fill = WM.Color.FromRgb(135, 211, 226);

    internal MP_FluidTabBar(Action<int> onActivate)
    {
        activate = onActivate;
        Height = 39;
        var root = new WC.Border
        {
            Background = new WM.SolidColorBrush(WM.Color.FromRgb(37, 50, 59)),
            ClipToBounds = true
        };
        canvas = new WC.Grid { VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Left, Height = 35 };
        slider = new WC.Border
        {
            Background = new WM.SolidColorBrush(Fill),
            BorderBrush = new WM.SolidColorBrush(WM.Color.FromRgb(194, 243, 249)),
            BorderThickness = new W.Thickness(.8),
            CornerRadius = new W.CornerRadius(4),
            Height = 29, Width = 74,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(3, 0, 0, 0),
            RenderTransform = slide,
            IsHitTestVisible = false,
            Opacity = .92
        };
        items = new WC.StackPanel
        {
            Orientation = WC.Orientation.Horizontal,
            HorizontalAlignment = W.HorizontalAlignment.Left
        };
        WC.Panel.SetZIndex(slider, 0);
        WC.Panel.SetZIndex(items, 1);
        canvas.Children.Add(slider);
        canvas.Children.Add(items);
        var scroll = new WC.ScrollViewer
        {
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Content = canvas
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            scroll.ScrollToHorizontalOffset(Math.Max(0,
                scroll.HorizontalOffset - e.Delta * .32));
            e.Handled = true;
        };
        root.Child = scroll;
        Content = root;
        SizeChanged += (_, _) => Position(false);
        Loaded += (_, _) => Position(false);
    }

    internal void AddTab(string title)
    {
        int index = entries.Count;
        var fg = new WM.SolidColorBrush(Muted);
        var caption = new WC.TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontFamily = new WM.FontFamily("Microsoft YaHei UI"),
            FontWeight = W.FontWeights.Medium,
            Foreground = fg,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(10, 0, 10, 0)
        };
        var border = new WC.Border
        {
            Width = Math.Max(70, title.Sum(c => c > 127 ? 12.7 : 7.7) + 27),
            Height = 34,
            Background = new WM.SolidColorBrush(WM.Color.FromArgb(0, 120, 196, 218)),
            Cursor = WI.Cursors.Hand,
            Child = caption
        };
        border.MouseEnter += (_, _) =>
        {
            if (index == active) return;
            AnimateColor((WM.SolidColorBrush)border.Background,
                WM.Color.FromArgb(42, 125, 189, 206), 120);
        };
        border.MouseLeave += (_, _) =>
        {
            AnimateColor((WM.SolidColorBrush)border.Background,
                WM.Color.FromArgb(0, 125, 189, 206), 130);
        };
        border.MouseLeftButtonDown += (_, e) =>
        {
            activate?.Invoke(index);
            e.Handled = true;
        };
        items.Children.Add(border);
        entries.Add(new Entry { Border = border, Label = caption, Foreground = fg });
        Position(false);
    }

    internal void Select(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        active = index;
        for (int i = 0; i < entries.Count; i++)
        {
            AnimateColor(entries[i].Foreground, i == index ? Dark : Muted, 155);
            if (i == index)
                AnimateColor((WM.SolidColorBrush)entries[i].Border.Background,
                    WM.Color.FromArgb(0, 125, 189, 206), 120);
        }
        Position(true);
    }

    private void Position(bool animated)
    {
        if (active < 0 || active >= entries.Count) return;
        double x = 0;
        for (int i = 0; i < active; i++) x += entries[i].Border.Width;
        double width = entries[active].Border.Width - 6;
        if (!layoutReady || !MP_Motion.Enabled || !animated)
        {
            slide.BeginAnimation(WM.TranslateTransform.XProperty, null);
            slider.BeginAnimation(WidthProperty, null);
            slide.X = x;
            slider.Width = width;
            layoutReady = true;
            return;
        }
        var ease = new WA.BackEase
        {
            Amplitude = .14,
            EasingMode = WA.EasingMode.EaseOut
        };
        double fromX = slide.X;
        double fromWidth = slider.Width;
        slide.BeginAnimation(WM.TranslateTransform.XProperty, null);
        slider.BeginAnimation(WidthProperty, null);
        slide.X = x;
        slider.Width = width;
        slide.BeginAnimation(WM.TranslateTransform.XProperty,
            new WA.DoubleAnimation(fromX, x, TimeSpan.FromMilliseconds(245))
            { EasingFunction = ease });
        slider.BeginAnimation(WidthProperty,
            new WA.DoubleAnimation(fromWidth, width, TimeSpan.FromMilliseconds(235))
            {
                EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut }
            });
    }

    private static void AnimateColor(WM.SolidColorBrush brush, WM.Color color, int ms)
    {
        WM.Color from = brush.Color;
        brush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        brush.Color = color;
        if (MP_Motion.Enabled)
            brush.BeginAnimation(WM.SolidColorBrush.ColorProperty,
                new WA.ColorAnimation(from, color, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut }
                });
    }
}
