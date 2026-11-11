namespace StructureEditor;

// Each open structure has a small clickable × at the right. Clicking the text
// activates it; clicking × (or using middle mouse) requests the regular close
// flow so dirty documents still receive the existing Save/Discard/Cancel dialog.
internal sealed class MP_DocumentTabButton : ToolStripButton
{
    internal event Action ActivateRequested;
    internal event Action CloseRequested;

    internal MP_DocumentTabButton()
    {
        ForeColor = Color.FromArgb(226, 236, 242);
        DisplayStyle = ToolStripItemDisplayStyle.Text;
        AutoToolTip = false;
        Padding = new Padding(5, 0, 5, 0);
        AccessibleDescription = "点击标签切换结构；点击右侧 × 关闭";
    }

    internal void UpdateTitle(string title)
    {
        Text = title + "   ×";
    }

    protected override void OnClick(EventArgs e)
    {
        if (Owner != null)
        {
            int x = Owner.PointToClient(Control.MousePosition).X;
            // The final 23px are a separate close target, not a second
            // ToolStripItem that could be detached from its document tab.
            if (x >= Bounds.Right - 23 && x < Bounds.Right)
            {
                CloseRequested?.Invoke();
                return;
            }
        }
        ActivateRequested?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle)
        {
            CloseRequested?.Invoke();
            return;
        }
        base.OnMouseUp(e);
    }
}
