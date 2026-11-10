using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;
namespace StructureEditor;

// Shared motion dialect tuned to the reference video. Animations start from
// the CURRENT composited value so repeated keyboard/mouse input never snaps.
internal static class MP_WpfMotion
{
    internal const int Hover = 125;
    internal const int Selection = 245;
    internal const int Page = 210;

    private static WA.IEasingFunction Curve(bool elastic)
        => elastic
            ? new WA.BackEase { Amplitude = .10,
                EasingMode = WA.EasingMode.EaseOut }
            : new WA.CubicEase { EasingMode = WA.EasingMode.EaseOut };

    internal static void Translate(WM.TranslateTransform transform,
        W.DependencyProperty axis, double destination, int ms = Selection,
        bool elastic = true)
    {
        if (transform == null) return;
        double origin = (double)transform.GetValue(axis);
        transform.BeginAnimation(axis, null);
        transform.SetValue(axis, destination);
        if (!MP_Motion.Enabled || ms <= 0 || Math.Abs(destination - origin) < .2) return;
        transform.BeginAnimation(axis, new WA.DoubleAnimation(origin, destination,
            TimeSpan.FromMilliseconds(ms)) { EasingFunction = Curve(elastic) });
    }

    internal static void Width(WC.Border border, double destination,
        int ms = Selection)
    {
        if (border == null) return;
        double origin = border.Width;
        border.BeginAnimation(W.FrameworkElement.WidthProperty, null);
        border.Width = destination;
        if (!MP_Motion.Enabled || ms <= 0 || Math.Abs(origin - destination) < .2) return;
        border.BeginAnimation(W.FrameworkElement.WidthProperty,
            new WA.DoubleAnimation(origin, destination, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = Curve(false) });
    }

    internal static void Tint(WM.SolidColorBrush brush, WM.Color destination,
        int ms = Hover)
    {
        if (brush == null || brush.IsFrozen) return;
        var origin = brush.Color;
        brush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        brush.Color = destination;
        if (!MP_Motion.Enabled || ms <= 0 || origin == destination) return;
        brush.BeginAnimation(WM.SolidColorBrush.ColorProperty,
            new WA.ColorAnimation(origin, destination, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = Curve(false) });
    }

    internal static void Scroll(MP_ScrollOffsetProxy proxy,
        double destination, int ms = 230)
    {
        if (proxy == null) return;
        double origin = proxy.Offset;
        proxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty, null);
        proxy.Offset = destination;
        if (!MP_Motion.Enabled || Math.Abs(origin - destination) < .3) return;
        proxy.BeginAnimation(MP_ScrollOffsetProxy.OffsetProperty,
            new WA.DoubleAnimation(origin, destination,
                TimeSpan.FromMilliseconds(ms)) { EasingFunction = Curve(false) });
    }

    // Focus is conveyed by a cyan rim and a very low-intensity halo, matching
    // the LCD-style active text field without tinting the input's contents.
    internal static void AttachSearch(WC.TextBox textbox)
    {
        if (textbox == null) return;
        var border = MP_StudioTheme.Brush(MP_StudioTheme.Divider);
        var shadow = new WM.Effects.DropShadowEffect
        {
            Color = MP_StudioTheme.WpfColor(MP_StudioTheme.Text),
            BlurRadius = 1, ShadowDepth = 0, Opacity = 0
        };
        textbox.BorderBrush = border;
        textbox.Effect = shadow;
        void Change(bool focused)
        {
            Tint(border, focused ? MP_StudioTheme.WpfColor(MP_StudioTheme.Accent)
                : MP_StudioTheme.WpfColor(MP_StudioTheme.Divider), 170);
            double target = focused && MP_Motion.Enabled ? .08 : 0;
            double from = shadow.Opacity;
            shadow.BeginAnimation(WM.Effects.DropShadowEffect.OpacityProperty, null);
            shadow.Opacity = target;
            if (MP_Motion.Enabled)
                shadow.BeginAnimation(WM.Effects.DropShadowEffect.OpacityProperty,
                    new WA.DoubleAnimation(from, target, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = Curve(false) });
        }
        textbox.GotKeyboardFocus += (_, _) => Change(true);
        textbox.LostKeyboardFocus += (_, _) => Change(false);
    }
}
