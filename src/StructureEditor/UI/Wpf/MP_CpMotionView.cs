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
    private readonly WC.Grid scrollRail;
    private readonly WC.Border scrollThumb;
    private readonly WM.TranslateTransform thumbTransform = new();
    private bool draggingScrollThumb;
    private readonly WC.StackPanel rows;
    private readonly WC.Grid content;
    private readonly WC.Border selectionVisual;
    private readonly MP_LiltCursor travellingCursor;
    private readonly WM.TranslateTransform entranceTransform = new();
    private readonly MP_ScrollOffsetProxy scrollProxy;
    private readonly MP_LiltScroll scrollMotion;
    private readonly System.Windows.Threading.DispatcherTimer filterDelay = new()
    {
        Interval = TimeSpan.FromMilliseconds(135)
    };
    private readonly List<(CatalogItem Item, WC.Image Image)> images = new();
    private readonly List<(WM.SolidColorBrush Name, WM.SolidColorBrush Def,
        WM.SolidColorBrush Cp)> rowText = new();
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
    private static readonly WM.SolidColorBrush SurfaceBrush = MP_StudioTheme.Brush(MP_StudioTheme.Panel);
    private static readonly WM.SolidColorBrush Secondary = MP_StudioTheme.Brush(MP_StudioTheme.Secondary);
    private static readonly WM.SolidColorBrush MainText = MP_StudioTheme.Brush(MP_StudioTheme.Text);
    private static readonly WM.SolidColorBrush Accent = MP_StudioTheme.Brush(MP_StudioTheme.Accent);

    internal bool MP_HasSafeLayering => scroller.Margin.Right >= 12;

    internal MP_CpMotionView(Action<CatalogItem> onActivate)
    {
        activate = onActivate;
        MP_LiltTheme.Apply(this);
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
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Input), Foreground = MainText,
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(1),
            Padding = new W.Thickness(9, 5, 6, 3),
            FontSize = 16, FontFamily = MP_LiltTheme.WpfFont,
            CaretBrush = Accent,
            SelectionBrush = MP_StudioTheme.Brush(85, MP_StudioTheme.Text),
            VerticalContentAlignment = W.VerticalAlignment.Center
        };
        var placeholder = new WC.TextBlock
        {
            Text = "搜索集群建筑 / DefName",
            Foreground = Secondary, Margin = new W.Thickness(10, 7, 0, 0),
            FontFamily = search.FontFamily, FontSize = 16,
            IsHitTestVisible = false
        };
        filterDelay.Tick += (_, _) => { filterDelay.Stop(); Filter(); };
        MP_WpfMotion.AttachSearch(search);
        search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = search.Text.Length == 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
            filterDelay.Stop();
            filterDelay.Start();
        };
        searchFrame.Children.Add(search);
        searchFrame.Children.Add(placeholder);
        WC.Grid.SetRow(searchFrame, 0);
        root.Children.Add(searchFrame);

        hint = new WC.TextBlock
        {
            Text = "尚未加载", Foreground = Secondary,
            VerticalAlignment = W.VerticalAlignment.Center,
            FontFamily = search.FontFamily, FontSize = 14
        };
        WC.Grid.SetRow(hint, 1); root.Children.Add(hint);

        rows = new WC.StackPanel { Orientation = WC.Orientation.Vertical };
        selectionVisual = new WC.Border
        {
            Background = Brush(Color(0, 0, 0, 0)),
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(0),
            Margin = new W.Thickness(2, 3, 2, 0), Height = RowHeight - 6,
            VerticalAlignment = W.VerticalAlignment.Top,
            Visibility = W.Visibility.Collapsed,
            IsHitTestVisible = false,
            Effect = new WM.Effects.DropShadowEffect
            {
                BlurRadius = 0, ShadowDepth = 0, Opacity = 0,
                Color = MP_StudioTheme.WpfColor(MP_StudioTheme.Text)
            }
        };
        travellingCursor = new MP_LiltCursor(selectionVisual, false);
        content = new WC.Grid { Background = SurfaceBrush };
        WC.Panel.SetZIndex(selectionVisual, 0);
        WC.Panel.SetZIndex(rows, 1);
        content.Children.Add(selectionVisual); content.Children.Add(rows);

        scroller = new WC.ScrollViewer
        {
            Content = content,
            Background = SurfaceBrush,
            Margin = new W.Thickness(0, 0, 12, 0),
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = false,
            PanningMode = WC.PanningMode.VerticalOnly
        };
        scrollProxy = new MP_ScrollOffsetProxy(scroller);
        // Keep rubber-band motion clipped by the ScrollViewer viewport.
        scrollMotion = new MP_LiltScroll(scrollProxy);
        scroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
                scrollTarget = Math.Clamp(scrollTarget, 0, Math.Max(0, scroller.ScrollableHeight));
            RefreshImages();
            UpdateScrollIndicator();
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if (!MP_Motion.Enabled) return;
            double step = e.Delta / 120d * RowHeight * 2.3;
            double attempted = scrollTarget - step;
            scrollTarget = Math.Clamp(attempted, 0, Math.Max(0, scroller.ScrollableHeight));
            scrollMotion.Nudge(attempted - scrollTarget, scroller.ViewportHeight);
            ScrollTo(scrollTarget);
            e.Handled = true;
        };
        // Narrow translucent scroll affordance similar to the video; unlike
        // the stock scrollbar it can also be dragged without changing layout.
        var scrollHost = new WC.Grid { ClipToBounds = true };
        scrollHost.Children.Add(scroller);
        scrollRail = new WC.Grid
        {
            Width = 9, HorizontalAlignment = W.HorizontalAlignment.Right,
            Margin = new W.Thickness(0, 3, 0, 3),
            Background = MP_StudioTheme.Brush(25, MP_StudioTheme.Text),
            Cursor = WI.Cursors.Hand
        };
        scrollThumb = new WC.Border
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            CornerRadius = new W.CornerRadius(2),
            Width = 3, Height = 24,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Top,
            RenderTransform = thumbTransform,
            IsHitTestVisible = false
        };
        scrollRail.Children.Add(scrollThumb);
        scrollHost.Children.Add(scrollRail);
        scrollRail.SizeChanged += (_, _) => UpdateScrollIndicator();
        scrollRail.MouseLeftButtonDown += (_, e) =>
        {
            draggingScrollThumb = true;
            scrollRail.CaptureMouse();
            ScrollFromRail(e.GetPosition(scrollRail).Y);
            e.Handled = true;
        };
        scrollRail.MouseMove += (_, e) =>
        {
            if (draggingScrollThumb) ScrollFromRail(e.GetPosition(scrollRail).Y);
        };
        scrollRail.MouseLeftButtonUp += (_, e) =>
        {
            draggingScrollThumb = false;
            scrollRail.ReleaseMouseCapture();
            e.Handled = true;
        };
        scrollRail.LostMouseCapture += (_, _) => draggingScrollThumb = false;
        WC.Grid.SetRow(scrollHost, 2);
        root.Children.Add(scrollHost);
        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            RefreshImages();
            PlayEntrance();
        };
        Unloaded += (_, _) => filterDelay.Stop();
    }

    private void UpdateScrollIndicator()
    {
        if (scrollRail == null || scrollThumb == null || scroller == null) return;
        double height = Math.Max(1, scrollRail.ActualHeight - 6);
        if (scroller.ScrollableHeight <= 1 || scroller.ExtentHeight <= 0)
        {
            scrollRail.Visibility = W.Visibility.Collapsed;
            return;
        }
        scrollRail.Visibility = W.Visibility.Visible;
        scrollThumb.Height = Math.Clamp(
            scroller.ViewportHeight / scroller.ExtentHeight * height,
            Math.Min(22, height), height);
        double available = Math.Max(0, height - scrollThumb.Height);
        thumbTransform.Y = 3 + available *
            Math.Clamp(scroller.VerticalOffset / scroller.ScrollableHeight, 0d, 1d);
    }

    private void ScrollFromRail(double position)
    {
        if (scroller.ScrollableHeight <= 0) return;
        double travel = Math.Max(1, scrollRail.ActualHeight - 6 - scrollThumb.Height);
        double ratio = Math.Clamp((position - 3 - scrollThumb.Height / 2) / travel, 0, 1);
        double target = ratio * scroller.ScrollableHeight;
        scrollTarget = target;
        scrollMotion.Jump(target);
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
        UpdateRowContrast(-1);
        selectionVisual.Visibility = W.Visibility.Collapsed;
    }

    internal void RefreshImages()
    {
        if (source?.Textures == null || !IsVisible || scroller.ViewportHeight <= 0) return;
        int first = Math.Max(0, (int)(scroller.VerticalOffset / RowHeight) - 1);
        int last = Math.Min(images.Count,
            (int)Math.Ceiling((scroller.VerticalOffset + scroller.ViewportHeight) / RowHeight) + 1);
        for (int i = first; i < last; i++)
        {
            var (item, image) = images[i];
            if (image.Source == null) image.Source = Thumbnail(item);
        }
    }

    internal void PlayEntrance()
    {
        BeginAnimation(OpacityProperty, null);
        entranceTransform.BeginAnimation(WM.TranslateTransform.YProperty, null);
        Opacity = 1;
        entranceTransform.Y = 0;
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
        rowText.Clear();
        rows.BeginAnimation(OpacityProperty, null);
        rows.Opacity = 1;
        rows.Children.Clear();
        double max = Math.Max(1, next.Length == 0
            ? 0 : next.Max(item => item.ClusterCombatPower.GetValueOrDefault()));
        for (int i = 0; i < next.Length; i++)
            rows.Children.Add(CreateRow(next[i], i, max));
        hint.Text = source == null
            ? "尚未加载" : $"集群设施 {next.Length} 种 · 单击选择放置";
        int selectedAt = Array.FindIndex(next, item => ReferenceEquals(item, selected));
        currentIndex = selectedAt;
        UpdateRowContrast(selectedAt);
        if (selectedAt < 0) selectionVisual.Visibility = W.Visibility.Collapsed;
        else
        {
            selectionVisual.Visibility = W.Visibility.Visible;
            travellingCursor.Move(selectedAt * RowHeight, RowHeight - 6, immediate: true);
        }
        scrollTarget = 0;
        scrollMotion.Jump(0);
        RefreshImages();
        // Do not fade all rows whenever the catalog/search changes.
    }

    private WC.Border CreateRow(CatalogItem item, int index, double maxCp)
    {
        var row = new WC.Border
        {
            Height = RowHeight - 2,
            Margin = new W.Thickness(0, 1, 0, 1),
            CornerRadius = new W.CornerRadius(0),
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
            Stretch = WM.Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        images.Add((item, icon));
        WC.Grid.SetColumn(icon, 0); WC.Grid.SetRowSpan(icon, 2); body.Children.Add(icon);

        var nameColor = MP_StudioTheme.Brush(MP_StudioTheme.Text);
        var defColor = MP_StudioTheme.Brush(MP_StudioTheme.Secondary);
        var cpColor = MP_StudioTheme.Brush(MP_StudioTheme.Text);
        rowText.Add((nameColor, defColor, cpColor));
        var name = new WC.TextBlock
        {
            Text = item.ClusterWikiName ?? item.Label, Foreground = nameColor,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            FontFamily = search.FontFamily, FontSize = 16,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetRow(name, 0); WC.Grid.SetColumn(name, 1); body.Children.Add(name);

        var detail = new WC.Grid();
        detail.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(1, W.GridUnitType.Star) });
        detail.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = W.GridLength.Auto });
        var def = new WC.TextBlock
        {
            Text = item.DefName, Foreground = defColor,
            FontFamily = search.FontFamily, FontSize = 14,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        var cp = new WC.TextBlock
        {
            Text = item.ClusterCombatPower.HasValue
                ? item.ClusterCombatPower.Value.ToString("0.##", CultureInfo.InvariantCulture) + " CP" : "—",
            Foreground = cpColor, Margin = new W.Thickness(7, 0, 0, 0),
            FontFamily = search.FontFamily, FontSize = 14,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetColumn(cp, 1);
        detail.Children.Add(def); detail.Children.Add(cp);
        WC.Grid.SetRow(detail, 1); WC.Grid.SetColumn(detail, 1); body.Children.Add(detail);

        var track = new WC.Border
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Divider), Height = 2,
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        var bar = new WC.Grid();
        bar.Children.Add(track);
        var fill = new WC.Border
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Text),
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
            if (currentIndex == index) return;
            AnimateColor((WM.SolidColorBrush)row.Background,
                ((WM.SolidColorBrush)row.Background).Color,
                MP_StudioTheme.WpfOpacity(38, MP_StudioTheme.Text), 140);
            AnimateMove((WM.TranslateTransform)row.RenderTransform,
                ((WM.TranslateTransform)row.RenderTransform).X, 2, 135);
        };
        row.MouseLeave += (_, _) =>
        {
            if (currentIndex == index) return;
            AnimateColor((WM.SolidColorBrush)row.Background,
                ((WM.SolidColorBrush)row.Background).Color,
                MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 115);
            AnimateMove((WM.TranslateTransform)row.RenderTransform,
                ((WM.TranslateTransform)row.RenderTransform).X, 0, 115);
        };
        row.MouseLeftButtonDown += (_, e) =>
        {
            travellingCursor.Press();
            row.Focus();
            e.Handled = true;
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            travellingCursor.Release();
            if (row.IsMouseOver) Select(index);
            e.Handled = true;
        };
        row.MouseLeave += (_, _) => travellingCursor.Release();
        row.Focusable = true;
        return row;
    }

    private void UpdateRowContrast(int selectedIndex)
    {
        // The reference LCD uses a solid cyan capsule with darker glyphs.
        // Animate only the two text hues of the actual selected row; all
        // other rows stay readable in the editor's dark palette.
        for (int i = 0; i < rowText.Count; i++)
        {
            bool active = i == selectedIndex;
            if (i < rows.Children.Count &&
                rows.Children[i] is WC.Border row)
                MP_WpfMotion.Tint((WM.SolidColorBrush)row.Background,
                    active ? MP_StudioTheme.WpfColor(MP_StudioTheme.Active)
                        : MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 155);
            MP_WpfMotion.Tint(rowText[i].Name,
                active ? MP_StudioTheme.WpfColor(MP_StudioTheme.ActiveText)
                    : MP_StudioTheme.WpfColor(MP_StudioTheme.Text), 160);
            MP_WpfMotion.Tint(rowText[i].Def,
                active ? MP_StudioTheme.WpfColor(MP_StudioTheme.ActiveText)
                    : MP_StudioTheme.WpfColor(MP_StudioTheme.Secondary), 160);
            MP_WpfMotion.Tint(rowText[i].Cp,
                active ? MP_StudioTheme.WpfColor(MP_StudioTheme.ActiveText)
                    : MP_StudioTheme.WpfColor(MP_StudioTheme.Text), 160);
        }
    }

    private void Select(int index)
    {
        if (index < 0 || index >= visible.Length) return;
        bool hadSelection = currentIndex >= 0;
        currentIndex = index;
        selected = visible[index];
        UpdateRowContrast(index);
        selectionVisual.Visibility = W.Visibility.Visible;
        double target = index * RowHeight;
        travellingCursor.Move(target, RowHeight - 6, immediate: !hadSelection);
        if (!hadSelection && MP_Motion.Enabled)
        {
            selectionVisual.BeginAnimation(OpacityProperty, null);
            selectionVisual.Opacity = 1;
            selectionVisual.BeginAnimation(OpacityProperty, new WA.DoubleAnimation(
                0.25, 1d, TimeSpan.FromMilliseconds(175)));
        }
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
        scrollMotion.To(target);
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
            image.DecodePixelWidth = 96;
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
        MP_WpfMotion.Tint(brush, to, duration);
    }

    private static void AnimateMove(WM.TranslateTransform transform, double from, double to, int duration)
    {
        MP_WpfMotion.Translate(transform, WM.TranslateTransform.XProperty, to, duration, false);
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
