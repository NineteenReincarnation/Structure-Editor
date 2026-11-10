using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms.Integration;
using StructureEditor.Compatibility;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WI = System.Windows.Input;
using WB = System.Windows.Media.Imaging;

namespace StructureEditor;

// A WPF visual island inside the existing WinForms shell. Only presentation is
// migrated: Catalog, structure files, commands, placement and CP rules stay shared.
internal sealed class MP_CpMotionView : WC.UserControl
{
    private const double RowHeight = 59;
    private readonly Action<CatalogItem> activate;
    private readonly WC.TextBox search;
    private readonly WC.TextBlock hint;
    private readonly WC.ScrollViewer scroller;
    private readonly WC.StackPanel rows;
    private readonly WC.Grid content;
    private readonly WC.Border selectionVisual;
    private readonly WM.TranslateTransform selectionTransform = new();
    private readonly WM.TranslateTransform entranceTransform = new();
    private readonly MP_ScrollOffsetProxy scrollProxy;
    private readonly List<(CatalogItem Item, WC.Image Image)> images = new();
    private readonly Dictionary<string, WB.BitmapSource> imageCache = new(StringComparer.Ordinal);
    private Catalog source;
    private CatalogItem[] visible = Array.Empty<CatalogItem>();
    private CatalogItem selected;
    private double scrollTarget;
    private int currentIndex = -1;

