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
    private readonly WC.TextBlock previewTitle, previewDetail;
    private readonly MP_PixelReadout count;
    private readonly WC.Image preview;
    private readonly WC.StackPanel rows = new();
    private readonly WC.Grid rowLayer;
    private readonly WC.ScrollViewer scroll;
    private readonly MP_WpfScrollRail scrollRail;
    private readonly WC.Border slider;
    private readonly MP_LiltCursor travellingCursor;
    private readonly System.Windows.Threading.DispatcherTimer filterDelay =
        new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<string, WC.Border> leafRows = new(StringComparer.Ordinal);
    private readonly List<MP_StructureViewEntry> keyboardRows = new();
    private readonly Dictionary<string, WM.SolidColorBrush> leafInks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WM.SolidColorBrush> leafFills = new(StringComparer.Ordinal);
    private readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MP_StructureViewEntry> entries = Array.Empty<MP_StructureViewEntry>();
    private MP_StructureViewEntry active;
    private MP_ScrollOffsetProxy scrollProxy;
    private readonly MP_LiltScroll scrollMotion;
    private readonly MP_LiltWaveScroll rowWave = new();
    private double scrollTarget;

    private static WM.SolidColorBrush Solid(byte r, byte g, byte b)
        => new(WM.Color.FromRgb(r, g, b));
    private static WM.SolidColorBrush Solid(byte a, byte r, byte g, byte b)
        => new(WM.Color.FromArgb(a, r, g, b));
    private static readonly WM.SolidColorBrush Surface = Solid(10, 15, 21);
    private static readonly WM.SolidColorBrush Text = Solid(199, 231, 249);
    private static readonly WM.SolidColorBrush Muted = Solid(132, 169, 191);
    private static readonly WM.SolidColorBrush Cyan = Solid(181, 226, 250);

    internal MP_FluidStructureLibrary(
        Action<string, string> onSelected,
        Action<string, string> onOpened,
        Action<string, string> onRenamed)
    {
        selectedCallback = onSelected;
        openCallback = onOpened;
        renameCallback = onRenamed;
        MP_LiltTheme.Apply(this);
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
            Background = Solid(10, 17, 24), Foreground = Text,
            BorderBrush = Solid(66, 91, 103), BorderThickness = new W.Thickness(1),
            Padding = new W.Thickness(9, 5, 7, 3), FontSize = 16,
            FontFamily = MP_LiltTheme.WpfFont,
            CaretBrush = Cyan, SelectionBrush = Solid(93, 159, 179),
            VerticalContentAlignment = W.VerticalAlignment.Center
        };
        var watermark = new WC.TextBlock
        {
            Text = "搜索结构 / DefName",
            Foreground = Muted, IsHitTestVisible = false,
            FontFamily = search.FontFamily, FontSize = 16,
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

        count = new MP_PixelReadout
        {
            Text = "INDEX / 00", Foreground = Cyan, PixelSize = 2,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(4, 0, 0, 0)
        };
        count.ToolTip = "STRUCTURE INDEX";
        WC.Grid.SetRow(count, 1);
        layout.Children.Add(count);

        rowLayer = new WC.Grid { Background = Surface, ClipToBounds = true };
        slider = new WC.Border
        {
            Height = 35, Margin = new W.Thickness(2, 0, 2, 0),
            // Travelling focus is only a stroke; opaque selection stays
            // inside the actual row so it cannot cover folder headings.
            BorderBrush = Solid(220, 210, 247, 255),
            Background = Solid(0, 0, 0, 0),
            BorderThickness = new W.Thickness(1),
            CornerRadius = new W.CornerRadius(0),
            VerticalAlignment = W.VerticalAlignment.Top,
            Visibility = W.Visibility.Collapsed,
            IsHitTestVisible = false
        };
        travellingCursor = new MP_LiltCursor(slider, false);
        // The moving plate must sit BEHIND its text, not obscure it.
        WC.Panel.SetZIndex(rows, 0);
        WC.Panel.SetZIndex(slider, 1);
        rowLayer.Children.Add(rows); rowLayer.Children.Add(slider);
        scroll = new WC.ScrollViewer
        {
            Content = rowLayer, Background = Surface,
            Margin = new W.Thickness(0, 0, 12, 0),
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = WC.PanningMode.VerticalOnly
        };
        scrollProxy = new MP_ScrollOffsetProxy(scroll);
        scrollMotion = new MP_LiltScroll(scrollProxy, scroll);
        scroll.ScrollChanged += (_, change) =>
        {
            scrollTarget = Math.Clamp(scrollTarget, 0, Math.Max(0, scroll.ScrollableHeight));
            int focus = active != null && leafRows.TryGetValue(active.Id, out var selectedRow)
                ? rows.Children.IndexOf(selectedRow) : -1;
            if (Math.Abs(change.VerticalChange) > .001)
                rowWave.Shift(rows.Children.OfType<W.UIElement>(), focus,
                    change.VerticalChange);
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (!MP_Motion.Enabled) return;
            double attempted = scrollTarget - e.Delta / 120d * 110d;
            scrollTarget = Math.Clamp(attempted, 0, Math.Max(0, scroll.ScrollableHeight));
            scrollMotion.Nudge(attempted - scrollTarget, scroll.ViewportHeight);
            scrollMotion.To(scrollTarget);
            e.Handled = true;
        };
        var scrollHost = new WC.Grid { ClipToBounds = true };
        scrollHost.Children.Add(scroll);
        scrollRail = new MP_WpfScrollRail(scroll, scrollProxy,
            offset =>
            {
                scrollTarget = offset;
                scrollMotion.Jump(offset);
            });
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
            if (e.OriginalSource is WC.TextBox) return;
            if (e.Key is WI.Key.Down or WI.Key.Up)
            {
                if (keyboardRows.Count == 0) return;
                int current = keyboardRows.FindIndex(item =>
                    active != null && item.Id == active.Id);
                int target = e.Key == WI.Key.Down
                    ? Math.Min(keyboardRows.Count - 1, current + 1)
                    : current < 0 ? keyboardRows.Count - 1 : Math.Max(0, current - 1);
                MP_StructureViewEntry next = keyboardRows[target];
                Select(next);
                if (leafRows.TryGetValue(next.Id, out WC.Border focusRow))
                {
                    focusRow.BringIntoView();
                    focusRow.Focus();
                }
                e.Handled = true;
                return;
            }
            if (active == null) return;
            if (e.Key == WI.Key.Enter)
                openCallback?.Invoke(active.File, active.Id);
            else if (e.Key == WI.Key.F2)
                renameCallback?.Invoke(active.File, active.Id);
            else return;
            e.Handled = true;
        };
        Loaded += (_, _) => UpdateSlider(false);
        Unloaded += (_, _) => { filterDelay.Stop(); rowWave.Reset(); };
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
        count.Text = $"INDEX / {visible.Length:00}";
        rowWave.Reset();
        rows.Children.Clear();
        leafRows.Clear();
        keyboardRows.Clear();
        leafInks.Clear();
        leafFills.Clear();

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
                    {
                        keyboardRows.Add(entry);
                        rows.Children.Add(Leaf(entry, showFolder ? 1 : 0));
                    }
            }
        }
        if (visible.Length == 0)
            rows.Children.Add(Heading(query.Length > 0 ? "没有匹配的结构" : "暂无结构文件", 0, false));

        scrollTarget = Math.Clamp(scroll.VerticalOffset, 0, Math.Max(0, scroll.ScrollableHeight));
        rows.Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshSelectedInk();
            UpdateSlider(false);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
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
        // Narrow cyan marker, technical index and hairline separator resemble
        // an electronic library readout rather than a native TreeView node.
        var body = new WC.Grid { Margin = new W.Thickness(6, 0, 5, 0) };
        body.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(26) });
        body.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        var index = new WC.TextBlock
        {
            Text = label == "项目结构" ? "01" : label == "其他位置" ? "02" : "--",
            Foreground = Cyan, Effect = MP_LcdVisual.Glow(.16),
            FontFamily = MP_LiltTheme.WpfFont,
            FontSize = 10.5, FontWeight = W.FontWeights.SemiBold,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        var title = new WC.TextBlock
        {
            Text = label, Foreground = strong ? Text : Muted,
            FontFamily = search.FontFamily,
            FontWeight = W.FontWeights.Normal,
            FontSize = 16,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        WC.Grid.SetColumn(title, 1);
        body.Children.Add(index);
        body.Children.Add(title);
        return new WC.Border
        {
            Height = 34, Margin = new W.Thickness(0, 6, 1, 3),
            Background = Solid(16, 26, 35),
            BorderBrush = Solid(49, 89, 112),
            BorderThickness = new W.Thickness(2, 0, 0, 1),
            CornerRadius = new W.CornerRadius(1),
            Child = body
        };
    }

    private WC.Border Folder(string key, string name, int amount)
    {
        bool folded = collapsed.Contains(key);
        var hover = Solid(0, 80, 154, 174);
        var grid = new WC.Grid { Margin = new W.Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(23) });
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = W.GridLength.Auto });
        var chevron = new WC.TextBlock
        {
            Text = folded ? "▸" : "▾", Foreground = Cyan,
            FontSize = 12.5, VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Center
        };
        var title = new WC.TextBlock
        {
            Text = name, Foreground = Muted, FontSize = 16,
            FontFamily = search.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        var total = new WC.TextBlock
        {
            Text = amount.ToString("00"),
            FontFamily = MP_LiltTheme.WpfFont,
            FontSize = 14, Foreground = Solid(120, 179, 198),
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(7, 0, 1, 0)
        };
        WC.Grid.SetColumn(title, 1); WC.Grid.SetColumn(total, 2);
        grid.Children.Add(chevron); grid.Children.Add(title); grid.Children.Add(total);
        var row = new WC.Border
        {
            Height = 32, Margin = new W.Thickness(0, 1, 0, 1),
            Background = hover, Cursor = WI.Cursors.Hand,
            Child = grid
        };
        row.MouseEnter += (_, _) => MP_WpfMotion.Tint(hover,
            WM.Color.FromArgb(43, 73, 138, 161), 135);
        row.MouseLeave += (_, _) => MP_WpfMotion.Tint(hover,
            WM.Color.FromArgb(0, 73, 138, 161), 120);
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
            Height = 40, Margin = new W.Thickness(2, 1, 2, 1),
            CornerRadius = new W.CornerRadius(0),
            Background = Solid(0, 73, 125, 149),
            Cursor = WI.Cursors.Hand,
            Focusable = true,
            ClipToBounds = true
        };
        var content = new WC.Grid { Margin = new W.Thickness(8 + level * 8, 0, 6, 0) };
        content.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(13) });
        content.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        var marker = new WC.Border
        {
            Width = 4, Height = 4, CornerRadius = new W.CornerRadius(1),
            Background = Solid(113, 188, 209),
            VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            IsHitTestVisible = false
        };
        var ink = Solid(201, 239, 246);
        leafInks[entry.Id] = ink;
        leafFills[entry.Id] = (WM.SolidColorBrush)row.Background;
        var text = new WC.TextBlock
        {
            Text = entry.Label,
            Foreground = ink, FontSize = 16,
            FontFamily = search.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        WC.Grid.SetColumn(text, 1);
        content.Children.Add(marker);
        content.Children.Add(text);
        row.Child = content;
        row.ToolTip = entry.Label + "\n" + entry.Id + "\n" + Path.GetFileName(entry.File);
        row.MouseEnter += (_, _) =>
        {
            if (active?.Id != entry.Id)
                MP_WpfMotion.Tint(leafFills[entry.Id],
                    WM.Color.FromArgb(37, 92, 147, 170), 120);
        };
        row.MouseLeave += (_, _) =>
        {
            if (active?.Id != entry.Id)
                MP_WpfMotion.Tint(leafFills[entry.Id],
                    WM.Color.FromArgb(0, 73, 125, 149), 120);
        };
        row.MouseLeftButtonDown += (_, _) => travellingCursor.Press();
        row.MouseLeave += (_, _) => travellingCursor.Release();
        row.MouseLeftButtonUp += (_, e) =>
        {
            travellingCursor.Release();
            if (row.IsMouseOver)
            {
                row.Focus();
                Select(entry);
                if (e.ClickCount >= 2) openCallback?.Invoke(entry.File, entry.Id);
            }
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

    private void RefreshSelectedInk()
    {
        foreach (var (id, ink) in leafInks)
        {
            bool selected = active?.Id == id;
            MP_WpfMotion.Tint(ink, selected
                ? WM.Color.FromRgb(9, 22, 32)
                : WM.Color.FromRgb(201, 239, 246), 145);
            if (leafFills.TryGetValue(id, out var fill))
                MP_WpfMotion.Tint(fill, selected
                    ? WM.Color.FromRgb(175, 222, 245)
                    : WM.Color.FromArgb(0, 73, 125, 149), 145);
        }
    }

    private void Select(MP_StructureViewEntry entry)
    {
        active = entry;
        previewTitle.Text = entry.Label;
        previewDetail.Text = entry.HasXml ? "JSON / XML" : "结构";
        selectedCallback?.Invoke(entry.File, entry.Id);
        RefreshSelectedInk();
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
        travellingCursor.Move(to, row.ActualHeight, immediate: !animate || !wasVisible);
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
