using System.Drawing.Imaging;
using System.Globalization;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WI = System.Windows.Input;
using WB = System.Windows.Media.Imaging;

namespace StructureEditor;

// WPF native virtualized asset library. Search/filter remain shared with the
// established catalog query; the visible surface never owns editor state.
internal sealed class MP_FluidAssetPalette : WC.UserControl
{
    private const double RowHeight = 70;
    private readonly WC.ListBox list;
    private readonly WC.Grid layout;
    private readonly WC.Border selection;
    private readonly MP_LiltCursor travellingCursor;
    private readonly Action<CatalogItem> activate;
    private readonly MP_WpfTextureCache textures = new();
    private WC.ScrollViewer scroller;
    private MP_ScrollOffsetProxy scrollProxy;
    private MP_LiltScroll scrollMotion;
    private MP_WpfScrollRail scrollRail;
    private bool scrollEventsHooked;
    private Catalog source;
    private CatalogItem[] items = Array.Empty<CatalogItem>();
    private bool selecting;
    private int selectedIndex = -1;
    private double targetScroll;
    private static readonly WM.SolidColorBrush Surface = MP_StudioTheme.Brush(MP_StudioTheme.Panel);

    internal MP_FluidAssetPalette(Action<CatalogItem> onActivate)
    {
        activate = onActivate;
        MP_LiltTheme.Apply(this);
        Background = Surface;
        layout = new WC.Grid { Background = Surface, ClipToBounds = true };
        var rowFactory = new W.FrameworkElementFactory(typeof(MP_FluidAssetRow));
        var itemTemplate = new W.DataTemplate(typeof(CatalogItem)) { VisualTree = rowFactory };
        var containerTemplate = new WC.ControlTemplate(typeof(WC.ListBoxItem));
        var presenter = new W.FrameworkElementFactory(typeof(WC.ContentPresenter));
        presenter.SetValue(WC.ContentPresenter.HorizontalAlignmentProperty, W.HorizontalAlignment.Stretch);
        presenter.SetValue(WC.ContentPresenter.VerticalAlignmentProperty, W.VerticalAlignment.Center);
        presenter.SetValue(WC.ContentPresenter.ContentProperty,
            new W.TemplateBindingExtension(WC.ContentControl.ContentProperty));
        presenter.SetValue(WC.ContentPresenter.ContentTemplateProperty,
            new W.TemplateBindingExtension(WC.ContentControl.ContentTemplateProperty));
        containerTemplate.VisualTree = presenter;
        var containerStyle = new W.Style(typeof(WC.ListBoxItem));
        containerStyle.Setters.Add(new W.Setter(WC.Control.TemplateProperty, containerTemplate));
        containerStyle.Setters.Add(new W.Setter(WC.Control.PaddingProperty, new W.Thickness(0)));
        containerStyle.Setters.Add(new W.Setter(WC.Control.BorderThicknessProperty, new W.Thickness(0)));
        containerStyle.Setters.Add(new W.Setter(WC.Control.HorizontalContentAlignmentProperty, W.HorizontalAlignment.Stretch));
        var panel = new WC.ItemsPanelTemplate(
            new W.FrameworkElementFactory(typeof(WC.VirtualizingStackPanel)));
        list = new WC.ListBox
        {
            Background = Surface, Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            BorderThickness = new W.Thickness(0), Padding = new W.Thickness(4, 3, 4, 4),
            ItemTemplate = itemTemplate, ItemContainerStyle = containerStyle,
            ItemsPanel = panel, FocusVisualStyle = null,
            Margin = new W.Thickness(0, 0, 12, 0)
        };
        WC.ScrollViewer.SetVerticalScrollBarVisibility(list, WC.ScrollBarVisibility.Hidden);
        WC.ScrollViewer.SetHorizontalScrollBarVisibility(list, WC.ScrollBarVisibility.Disabled);
        WC.ScrollViewer.SetCanContentScroll(list, true);
        WC.VirtualizingPanel.SetIsVirtualizing(list, true);
        WC.VirtualizingPanel.SetVirtualizationMode(list, WC.VirtualizationMode.Recycling);
        WC.VirtualizingPanel.SetScrollUnit(list, WC.ScrollUnit.Pixel);
        list.SelectionChanged += (_, _) =>
        {
            if (selecting) return;
            selectedIndex = list.SelectedIndex;
            RefreshSelectedRows();
            UpdateSelection(true);
            if (list.SelectedItem is CatalogItem item) activate?.Invoke(item);
        };
        // Clicking the already selected row must re-enter placement after the
        // user switched to another editor tool.
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            W.DependencyObject current = e.OriginalSource as W.DependencyObject;
            while (current != null && current is not WC.ListBoxItem)
                current = WM.VisualTreeHelper.GetParent(current);
            if (current is WC.ListBoxItem container &&
                ReferenceEquals(container.DataContext, list.SelectedItem) &&
                list.SelectedItem is CatalogItem selected)
                activate?.Invoke(selected);
        };
        list.PreviewMouseWheel += (_, e) =>
        {
            if (!MP_Motion.Enabled || scroller == null) return;
            targetScroll = Math.Clamp(targetScroll - e.Delta / 120d * 120d,
                0d, Math.Max(0d, scroller.ScrollableHeight));
            ScrollTo(targetScroll);
            e.Handled = true;
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WI.Key.Enter && list.SelectedItem is CatalogItem item)
            {
                activate?.Invoke(item);
                e.Handled = true;
            }
        };
        selection = new WC.Border
        {
            Height = RowHeight - 5, Margin = new W.Thickness(6, 0, 16, 0),
            // The moving cursor may pass other rows. Paint only its outline.
            Background = Solid(0, 0, 0, 0),
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            BorderThickness = new W.Thickness(1),
            CornerRadius = new W.CornerRadius(0),
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            VerticalAlignment = W.VerticalAlignment.Top,
            IsHitTestVisible = false, Visibility = W.Visibility.Collapsed
        };
        travellingCursor = new MP_LiltCursor(selection, false);
        WC.Panel.SetZIndex(list, 0);
        WC.Panel.SetZIndex(selection, 1);
        layout.Children.Add(list);
        layout.Children.Add(selection);
        Content = layout;

        Loaded += (_, _) =>
        {
            scroller = FindChild<WC.ScrollViewer>(list);
            if (scroller != null && !scrollEventsHooked)
            {
                scrollEventsHooked = true;
                scrollProxy = new MP_ScrollOffsetProxy(scroller);
                scrollMotion = new MP_LiltScroll(scrollProxy);
                scrollRail = new MP_WpfScrollRail(scroller, scrollProxy,
                    offset =>
                    {
                        targetScroll = offset;
                        scrollMotion.Jump(offset);
                    });
                WC.Panel.SetZIndex(scrollRail, 2);
                layout.Children.Add(scrollRail);
                scroller.ScrollChanged += (_, _) =>
                {
                    // Passive movement never jumps back to the selected item.
                    UpdateSelection(false);
                    RefreshVisible();
                };
            }
            scrollRail?.Refresh();
            RefreshVisible();
            UpdateSelection(false);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { PlayEntrance(); RefreshVisible(); }
        };
    }

    internal void SetItems(Catalog catalog, CatalogItem[] next)
    {
        next ??= Array.Empty<CatalogItem>();
        if (!ReferenceEquals(source, catalog))
        {
            source = catalog;
            textures.SetCatalog(catalog);
        }
        bool identical = next.Length == items.Length;
        for (int i = 0; identical && i < next.Length; i++)
            identical = ReferenceEquals(next[i], items[i]);
        if (identical) return;
        CatalogItem previous = list.SelectedItem as CatalogItem;
        items = next;
        selecting = true;
        try
        {
            list.ItemsSource = next;
            int index = previous == null ? -1 :
                Array.FindIndex(next, i => ReferenceEquals(i, previous));
            list.SelectedIndex = index;
            selectedIndex = index;
        }
        finally { selecting = false; }
        UpdateSelection(false);
        RefreshSelectedRows();
        // ItemSource replacement already invalidates realized containers.
        // A whole-list opacity transition doubles the repaint and flickers.
        list.BeginAnimation(OpacityProperty, null);
        list.Opacity = 1;
        RefreshVisible();
    }

    internal void MarkSelected(CatalogItem item)
    {
        if (item == null || !items.Contains(item)) return;
        if (ReferenceEquals(list.SelectedItem, item)) return;
        selecting = true;
        try
        {
            list.SelectedItem = item;
            selectedIndex = list.SelectedIndex;
        }
        finally { selecting = false; }
        UpdateSelection(true);
        RefreshSelectedRows();
    }

    internal bool MP_HasSafeLayering =>
        selection.Background is WM.SolidColorBrush brush && brush.Color.A == 0
        && layout.ClipToBounds && list.Margin.Right >= 12;

    internal void PlayEntrance()
    {
        // ElementHost page switches are instant; never fade the entire
        // WPF subtree (text and thumbnails otherwise flash on every toggle).
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    internal void RefreshVisible()
    {
        if (source?.Textures == null || !IsVisible || items.Length == 0) return;
        int first = scroller == null ? 0 : Math.Max(0, (int)(scroller.VerticalOffset / RowHeight) - 3);
        int last = scroller == null ? Math.Min(items.Length, 25)
            : Math.Min(items.Length, (int)Math.Ceiling(
                (scroller.VerticalOffset + scroller.ViewportHeight) / RowHeight) + 5);
        for (int i = first; i < last; i++)
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is WC.ListBoxItem container)
            {
                var row = FindChild<MP_FluidAssetRow>(container);
                row?.SetSelected(i == selectedIndex);
                row?.RefreshThumbnail();
            }
    }

    private void RefreshSelectedRows()
    {
        // Only realized rows exist with recycling virtualization.
        int first = scroller == null ? 0
            : Math.Max(0, (int)(scroller.VerticalOffset / RowHeight) - 3);
        int last = scroller == null ? Math.Min(items.Length, 25)
            : Math.Min(items.Length, (int)Math.Ceiling(
                (scroller.VerticalOffset + scroller.ViewportHeight) / RowHeight) + 5);
        for (int i = first; i < last; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is WC.ListBoxItem container)
                FindChild<MP_FluidAssetRow>(container)?.SetSelected(i == selectedIndex);
        }
    }

    internal WB.BitmapSource Thumbnail(CatalogItem item) => textures.TryGet(item);

    private void UpdateSelection(bool animate)
    {
        if (selectedIndex < 0 || selectedIndex >= items.Length)
        {
            selection.Visibility = W.Visibility.Collapsed;
            return;
        }
        if (list.ItemContainerGenerator.ContainerFromIndex(selectedIndex) is not WC.ListBoxItem container)
        {
            // Passive scrolling must never force the selected asset back into
            // view. ScrollIntoView is reserved for explicit keyboard selection.
            if (!animate || !IsVisible)
            {
                selection.Visibility = W.Visibility.Collapsed;
                return;
            }
            if (list.IsLoaded)
            {
                list.ScrollIntoView(items[selectedIndex]);
                list.Dispatcher.BeginInvoke(new Action(() => UpdateSelection(animate)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return;
        }
        W.Point pos = container.TranslatePoint(new W.Point(0, 0), layout);
        double nextY = pos.Y + 2;
        if (!double.IsFinite(nextY)) return;
        bool wasVisible = selection.Visibility == W.Visibility.Visible;
        selection.Visibility = W.Visibility.Visible;
        travellingCursor.Move(nextY, RowHeight - 5,
            immediate: !wasVisible || !animate || !MP_Motion.Enabled);
    }

    private void ScrollTo(double destination)
    {
        if (scroller == null) return;
        if (scrollMotion == null)
            scroller.ScrollToVerticalOffset(destination);
        else
            scrollMotion.To(destination);
    }

    private static T FindChild<T>(W.DependencyObject parent) where T : W.DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < WM.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            W.DependencyObject child = WM.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            T next = FindChild<T>(child);
            if (next != null) return next;
        }
        return null;
    }

    internal static WM.SolidColorBrush Solid(byte r, byte g, byte b) =>
        new(WM.Color.FromRgb(r, g, b));
    internal static WM.SolidColorBrush Solid(byte a, byte r, byte g, byte b) =>
        new(WM.Color.FromArgb(a, r, g, b));
}

