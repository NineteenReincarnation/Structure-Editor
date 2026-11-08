using System.Runtime.InteropServices;

namespace StructureEditor;

// Native TreeView remains responsible for selection, indentation and keyboard
// navigation. Disable its Windows light horizontal scrollbar (long mod names
// are available as tooltips) and request Explorer's dark scrollbar theme.
internal sealed class MP_DarkTreeView : TreeView
{
    internal MP_DarkTreeView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer, true);
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
}
