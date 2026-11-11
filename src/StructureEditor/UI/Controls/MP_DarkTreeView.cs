using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StructureEditor;

// Native TreeView remains responsible for layout, navigation and scrollbar.
// Only the visible hovered/selected rows gain lightweight color transitions.
internal sealed class MP_DarkTreeView : TreeView
{
    private readonly Dictionary<TreeNode, float> hover = new();
    private readonly Dictionary<TreeNode, float> selection = new();
    private TreeNode hoveredNode, selectedNode;

    internal MP_DarkTreeView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal float Hover(TreeNode node) => node != null ? hover.GetValueOrDefault(node) : 0f;
    internal float Selected(TreeNode node) => node != null
        ? selection.GetValueOrDefault(node, node == SelectedNode ? 1f : 0f) : 0f;

    internal void ResetMotion()
    {
        MP_Motion.Cancel(this);
        hover.Clear(); selection.Clear();
        hoveredNode = null;
        selectedNode = SelectedNode;
        if (selectedNode != null) selection[selectedNode] = 1f;
    }

    private void InvalidateNode(TreeNode node)
    {
        if (!IsHandleCreated || node == null || node.TreeView != this) return;
        Rectangle bounds = node.Bounds;
        if (bounds.IntersectsWith(ClientRectangle))
            Invalidate(new Rectangle(0, bounds.Top, ClientSize.Width, bounds.Height));
    }

    private void AnimateNode(Dictionary<TreeNode, float> values, string channel,
        TreeNode node, float target, int duration)
    {
        if (node == null || node.TreeView != this) return;
        float from = values.GetValueOrDefault(node);
        MP_Motion.Tween(this, channel + RuntimeHelpers.GetHashCode(node), from, target, t =>
        {
            if (node.TreeView != this) return;
            if (target == 0f && t < .001f) values.Remove(node);
            else values[node] = t;
            InvalidateNode(node);
        }, duration);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        TreeNode next = GetNodeAt(e.Location);
        if (ReferenceEquals(next, hoveredNode)) return;
        AnimateNode(hover, "tree-hover-", hoveredNode, 0f, 110);
        hoveredNode = next;
        AnimateNode(hover, "tree-hover-", next, 1f, 150);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        AnimateNode(hover, "tree-hover-", hoveredNode, 0f, 110);
        hoveredNode = null;
    }

    protected override void OnAfterSelect(TreeViewEventArgs e)
    {
        base.OnAfterSelect(e);
        if (ReferenceEquals(selectedNode, e.Node)) return;
        AnimateNode(selection, "tree-select-", selectedNode, 0f, 120);
        selectedNode = e.Node;
        AnimateNode(selection, "tree-select-", selectedNode, 1f, 167);
    }

    private const int TVS_NOHSCROLL = 0x8000;
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.Style |= TVS_NOHSCROLL;
            return p;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindows())
            _ = SetWindowTheme(Handle, "DarkMode_Explorer", null);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        MP_Motion.Cancel(this);
        base.OnHandleDestroyed(e);
    }
}
