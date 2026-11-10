namespace StructureEditor;

// Presentation-only layout composition. Page ownership, row spacing and
// field-label rhythm are centralized here; commands and data remain in
// MainForm / document services. Keep table rows content-sized so wrapped
// DefNames and selected-object details never overlap at high DPI.
internal static class MP_InspectorLayout
{
    internal const int RowGap = 7;

    internal static TableLayoutPanel CreatePage(
        MP_SectionTabs sections, string title, Color background, Color foreground)
    {
        var page = new TabPage(title)
        {
            BackColor = background,
            ForeColor = foreground,
            Padding = new Padding(MP_StudioTheme.PanelInset),
            AutoScroll = true
        };
        var contents = new MP_SmoothLayout
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(3),
            Margin = Padding.Empty
        };
        contents.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // The stable page host preserves this live control and its event
        // handlers. WinForms' AutoScroll measures it without a fixed
        // AutoScrollMinSize that would clip rows on resize.
        page.Controls.Add(contents);
        sections.AddPage(page);
        return contents;
    }

    internal static void AddRow(TableLayoutPanel panel, Control control, int height = 0)
    {
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 0, RowGap);
        if (height > 0) control.Height = height;
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, row);
    }

    internal static void AddField(TableLayoutPanel panel, string name,
        Control editor, Color secondaryText)
    {
        AddRow(panel, new Label
        {
            Text = name, ForeColor = secondaryText, AutoSize = true
        });
        AddRow(panel, editor, MP_StudioTheme.FieldHeight);
    }
}
