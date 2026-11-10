using System.Globalization;
using StructureEditor.Compatibility;
using W = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;
using WA = System.Windows.Media.Animation;

namespace StructureEditor;

// Composited counter and reusable WPF breakdown rows; no GDI draw loop.
internal sealed class MP_FluidCpSummary : WC.UserControl
{
    private readonly MP_PixelReadout total;
    private readonly WC.TextBlock subtitle;
    private readonly WC.StackPanel lines;
    private readonly WM.ScaleTransform fillScale = new();
    private readonly MP_FloatProxy proxy;
    private float displayed;
    private MP_ClusterCombatPower.Row[] last;

    private static WM.SolidColorBrush Brush(byte r, byte g, byte b)
        => new(WM.Color.FromRgb(r, g, b));
    private static WM.SolidColorBrush Brush(byte a, byte r, byte g, byte b)
        => new(WM.Color.FromArgb(a, r, g, b));

    internal MP_FluidCpSummary()
    {
        MP_LiltTheme.Apply(this);
        Background = MP_StudioTheme.Brush(MP_StudioTheme.Panel);
        var body = new WC.Grid
        {
            Background = Background, Margin = new W.Thickness(12, 11, 12, 7)
        };
        body.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(48) });
        body.RowDefinitions.Add(new WC.RowDefinition { Height = new W.GridLength(7) });
        body.RowDefinitions.Add(new WC.RowDefinition
            { Height = new W.GridLength(1, W.GridUnitType.Star) });
        var header = new WC.Grid();
        header.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        header.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = W.GridLength.Auto });
        var left = new WC.StackPanel
        {
            VerticalAlignment = W.VerticalAlignment.Center,
            Orientation = WC.Orientation.Vertical
        };
        left.Children.Add(new WC.TextBlock
        {
            Text = "结构消耗  /  CP",
            Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Accent),
            FontFamily = MP_LiltTheme.WpfFont,
            FontWeight = W.FontWeights.Normal, FontSize = 14
        });
        subtitle = new WC.TextBlock
        {
            Text = "不计墙和路障", Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Secondary),
            FontFamily = MP_LiltTheme.WpfFont, FontSize = 12,
            Margin = new W.Thickness(0, 3, 0, 0)
        };
        left.Children.Add(subtitle);
        header.Children.Add(left);
        total = new MP_PixelReadout
        {
            Text = "0 CP", Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Accent),
            PixelSize = 2,
            VerticalAlignment = W.VerticalAlignment.Center,
            HorizontalAlignment = W.HorizontalAlignment.Right,
            RenderTransformOrigin = new W.Point(.5, .5),
            RenderTransform = new WM.ScaleTransform(1, 1)
        };
        WC.Grid.SetColumn(total, 1); header.Children.Add(total);
        WC.Grid.SetRow(header, 0); body.Children.Add(header);

        var rail = new WC.Grid
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Divider), Height = 2,
            VerticalAlignment = W.VerticalAlignment.Center
        };
        var fill = new WC.Border
        {
            Background = MP_StudioTheme.Brush(MP_StudioTheme.Accent),
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            RenderTransformOrigin = new W.Point(0, .5),
            RenderTransform = fillScale
        };
        rail.Children.Add(fill);
        WC.Grid.SetRow(rail, 1); body.Children.Add(rail);
        fillScale.ScaleX = 0;

        lines = new WC.StackPanel { Orientation = WC.Orientation.Vertical };
        var scroller = new WC.ScrollViewer
        {
            Content = lines, Background = Background,
            VerticalScrollBarVisibility = WC.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WC.ScrollBarVisibility.Disabled
        };
        WC.Grid.SetRow(scroller, 2); body.Children.Add(scroller);
        Content = new WC.Border
        {
            BorderBrush = MP_StudioTheme.Brush(MP_StudioTheme.Divider),
            BorderThickness = new W.Thickness(0, 1, 0, 0),
            Background = Background, Child = body
        };

        proxy = new MP_FloatProxy(value =>
        {
            displayed = (float)value;
            total.Text = value.ToString("0.##", CultureInfo.InvariantCulture) + " CP";
        });
        SetRows(Array.Empty<MP_ClusterCombatPower.Row>());
    }

    internal void SetRows(MP_ClusterCombatPower.Row[] rows)
    {
        rows ??= Array.Empty<MP_ClusterCombatPower.Row>();
        float goal = MP_ClusterCombatPower.Total(rows);
        if (Math.Abs(goal - displayed) > .001f)
        {
            double from = displayed;
            proxy.BeginAnimation(MP_FloatProxy.ValueProperty, null);
            proxy.Value = goal;
            if (MP_Motion.Enabled && IsVisible)
            {
                proxy.BeginAnimation(MP_FloatProxy.ValueProperty,
                    new WA.DoubleAnimation(from, goal, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new WA.CubicEase
                            { EasingMode = WA.EasingMode.EaseOut }
                    });
                if (total.RenderTransform is WM.ScaleTransform t)
                {
                    t.BeginAnimation(WM.ScaleTransform.ScaleXProperty,
                        new WA.DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(230)));
                    t.BeginAnimation(WM.ScaleTransform.ScaleYProperty,
                        new WA.DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(230)));
                }
            }
        }
        bool same = last != null && rows.Length == last.Length;
        for (int i = 0; same && i < rows.Length; i++)
            same = ReferenceEquals(rows[i].Item, last[i].Item)
                && rows[i].Count == last[i].Count;
        if (same) return;
        last = rows.ToArray();
        lines.Children.Clear();
        subtitle.Text = rows.Length == 0 ? "不计墙和路障"
            : $"已计入 {rows.Sum(x => x.Count)} 座设施";
        if (rows.Length == 0)
            lines.Children.Add(new WC.TextBlock
            {
                Text = "暂无计入 CP 的设施", FontSize = 11,
                FontFamily = MP_LiltTheme.WpfFont,
                Foreground = Brush(121, 155, 170),
                Margin = new W.Thickness(0, 19, 0, 0)
            });
        else
            foreach (var line in rows) lines.Children.Add(CreateRow(line));
        fillScale.BeginAnimation(WM.ScaleTransform.ScaleXProperty, null);
        fillScale.ScaleX = rows.Length == 0 ? 0 : 1;
        if (MP_Motion.Enabled && IsVisible)
        {
            fillScale.BeginAnimation(WM.ScaleTransform.ScaleXProperty,
                new WA.DoubleAnimation(0, rows.Length == 0 ? 0 : 1,
                    TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new WA.CubicEase
                        { EasingMode = WA.EasingMode.EaseOut }
                });
            lines.BeginAnimation(OpacityProperty,
                new WA.DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(185)));
        }
    }

    private WC.Border CreateRow(MP_ClusterCombatPower.Row row)
    {
        var bg = Brush(0, 88, 147, 163);
        var host = new WC.Border
        {
            Height = 30, Margin = new W.Thickness(0, 1, 0, 1),
            CornerRadius = new W.CornerRadius(0), Background = bg
        };
        var layout = new WC.Grid { Margin = new W.Thickness(4, 0, 5, 0) };
        layout.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = new W.GridLength(1, W.GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new WC.ColumnDefinition
            { Width = W.GridLength.Auto });
        var name = new WC.TextBlock
        {
            Text = (row.Item.ClusterWikiName ?? row.Item.Label) + " ×" + row.Count,
            Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Text),
            FontFamily = MP_LiltTheme.WpfFont,
            FontSize = 13, VerticalAlignment = W.VerticalAlignment.Center,
            TextTrimming = W.TextTrimming.CharacterEllipsis
        };
        var value = new WC.TextBlock
        {
            Text = row.Total.ToString("0.##", CultureInfo.InvariantCulture),
            Foreground = MP_StudioTheme.Brush(MP_StudioTheme.Accent), FontSize = 13,
            FontFamily = name.FontFamily,
            VerticalAlignment = W.VerticalAlignment.Center,
            Margin = new W.Thickness(6, 0, 0, 0)
        };
        WC.Grid.SetColumn(value, 1);
        layout.Children.Add(name); layout.Children.Add(value);
        host.Child = layout;
        host.MouseEnter += (_, _) => Animate(bg, WM.Color.FromArgb(64, 77, 147, 161));
        host.MouseLeave += (_, _) => Animate(bg, WM.Color.FromArgb(0, 77, 147, 161));
        return host;
    }

    private static void Animate(WM.SolidColorBrush brush, WM.Color color)
    {
        var from = brush.Color;
        brush.BeginAnimation(WM.SolidColorBrush.ColorProperty, null);
        brush.Color = color;
        if (MP_Motion.Enabled)
            brush.BeginAnimation(WM.SolidColorBrush.ColorProperty,
                new WA.ColorAnimation(from, color, TimeSpan.FromMilliseconds(145))
                {
                    EasingFunction = new WA.CubicEase
                        { EasingMode = WA.EasingMode.EaseOut }
                });
    }
}

internal sealed class MP_FloatProxy : WA.Animatable
{
    private readonly Action<double> changed;
    internal MP_FloatProxy(Action<double> changed) => this.changed = changed;
    protected override W.Freezable CreateInstanceCore() => new MP_FloatProxy(changed);
    internal static readonly W.DependencyProperty ValueProperty =
        W.DependencyProperty.Register(nameof(Value), typeof(double), typeof(MP_FloatProxy),
            new W.PropertyMetadata(0d, (d, e) =>
                ((MP_FloatProxy)d).changed?.Invoke((double)e.NewValue)));
    internal double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