// Content reused by the virtualizing panel; no per-row timers or GDI drawing.
public sealed class MP_FluidAssetRow : WC.Border
{
    private readonly WC.Image icon;
    private readonly WC.TextBlock title, def, detail;
    private readonly WM.TranslateTransform offset = new();
    private readonly WM.SolidColorBrush hoverBrush =
        MP_StudioTheme.Brush(0, MP_StudioTheme.Text);
    private bool selectedState;

    public MP_FluidAssetRow()
    {
        Height = 67;
        Margin = new W.Thickness(2, 1, 2, 1);
        Background = hoverBrush;
        CornerRadius = new W.CornerRadius(0);
        RenderTransform = offset;
        var layout = new WC.Grid { Margin = new W.Thickness(6, 5, 6, 3), ClipToBounds = true };
        layout.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(52) });
        layout.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        layout.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(25) });
        layout.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(19) });
        layout.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(15) });
        icon = new WC.Image
        {
            Width = 45, Height = 45, Stretch = WM.Stretch.Uniform,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetRowSpan(icon, 3);
        layout.Children.Add(icon);
        title = Label(16, MP_StudioTheme.Brush(MP_StudioTheme.Text));
        def = Label(14, MP_StudioTheme.Brush(MP_StudioTheme.Secondary));
        detail = Label(13, MP_StudioTheme.Brush(MP_StudioTheme.Secondary));
        foreach (var (control, row) in new[] { (title, 0), (def, 1), (detail, 2) })
        {
            WC.Grid.SetColumn(control, 1);
            WC.Grid.SetRow(control, row);
            layout.Children.Add(control);
        }
        Child = layout;
        DataContextChanged += (_, _) => Fill();
        Loaded += (_, _) => Fill();
        MouseEnter += (_, _) =>
        {
            if (selectedState) return;
            AnimColor(MP_StudioTheme.WpfOpacity(40, MP_StudioTheme.Text), 145);
            AnimX(2, 140);
        };
        MouseLeave += (_, _) =>
        {
            if (selectedState) return;
            AnimColor(MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 125);
            AnimX(0, 120);
        };
    }

    private static WC.TextBlock Label(double size, WM.Brush color) => new()
    {
        FontFamily = MP_LiltTheme.WpfFont, FontSize = size,
        Foreground = color, TextTrimming = W.TextTrimming.CharacterEllipsis,
        VerticalAlignment = W.VerticalAlignment.Center
    };

    internal void SetSelected(bool next)
    {
        if (selectedState == next) return;
        selectedState = next;
        AnimColor(next ? MP_StudioTheme.WpfColor(MP_StudioTheme.Active)
            : MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 145);
        title.Foreground = next
            ? MP_StudioTheme.Brush(MP_StudioTheme.ActiveText)
            : MP_StudioTheme.Brush(MP_StudioTheme.Text);
        def.Foreground = next
            ? MP_StudioTheme.Brush(MP_StudioTheme.ActiveText)
            : MP_StudioTheme.Brush(MP_StudioTheme.Secondary);
        detail.Foreground = next
            ? MP_StudioTheme.Brush(MP_StudioTheme.ActiveText)
            : MP_StudioTheme.Brush(MP_StudioTheme.Secondary);
        if (next) AnimX(0, 0);
    }

    private void Fill()
    {
        icon.Source = null;
        if (DataContext is not CatalogItem item) return;
        title.Text = item.Label;
        def.Text = item.DefName;
        detail.Text = item.Group + " · " + item.Pack;
        ToolTip = item.Label + "\n" + item.DefName + "\n" + item.Pack;
        // Recycler may reuse an earlier selected row; refresh safely.
        selectedState = false;
        hoverBrush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        hoverBrush.Color = MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text);
        W.DependencyObject ancestor = this;
        while (ancestor != null && ancestor is not WC.ListBoxItem)
            ancestor = WM.VisualTreeHelper.GetParent(ancestor);
        if (ancestor is WC.ListBoxItem holder)
            SetSelected(holder.IsSelected);
        RefreshThumbnail();
    }

    internal void RefreshThumbnail()
    {
        if (icon.Source != null || DataContext is not CatalogItem item || !IsVisible) return;
        W.DependencyObject parent = this;
        while (parent != null && parent is not MP_FluidAssetPalette)
            parent = WM.VisualTreeHelper.GetParent(parent);
        if (parent is MP_FluidAssetPalette view)
            icon.Source = view.Thumbnail(item);
    }

    private void AnimColor(WM.Color target, int ms)
    {
        WM.Color from = hoverBrush.Color;
        hoverBrush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        hoverBrush.Color = target;
        if (MP_Motion.Enabled)
            hoverBrush.BeginAnimation(WM.SolidColorBrush.ColorProperty,
                new WA.ColorAnimation(from, target, TimeSpan.FromMilliseconds(ms))
                { EasingFunction = new WA.CubicEase
                    { EasingMode = WA.EasingMode.EaseOut } });
    }

    private void AnimX(double target, int ms)
    {
        double from = offset.X;
        offset.BeginAnimation(WM.TranslateTransform.XProperty, null);
        offset.X = target;
        if (MP_Motion.Enabled)
            offset.BeginAnimation(WM.TranslateTransform.XProperty,
                new WA.DoubleAnimation(from, target, TimeSpan.FromMilliseconds(ms))
                { EasingFunction = new WA.CubicEase
                    { EasingMode = WA.EasingMode.EaseOut } });
    }
}

