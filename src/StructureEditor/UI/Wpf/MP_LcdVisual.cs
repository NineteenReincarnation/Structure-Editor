using W = System.Windows;
using WM = System.Windows.Media;
namespace StructureEditor;

// Small, opt-in LCD phosphor halo on titles and indicators only.
// Do NOT place effects on every virtualized row, on the canvas, or on the
// moving selection plate: those must remain fast and sharply rendered.
internal static class MP_LcdVisual
{
    internal static WM.Effects.DropShadowEffect Glow(double intensity = .24)
        => new()
        {
            Color = WM.Color.FromRgb(134, 231, 247),
            BlurRadius = 2,
            ShadowDepth = 0,
            Opacity = Math.Clamp(intensity * .12, 0d, .06d)
        };

    internal static void GlowSelected(WM.Effects.DropShadowEffect effect, bool selected)
    {
        if (effect == null) return;
        double target = selected ? 0 : .025;
        double from = effect.Opacity;
        effect.BeginAnimation(WM.Effects.DropShadowEffect.OpacityProperty, null);
        effect.Opacity = target;
        if (MP_Motion.Enabled && Math.Abs(from - target) > .002)
            effect.BeginAnimation(WM.Effects.DropShadowEffect.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(from, target,
                    TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                });
    }
}
