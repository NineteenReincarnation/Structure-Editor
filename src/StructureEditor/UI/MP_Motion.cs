using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StructureEditor;

// UI-thread-only, demand-driven motion scheduler. Animations are interruptible,
// never delay actions, and never run a timer while the UI is idle.
internal static class MP_Motion
{
    private sealed class Track
    {
        internal Control Owner;
        internal float From, To;
        internal long Start;
        internal int Duration;
        internal Action<float> Apply;
    }

    private static readonly Dictionary<(Control Owner, string Channel), Track> Tracks = new();
    private static readonly System.Windows.Forms.Timer Clock = new() { Interval = 16 };
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    private static readonly bool ClientAnimationsEnabled = ReadSystemMotionPreference();

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        out int value, uint flags);

    static MP_Motion()
    {
        Clock.Tick += (_, _) => Tick();
    }

    private static bool ReadSystemMotionPreference()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            // Windows Ease of Access / Accessibility: animation effects.
            return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0)
                || enabled != 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    internal static bool Enabled => ClientAnimationsEnabled;

    internal static float EaseOut(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return 1f - (1f - value) * (1f - value) * (1f - value);
    }

    internal static Color Blend(Color a, Color b, float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        int Mix(int from, int to) => (int)Math.Round(from + (to - from) * progress);
        return Color.FromArgb(Mix(a.A, b.A), Mix(a.R, b.R),
            Mix(a.G, b.G), Mix(a.B, b.B));
    }

    internal static void Tween(Control owner, string channel, float from, float to,
        Action<float> apply, int durationMs = 167)
    {
        if (owner == null || apply == null) return;
        var key = (owner, channel);
        Tracks.Remove(key);
        if (owner.IsDisposed || !owner.IsHandleCreated || !owner.Visible ||
            !Enabled || durationMs <= 0 || Math.Abs(from - to) < 0.001f)
        {
            if (!owner.IsDisposed) apply(to);
            if (Tracks.Count == 0) Clock.Stop();
            return;
        }
        Tracks[key] = new Track
        {
            Owner = owner, From = from, To = to,
            Start = Stopwatch.GetTimestamp(), Duration = durationMs, Apply = apply
        };
        if (!Clock.Enabled) Clock.Start();
    }

    internal static void Cancel(Control owner)
    {
        if (owner == null) return;
        foreach (var key in Tracks.Keys.Where(key => ReferenceEquals(key.Owner, owner)).ToArray())
            Tracks.Remove(key);
        if (Tracks.Count == 0) Clock.Stop();
    }

    private static void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        foreach (var pair in Tracks.ToArray())
        {
            if (!Tracks.TryGetValue(pair.Key, out Track track) ||
                !ReferenceEquals(track, pair.Value)) continue;
            if (track.Owner.IsDisposed)
            {
                Tracks.Remove(pair.Key);
                continue;
            }
            float elapsedMs = (now - track.Start) * 1000f / Stopwatch.Frequency;
            float progress = Math.Clamp(elapsedMs / track.Duration, 0f, 1f);
            if (!track.Owner.Visible) progress = 1f;
            // Remove first: an Apply callback can retarget the same channel.
            if (progress >= 1f) Tracks.Remove(pair.Key);
            track.Apply(track.From + (track.To - track.From) * EaseOut(progress));
        }
        if (Tracks.Count == 0) Clock.Stop();
    }
}

