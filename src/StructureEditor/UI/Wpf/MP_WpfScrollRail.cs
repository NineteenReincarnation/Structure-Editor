using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WI = System.Windows.Input;

namespace StructureEditor;

// Single reusable, composited scrollbar affordance for WPF editor lists.
// Native scrolling and virtualization remain owned by ScrollViewer; this
// lightweight rail is only the visible thumb and a direct-drag surface.
internal sealed class MP_WpfScrollRail : WC.Grid
{
    private readonly WC.ScrollViewer viewer;
    private readonly MP_ScrollOffsetProxy proxy;
    private readonly Action<double> onDirectScroll;
    private readonly WC.Border thumb;
    private readonly WM.TranslateTransform offset = new();
    private bool dragging;

    internal MP_WpfScrollRail(WC.ScrollViewer scroll,
        MP_ScrollOffsetProxy scrollProxy, Action<double> onDragged = null)
    {
        viewer = scroll ?? throw new ArgumentNullException(nameof(scroll));
        proxy = scrollProxy;
        onDirectScroll = onDragged;
        Width = 9;
        Margin = new W.Thickness(0, 4, 2, 4);
        HorizontalAlignment = W.HorizontalAlignment.Right;
        Background = new WM.SolidColorBrush(WM.Color.FromArgb(24, 65, 91, 107));
        Cursor = WI.Cursors.Hand;
        thumb = new WC.Border
        {
            Width = 3, Height = 24,
            Background = new WM.SolidColorBrush(WM.Color.FromArgb(192, 155, 218, 231)),
            CornerRadius = new W.CornerRadius(2),
            HorizontalAlignment = W.HorizontalAlignment.Center,
            VerticalAlignment = W.VerticalAlignment.Top,
            RenderTransform = offset,
            IsHitTestVisible = false
        };
        Children.Add(thumb);
        viewer.ScrollChanged += (_, _) => Refresh();
        SizeChanged += (_, _) => Refresh();
        MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            CaptureMouse();
            Seek(e.GetPosition(this).Y);
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (dragging) Seek(e.GetPosition(this).Y);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        };
        LostMouseCapture += (_, _) => dragging = false;
    }

    internal void Refresh()
    {
        double viewport = Math.Max(0, viewer.ViewportHeight);
        double total = Math.Max(0, viewer.ExtentHeight);
        double travelHeight = Math.Max(0, ActualHeight);
        bool canScroll = viewer.ScrollableHeight > 1 && viewport > 0
            && total > viewport && travelHeight > 1;
        Visibility = canScroll ? W.Visibility.Visible : W.Visibility.Collapsed;
        if (!canScroll) return;
        double thumbHeight = Math.Clamp(viewport / total * travelHeight,
            Math.Min(22, travelHeight), travelHeight);
        thumb.Height = thumbHeight;
        double progress = Math.Clamp(viewer.VerticalOffset / viewer.ScrollableHeight, 0d, 1d);
        offset.Y = Math.Max(0, travelHeight - thumbHeight) * progress;
    }

    private void Seek(double position)
    {
        double travel = Math.Max(1, ActualHeight - thumb.Height);
        double ratio = Math.Clamp((position - thumb.Height * .5) / travel, 0d, 1d);
        double target = ratio * Math.Max(0, viewer.ScrollableHeight);
        proxy?.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        if (proxy != null) proxy.Offset = target;
        viewer.ScrollToVerticalOffset(target);
        onDirectScroll?.Invoke(target);
    }
}
