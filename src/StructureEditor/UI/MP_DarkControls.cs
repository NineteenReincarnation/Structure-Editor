using System.Drawing.Drawing2D;

namespace StructureEditor;

// WinForms Flat ComboBox still paints a very bright native arrow region.
// Cover only the arrow button after the OS has drawn the editable area.
internal sealed class MP_DarkComboBox : ComboBox
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || !IsHandleCreated ||
            DropDownStyle != ComboBoxStyle.DropDownList || ClientSize.Width < 26) return;
        using Graphics g = Graphics.FromHwnd(Handle);
        int width = Math.Min(23, ClientSize.Width / 3), x = ClientSize.Width - width;
        Color bg = Enabled ? Color.FromArgb(48, 65, 76) : Color.FromArgb(40, 50, 58);
        using var brush = new SolidBrush(bg);
        g.FillRectangle(brush, x, 1, width - 1, ClientSize.Height - 2);
        using var separator = new Pen(Color.FromArgb(65, 84, 96));
        g.DrawLine(separator, x, 3, x, ClientSize.Height - 4);
        int cx = x + width / 2, cy = ClientSize.Height / 2;
        using var arrow = new SolidBrush(Enabled
            ? Color.FromArgb(184, 207, 217) : Color.FromArgb(106, 125, 135));
        g.FillPolygon(arrow, new[]
        {
            new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2),
            new Point(cx, cy + 3)
        });
    }
}

// Opaque WinForms overlay positioned over the native ListBox scrollbar.
// The native list retains keyboard/wheel scrolling while this supplies a dark,
// draggable scroll track without replacing the list or its selection logic.
internal sealed class MP_DarkPaletteScrollbar : Control
{
    private readonly ListBox list;
    private bool dragging;

    internal MP_DarkPaletteScrollbar(ListBox list)
    {
        this.list = list;
        Width = 17;
        Dock = DockStyle.Right;
        BackColor = Color.FromArgb(37, 49, 59);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        list.MouseWheel += (_, _) => BeginInvoke((Action)Invalidate);
        list.SelectedIndexChanged += (_, _) => Invalidate();
        list.SizeChanged += (_, _) => Invalidate();
        list.KeyUp += (_, _) => Invalidate();
    }

    private int VisibleCount => Math.Max(1,
        list.ItemHeight <= 0 ? 1 : list.ClientSize.Height / list.ItemHeight);
    private int MaxTop => Math.Max(0, list.Items.Count - VisibleCount);

    private Rectangle Thumb()
    {
        int track = Math.Max(1, Height - 8);
        int size = MaxTop == 0 ? track
            : Math.Clamp((int)((double)VisibleCount / Math.Max(1, list.Items.Count) * track), Math.Min(22, track), track);
        int offset = MaxTop == 0 ? 0
            : (int)((double)list.TopIndex / MaxTop * (track - size));
        return new Rectangle(4, 4 + offset, Math.Max(6, Width - 8), size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        using var edge = new Pen(Color.FromArgb(58, 76, 88));
        e.Graphics.DrawLine(edge, 0, 0, 0, Height);
        Rectangle thumb = Thumb();
        using var fill = new SolidBrush(dragging ? Color.FromArgb(111, 149, 163)
            : Color.FromArgb(81, 113, 130));
        e.Graphics.FillRectangle(fill, thumb);
    }

    private void ScrollTo(int y)
    {
        if (MaxTop == 0) return;
        Rectangle thumb = Thumb();
        int available = Math.Max(1, Height - 8 - thumb.Height);
        list.TopIndex = Math.Clamp((int)Math.Round(
            ((double)y - 4 - thumb.Height / 2.0) / available * MaxTop), 0, MaxTop);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        Capture = true;
        ScrollTo(e.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) ScrollTo(e.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false;
        Capture = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int delta = e.Delta > 0 ? -3 : 3;
        list.TopIndex = Math.Clamp(list.TopIndex + delta, 0, MaxTop);
        Invalidate();
        base.OnMouseWheel(e);
    }
}
