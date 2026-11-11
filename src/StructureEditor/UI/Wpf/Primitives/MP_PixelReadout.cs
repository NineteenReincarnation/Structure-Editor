using W = System.Windows;
using WM = System.Windows.Media;

namespace StructureEditor;

// An original, font-file-free 5x7 bitmap fallback for numeric readouts.
// Normal UI text still uses Unifont when it is available. Drawing each lit
// cell as a pixel keeps the digital counters crisp even without that font.
internal sealed class MP_PixelReadout : W.FrameworkElement
{
    private string value = "";
    private int pixel = 2;
    internal string Text
    {
        get => value;
        set { this.value = (value ?? "").ToUpperInvariant();
            InvalidateMeasure(); InvalidateVisual(); }
    }
    internal int PixelSize
    {
        get => pixel;
        set { pixel = Math.Clamp(value, 1, 4); InvalidateMeasure(); InvalidateVisual(); }
    }
    internal WM.Brush Foreground { get; set; } =
        new WM.SolidColorBrush(WM.Color.FromRgb(188, 228, 249));

    // Row data is five pixels wide, with the most-significant bit at left.
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['A'] = new byte[]{14,17,17,31,17,17,17},
        ['B'] = new byte[]{30,17,17,30,17,17,30},
        ['C'] = new byte[]{15,16,16,16,16,16,15},
        ['D'] = new byte[]{30,17,17,17,17,17,30},
        ['E'] = new byte[]{31,16,16,30,16,16,31},
        ['F'] = new byte[]{31,16,16,30,16,16,16},
        ['G'] = new byte[]{15,16,16,23,17,17,15},
        ['H'] = new byte[]{17,17,17,31,17,17,17},
        ['I'] = new byte[]{14,4,4,4,4,4,14},
        ['J'] = new byte[]{7,2,2,2,18,18,12},
        ['K'] = new byte[]{17,18,20,24,20,18,17},
        ['L'] = new byte[]{16,16,16,16,16,16,31},
        ['M'] = new byte[]{17,27,21,21,17,17,17},
        ['N'] = new byte[]{17,25,21,19,17,17,17},
        ['O'] = new byte[]{14,17,17,17,17,17,14},
        ['P'] = new byte[]{30,17,17,30,16,16,16},
        ['Q'] = new byte[]{14,17,17,17,21,18,13},
        ['R'] = new byte[]{30,17,17,30,20,18,17},
        ['S'] = new byte[]{15,16,16,14,1,1,30},
        ['T'] = new byte[]{31,4,4,4,4,4,4},
        ['U'] = new byte[]{17,17,17,17,17,17,14},
        ['V'] = new byte[]{17,17,17,17,17,10,4},
        ['W'] = new byte[]{17,17,17,21,21,21,10},
        ['X'] = new byte[]{17,17,10,4,10,17,17},
        ['Y'] = new byte[]{17,17,10,4,4,4,4},
        ['Z'] = new byte[]{31,1,2,4,8,16,31},
        ['0'] = new byte[]{14,17,19,21,25,17,14},
        ['1'] = new byte[]{4,12,4,4,4,4,14},
        ['2'] = new byte[]{14,17,1,2,4,8,31},
        ['3'] = new byte[]{30,1,1,14,1,1,30},
        ['4'] = new byte[]{2,6,10,18,31,2,2},
        ['5'] = new byte[]{31,16,16,30,1,1,30},
        ['6'] = new byte[]{14,16,16,30,17,17,14},
        ['7'] = new byte[]{31,1,2,4,8,8,8},
        ['8'] = new byte[]{14,17,17,14,17,17,14},
        ['9'] = new byte[]{14,17,17,15,1,1,14},
        [' '] = new byte[]{0,0,0,0,0,0,0},
        ['/'] = new byte[]{1,1,2,4,8,16,16},
        [':'] = new byte[]{0,4,4,0,4,4,0},
        ['-'] = new byte[]{0,0,0,31,0,0,0},
        ['.'] = new byte[]{0,0,0,0,0,4,4},
        ['_'] = new byte[]{0,0,0,0,0,0,31},
        ['*'] = new byte[]{0,4,21,14,21,4,0}
    };

    internal MP_PixelReadout()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        WM.RenderOptions.SetEdgeMode(this, WM.EdgeMode.Aliased);
    }

    protected override W.Size MeasureOverride(W.Size availableSize)
        => new(Math.Max(1, value.Length * 6 * pixel),
               Math.Max(1, 7 * pixel));

    protected override void OnRender(WM.DrawingContext dc)
    {
        base.OnRender(dc);
        for (int i = 0; i < value.Length; i++)
        {
            if (!Glyphs.TryGetValue(value[i], out byte[] pattern)) continue;
            for (int row = 0; row < 7; row++)
                for (int column = 0; column < 5; column++)
                    if ((pattern[row] & (1 << (4 - column))) != 0)
                        dc.DrawRectangle(Foreground, null,
                            new W.Rect((i * 6 + column) * pixel,
                                row * pixel, pixel, pixel));
        }
    }
}
