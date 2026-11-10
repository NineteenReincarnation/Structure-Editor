using System.Drawing.Imaging;
using System.Globalization;
using StructureEditor.Compatibility;

namespace StructureEditor;

// UI only: game/Mod classification, search and cost calculations live in
// Compatibility/MP_ClusterCombatPower. Both lists reuse one dark host.
public partial class MainForm
{
    private TextBox MP_cpSearch;
    private ListBox MP_cpCatalogList, MP_cpBreakdown;
    private Label MP_cpCatalogCount, MP_cpTotal;
    private ToolTip MP_cpTooltip;
    private int MP_cpTooltipIndex = -1;
    private float MP_cpCatalogMax = 1f;
    private bool MP_cpRefreshing;

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
        MP_SmoothLayout layout = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = surface, Padding = new Padding(7), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        MP_cpSearch = new TextBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4),
            PlaceholderText = "搜索集群建筑 / DefName",
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(49, 58, 64), ForeColor = text
        };
        MP_cpSearch.TextChanged += (_, _) => MP_RefreshClusterCpCatalog();
        MP_cpCatalogCount = new Label
        {
            Dock = DockStyle.Fill, ForeColor = muted,
            Text = "尚未加载", TextAlign = ContentAlignment.MiddleLeft
        };
        MP_cpCatalogList = new ListBox
        {
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 52,
            BorderStyle = BorderStyle.None, IntegralHeight = false,
            BackColor = surface, ForeColor = text,
            SelectionMode = SelectionMode.One, Cursor = Cursors.Hand
        };
        MP_cpCatalogList.DrawItem += MP_DrawClusterCpCatalog;
        // Switching the selected item with keyboard or mouse immediately
        // activates the editor's existing placement brush.
        MP_cpCatalogList.SelectedIndexChanged += (_, _) => MP_SelectClusterCpBrush();
        // Re-clicking the same row also re-enters placement mode after switching tools.
        MP_cpCatalogList.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && MP_cpCatalogList.IndexFromPoint(e.Location) >= 0)
                MP_SelectClusterCpBrush();
        };
        MP_cpCatalogList.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            MP_SelectClusterCpBrush(); e.Handled = true; e.SuppressKeyPress = true;
        };
        MP_cpTooltip = new ToolTip { InitialDelay = 450, ReshowDelay = 120, AutoPopDelay = 9000 };
        MP_cpCatalogList.MouseMove += (_, e) =>
        {
            int index = MP_cpCatalogList.IndexFromPoint(e.Location);
            if (MP_cpTooltipIndex == index) return;
            MP_cpTooltipIndex = index;
            if (index < 0 || index >= MP_cpCatalogList.Items.Count)
            {
                MP_cpTooltip.SetToolTip(MP_cpCatalogList, "");
                return;
            }
            CatalogItem item = (CatalogItem)MP_cpCatalogList.Items[index];
            string cp = item.ClusterCombatPower.HasValue
                ? MP_FormatCp(item.ClusterCombatPower.Value) + " CP" : "未配置 CP";
            MP_cpTooltip.SetToolTip(MP_cpCatalogList,
                $"{item.ClusterWikiName ?? item.Label}\n{item.DefName}\n{item.Pack} · {cp}\n单击以选择放置");
        };
        MP_cpCatalogList.MouseLeave += (_, _) =>
        {
            MP_cpTooltipIndex = -1;
            MP_cpTooltip.SetToolTip(MP_cpCatalogList, "");
        };
        layout.Controls.Add(MP_cpSearch, 0, 0);
        layout.Controls.Add(MP_cpCatalogCount, 0, 1);
        layout.Controls.Add(MP_CpListHost(MP_cpCatalogList), 0, 2);
        return layout;
    }

    private void MP_SelectClusterCpBrush()
    {
        if (MP_cpRefreshing || MP_cpCatalogList?.SelectedItem is not CatalogItem item) return;
        if (ReferenceEquals(canvas.Brush, item) && canvas.Tool == CanvasTool.Place) return;
        MP_ActivatePaletteBrush(item);
        // Leave the item selected so placement has an obvious visible source.
    }

    private void MP_RefreshClusterCpCatalog()
    {
        if (MP_cpCatalogList == null || MP_cpCatalogCount == null) return;
        CatalogItem[] entries = MP_ClusterCombatPower.Search(catalog, MP_cpSearch?.Text);
        MP_cpCatalogCount.Text = catalog == null
            ? "尚未加载" : $"集群设施 {entries.Length} 种 · 单击选择放置";
        MP_cpCatalogMax = Math.Max(1f, entries.Length == 0 ? 0 :
            entries.Max(i => i.ClusterCombatPower.GetValueOrDefault()));
        bool identical = MP_cpCatalogList.Items.Count == entries.Length;
        for (int i = 0; identical && i < entries.Length; i++)
            identical = ReferenceEquals(MP_cpCatalogList.Items[i], entries[i]);
        if (identical)
        {
            MP_cpCatalogList.Invalidate();
            return;
        }
        CatalogItem selected = MP_cpCatalogList.SelectedItem as CatalogItem;
        MP_cpRefreshing = true;
        MP_cpTooltipIndex = -1;
        MP_cpCatalogList.BeginUpdate();
        try
        {
            MP_cpCatalogList.Items.Clear();
            MP_cpCatalogList.Items.AddRange(entries);
            if (selected != null)
            {
                int index = Array.FindIndex(entries, i => ReferenceEquals(i, selected));
                if (index >= 0) MP_cpCatalogList.SelectedIndex = index;
            }
        }
        finally
        {
            MP_cpCatalogList.EndUpdate();
            MP_cpRefreshing = false;
        }
    }

    private void MP_DrawClusterCpCatalog(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || catalog?.Textures == null) return;
        CatalogItem item = (CatalogItem)MP_cpCatalogList.Items[e.Index];
        Rectangle bounds = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        int Scale(float pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);
        using (SolidBrush bg = new(selected ? Color.FromArgb(52, 83, 92) : surface))
            e.Graphics.FillRectangle(bg, bounds);
        if (selected)
        {
            using SolidBrush accent = new(Color.FromArgb(133, 189, 207));
            e.Graphics.FillRectangle(accent, bounds.Left, bounds.Top, Scale(3), bounds.Height);
        }

        int iconSize = Math.Min(Scale(37), Math.Max(20, bounds.Height - Scale(12)));
        Rectangle iconRect = new(bounds.Left + Scale(5),
            bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        string texture = item.Icon ?? catalog.TextureFor(item);
        Bitmap image = catalog.Textures.Request(texture);
        if (image != null)
        {
            Rectangle source = item.Linked && item.Icon == null &&
                LinkedGraphics.IsAtlas(image, texture)
                ? LinkedGraphics.Source(image.Width, image.Height, 0)
                : new Rectangle(0, 0, image.Width, image.Height);
            float factor = Math.Min(iconSize / (float)source.Width,
                iconSize / (float)source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * factor));
            int height = Math.Max(1, (int)Math.Round(source.Height * factor));
            e.Graphics.DrawImage(image, new Rectangle(iconRect.X + (iconSize - width) / 2,
                iconRect.Y + (iconSize - height) / 2, width, height), source, GraphicsUnit.Pixel);
        }

        int left = iconRect.Right + Scale(8);
        int right = bounds.Right - Scale(6);
        int textWidth = Math.Max(1, right - left);
        int top = bounds.Top + Scale(4);
        // Give the name a full row. CP sits on the second row, never stealing
        // width from long labels such as 巨型自动迫击炮.
        string name = item.ClusterWikiName ?? item.Label;
        TextRenderer.DrawText(e.Graphics, name, Font,
            new Rectangle(left, top, textWidth, Scale(22)),
            selected ? Color.White : text,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string cp = item.ClusterCombatPower.HasValue
            ? MP_FormatCp(item.ClusterCombatPower.Value) + " CP" : "—";
        int cpWidth = Math.Max(Scale(53),
            TextRenderer.MeasureText(e.Graphics, cp, smallFont).Width + Scale(2));
        int secondTop = bounds.Top + Scale(25);
        TextRenderer.DrawText(e.Graphics, item.DefName, smallFont,
            new Rectangle(left, secondTop, Math.Max(1, textWidth - cpWidth - Scale(7)), Scale(20)),
            selected ? Color.FromArgb(196, 223, 230) : muted,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, cp, smallFont,
            new Rectangle(Math.Max(left, right - cpWidth), secondTop, cpWidth, Scale(20)),
            item.ClusterCombatPower.HasValue ? Color.FromArgb(165, 216, 229) : muted,
            TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        int barY = bounds.Bottom - Scale(3);
        int barWidth = Math.Max(0, right - left);
        using (SolidBrush track = new(Color.FromArgb(47, 64, 73)))
            e.Graphics.FillRectangle(track, left, barY, barWidth, Scale(2));
        int filled = (int)Math.Round(barWidth *
            Math.Clamp(item.ClusterCombatPower.GetValueOrDefault() / MP_cpCatalogMax, 0f, 1f));
        using (SolidBrush fill = new(Color.FromArgb(107, 166, 181)))
            e.Graphics.FillRectangle(fill, left, barY, filled, Scale(2));
        using Pen separator = new(Color.FromArgb(46, 61, 71));
        e.Graphics.DrawLine(separator, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

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

        MP_cpBreakdown = new ListBox
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
        MP_cpTotal.Text = MP_FormatCp(total) + " CP";
        bool same = MP_cpBreakdown.Items.Count == rows.Length;
        for (int i = 0; same && i < rows.Length; i++)
            same = MP_cpBreakdown.Items[i] is MP_ClusterCombatPower.Row old &&
                ReferenceEquals(old.Item, rows[i].Item) && old.Count == rows[i].Count &&
                Math.Abs(old.Total - rows[i].Total) < 0.001f;
        if (same) return;
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
        using (SolidBrush bg = new(Color.FromArgb(28, 40, 49)))
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
