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
    private readonly WC.TextBlock MP_previewState;
    private readonly WC.StackPanel rows = new();
    private readonly WC.Grid rowLayer;
    private readonly WC.ScrollViewer scroll;
    private readonly MP_WpfScrollRail scrollRail;
    private readonly WC.Border slider;
    private readonly MP_LiltCursor travellingCursor;
    private readonly System.Windows.Threading.DispatcherTimer filterDelay =
        new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<string, WC.Border> leafRows = new(StringComparer.Ordinal);
    private readonly List<WC.Border> keyboardNodes = new();
    private readonly Dictionary<string, Action> folderToggles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WM.SolidColorBrush> leafInks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WM.SolidColorBrush> leafFills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WC.Border> fileRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WM.SolidColorBrush> fileGlyphInks = new(StringComparer.Ordinal);
    private MP_StructureViewEntry[] visibleEntries = Array.Empty<MP_StructureViewEntry>();
    private readonly HashSet<string> collapsed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MP_StructureViewEntry> entries = Array.Empty<MP_StructureViewEntry>();
    private MP_StructureViewEntry active;
    private MP_ScrollOffsetProxy scrollProxy;
    private readonly MP_LiltScroll scrollMotion;
    private readonly Action<int> switchLibrary;
    private double scrollTarget;
    internal Action<int> MP_NavigateRegion { get; set; }
    internal Action MP_NavigationFocused { get; set; }

    private static WM.SolidColorBrush Solid(byte r, byte g, byte b)
        => new(WM.Color.FromRgb(r, g, b));
    private static WM.SolidColorBrush Solid(byte a, byte r, byte g, byte b)
        => new(WM.Color.FromArgb(a, r, g, b));
    private static readonly WM.SolidColorBrush Surface = MP_StudioTheme.Brush(MP_StudioTheme.Panel);
    private static readonly WM.SolidColorBrush Text = MP_StudioTheme.Brush(MP_StudioTheme.Text);
    private static readonly WM.SolidColorBrush Muted = MP_StudioTheme.Brush(MP_StudioTheme.Secondary);
    private static readonly WM.SolidColorBrush Cyan = MP_StudioTheme.Brush(MP_StudioTheme.Accent);

    internal MP_FluidStructureLibrary(
        Action<string, string> onSelected,
        Action<string, string> onOpened,
        Action<string, string> onRenamed,
        Action<int> onSwitchLibrary = null)
    {
        switchLibrary = onSwitchLibrary;
        selectedCallback = onSelected;
        openCallback = onOpened;
        renameCallback = onRenamed;
        MP_LiltTheme.Apply(this);
        GotKeyboardFocus += (_, _) => MP_NavigationFocused?.Invoke();
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
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Input), Foreground = Text,
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(1),
            Padding = new W.Thickness(9, 5, 7, 3), FontSize = 16,
            FontFamily = MP_LiltTheme.WpfFont,
            CaretBrush = Cyan, SelectionBrush = MP_StudioTheme.Brush(86, MP_StudioTheme.Text),
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
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Accent),
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
        // Bounce only content INSIDE the viewport; never move the
        // ScrollViewer itself into the search bar or preview panel.
        // A spring still smooths the scroll offset, but moving every row
        // independently caused repeated text/edge rasterization and flicker.
        scrollMotion = new MP_LiltScroll(scrollProxy);
        scroll.ScrollChanged += (_, _) =>
        {
            scrollTarget = Math.Clamp(scrollTarget, 0, Math.Max(0, scroll.ScrollableHeight));
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
            CornerRadius = new W.CornerRadius(2),
            Background = MP_StudioTheme.Brush(MP_StudioTheme.PanelRaised),
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(1),
            Margin = new W.Thickness(0, 8, 0, 5),
            Padding = new W.Thickness(6)
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
        MP_previewState = new WC.TextBlock
        {
            Text = "选择文件以查看预览",
            Foreground = Muted,
            FontFamily = MP_LiltTheme.WpfFont,
            FontSize = 13,
            TextAlignment = W.TextAlignment.Center,
            TextWrapping = W.TextWrapping.Wrap,
            VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            Margin = new W.Thickness(14, 0, 14, 0),
            IsHitTestVisible = false
        };
        previewGrid.Children.Add(MP_previewState);
        previewPanel.Child = previewGrid;
        WC.Grid.SetRow(previewPanel, 3);
        layout.Children.Add(previewPanel);

        var previewCaption = new WC.Grid();
        previewCaption.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        previewTitle = new WC.TextBlock
        {
            Foreground = Text, FontFamily = search.FontFamily,
            FontSize = 14, TextTrimming = W.TextTrimming.CharacterEllipsis,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        previewDetail = new WC.TextBlock
        {
            Foreground = Muted, FontFamily = search.FontFamily, FontSize = 12,
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
            if (e.Key == WI.Key.F6)
            {
                MP_NavigateRegion?.Invoke(WI.Keyboard.Modifiers == WI.ModifierKeys.Shift ? -1 : 1);
                e.Handled = MP_NavigateRegion != null;
                return;
            }
            if (e.Key == WI.Key.Tab && WI.Keyboard.Modifiers == WI.ModifierKeys.None
                && WI.Keyboard.FocusedElement is WC.Border focusedRow)
            {
                if (focusedRow.Tag is string folder && folderToggles.TryGetValue(folder, out Action flip))
                {
                    flip();
                    MP_UiSound.Play(MP_UiSound.Cue.Open);
                }
                else MP_NavigateRegion?.Invoke(-1);
                e.Handled = true;
                return;
            }
            if (e.OriginalSource is WC.TextBox ||
                WI.Keyboard.Modifiers != WI.ModifierKeys.None) return;
            if (e.Key is WI.Key.A or WI.Key.D)
            {
                switchLibrary?.Invoke(e.Key == WI.Key.A ? -1 : 1);
                e.Handled = switchLibrary != null;
                return;
            }
            // RimWorld-style W/S navigation is active while a structure row
            // has focus. Search text boxes retain normal typing.
            if (e.Key is WI.Key.Down or WI.Key.Up or WI.Key.W or WI.Key.S)
            {
                if (keyboardNodes.Count == 0) return;
                int current = keyboardNodes.FindIndex(row =>
                    ReferenceEquals(row, WI.Keyboard.FocusedElement));
                if (current < 0 && active != null &&
                    leafRows.TryGetValue(active.Id, out WC.Border selectedRow))
                    current = keyboardNodes.IndexOf(selectedRow);
                bool down = e.Key is WI.Key.Down or WI.Key.S;
                int target = MP_LibraryNavigation.Step(
                    current, down ? 1 : -1, keyboardNodes.Count);
                WC.Border row = keyboardNodes[target];
                row.BringIntoView();
                row.Focus();
                if (row.Tag is MP_StructureViewEntry next) Select(next);
                MP_UiSound.Play(MP_UiSound.Cue.Navigate);
                e.Handled = true;
                return;
            }
            if (e.Key is WI.Key.Enter or WI.Key.Space &&
                WI.Keyboard.FocusedElement is WC.Border focused &&
                focused.Tag is string folderKey &&
                folderToggles.TryGetValue(folderKey, out Action toggle))
            {
                toggle();
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
        Unloaded += (_, _) => filterDelay.Stop();
    }

    internal bool MP_HasSafeLayering =>
        slider.Background is WM.SolidColorBrush brush && brush.Color.A == 0
        && rowLayer.ClipToBounds && scroll.Margin.Right >= 12;

    internal void FocusSearch() => search.Focus();

    internal void FocusNavigation()
    {
        WC.Border target = keyboardNodes.FirstOrDefault(
            node => node.Tag is MP_StructureViewEntry entry && active?.Id == entry.Id)
            ?? keyboardNodes.FirstOrDefault();
        if (target != null) target.Focus();
        else Focus();
    }

    internal void ClearSearch()
    {
        search.Clear();
        // TextChanged starts the timer. Stop it AFTER clearing so a second
        // delayed rebuild cannot repaint the list again 150ms later.
        filterDelay.Stop();
        Rebuild();
    }

    internal void SelectById(string id)
    {
        MP_StructureViewEntry found = entries.FirstOrDefault(e =>
            e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (found == null) return;
        string key = (IsProject(found.File) ? "项目结构" : "其他位置")
            + "/" + GroupPath(found.File);
        if (collapsed.Contains(key) || !leafRows.ContainsKey(found.Id))
        {
            collapsed.Remove(key);
            Rebuild();
        }
        Select(found);
        if (leafRows.TryGetValue(found.Id, out WC.Border row))
            row.BringIntoView();
    }

    internal void SetEntries(IReadOnlyList<MP_StructureViewEntry> next)
    {
        next ??= Array.Empty<MP_StructureViewEntry>();
        // Opening/switching a document often re-queries the same library.
        // Avoid tearing down every row and starting dozens of WPF animations.
        if (entries.SequenceEqual(next)) return;
        entries = next.ToArray();
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
        visibleEntries = visible;
        if (query.Length > 0) collapsed.Clear();
        count.Text = $"INDEX / {visible.Length:00}";
        rows.BeginAnimation(OpacityProperty, null);
        rows.Opacity = 1;
        rows.Children.Clear();
        leafRows.Clear();
        keyboardNodes.Clear();
        folderToggles.Clear();
        leafInks.Clear();
        leafFills.Clear();
        fileRows.Clear();
        fileGlyphInks.Clear();

        foreach (var group in visible.GroupBy(e => IsProject(e.File) ? "项目结构" : "其他位置"))
        {
            rows.Children.Add(Heading(group.Key, 0, true));
            foreach (var folder in group.GroupBy(e => GroupPath(e.File)))
            {
                string key = group.Key + "/" + folder.Key;
                bool showFolder = folder.Key != ".";
                var folderChildren = folder
                    .Select(entry => Leaf(entry, showFolder ? 1 : 0)).ToArray();
                if (showFolder)
                {
                    var folderItems = new WC.StackPanel();
                    foreach (var child in folderChildren) folderItems.Children.Add(child);
                    double fullHeight = folderChildren.Length * 44d;
                    var expansion = new WC.Border
                    {
                        Child = folderItems, ClipToBounds = true,
                        Tag = key,
                        Height = collapsed.Contains(key) ? 0 : fullHeight
                    };
                    rows.Children.Add(Folder(key, folder.Key, folderChildren.Length,
                        expansion, fullHeight));
                    rows.Children.Add(expansion);
                }
                else
                    foreach (var child in folderChildren)
                        rows.Children.Add(child);
            }
        }
        if (visible.Length == 0)
            rows.Children.Add(Heading(query.Length > 0 ? "没有匹配的结构" : "暂无结构文件", 0, false));

        RefreshKeyboardRows();
        // Set colors directly on freshly created visuals; no entry fade or
        // color clocks on every tree refresh.
        RefreshSelectedInk(false);
        scrollTarget = Math.Clamp(scroll.VerticalOffset, 0,
            Math.Max(0, scroll.ScrollableHeight));
        slider.Visibility = W.Visibility.Collapsed;
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
            Background = MP_StudioTheme.Brush(MP_StudioTheme.PanelRaised),
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(2, 0, 0, 1),
            CornerRadius = new W.CornerRadius(1),
            Child = body
        };
    }

    // Folder rows have their own raised surface, outlined folder icon,
    // disclosure arrow and file count. Children are hidden in place instead
    // of destroying and recreating the entire WPF tree on each click.
    private WC.Border Folder(string key, string name, int count,
        WC.Border expansion, double fullHeight)
    {
        var hover = MP_StudioTheme.Brush(MP_StudioTheme.PanelRaised);
        var grid = new WC.Grid { Margin = new W.Thickness(7, 0, 7, 0) };
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(16) });
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(22) });
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = W.GridLength.Auto });
        var arrow = new WC.TextBlock
        {
            Text = collapsed.Contains(key) ? "▸" : "▾",
            Foreground = Cyan, FontSize = 14,
            FontFamily = MP_LiltTheme.WpfFont,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        var glyph = MP_StructureGlyph(true);
        WC.Grid.SetColumn(glyph, 1);
        var title = new WC.TextBlock
        {
            Text = name, Foreground = Text, FontSize = 15,
            FontWeight = W.FontWeights.SemiBold, FontFamily = search.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis,
            ToolTip = name
        };
        WC.Grid.SetColumn(title, 2);
        var total = new WC.TextBlock
        {
            Text = count.ToString("00"),
            FontFamily = MP_LiltTheme.WpfFont, FontSize = 13, Foreground = Muted,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(7, 0, 0, 0)
        };
        WC.Grid.SetColumn(total, 3);
        grid.Children.Add(arrow);
        grid.Children.Add(glyph);
        grid.Children.Add(title);
        grid.Children.Add(total);
        var row = new WC.Border
        {
            Height = 39, Margin = new W.Thickness(0, 3, 1, 3),
            Background = hover, BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(1, 0, 0, 1),
            Cursor = WI.Cursors.Hand, Child = grid,
            Focusable = true, FocusVisualStyle = null, Tag = key,
            ToolTip = $"文件夹：{name}（{count} 个结构）"
        };
        row.MouseEnter += (_, _) => hover.Color =
            MP_StudioTheme.WpfColor(MP_StudioTheme.Hover);
        row.MouseLeave += (_, _) =>
        {
            if (!row.IsKeyboardFocusWithin)
                hover.Color = MP_StudioTheme.WpfColor(MP_StudioTheme.PanelRaised);
        };
        row.GotKeyboardFocus += (_, _) => hover.Color =
            MP_StudioTheme.WpfColor(MP_StudioTheme.Hover);
        row.LostKeyboardFocus += (_, _) =>
        {
            if (!row.IsMouseOver)
                hover.Color = MP_StudioTheme.WpfColor(MP_StudioTheme.PanelRaised);
        };
        void Toggle()
        {
            bool folded = collapsed.Add(key);
            if (!folded) collapsed.Remove(key);
            arrow.Text = folded ? "▸" : "▾";
            // Expand/collapse a clipped child panel. Never recreate its rows
            // or use ScaleTransform (which squashes font glyphs/bitmaps).
            double destination = folded ? 0 : fullHeight;
            double start = expansion.ActualHeight;
            expansion.BeginAnimation(W.FrameworkElement.HeightProperty, null);
            expansion.Height = destination;
            if (MP_Motion.Enabled && Math.Abs(destination - start) > 0.5)
                expansion.BeginAnimation(W.FrameworkElement.HeightProperty,
                    new WA.DoubleAnimation(start, destination,
                        TimeSpan.FromMilliseconds(205))
                    {
                        EasingFunction = new WA.CubicEase
                            { EasingMode = WA.EasingMode.EaseOut }
                    });
            RefreshKeyboardRows();
            slider.Visibility = W.Visibility.Collapsed;
            // Defer the extent correction until layout has completed, so
            // a collapsing group cannot leave a stale scroll target.
            rows.Dispatcher.BeginInvoke(new Action(() =>
            {
                double offset = Math.Clamp(scroll.VerticalOffset, 0,
                    Math.Max(0, scroll.ScrollableHeight));
                scrollTarget = offset;
                scrollMotion.Jump(offset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        folderToggles[key] = Toggle;
        row.MouseLeftButtonUp += (_, e) =>
        {
            row.Focus();
            Toggle();
            MP_UiSound.Play(MP_UiSound.Cue.Toggle);
            e.Handled = true;
        };
        return row;
    }

    private static W.FrameworkElement MP_StructureGlyph(bool folder)
    {
        var outline = new System.Windows.Shapes.Path
        {
            Data = WM.Geometry.Parse(folder
                ? "M 1,5 L 1,13 L 15,13 L 15,5 L 7,5 L 5,3 L 1,3 Z"
                : "M 3,1 L 11,1 L 15,5 L 15,15 L 3,15 Z M 11,1 L 11,5 L 15,5"),
            Stroke = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            StrokeThickness = 1, StrokeLineJoin = WM.PenLineJoin.Miter,
            Fill = MP_StudioTheme.Brush(0, MP_StudioTheme.Text),
            SnapsToDevicePixels = true
        };
        return new WC.Viewbox
        {
            Child = outline, Width = 15, Height = 15,
            VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private void RefreshKeyboardRows()
    {
        // Traverse the ACTUAL rendered order, including folder headers.
        // Using entries sorted by label ignored folder grouping, making
        // keyboard W/S jump unpredictably across different folders.
        keyboardNodes.Clear();
        foreach (var element in rows.Children)
        {
            if (element is not WC.Border node) continue;
            if (node.Tag is MP_StructureViewEntry)
                keyboardNodes.Add(node);
            else if (node.Tag is string folderKey)
            {
                if (node.Child is WC.Grid)
                    keyboardNodes.Add(node);
                else if (node.Child is WC.StackPanel childPanel &&
                         !collapsed.Contains(folderKey))
                    foreach (var child in childPanel.Children.OfType<WC.Border>())
                        keyboardNodes.Add(child);
            }
        }
    }

    private WC.Border Leaf(MP_StructureViewEntry entry, int level)
    {
        var row = new WC.Border
        {
            Height = 42, Margin = new W.Thickness(0, 1, 1, 1),
            CornerRadius = new W.CornerRadius(0),
            Background = MP_StudioTheme.Brush(0, MP_StudioTheme.Text),
            BorderBrush = MP_StudioTheme.Brush(0, MP_StudioTheme.Text),
            BorderThickness = new W.Thickness(1),
            Cursor = WI.Cursors.Hand,
            Focusable = true, Tag = entry,
            ClipToBounds = true
        };
        var content = new WC.Grid { Margin = new W.Thickness(17 + level * 8, 0, 6, 0) };
        content.ColumnDefinitions.Add(new WC.ColumnDefinition { Width = new W.GridLength(24) });
        content.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        var marker = MP_StructureGlyph(false);
        fileGlyphInks[entry.Id] = (WM.SolidColorBrush)
            ((System.Windows.Shapes.Path)((WC.Viewbox)marker).Child).Stroke;
        var ink = MP_StudioTheme.Brush(MP_StudioTheme.Text);
        leafInks[entry.Id] = ink;
        leafFills[entry.Id] = (WM.SolidColorBrush)row.Background;
        fileRows[entry.Id] = row;
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
                    MP_StudioTheme.WpfOpacity(35, MP_StudioTheme.Text), 120);
        };
        row.MouseLeave += (_, _) =>
        {
            if (active?.Id != entry.Id)
                MP_WpfMotion.Tint(leafFills[entry.Id],
                    MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 120);
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (row.IsMouseOver)
            {
                row.Focus();
                Select(entry);
                MP_UiSound.Play(MP_UiSound.Cue.Navigate);
                if (e.ClickCount >= 2) openCallback?.Invoke(entry.File, entry.Id);
            }
            e.Handled = true;
        };
        var menu = new WC.ContextMenu
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.PanelRaised), Foreground = Text,
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider)
        };
        var rename = new WC.MenuItem { Header = "重命名…" };
        rename.Click += (_, _) => renameCallback?.Invoke(entry.File, entry.Id);
        menu.Items.Add(rename);
        row.ContextMenu = menu;
        row.MouseRightButtonDown += (_, _) => Select(entry);
        leafRows[entry.Id] = row;
        return row;
    }

    private void RefreshSelectedInk(bool animate = true)
    {
        foreach (var (id, ink) in leafInks)
        {
            bool selected = active?.Id == id;
            WM.Color foreground = MP_StudioTheme.WpfColor(
                selected ? MP_StudioTheme.ActiveText : MP_StudioTheme.Text);
            WM.Color background = selected
                ? MP_StudioTheme.WpfColor(MP_StudioTheme.Active)
                : MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text);
            if (animate && !MP_Motion.Enabled) animate = false;
            if (ink.Color != foreground)
            {
                if (animate) MP_WpfMotion.Tint(ink, foreground, 115);
                else ink.Color = foreground;
            }
            if (leafFills.TryGetValue(id, out var fill) && fill.Color != background)
            {
                if (animate) MP_WpfMotion.Tint(fill, background, 115);
                else fill.Color = background;
            }
            if (fileRows.TryGetValue(id, out var border))
                ((WM.SolidColorBrush)border.BorderBrush).Color =
                    selected ? MP_StudioTheme.WpfColor(MP_StudioTheme.Active)
                    : MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text);
            if (fileGlyphInks.TryGetValue(id, out var glyphInk))
                glyphInk.Color = selected
                    ? MP_StudioTheme.WpfColor(MP_StudioTheme.ActiveText)
                    : MP_StudioTheme.WpfColor(MP_StudioTheme.Text);
        }
    }

    private void Select(MP_StructureViewEntry entry)
    {
        if (active?.Id == entry.Id && active.File == entry.File) return;
        active = entry;
        previewTitle.Text = entry.Label;
        previewTitle.ToolTip = entry.Label;
        previewDetail.Text = entry.HasXml ? "JSON / XML" : "结构";
        preview.Source = null;
        MP_previewState.Text = "正在准备预览…";
        MP_previewState.Visibility = W.Visibility.Visible;
        // Instant inversion is both clearer and more stable than animating
        // the fill/text separately while the WPF host swaps pages.
        RefreshSelectedInk(false);
        slider.Visibility = W.Visibility.Collapsed;
        selectedCallback?.Invoke(entry.File, entry.Id);
    }

    private void UpdateSlider(bool animate)
    {
        // Focus is bounded by the selected row itself. An overlay travelling
        // across folder headings produced misleading focus and overlaps.
        slider.Visibility = W.Visibility.Collapsed;
    }

    internal void SetPreviewStatus(string id, string message)
    {
        if (active == null || id != active.Id) return;
        if (!string.IsNullOrWhiteSpace(message))
        {
            preview.Source = null;
            MP_previewState.Text = message;
            MP_previewState.Visibility = W.Visibility.Visible;
        }
    }

    internal void SetPreviewBytes(string id, byte[] bytes)
    {
        if (active == null || id != active.Id) return;
        if (bytes == null || bytes.Length == 0)
        {
            SetPreviewStatus(id, "暂无可用预览");
            return;
        }
        try
        {
            using var data = new MemoryStream(bytes, writable: false);
            var bitmap = new WB.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = WB.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 420;
            bitmap.StreamSource = data;
            bitmap.EndInit();
            bitmap.Freeze();
            preview.Source = bitmap;
            MP_previewState.Visibility = W.Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is ArgumentException or
            InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            SetPreviewStatus(id, "预览文件无法读取");
        }
    }

    internal void SetPreview(string id, System.Drawing.Image image)
    {
        if (active == null || id != active.Id) return;
        if (image == null) { SetPreviewStatus(id, "暂无可用预览"); return; }
        try
        {
            using var data = new MemoryStream();
            image.Save(data, ImageFormat.Png);
            SetPreviewBytes(id, data.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or
            InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            SetPreviewStatus(id, "预览文件无法读取");
        }
    }
}