// Shared button feedback for ordinary actions and the editor's section tabs.
// Owner-painted and double-buffered; no animated layout or hover reflow.
internal sealed class MP_AnimatedButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color IdleColor { get; set; } = Color.FromArgb(51, 62, 66);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color SelectedColor { get; set; } = Color.FromArgb(51, 87, 94);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color HoverColor { get; set; } = Color.FromArgb(67, 91, 102);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color PressColor { get; set; } = Color.FromArgb(39, 71, 82);
    private Color paintColor = Color.FromArgb(51, 62, 66);
    private bool hovered, pressed, selected;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            AnimateFill(167);
        }
    }

    internal MP_AnimatedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        paintColor = SelectedFill();
        Invalidate();
    }

    private Color SelectedFill()
    {
        if (!Enabled) return IdleColor;
        if (pressed) return PressColor;
        if (hovered) return HoverColor;
        return selected ? SelectedColor : IdleColor;
    }

    private void AnimateFill(int duration)
    {
        Color from = paintColor, to = SelectedFill();
        MP_Motion.Tween(this, "fill", 0, 1,
            t => { paintColor = MP_Motion.Blend(from, to, t); Invalidate(); }, duration);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e); hovered = true; AnimateFill(145);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); hovered = false; pressed = false; AnimateFill(120);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { pressed = true; AnimateFill(83); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); pressed = false; AnimateFill(120);
    }
    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e); AnimateFill(120);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width < 1 || bounds.Height < 1) return;
        using (var fill = new SolidBrush(paintColor))
            e.Graphics.FillRectangle(fill, bounds);
        if (FlatAppearance.BorderSize > 0)
        {
            using Pen border = new(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
            e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
                => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            Enabled ? ForeColor : Color.FromArgb(112, 133, 144), flags);
        if (Focused && ShowFocusCues && bounds.Width > 6 && bounds.Height > 6)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3));
    }
}

// Only hovered and selected rows are repainted, never the whole scroll surface.
internal sealed class MP_MotionListBox : ListBox
{
    private readonly Dictionary<int, float> hover = new(), selection = new();
    private int hoveredIndex = -1, selectedIndex = -1;

    internal MP_MotionListBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    internal float Hover(int index) => hover.GetValueOrDefault(index);
    internal float Selected(int index) => selection.GetValueOrDefault(index);

    internal void ResetMotion()
    {
        MP_Motion.Cancel(this);
        hover.Clear(); selection.Clear(); hoveredIndex = -1;
        selectedIndex = SelectedIndex;
        if (selectedIndex >= 0) selection[selectedIndex] = 1f;
    }

    private void InvalidateRow(int index)
    {
        if (!IsHandleCreated || index < 0 || index >= Items.Count) return;
        Rectangle rect = GetItemRectangle(index);
        if (rect.IntersectsWith(ClientRectangle))
            Invalidate(new Rectangle(0, rect.Top, ClientSize.Width, rect.Height));
    }

    private void FadeRow(Dictionary<int, float> values, string prefix, int index,
        float destination, int duration)
    {
        if (index < 0 || index >= Items.Count) return;
        float from = values.GetValueOrDefault(index);
        MP_Motion.Tween(this, prefix + index, from, destination, value =>
        {
            if (index >= Items.Count) return;
            if (value < .001f && destination == 0f) values.Remove(index);
            else values[index] = value;
            InvalidateRow(index);
        }, duration);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int next = IndexFromPoint(e.Location);
        if (next == hoveredIndex) return;
        FadeRow(hover, "hover-", hoveredIndex, 0f, 110);
        hoveredIndex = next;
        FadeRow(hover, "hover-", next, 1f, 150);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        FadeRow(hover, "hover-", hoveredIndex, 0f, 110);
        hoveredIndex = -1;
    }
    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        if (selectedIndex == SelectedIndex) return;
        FadeRow(selection, "select-", selectedIndex, 0f, 120);
        selectedIndex = SelectedIndex;
        FadeRow(selection, "select-", selectedIndex, 1f, 167);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        MP_Motion.Cancel(this);
        base.OnHandleDestroyed(e);
    }
}

// Preview image fades in only after the file is loaded; painting is bounded
// to the 155px thumbnail panel and never touches the main editor canvas.
internal sealed class MP_FadePictureBox : PictureBox
{
    private float opacity = 1f;
    internal void Reveal()
    {
        opacity = 0f;
        MP_Motion.Tween(this, "preview", 0f, 1f,
            t => { opacity = t; Invalidate(); }, 180);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (Image == null || Image.Width < 1 || Image.Height < 1) return;
        float ratio = Math.Min(Width / (float)Image.Width, Height / (float)Image.Height);
        int w = Math.Max(1, (int)(Image.Width * ratio));
        int h = Math.Max(1, (int)(Image.Height * ratio));
        Rectangle destination = new((Width - w) / 2, (Height - h) / 2, w, h);
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
        attributes.SetColorMatrix(matrix);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        e.Graphics.DrawImage(Image, destination, 0, 0, Image.Width, Image.Height,
            GraphicsUnit.Pixel, attributes);
    }
}
