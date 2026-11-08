namespace StructureEditor;

// Windows TabControl paints the unused title strip with the system theme,
// which can leave a bright white patch beside the last tab in a dark workspace.
// Only tone that unused header area; leave native tab interaction/scrolling intact.
internal sealed class MP_SeamlessTabs : TabControl
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x000F || !IsHandleCreated || IsDisposed ||
            TabCount == 0 || ClientSize.Width <= 0) return;

        Rectangle last = GetTabRect(TabCount - 1);
        // When there are more tabs than fit, Windows draws its own scroll
        // buttons on the right: do not cover them.
        if (last.Right > ClientSize.Width - 32) return;

        int left = Math.Clamp(last.Right, 0, ClientSize.Width);
        int bottom = Math.Clamp(last.Bottom, 0, ClientSize.Height);
        if (left >= ClientSize.Width || bottom == 0) return;

        using Graphics g = Graphics.FromHwnd(Handle);
        using SolidBrush fill = new(BackColor);
        g.FillRectangle(fill, left, 0, ClientSize.Width - left, bottom);
        using Pen edge = new(Color.FromArgb(53, 72, 82));
        g.DrawLine(edge, left, bottom - 1, ClientSize.Width - 1, bottom - 1);
    }
}