    private static WM.Color Color(byte r, byte g, byte b) => WM.Color.FromRgb(r, g, b);
    private static WM.Color Color(byte a, byte r, byte g, byte b) => WM.Color.FromArgb(a, r, g, b);
    private static WM.SolidColorBrush Brush(WM.Color color)
    {
        var brush = new WM.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    private static readonly WM.SolidColorBrush SurfaceBrush = Brush(Color(33, 43, 52));
    private static readonly WM.SolidColorBrush Secondary = Brush(Color(157, 176, 188));
    private static readonly WM.SolidColorBrush MainText = Brush(Color(224, 234, 240));
    private static readonly WM.SolidColorBrush Accent = Brush(Color(173, 228, 240));

    internal MP_CpMotionView(Action<CatalogItem> onActivate)
    {
        activate = onActivate;
        this.Background = SurfaceBrush; // visual resource shared across lists
        Focusable = true;
        var root = new WC.Grid
        {
            Background = SurfaceBrush,
            Margin = new W.Thickness(7, 8, 7, 6),
            RenderTransform = entranceTransform
        };
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(37) });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(28) });
        root.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(1, W.GridUnitType.Star) });

        var searchFrame = new WC.Grid { Margin = new W.Thickness(0, 0, 0, 5) };
        search = new WC.TextBox
        {
            Background = Brush(Color(43, 58, 68)), Foreground = MainText,
            BorderBrush = Brush(Color(66, 91, 104)), BorderThickness = new W.Thickness(1),
            Padding = new W.Thickness(9, 5, 6, 3),
            FontSize = 12.5, FontFamily = new WM.FontFamily("Microsoft YaHei UI"),
            CaretBrush = Accent, SelectionBrush = Brush(Color(98, 177, 195)),
            VerticalContentAlignment = W.VerticalAlignment.Center
        };
        var placeholder = new WC.TextBlock
        {
            Text = "搜索集群建筑 / DefName",
            Foreground = Secondary, Margin = new W.Thickness(10, 7, 0, 0),
            FontFamily = search.FontFamily, FontSize = 12.5,
            IsHitTestVisible = false
        };
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
            Filter();
        };
        searchFrame.Children.Add(search);
        searchFrame.Children.Add(placeholder);
        WC.Grid.SetRow(searchFrame, 0);
        root.Children.Add(searchFrame);

        hint = new WC.TextBlock
        {
            Text = "尚未加载", Foreground = Secondary,
            VerticalAlignment = W.VerticalAlignment.Center,
            FontFamily = search.FontFamily, FontSize = 11.5
        };
        WC.Grid.SetRow(hint, 1); root.Children.Add(hint);

        rows = new WC.StackPanel { Orientation = WC.Orientation.Vertical };
        selectionVisual = new WC.Border
        {
            Background = Brush(Color(49, 110, 183, 199)),
            BorderBrush = Brush(Color(141, 120, 195, 212)),
            BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(5),
            Margin = new W.Thickness(2, 3, 2, 0), Height = RowHeight - 6,
            RenderTransform = selectionTransform,
            VerticalAlignment = W.VerticalAlignment.Top,
            Visibility = W.Visibility.Collapsed,
            IsHitTestVisible = false,
            Effect = new WM.Effects.DropShadowEffect
            {
                BlurRadius = 11, ShadowDepth = 0, Opacity = .32,
                Color = Color(90, 198, 222)
            }
        };
        content = new WC.Grid { Background = Background };
        WC.Panel.SetZIndex(selectionVisual, 0);
        WC.Panel.SetZIndex(rows, 1);
        content.Children.Add(selectionVisual); content.Children.Add(rows);

        scroller = new WC.ScrollViewer
        {
            Content = content,
            Background = SurfaceBrush,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false,
            PanningMode = WC.PanningMode.VerticalOnly
        };
        scrollProxy = new MP_ScrollOffsetProxy(scroller);
        scroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                scrollTarget = Math.Clamp(scrollTarget, 0, Math.Max(0, scroller.ScrollableHeight));
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (!MP_Motion.Enabled) return;
            double step = e.Delta / 120d * RowHeight * 2.3;
            scrollTarget = Math.Clamp(scrollTarget - step, 0, Math.Max(0, scroller.ScrollableHeight));
            ScrollTo(scrollTarget);
            e.Handled = true;
        };
        WC.Grid.SetRow(scroller, 2);
        root.Children.Add(scroller);
        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, WI.KeyEventArgs e)
    {
        if (e.Key is not (WI.Key.Down or WI.Key.Up or WI.Key.Enter)) return;
        if (visible.Length == 0) return;
        int next = currentIndex;
        if (e.Key == WI.Key.Down) next = Math.Min(visible.Length - 1, Math.Max(0, next + 1));
        else if (e.Key == WI.Key.Up) next = Math.Max(0, next <= 0 ? 0 : next - 1);
        else if (next < 0) next = 0;
        Select(next);
        e.Handled = true;
    }

    internal void SetCatalog(Catalog catalog)
    {
        if (!ReferenceEquals(source, catalog))
        {
            source = catalog;
            imageCache.Clear();
        }
        Filter();
    }

    internal void ClearSelection()
    {
        selected = null;
        currentIndex = -1;
        selectionVisual.Visibility = W.Visibility.Collapsed;
    }

    internal void RefreshImages()
    {
        if (source?.Textures == null || !IsVisible) return;
        foreach (var (item, image) in images)
        {
            if (image.Source == null)
                image.Source = Thumbnail(item);
        }
    }

    internal void PlayEntrance()
    {
        if (!IsVisible) return;
        if (!MP_Motion.Enabled) { Opacity = 1; entranceTransform.Y = 0; return; }
        Opacity = 1;
        entranceTransform.Y = 9;
        BeginAnimation(OpacityProperty, new WA.DoubleAnimation(0.55, 1.0,
            TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut }
        });
        entranceTransform.BeginAnimation(WM.TranslateTransform.YProperty,
            new WA.DoubleAnimation(9d, 0d, TimeSpan.FromMilliseconds(235))
            {
                EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut }
            });
    }

    private void Filter()
    {
        var next = MP_ClusterCombatPower.Search(source, search.Text);
        bool same = next.Length == visible.Length;
        for (int i = 0; same && i < next.Length; i++)
            same = ReferenceEquals(next[i], visible[i]);
        if (same) return;
        visible = next;
        images.Clear();
        rows.Children.Clear();
        double max = Math.Max(1, next.Length == 0
            ? 0 : next.Max(item => item.ClusterCombatPower.GetValueOrDefault()));
        for (int i = 0; i < next.Length; i++)
            rows.Children.Add(CreateRow(next[i], i, max));
        hint.Text = source == null
            ? "尚未加载" : $"集群设施 {next.Length} 种 · 单击选择放置";
        int selectedAt = Array.FindIndex(next, item => ReferenceEquals(item, selected));
        currentIndex = selectedAt;
        if (selectedAt < 0) selectionVisual.Visibility = W.Visibility.Collapsed;
        else
        {
            selectionTransform.BeginAnimation(WM.TranslateTransform.YProperty, null);
            selectionTransform.Y = selectedAt * RowHeight;
            selectionVisual.Visibility = W.Visibility.Visible;
        }
        scrollTarget = 0;
        scrollProxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        scrollProxy.Offset = 0;
        scroller.ScrollToVerticalOffset(0);
        if (MP_Motion.Enabled && IsVisible)
        {
            rows.Opacity = 1;
            rows.BeginAnimation(OpacityProperty,
                new WA.DoubleAnimation(0.67, 1d, TimeSpan.FromMilliseconds(150)));
        }
    }

    private WC.Border CreateRow(CatalogItem item, int index, double maxCp)
    {
        var row = new WC.Border
        {
            Height = RowHeight - 2,
            Margin = new W.Thickness(0, 1, 0, 1),
            CornerRadius = new W.CornerRadius(4),
            Background = new WM.SolidColorBrush(Color(0, 0, 0, 0)),
            Cursor = WI.Cursors.Hand,
            ToolTip = $"{item.ClusterWikiName ?? item.Label}\n{item.DefName}\n{item.Pack} · {(item.ClusterCombatPower.HasValue ? item.ClusterCombatPower.Value.ToString("0.##", CultureInfo.InvariantCulture) + " CP" : "未配置 CP")}",
            RenderTransform = new WM.TranslateTransform()
        };

        var body = new WC.Grid { Margin = new W.Thickness(5, 2, 5, 3) };
        body.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(44) });
        body.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(1, W.GridUnitType.Star) });
        body.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(25) });
        body.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(23) });
        body.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(2) });

        var icon = new WC.Image
        {
            Width = 38, Height = 38,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Center,
            Stretch = WM.Stretch.Uniform, Source = Thumbnail(item),
            SnapsToDevicePixels = true
        };
        images.Add((item, icon));
        WC.Grid.SetColumn(icon, 0); WC.Grid.SetRowSpan(icon, 2); body.Children.Add(icon);

        var name = new WC.TextBlock
        {
            Text = item.ClusterWikiName ?? item.Label, Foreground = MainText,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            FontFamily = search.FontFamily, FontSize = 13,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetRow(name, 0); WC.Grid.SetColumn(name, 1); body.Children.Add(name);

        var detail = new WC.Grid();
        detail.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(1, W.GridUnitType.Star) });
        detail.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = W.GridLength.Auto });
        var def = new WC.TextBlock
        {
            Text = item.DefName, Foreground = Secondary,
            FontFamily = search.FontFamily, FontSize = 11,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        var cp = new WC.TextBlock
        {
            Text = item.ClusterCombatPower.HasValue
                ? item.ClusterCombatPower.Value.ToString("0.##", CultureInfo.InvariantCulture) + " CP" : "—",
            Foreground = Accent, Margin = new W.Thickness(7, 0, 0, 0),
            FontFamily = search.FontFamily, FontSize = 11.2,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetColumn(cp, 1);
        detail.Children.Add(def); detail.Children.Add(cp);
        WC.Grid.SetRow(detail, 1); WC.Grid.SetColumn(detail, 1); body.Children.Add(detail);

        var track = new WC.Border
        {
            Background = Brush(Color(53, 70, 80)), Height = 2,
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        var bar = new WC.Grid();
        bar.Children.Add(track);
        var fill = new WC.Border
        {
            Background = Brush(Color(113, 170, 186)),
            HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Stretch
        };
        double percent = Math.Clamp(item.ClusterCombatPower.GetValueOrDefault() / maxCp, 0, 1);
        bar.SizeChanged += (_, e) => fill.Width = Math.Max(0, e.NewSize.Width * percent);
        bar.Children.Add(fill);
        WC.Grid.SetRow(bar, 2); WC.Grid.SetColumn(bar, 1); body.Children.Add(bar);
        row.Child = body;

        row.MouseEnter += (_, _) =>
        {
            AnimateColor((WM.SolidColorBrush)row.Background, Color(0, 0, 0, 0),
                Color(45, 67, 99, 113), 140);
            AnimateMove((WM.TranslateTransform)row.RenderTransform, 0, 2, 135);
        };
        row.MouseLeave += (_, _) =>
        {
            AnimateColor((WM.SolidColorBrush)row.Background,
                ((WM.SolidColorBrush)row.Background).Color, Color(0, 0, 0, 0), 115);
            AnimateMove((WM.TranslateTransform)row.RenderTransform,
                ((WM.TranslateTransform)row.RenderTransform).X, 0, 115);
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            Select(index); row.Focus(); e.Handled = true;
        };
        row.Focusable = true;
        return row;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= visible.Length) return;
        currentIndex = index;
        selected = visible[index];
        selectionVisual.Visibility = W.Visibility.Visible;
        double target = index * RowHeight;
        double from = selectionTransform.Y;
        if (MP_Motion.Enabled)
        {
            selectionTransform.BeginAnimation(WM.TranslateTransform.YProperty, null);
            selectionTransform.Y = target;
            selectionTransform.BeginAnimation(WM.TranslateTransform.YProperty,
                new WA.DoubleAnimation(from, target, TimeSpan.FromMilliseconds(255))
                {
                    EasingFunction = new WA.BackEase
                    {
                        Amplitude = .18, EasingMode = WA.EasingMode.EaseOut
                    }
                });
        }
        else selectionTransform.Y = target;
        double top = index * RowHeight;
        double bottom = top + RowHeight;
        double viewTop = scroller.VerticalOffset, viewBottom = viewTop + scroller.ViewportHeight;
        if (bottom > viewBottom - 6) ScrollTo(Math.Min(scroller.ScrollableHeight,
            bottom - scroller.ViewportHeight + 6));
        else if (top < viewTop + 6) ScrollTo(Math.Max(0, top - 6));
        activate?.Invoke(selected);
    }

    private void ScrollTo(double target)
    {
        target = Math.Clamp(target, 0, Math.Max(0, scroller.ScrollableHeight));
        scrollTarget = target;
        if (!MP_Motion.Enabled) { scroller.ScrollToVerticalOffset(target); return; }
        double from = scroller.VerticalOffset;
        scrollProxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        scrollProxy.Offset = target;
        scrollProxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty,
            new WA.DoubleAnimation(from, target, TimeSpan.FromMilliseconds(255))
            {
                EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut }
            });
    }

    private WB.BitmapSource Thumbnail(CatalogItem item)
    {
        if (source?.Textures == null) return null;
        string path = item.Icon ?? source.TextureFor(item);
        if (string.IsNullOrEmpty(path)) return null;
        if (imageCache.TryGetValue(path, out var cached)) return cached;
        System.Drawing.Bitmap bitmap = source.Textures.Request(path);
        if (bitmap == null) return null;
        try
        {
            using var data = new MemoryStream();
            System.Drawing.Rectangle region = item.Linked && item.Icon == null &&
                LinkedGraphics.IsAtlas(bitmap, path)
                ? LinkedGraphics.Source(bitmap.Width, bitmap.Height, 0)
                : new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            if (region.Width != bitmap.Width || region.Height != bitmap.Height)
            {
                using var cropped = bitmap.Clone(region, PixelFormat.Format32bppArgb);
                cropped.Save(data, ImageFormat.Png);
            }
            else bitmap.Save(data, ImageFormat.Png);
            data.Position = 0;
            var image = new WB.BitmapImage();
            image.BeginInit();
            image.CacheOption = WB.BitmapCacheOption.OnLoad;
            image.StreamSource = data;
            image.EndInit();
            image.Freeze();
            imageCache[path] = image;
            return image;
        }
        catch (ArgumentException) { return null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }

    private static void AnimateColor(WM.SolidColorBrush brush, WM.Color from, WM.Color to, int duration)
    {
        brush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        brush.Color = to;
        if (MP_Motion.Enabled)
            brush.BeginAnimation(WM.SolidColorBrush.ColorProperty, new WA.ColorAnimation(
                from, to, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut } });
    }

    private static void AnimateMove(WM.TranslateTransform transform, double from, double to, int duration)
    {
        transform.BeginAnimation(WM.TranslateTransform.XProperty, null);
        transform.X = to;
        if (MP_Motion.Enabled)
            transform.BeginAnimation(WM.TranslateTransform.XProperty, new WA.DoubleAnimation(
                from, to, TimeSpan.FromMilliseconds(duration))
            { EasingFunction = new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut } });
    }
}

// One WPF dependency property exposes pixel scrolling to the animation engine;
// no WinForms timer or forced repaint is needed.
internal sealed class MP_ScrollOffsetProxy : WA.Animatable
{
    private readonly WC.ScrollViewer viewer;
    internal MP_ScrollOffsetProxy(WC.ScrollViewer viewer) => this.viewer = viewer;
    protected override W.Freezable CreateInstanceCore() => new MP_ScrollOffsetProxy(viewer);
    internal static readonly W.DependencyProperty OffsetProperty =
        W.DependencyProperty.Register(nameof(Offset), typeof(double), typeof(MP_ScrollOffsetProxy),
            new W.PropertyMetadata(0d, (owner, e) =>
                ((MP_ScrollOffsetProxy)owner).viewer.ScrollToVerticalOffset((double)e.NewValue)));
    internal double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }
}
