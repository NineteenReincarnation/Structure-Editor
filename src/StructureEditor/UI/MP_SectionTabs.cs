using System.Windows.Forms.Integration;
namespace StructureEditor;

// WPF owns navigation visuals; native TabControl still owns WinForms TabPages,
// preserving document commands, right-side inspector and structure library logic.
internal sealed class MP_SectionTabs : UserControl
{
    private readonly MP_BorderlessTabHost content;
    private readonly ElementHost header;
    private readonly MP_FluidTabBar bar;
    private readonly List<TabPage> pages = new();
    private int selection = -1;

    internal event EventHandler SelectedIndexChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selection;
        set
        {
            if (value < 0 || value >= pages.Count || selection == value) return;
            selection = value;
            if (content.SelectedIndex != value) content.SelectedIndex = value;
            bar.Select(value);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal MP_SectionTabs()
    {
        BackColor = MP_StudioTheme.Panel;
        content = new MP_BorderlessTabHost
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ForeColor = MP_StudioTheme.Text,
            Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(1, 1),
            Multiline = false,
            TabStop = false
        };
        content.SelectedIndexChanged += (_, _) =>
        {
            if (content.SelectedIndex >= 0 && content.SelectedIndex != selection)
                SelectedIndex = content.SelectedIndex;
        };
        bar = new MP_FluidTabBar(value => SelectedIndex = value);
        header = new ElementHost
        {
            Child = bar, Dock = DockStyle.Fill,
            BackColor = MP_StudioTheme.Toolbar, TabStop = false
        };
        // Dock.Fill and BringToFront allowed the tab header to overlay the
        // first input row (visible in the reported screenshot). Two explicit
        // grid rows guarantee that the page starts BELOW the header at every DPI.
        TableLayoutPanel MP_navigationLayout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        MP_navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        MP_navigationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, MP_StudioTheme.HeaderHeight));
        MP_navigationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        MP_navigationLayout.Controls.Add(header, 0, 0);
        MP_navigationLayout.Controls.Add(content, 0, 1);
        Controls.Add(MP_navigationLayout);
    }

    internal bool SelectPage(TabPage page)
    {
        int index = pages.FindIndex(p => ReferenceEquals(p, page));
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    internal void AddPage(TabPage page)
    {
        page.Margin = Padding.Empty;
        page.BackColor = BackColor;
        pages.Add(page);
        content.TabPages.Add(page);
        bar.AddTab(page.Text);
        if (selection < 0) SelectedIndex = 0;
    }
}

internal sealed class MP_BorderlessTabHost : TabControl
{
    // TabControl normally reserves a light 2px frame and native tab header even
    // when the header items are tiny. Our visible header is the dark button row.
    public override Rectangle DisplayRectangle => ClientRectangle;

    protected override void WndProc(ref Message m)
    {
        // TCM_ADJUSTRECT (Win32): leave the client rect unchanged so TabPages
        // occupy the full inner surface instead of displaying native light gaps.
        if (m.Msg == 0x1328 && m.LParam != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 0, 0);
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 4, 0);
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 8, ClientSize.Width);
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 12, ClientSize.Height);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
}
