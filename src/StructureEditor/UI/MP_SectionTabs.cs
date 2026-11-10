using System.Windows.Forms.Integration;
namespace StructureEditor;

// The tab header is a WPF surface. Native WinForms TabControl changes create
// their own HWND/tab frame and erase its background during rapid switches.
// A stable Panel hosts existing TabPages directly and toggles only visibility.
internal sealed class MP_SectionTabs : UserControl
{
    private readonly MP_StablePageHost content;
    private readonly ElementHost header;
    private readonly TableLayoutPanel navigationLayout;
    private readonly MP_FluidTabBar bar;
    private readonly List<TabPage> pages = new();
    private readonly List<MP_PageSurface> surfaces = new();
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
            content.SelectPage(surfaces[value]);
            bar.Select(value);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal MP_SectionTabs()
    {
        BackColor = MP_StudioTheme.Panel;
        content = new MP_StablePageHost
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor
        };
        bar = new MP_FluidTabBar(value => SelectedIndex = value);
        header = new ElementHost
        {
            Child = bar, Dock = DockStyle.Fill,
            BackColor = MP_StudioTheme.Toolbar, TabStop = false
        };
        navigationLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = BackColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        navigationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // WinForms table rows are physical pixels while WPF tab height is
        // in DIPs. A fixed 40px row clips the 40-DIP tabs at 125%-200% DPI.
        navigationLayout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, MP_StudioTheme.HeaderHeight));
        navigationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigationLayout.Controls.Add(header, 0, 0);
        navigationLayout.Controls.Add(content, 0, 1);
        Controls.Add(navigationLayout);
        UpdateHeaderDpiHeight();
    }

    // WPF's 40 DIP tab strip must have an equally tall physical WinForms row.
    // The small reserve prevents rounded glyph descenders touching the page.
    private void UpdateHeaderDpiHeight()
    {
        if (navigationLayout == null) return;
        float factor = Math.Max(1f, DeviceDpi / 96f);
        float pixels = (float)Math.Ceiling(MP_StudioTheme.HeaderHeight * factor) + 3;
        RowStyle style = navigationLayout.RowStyles[0];
        if (Math.Abs(style.Height - pixels) < 0.5f) return;
        style.Height = pixels;
        navigationLayout.PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeaderDpiHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateHeaderDpiHeight();
    }

    internal bool MP_HeaderFitsDpi =>
        navigationLayout != null && navigationLayout.RowStyles[0].Height + 1 >=
        MP_StudioTheme.HeaderHeight * Math.Max(1f, DeviceDpi / 96f);

    internal bool SelectPage(TabPage page)
    {
        int index = pages.FindIndex(p => ReferenceEquals(p, page));
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    internal void AddPage(TabPage page)
    {
        // TabPage.AssignParent rejects any parent other than TabControl.
        // Transfer its existing children into an equivalent stable Panel,
        // retaining AutoScroll, padding, size and each child instance.
        var surface = new MP_PageSurface(page.Text)
        {
            Dock = DockStyle.Fill, BackColor = BackColor,
            ForeColor = page.ForeColor, Padding = page.Padding,
            AutoScroll = page.AutoScroll, Visible = false
        };
        var children = page.Controls.Cast<Control>().ToArray();
        foreach (var child in children)
        {
            page.Controls.Remove(child);
            surface.Controls.Add(child);
        }
        pages.Add(page);
        surfaces.Add(surface);
        content.Controls.Add(surface);
        bar.AddTab(page.Text);
        if (selection < 0) SelectedIndex = 0;
    }

    internal bool MP_HasStablePages =>
        content.Controls.Count == pages.Count
        && surfaces.All(surface => surface.Parent == content)
        && (selection < 0 || ReferenceEquals(content.ActivePage, surfaces[selection]))
        && surfaces.Count(page => page.MP_IsActive) == (selection < 0 ? 0 : 1);
}

// WinForms parent uses an opaque double-buffered background. We avoid
// WM_SETREDRAW and forced Refresh() calls; both produce white/black flashes
// in the mixed WPF/WinForms rendering stack on some Windows DPI settings.
internal sealed class MP_PageSurface : Panel
{
    internal string Title { get; }
    // Visibility returns false for every child before MainForm.Show().
    // Track page selection explicitly so startup checks do not misread it.
    internal bool MP_IsActive;
    internal MP_PageSurface(string title) => Title = title;
}

internal sealed class MP_StablePageHost : Panel
{
    internal MP_PageSurface ActivePage { get; private set; }

    internal MP_StablePageHost()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint, true);
    }

    internal void SelectPage(MP_PageSurface target)
    {
        if (ReferenceEquals(ActivePage, target)) return;
        SuspendLayout();
        try
        {
            // Bring the new page forward before hiding the old one.
            // Hiding first exposes a black parent for one frame in the
            // ElementHost/HWND mixed compositor when toggling rapidly.
            MP_PageSurface previous = ActivePage;
            if (target != null)
            {
                target.Bounds = ClientRectangle;
                target.MP_IsActive = true;
                target.Visible = true;
                target.BringToFront();
            }
            if (previous != null)
            {
                previous.MP_IsActive = false;
                previous.Visible = false;
            }
            ActivePage = target;
        }
        finally { ResumeLayout(false); }
    }
}