// Shared WPF image adapter stays separate from Catalog and supports lazy decoding.
// Decoded images are frozen for use in the WPF visual tree.
internal sealed class MP_WpfTextureCache
{
    private Catalog source;
    private readonly Dictionary<string, WB.BitmapSource> cache =
        new(StringComparer.Ordinal);
    private readonly Queue<string> cacheOrder = new();
    private const int MaxCachedThumbnails = 256;

    internal void SetCatalog(Catalog catalog)
    {
        if (ReferenceEquals(source, catalog)) return;
        source = catalog;
        cache.Clear();
        cacheOrder.Clear();
    }

    internal WB.BitmapSource TryGet(CatalogItem item)
    {
        if (source?.Textures == null || item == null) return null;
        string path = item.Icon ?? source.TextureFor(item);
        if (string.IsNullOrWhiteSpace(path)) return null;
        string key = path + (item.Linked && item.Icon == null ? "|linked" : "");
        if (cache.TryGetValue(key, out var cached)) return cached;
        System.Drawing.Bitmap bitmap = source.Textures.Request(path);
        if (bitmap == null) return null;
        try
        {
            System.Drawing.Rectangle src = item.Linked && item.Icon == null &&
                LinkedGraphics.IsAtlas(bitmap, path)
                ? LinkedGraphics.Source(bitmap.Width, bitmap.Height, 0)
                : new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            using var stream = new MemoryStream();
            if (src.Width != bitmap.Width || src.Height != bitmap.Height)
            {
                using System.Drawing.Bitmap crop = bitmap.Clone(src, PixelFormat.Format32bppArgb);
                crop.Save(stream, ImageFormat.Png);
            }
            else bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            var image = new WB.BitmapImage();
            image.BeginInit();
            image.CacheOption = WB.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            while (cacheOrder.Count >= MaxCachedThumbnails)
                cache.Remove(cacheOrder.Dequeue());
            cacheOrder.Enqueue(key);
            cache[key] = image;
            return image;
        }
        catch (ArgumentException) { return null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }
}
