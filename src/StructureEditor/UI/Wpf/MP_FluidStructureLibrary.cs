using System.Drawing.Imaging;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WI = System.Windows.Input;
using WB = System.Windows.Media.Imaging;

namespace StructureEditor;

internal sealed record MP_StructureViewEntry(string File, string Id, string Label, bool HasXml);

// Structure library is a fully composited WPF surface. WinForms remains the
// workspace controller for rename, open and preview generation, but never draws
// the library navigation or selected rows.
internal sealed class MP_FluidStructureLibrary : WC.UserControl
{
    private readonly Action<string, string> selectedCallback;
    private readonly Action<string, string> openCallback;
    private readonly Action<string, string> renameCallback;
    private readonly WC.TextBox search;
    private readonly WC.TextBlock count, previewTitle, previewDetail;
    private readonly WC.Image preview;
    private readonly WC.StackPanel rows = new();
    private readonly WC.Grid rowLayer;
    private readonly WC.ScrollViewer scroll;
    private readonly MP_WpfScrollRail scrollRail;
    private readonly WC.Border slider;
    private readonly WM.TranslateTransform sliderOffset = new();
    private readonly System.Windows.Threading.DispatcherTimer filterDelay =
        new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<string, WC.Border> leafRows = new(StringComparer.Ordinal);
    private readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MP_StructureViewEntry> entries = Array.Empty<MP_StructureViewEntry>();
    private MP_StructureViewEntry active;
    private MP_ScrollOffsetProxy scrollProxy;
    private double scrollTarget;

    private static WM.SolidColorBrush Solid(byte r, byte g, byte b)
        => new(WM.Color.FromRgb(r, g, b));
    private static WM.SolidColorBrush Solid(byte a, byte r, byte g, byte b)
        => new(WM.Color.FromArgb(a, r, g, b));
    private static readonly WM.SolidColorBrush Surface = Solid(33, 43, 52);
    private static readonly WM.SolidColorBrush Text = Solid(224, 234, 240);
    private static readonly WM.SolidColorBrush Muted = Solid(150, 178, 191);
    private static readonly WM.SolidColorBrush Cyan = Solid(165, 214, 230);

