using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
using WI = System.Windows.Input;

namespace StructureEditor;

internal sealed record MP_DocumentVisualState(int Index, string Title, bool Active);

// Top-level document strip rebuilt in WPF. Close targets are separate hit
// targets and always delegate to MainForm's existing save-confirmation flow.
internal sealed class MP_FluidDocumentBar : WC.UserControl
{
    private readonly Action<int, bool> invoke;
    private readonly WC.Grid layer;
    private readonly WC.ScrollViewer scrollingTabs;
    private readonly WC.StackPanel slots;
    private readonly WC.Border capsule;
    private readonly MP_LiltCursor travellingCursor;
    private readonly List<(WC.Border Container, WC.TextBlock Title)> visuals = new();
    private MP_DocumentVisualState[] current = Array.Empty<MP_DocumentVisualState>();
    private int active = -1;
    private bool ready;

    private static WM.SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(WM.Color.FromRgb(r, g, b));
    private static WM.SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(WM.Color.FromArgb(a, r, g, b));

    internal MP_FluidDocumentBar(Action<int, bool> onAction)
    {
        invoke = onAction;
        MP_LiltTheme.Apply(this);
        Background = MP_StudioTheme.Brush(MP_StudioTheme.Toolbar);
        HorizontalAlignment = W.HorizontalAlignment.Stretch;
        VerticalAlignment = W.VerticalAlignment.Stretch;
        layer = new WC.Grid
        {
            Height = 32, HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        capsule = new WC.Border
        {
            Height = 27,
            Width = 115,
            Margin = new W.Thickness(2, 0, 2, 0),
            VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Left,
            CornerRadius = new W.CornerRadius(0),
            Background = Brush(0, 0, 0, 0),
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Accent),
            BorderThickness = new W.Thickness(1),
            Visibility = W.Visibility.Collapsed,
            IsHitTestVisible = false
        };
        travellingCursor = new MP_LiltCursor(capsule, true);
        slots = new WC.StackPanel { Orientation = WC.Orientation.Horizontal,
            HorizontalAlignment = W.HorizontalAlignment.Center };
        WC.Panel.SetZIndex(slots, 1);
        WC.Panel.SetZIndex(capsule, 0);
        layer.Children.Add(capsule);
        layer.Children.Add(slots);
        scrollingTabs = new WC.ScrollViewer
        {
            Content = layer,
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            VerticalAlignment = W.VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        scrollingTabs.PreviewMouseWheel += (_, e) =>
        {
            scrollingTabs.ScrollToHorizontalOffset(Math.Clamp(
                scrollingTabs.HorizontalOffset - e.Delta * .4,
                0, scrollingTabs.ScrollableWidth));
            e.Handled = true;
        };
        Content = scrollingTabs;
        Loaded += (_, _) => Move(false);
        SizeChanged += (_, _) => Move(false);
    }

    internal void SetTabs(MP_DocumentVisualState[] next)
    {
        next ??= Array.Empty<MP_DocumentVisualState>();
        bool structureChanged = next.Length != current.Length;
        for (int i = 0; !structureChanged && i < next.Length; i++)
            structureChanged = next[i].Title != current[i].Title
                || next[i].Index != current[i].Index;
        current = next;
        if (structureChanged)
        {
            slots.Children.Clear();
            visuals.Clear();
            foreach (var entry in next)
            {
                var content = new WC.Grid { Margin = new W.Thickness(4, 0, 4, 0) };
                content.ColumnDefinitions.Add(new WC.ColumnDefinition
                    { Width = new W.GridLength(1, W.GridUnitType.Star) });
                content.ColumnDefinitions.Add(new WC.ColumnDefinition
                    { Width = new W.GridLength(24) });
                var text = new WC.TextBlock
                {
                    Text = entry.Title,
                    Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Text),
                    FontFamily = MP_LiltTheme.WpfFont,
                    FontSize = 16, FontWeight = W.FontWeights.Normal,
                    TextTrimming = W.TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = W.HorizontalAlignment.Center,
                    VerticalAlignment = W.VerticalAlignment.Center
                };
                content.Children.Add(text);
                var close = new WC.Border
                {
                    Width = 22, Height = 23, CornerRadius = new W.CornerRadius(0),
                    Background = MP_StudioTheme.Brush(0, MP_StudioTheme.Text),
                    Cursor = WI.Cursors.Hand,
                    ToolTip = "关闭结构"
                };
                close.Child = new WC.TextBlock
                {
                    Text = "×", FontSize = 16,
                    Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Text),
                    HorizontalAlignment = W.HorizontalAlignment.Center,
                    VerticalAlignment = W.VerticalAlignment.Center
                };
                close.MouseEnter += (_, _) => AnimateColor((WM.SolidColorBrush)close.Background,
                    MP_StudioTheme.WpfOpacity(54, MP_StudioTheme.Text));
                close.MouseLeave += (_, _) => AnimateColor((WM.SolidColorBrush)close.Background,
                    MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text));
                close.MouseLeftButtonDown += (_, e) =>
                {
                    invoke?.Invoke(entry.Index, true);
                    e.Handled = true;
                };
                WC.Grid.SetColumn(close, 1);
                content.Children.Add(close);
                double estimatedWidth = entry.Title.Sum(c => c > 127 ? 16 : 8.5) + 43;
                var container = new WC.Border
                {
                    Width = Math.Clamp(estimatedWidth, 102, 240), Height = 31,
                    Background = MP_StudioTheme.Brush(0, MP_StudioTheme.Text),
                    Cursor = WI.Cursors.Hand,
                    Child = content
                };
                container.MouseEnter += (_, _) =>
                {
                    if (entry.Index != active)
                        AnimateColor((WM.SolidColorBrush)container.Background,
                            MP_StudioTheme.WpfOpacity(42, MP_StudioTheme.Text));
                };
                container.MouseLeave += (_, _) =>
                {
                    if (entry.Index != active)
                        AnimateColor((WM.SolidColorBrush)container.Background,
                            MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text));
                };
                container.MouseLeftButtonDown += (_, e) =>
                {
                    travellingCursor.Press();
                    e.Handled = true;
                };
                container.MouseLeftButtonUp += (_, e) =>
                {
                    travellingCursor.Release();
                    if (container.IsMouseOver) invoke?.Invoke(entry.Index, false);
                    e.Handled = true;
                };
                container.MouseLeave += (_, _) => travellingCursor.Release();
                container.MouseDown += (_, e) =>
                {
                    if (e.ChangedButton != WI.MouseButton.Middle) return;
                    invoke?.Invoke(entry.Index, true);
                    e.Handled = true;
                };
                slots.Children.Add(container);
                visuals.Add((container, text));
            }
            if (MP_Motion.Enabled && IsVisible)
                slots.BeginAnimation(OpacityProperty,
                    new WA.DoubleAnimation(.7, 1d, TimeSpan.FromMilliseconds(145)));
        }
        int nextActive = Array.FindIndex(next, tab => tab.Active);
        if (nextActive != active || structureChanged)
        {
            active = nextActive;
            for (int i = 0; i < visuals.Count; i++)
            {
                MP_WpfMotion.Tint((WM.SolidColorBrush)visuals[i].Title.Foreground,
                    i == active
                        ? MP_StudioTheme.WpfColor(MP_StudioTheme.ActiveText)
                        : MP_StudioTheme.WpfColor(MP_StudioTheme.Secondary), 160);
                MP_WpfMotion.Tint((WM.SolidColorBrush)visuals[i].Container.Background,
                    i == active ? MP_StudioTheme.WpfColor(MP_StudioTheme.Active)
                        : MP_StudioTheme.WpfOpacity(0, MP_StudioTheme.Text), 155);
            }
            Move(!structureChanged);
        }
    }

    private void Move(bool animate)
    {
        if (active < 0 || active >= visuals.Count)
        {
            capsule.Visibility = W.Visibility.Collapsed;
            return;
        }
        double x = 0;
        for (int i = 0; i < active; i++) x += visuals[i].Container.Width;
        double w = visuals[active].Container.Width - 4;
        bool wasReady = ready && capsule.Visibility == W.Visibility.Visible;
        capsule.Visibility = W.Visibility.Visible;
        travellingCursor.Move(x, w, immediate: !animate || !wasReady);
        ready = true;
        // Keep the selected document accessible on narrow windows; an
        // active tab must never be hidden underneath the fixed right tools.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (active < 0 || active >= visuals.Count ||
                scrollingTabs.ViewportWidth <= 0) return;
            double start = 0;
            for (int i = 0; i < active; i++) start += visuals[i].Container.Width;
            double end = start + visuals[active].Container.Width;
            if (start < scrollingTabs.HorizontalOffset)
                scrollingTabs.ScrollToHorizontalOffset(start);
            else if (end > scrollingTabs.HorizontalOffset + scrollingTabs.ViewportWidth)
                scrollingTabs.ScrollToHorizontalOffset(
                    Math.Max(0, end - scrollingTabs.ViewportWidth));
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void AnimateColor(WM.SolidColorBrush brush, WM.Color to)
    {
        MP_WpfMotion.Tint(brush, to, 145);
    }
}
