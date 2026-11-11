using System.Drawing.Drawing2D;

namespace StructureEditor;

// WinForms Flat ComboBox still paints a very bright native arrow region.
// Cover only the arrow button after the OS has drawn the editable area.
internal sealed class MP_DarkComboBox : ComboBox
{
    // Opt-in keyboard routing. Inspector combos keep native input behavior.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    internal Func<Keys, bool> MP_NavigationKey { get; set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        => MP_NavigationKey?.Invoke(keyData) == true || base.ProcessCmdKey(ref msg, keyData);

    protected override bool ProcessDialogKey(Keys keyData)
        => MP_NavigationKey?.Invoke(keyData) == true || base.ProcessDialogKey(keyData);

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        MP_UiSound.Play(MP_UiSound.Cue.Open);
    }

    protected override void OnSelectionChangeCommitted(EventArgs e)
    {
        base.OnSelectionChangeCommitted(e);
        MP_UiSound.Play(MP_UiSound.Cue.Confirm);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    // Native themed dropdowns reserve a wider arrow well than 23px at
    // some DPI/theme combinations. Painting only the last 23px left an
    // exposed bright seam (the vertical white bar in the inspector).
    internal static int MP_ArrowOverlayWidth(int clientWidth) =>
        Math.Min(Math.Max(30, SystemInformation.VerticalScrollBarWidth + 10),
            Math.Max(18, clientWidth / 3));

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || !IsHandleCreated ||
            DropDownStyle != ComboBoxStyle.DropDownList || ClientSize.Width < 26) return;
        using Graphics g = Graphics.FromHwnd(Handle);
        int width = MP_ArrowOverlayWidth(ClientSize.Width);
        int x = ClientSize.Width - width;
        Color bg = Enabled ? MP_StudioTheme.Input : MP_StudioTheme.Panel;
        using var brush = new SolidBrush(bg);
        g.FillRectangle(brush, x, 1, width, ClientSize.Height - 2);
        using var separator = new Pen(MP_StudioTheme.Divider);
        g.DrawLine(separator, x, 3, x, ClientSize.Height - 4);
        int cx = x + width / 2, cy = ClientSize.Height / 2;
        using var arrow = new SolidBrush(Enabled
            ? MP_StudioTheme.Text : MP_StudioTheme.Secondary);
        g.FillPolygon(arrow, new[]
        {
            new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2),
            new Point(cx, cy + 3)
        });
        // The stock FlatStyle still leaves a bright 1px border under some
        // Windows themes. Paint a restrained frame with the same palette.
        using var frame = new Pen(Focused ? MP_StudioTheme.Accent : MP_StudioTheme.Divider);
        g.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
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
        BackColor = MP_StudioTheme.Panel;
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
        using var edge = new Pen(MP_StudioTheme.Divider);
        e.Graphics.DrawLine(edge, 0, 0, 0, Height);
        Rectangle thumb = Thumb();
        using var fill = new SolidBrush(dragging ? MP_StudioTheme.Accent
            : MP_StudioTheme.Secondary);
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
