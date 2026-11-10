using System.Drawing.Imaging;
using System.Globalization;
using StructureEditor.Compatibility;

namespace StructureEditor;

// UI only: game/Mod classification, search and cost calculations live in
// Compatibility/MP_ClusterCombatPower. Both lists reuse one dark host.
public partial class MainForm
{
    private MP_CpMotionView MP_cpMotionView;
    private ListBox MP_cpBreakdown;
    private float MP_cpDisplayedTotal;
    private Label MP_cpTotal;

    private static string MP_FormatCp(float value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private Control MP_CpListHost(ListBox list)
    {
        Panel host = new() { Dock = DockStyle.Fill, BackColor = surface };
        MP_DarkPaletteScrollbar scrollbar = new(list);
        list.Dock = DockStyle.None;
        list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        host.Resize += (_, _) =>
        {
            if (host.ClientSize.Width <= 0 || host.ClientSize.Height <= 0) return;
            list.Bounds = new Rectangle(0, 0,
                host.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
                host.ClientSize.Height);
            scrollbar.BringToFront();
        };
        host.Controls.Add(list);
        host.Controls.Add(scrollbar);
        scrollbar.BringToFront();
        return host;
    }

    private Control MP_CreateClusterCpCatalog()
    {
        MP_cpMotionView = new MP_CpMotionView(item =>
        {
            if (item == null) return;
            MP_ActivatePaletteBrush(item);
        });
        return new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill, BackColor = surface,
            Child = MP_cpMotionView, TabStop = true
        };
    }

    private void MP_RefreshClusterCpCatalog() => MP_cpMotionView?.SetCatalog(catalog);

    private Control MP_CreateClusterCpFooter()
    {
        MP_SmoothLayout layout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(9, 8, 9, 6),
            BackColor = Color.FromArgb(28, 40, 49)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Paint += (_, e) =>
        {
            using Pen border = new(Color.FromArgb(65, 89, 101));
            e.Graphics.DrawLine(border, 0, 0, layout.Width, 0);
        };

        Panel header = new() { Dock = DockStyle.Fill, BackColor = layout.BackColor };
        Label title = new()
        {
            Text = "当前结构 · 建筑 CP", AutoSize = false,
            Dock = DockStyle.Left, Width = 145,
            ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft
        };
        MP_cpTotal = new Label
        {
            Text = "0 CP", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(180, 223, 232),
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight
        };
        header.Controls.Add(MP_cpTotal);
        header.Controls.Add(title);

        MP_cpBreakdown = new MP_MotionListBox
        {
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27,
            IntegralHeight = false, BorderStyle = BorderStyle.None,
            BackColor = layout.BackColor, ForeColor = text,
            SelectionMode = SelectionMode.None
        };
        MP_cpBreakdown.DrawItem += MP_DrawClusterCpLine;
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(MP_CpListHost(MP_cpBreakdown), 0, 1);
        return layout;
    }

    private void MP_RefreshClusterCpSummary()
    {
        if (MP_cpBreakdown == null || MP_cpTotal == null) return;
        MP_ClusterCombatPower.Row[] rows = MP_ClusterCombatPower.Calculate(catalog, document);
        float total = MP_ClusterCombatPower.Total(rows);
        float from = MP_cpDisplayedTotal;
        MP_Motion.Tween(MP_cpTotal, "cp-total", from, total,
            value =>
            {
                MP_cpDisplayedTotal = value;
                MP_cpTotal.Text = MP_FormatCp(value) + " CP";
            }, 170);
        bool same = MP_cpBreakdown.Items.Count == rows.Length;
        for (int i = 0; same && i < rows.Length; i++)
            same = MP_cpBreakdown.Items[i] is MP_ClusterCombatPower.Row old &&
                ReferenceEquals(old.Item, rows[i].Item) && old.Count == rows[i].Count &&
                Math.Abs(old.Total - rows[i].Total) < 0.001f;
        if (same) return;
        (MP_cpBreakdown as MP_MotionListBox)?.ResetMotion();
        MP_cpBreakdown.BeginUpdate();
        try
        {
            MP_cpBreakdown.Items.Clear();
            if (rows.Length == 0) MP_cpBreakdown.Items.Add("无计入 CP 的集群设施");
            else MP_cpBreakdown.Items.AddRange(rows);
        }
        finally { MP_cpBreakdown.EndUpdate(); }
    }

    private void MP_DrawClusterCpLine(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        Rectangle bounds = e.Bounds;
        Color background = MP_Motion.Blend(Color.FromArgb(28, 40, 49),
            Color.FromArgb(45, 65, 76),
            (MP_cpBreakdown as MP_MotionListBox)?.Hover(e.Index) ?? 0f);
        using (SolidBrush bg = new(background))
            e.Graphics.FillRectangle(bg, bounds);
        if (MP_cpBreakdown.Items[e.Index] is not MP_ClusterCombatPower.Row row)
        {
            TextRenderer.DrawText(e.Graphics, MP_cpBreakdown.Items[e.Index].ToString(),
                smallFont, bounds, muted, TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);
            return;
        }
        string name = row.Item.ClusterWikiName ?? row.Item.Label;
        string cost = MP_FormatCp(row.Total);
        int costWidth = Math.Max(58, TextRenderer.MeasureText(e.Graphics,
            cost, Font).Width + 7);
        TextRenderer.DrawText(e.Graphics, $"{name} ×{row.Count}", Font,
            new Rectangle(bounds.Left, bounds.Top,
                Math.Max(1, bounds.Width - costWidth - 8), bounds.Height),
            text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, cost, Font,
            new Rectangle(Math.Max(bounds.Left, bounds.Right - costWidth),
                bounds.Top, costWidth - 3, bounds.Height),
            Color.FromArgb(176, 215, 226),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine);
    }
}
