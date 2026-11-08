namespace StructureEditor;

// Simple dark tab header + content switcher. Native TabControl otherwise paints
// a white unused header strip even when pages and selected tabs are owner-drawn.
internal sealed class MP_SectionTabs : UserControl
{
    private readonly FlowLayoutPanel header;
    private readonly Panel content;
    private readonly List<(TabPage Page, Button Button)> pages = new();
    private int selection = -1;

    internal event EventHandler SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selection;
        set
        {
            if (value < 0 || value >= pages.Count) return;
            if (selection == value) return;
            selection = value;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Page.Visible = i == value;
                pages[i].Button.BackColor = i == value
                    ? Color.FromArgb(54, 84, 99) : Color.FromArgb(37, 50, 59);
                pages[i].Button.ForeColor = i == value
                    ? Color.FromArgb(243, 247, 249) : Color.FromArgb(170, 193, 205);
                if (i == value) pages[i].Page.BringToFront();
            }
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal MP_SectionTabs()
    {
        BackColor = Color.FromArgb(33, 43, 52);
        content = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
        header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 31, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Color.FromArgb(37, 50, 59)
        };
        Controls.Add(content);
        Controls.Add(header);
    }

    internal void AddPage(TabPage page)
    {
        int index = pages.Count;
        page.Dock = DockStyle.Fill;
        page.Margin = Padding.Empty;
        page.Visible = false;
        page.BackColor = BackColor;
        var button = new Button
        {
            Text = page.Text,
            Width = Math.Max(50, TextRenderer.MeasureText(page.Text, Font).Width + 16),
            Height = 29, Margin = Padding.Empty, Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(170, 193, 205),
            BackColor = Color.FromArgb(37, 50, 59),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(61, 84, 96);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 96, 110);
        button.Click += (_, _) => SelectedIndex = index;
        pages.Add((page, button));
        content.Controls.Add(page);
        header.Controls.Add(button);
        if (selection == -1) SelectedIndex = 0;
    }
}