    internal MP_FluidStructureLibrary(
        Action<string, string> onSelected,
        Action<string, string> onOpened,
        Action<string, string> onRenamed)
    {
        selectedCallback = onSelected;
        openCallback = onOpened;
        renameCallback = onRenamed;
        Background = Surface;
        var layout = new WC.Grid
        {
            Background = Surface,
            Margin = new W.Thickness(7, 8, 7, 7),
            ClipToBounds = true
        };
        layout.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(37) });
        layout.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(29) });
        layout.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(1, W.GridUnitType.Star) });
        layout.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(164) });
        layout.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(27) });

        var searchHost = new WC.Grid { Margin = new W.Thickness(0, 0, 0, 5) };
        search = new WC.TextBox
        {
            Background = Solid(44, 59, 69), Foreground = Text,
            BorderBrush = Solid(66, 91, 103), BorderThickness = new W.Thickness(1),
            Padding = new W.Thickness(9, 5, 7, 3), FontSize = 12.5,
            FontFamily = new WM.FontFamily("Microsoft YaHei UI"),
            CaretBrush = Cyan, SelectionBrush = Solid(93, 159, 179),
            VerticalContentAlignment = W.VerticalAlignment.Center
        };
        var watermark = new WC.TextBlock
        {
            Text = "搜索结构 / DefName",
            Foreground = Muted, IsHitTestVisible = false,
            FontFamily = search.FontFamily, FontSize = 12,
            Margin = new W.Thickness(10, 7, 0, 0)
        };
        searchHost.Children.Add(search);
        searchHost.Children.Add(watermark);
        MP_WpfMotion.AttachSearch(search);
        search.TextChanged += (_, _) =>
        {
            watermark.Visibility = search.Text.Length == 0
                ? W.Visibility.Visible : W.Visibility.Collapsed;
            filterDelay.Stop(); filterDelay.Start();
        };
        filterDelay.Tick += (_, _) => { filterDelay.Stop(); Rebuild(); };
        WC.Grid.SetRow(searchHost, 0);
        layout.Children.Add(searchHost);

        count = new WC.TextBlock
        {
            Text = "结构库", Foreground = Muted,
            FontSize = 11.5, FontFamily = search.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        WC.Grid.SetRow(count, 1);
        layout.Children.Add(count);

        rowLayer = new WC.Grid { Background = Surface };
        slider = new WC.Border
        {
            Height = 35, Margin = new W.Thickness(2, 0, 2, 0),
            BorderBrush = Solid(125, 128, 203, 219),
            Background = Solid(62, 86, 157, 181),
            BorderThickness = new W.Thickness(1),
            CornerRadius = new W.CornerRadius(5),
            VerticalAlignment = W.VerticalAlignment.Top,
            RenderTransform = sliderOffset,
            Visibility = W.Visibility.Collapsed,
            IsHitTestVisible = false
        };
        WC.Panel.SetZIndex(rows, 0);
        WC.Panel.SetZIndex(slider, 1);
        rowLayer.Children.Add(rows); rowLayer.Children.Add(slider);
        scroll = new WC.ScrollViewer
        {
            Content = rowLayer, Background = Surface,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = WC.PanningMode.VerticalOnly
        };
        scrollProxy = new MP_ScrollOffsetProxy(scroll);
        scroll.ScrollChanged += (_, _) =>
        {
            scrollTarget = Math.Clamp(scrollTarget, 0, Math.Max(0, scroll.ScrollableHeight));
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (!MP_Motion.Enabled) return;
            scrollTarget = Math.Clamp(scrollTarget - e.Delta / 120d * 110d,
                0, Math.Max(0, scroll.ScrollableHeight));
            MP_WpfMotion.Scroll(scrollProxy, scrollTarget, 230);
            e.Handled = true;
        };
        var scrollHost = new WC.Grid { ClipToBounds = true };
        scrollHost.Children.Add(scroll);
        scrollRail = new MP_WpfScrollRail(scroll, scrollProxy,
            offset => scrollTarget = offset);
        WC.Panel.SetZIndex(scrollRail, 2);
        scrollHost.Children.Add(scrollRail);
        WC.Grid.SetRow(scrollHost, 2);
        layout.Children.Add(scrollHost);

        var previewPanel = new WC.Border
        {
            CornerRadius = new W.CornerRadius(6),
            Background = Solid(29, 38, 47),
            Margin = new W.Thickness(0, 8, 0, 5),
            Padding = new W.Thickness(5)
        };
        var previewGrid = new WC.Grid { ClipToBounds = true };
        preview = new WC.Image
        {
            Stretch = WM.Stretch.Uniform,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Center,
            RenderTransformOrigin = new W.Point(.5, .5),
            RenderTransform = new WM.ScaleTransform(1, 1)
        };
        previewGrid.Children.Add(preview);
        previewPanel.Child = previewGrid;
        WC.Grid.SetRow(previewPanel, 3);
        layout.Children.Add(previewPanel);

        var previewCaption = new WC.Grid();
        previewCaption.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        previewTitle = new WC.TextBlock
        {
            Foreground = Text, FontFamily = search.FontFamily,
            FontSize = 12, TextTrimming = W.TextTrimming.CharacterEllipsis,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        previewDetail = new WC.TextBlock
        {
            Foreground = Muted, FontFamily = search.FontFamily, FontSize = 10,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            HorizontalAlignment = W.HorizontalAlignment.Right,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        previewCaption.Children.Add(previewTitle);
        previewCaption.Children.Add(previewDetail);
        WC.Grid.SetRow(previewCaption, 4);
        layout.Children.Add(previewCaption);

        Content = layout;
        PreviewKeyDown += (_, e) =>
        {
            if (active == null || e.OriginalSource is WC.TextBox) return;
            if (e.Key == WI.Key.Enter)
                openCallback?.Invoke(active.File, active.Id);
            else if (e.Key == WI.Key.F2)
                renameCallback?.Invoke(active.File, active.Id);
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) => UpdateSlider(false);
        Unloaded += (_, _) => filterDelay.Stop();
    }

    internal void FocusSearch() => search.Focus();

    internal void ClearSearch()
    {
        filterDelay.Stop();
        search.Clear();
        Rebuild();
    }

    internal void SelectById(string id)
    {
        MP_StructureViewEntry found = entries.FirstOrDefault(e =>
            e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (found == null) return;
        if (!leafRows.ContainsKey(found.Id))
        {
            collapsed.Clear();
            Rebuild();
        }
        Select(found);
        if (leafRows.TryGetValue(found.Id, out WC.Border row))
            row.BringIntoView();
    }

    internal void SetEntries(IReadOnlyList<MP_StructureViewEntry> next)
    {
        entries = next ?? Array.Empty<MP_StructureViewEntry>();
        if (active != null)
            active = entries.FirstOrDefault(e => e.Id == active.Id);
        Rebuild();
    }

    private void Rebuild()
    {
        string query = search.Text.Trim();
        var visible = entries
            .Where(e => query.Length == 0 ||
                e.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.File.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        count.Text = $"{visible.Length} 个结构";
        rows.Children.Clear();
        leafRows.Clear();

        foreach (var group in visible.GroupBy(e => IsProject(e.File) ? "项目结构" : "其他位置"))
        {
            rows.Children.Add(Heading(group.Key, 0, true));
            foreach (var folder in group.GroupBy(e => GroupPath(e.File)))
            {
                string key = group.Key + "/" + folder.Key;
                bool showFolder = folder.Key != ".";
                if (showFolder)
                    rows.Children.Add(Folder(key, folder.Key, folder.Count()));
                if (!showFolder || !collapsed.Contains(key) || query.Length > 0)
                    foreach (var entry in folder)
                        rows.Children.Add(Leaf(entry, showFolder ? 1 : 0));
            }
        }
        if (visible.Length == 0)
            rows.Children.Add(Heading(query.Length > 0 ? "没有匹配的结构" : "暂无结构文件", 0, false));

        scrollTarget = Math.Clamp(scroll.VerticalOffset, 0, Math.Max(0, scroll.ScrollableHeight));
        rows.Dispatcher.BeginInvoke(new Action(() => UpdateSlider(false)),
            System.Windows.Threading.DispatcherPriority.Loaded);
        if (MP_Motion.Enabled && IsVisible)
            rows.BeginAnimation(OpacityProperty,
                new WA.DoubleAnimation(.65, 1d, TimeSpan.FromMilliseconds(165))
                { EasingFunction = new WA.CubicEase
                    { EasingMode = WA.EasingMode.EaseOut } });
    }

    private static bool IsProject(string file)
    {
        try
        {
            string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Program.StructureLibraryFolder));
            string filePath = Path.GetFullPath(file);
            return filePath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string GroupPath(string file)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(file)) ?? "";
        if (IsProject(file))
        {
            string relative = Path.GetRelativePath(Program.StructureLibraryFolder, folder);
            return relative == "." ? "." : relative;
        }
        return Path.GetFileName(folder);
    }

    private WC.Border Heading(string label, int level, bool strong)
    {
        var text = new WC.TextBlock
        {
            Text = label, Foreground = strong ? Cyan : Muted,
            FontFamily = search.FontFamily, FontWeight = strong
                ? W.FontWeights.SemiBold : W.FontWeights.Normal,
            FontSize = strong ? 12 : 11.5,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(9 + level * 12, 0, 0, 0)
        };
        return new WC.Border { Child = text, Height = 30 };
    }

    private WC.Border Folder(string key, string name, int amount)
    {
        bool folded = collapsed.Contains(key);
        var text = new WC.TextBlock
        {
            Text = (folded ? "▸  " : "▾  ") + name + $"  ({amount})",
            Foreground = Muted, FontSize = 11.5, FontFamily = search.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(9, 0, 0, 0),
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        var row = new WC.Border
        {
            Height = 29, Child = text,
            Background = Solid(0, 0, 0, 0),
            Cursor = WI.Cursors.Hand
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!collapsed.Add(key)) collapsed.Remove(key);
            Rebuild();
            e.Handled = true;
        };
        return row;
    }

    private WC.Border Leaf(MP_StructureViewEntry entry, int level)
    {
        var row = new WC.Border
        {
            Height = 37, Margin = new W.Thickness(1, 1, 1, 1),
            CornerRadius = new W.CornerRadius(4),
            Background = Solid(0, 68, 112, 128),
            Cursor = WI.Cursors.Hand
        };
        var text = new WC.TextBlock
        {
            Text = entry.Label,
            Foreground = Text, FontSize = 12.3,
            FontFamily = search.FontFamily,
            Margin = new W.Thickness(16 + level * 12, 0, 5, 0),
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        row.Child = text;
        row.ToolTip = entry.Label + "\n" + entry.Id + "\n" + Path.GetFileName(entry.File);
        row.MouseLeftButtonUp += (_, e) =>
        {
            Select(entry);
            if (e.ClickCount >= 2) openCallback?.Invoke(entry.File, entry.Id);
            e.Handled = true;
        };
        var menu = new WC.ContextMenu
        {
            Background = Solid(39, 53, 63), Foreground = Text,
            BorderBrush = Solid(68, 96, 110)
        };
        var rename = new WC.MenuItem { Header = "重命名…" };
        rename.Click += (_, _) => renameCallback?.Invoke(entry.File, entry.Id);
        menu.Items.Add(rename);
        row.ContextMenu = menu;
        row.MouseRightButtonDown += (_, _) => Select(entry);
        leafRows[entry.Id] = row;
        return row;
    }

    private void Select(MP_StructureViewEntry entry)
    {
        active = entry;
        previewTitle.Text = entry.Label;
        previewDetail.Text = entry.HasXml ? "JSON / XML" : "结构";
        selectedCallback?.Invoke(entry.File, entry.Id);
        UpdateSlider(true);
    }

    private void UpdateSlider(bool animate)
    {
        if (active == null || !leafRows.TryGetValue(active.Id, out WC.Border row) ||
            row.ActualHeight <= 0)
        {
            slider.Visibility = W.Visibility.Collapsed;
            return;
        }
        double to = row.TranslatePoint(new W.Point(0, 0), rowLayer).Y;
        if (!double.IsFinite(to)) return;
        bool wasVisible = slider.Visibility == W.Visibility.Visible;
        slider.Visibility = W.Visibility.Visible;
        slider.Height = row.ActualHeight;
        MP_WpfMotion.Translate(sliderOffset, WM.TranslateTransform.YProperty,
            to, animate && wasVisible ? 245 : 0);
    }

    internal void SetPreview(string id, System.Drawing.Image image)
    {
        if (active == null || id != active.Id) return;
        if (image == null) { preview.Source = null; return; }
        try
        {
            using var data = new MemoryStream();
            image.Save(data, ImageFormat.Png);
            data.Position = 0;
            var bitmap = new WB.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = WB.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = data;
            bitmap.EndInit();
            bitmap.Freeze();
            preview.Source = bitmap;
            if (!MP_Motion.Enabled) return;
            preview.BeginAnimation(OpacityProperty,
                new WA.DoubleAnimation(.15, 1d, TimeSpan.FromMilliseconds(235))
                { EasingFunction = new WA.CubicEase
                    { EasingMode = WA.EasingMode.EaseOut } });
            if (preview.RenderTransform is WM.ScaleTransform scale)
            {
                scale.BeginAnimation(WM.ScaleTransform.ScaleXProperty,
                    new WA.DoubleAnimation(.97, 1, TimeSpan.FromMilliseconds(235)));
                scale.BeginAnimation(WM.ScaleTransform.ScaleYProperty,
                    new WA.DoubleAnimation(.97, 1, TimeSpan.FromMilliseconds(235)));
            }
        }
        catch (ArgumentException) { preview.Source = null; }
        catch (System.Runtime.InteropServices.ExternalException) { preview.Source = null; }
    }
}
