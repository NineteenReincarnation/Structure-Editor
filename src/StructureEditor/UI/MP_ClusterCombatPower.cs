using System.Globalization;

namespace StructureEditor;

public partial class MainForm
{
    private TextBox MP_cpSearch;
    private ListBox MP_cpCatalogList, MP_cpBreakdown;
    private Label MP_cpCatalogCount, MP_cpTotal;
    private float MP_cpCatalogMax = 1f;

    private sealed record MP_CpLine(CatalogItem Item, int Count)
    {
        internal float Total => Count * Item.ClusterCombatPower.GetValueOrDefault();
    }

    // Use only the already classified mech-cluster catalogue; the two
    // defensive construction primitives are intentionally free in this view.
    private static bool MP_ClusterCpEligible(CatalogItem item)
        => item?.Kind == "Building" && item.ClusterGroup != null
            && item.ClusterGroup != "防御设施";

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
            if (host.ClientSize.Width == 0 || host.ClientSize.Height == 0) return;
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
            BackColor = surface, Padding = new Padding(8), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        MP_cpSearch = new TextBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5),
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
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 70,
            BorderStyle = BorderStyle.None, IntegralHeight = false,
            BackColor = surface, ForeColor = text, SelectionMode = SelectionMode.None
        };
        MP_cpCatalogList.DrawItem += MP_DrawClusterCpCatalog;
        layout.Controls.Add(MP_cpSearch, 0, 0);
        layout.Controls.Add(MP_cpCatalogCount, 0, 1);
        layout.Controls.Add(MP_CpListHost(MP_cpCatalogList), 0, 2);
        return layout;
    }

    private void MP_RefreshClusterCpCatalog()
    {
        if (MP_cpCatalogList == null || MP_cpCatalogCount == null) return;
        string search = MP_cpSearch?.Text.Trim() ?? "";
        CatalogItem[] entries = catalog == null ? Array.Empty<CatalogItem>() :
            catalog.Items.Where(MP_ClusterCpEligible)
                .Where(i => search.Length == 0 ||
                    (i.Label?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) ||
                    i.DefName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (i.ClusterWikiName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                .OrderByDescending(i => i.ClusterCombatPower.HasValue)
                .ThenByDescending(i => i.ClusterCombatPower.GetValueOrDefault())
                .ThenBy(i => i.Label).ToArray();
        MP_cpCatalogMax = Math.Max(1f, entries.Length == 0 ? 0f :
            entries.Max(i => i.ClusterCombatPower.GetValueOrDefault()));
        MP_cpCatalogList.BeginUpdate();
        try
        {
            MP_cpCatalogList.Items.Clear();
            MP_cpCatalogList.Items.AddRange(entries);
        }
        finally { MP_cpCatalogList.EndUpdate(); }
        MP_cpCatalogCount.Text = catalog == null ? "尚未加载" : $"集群设施 {entries.Length} 种";
    }

    private void MP_DrawClusterCpCatalog(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || catalog?.Textures == null) return;
        CatalogItem item = (CatalogItem)MP_cpCatalogList.Items[e.Index];
        Rectangle rect = e.Bounds;
        using (SolidBrush bg = new(surface)) e.Graphics.FillRectangle(bg, rect);
        int icon = Math.Min(47, Math.Max(28, rect.Height - 17));
        Rectangle iconBox = new(rect.Left + 3, rect.Top + 6, icon, icon);
        Bitmap image = catalog.Textures.Request(item.Icon ?? catalog.TextureFor(item));
        if (image != null)
        {
            Rectangle source = item.Linked && item.Icon == null &&
                LinkedGraphics.IsAtlas(image, item.Texture)
                ? LinkedGraphics.Source(image.Width, image.Height, 0)
                : new Rectangle(0, 0, image.Width, image.Height);
            float factor = Math.Min(icon / (float)source.Width, icon / (float)source.Height);
            int width = Math.Max(1, (int)(source.Width * factor));
            int height = Math.Max(1, (int)(source.Height * factor));
            e.Graphics.DrawImage(image,
                new Rectangle(iconBox.Left + (icon - width) / 2,
                    iconBox.Top + (icon - height) / 2, width, height),
                source, GraphicsUnit.Pixel);
        }
        int left = iconBox.Right + 7;
        int widthText = Math.Max(1, rect.Right - left - 74);
        string name = item.ClusterWikiName ?? item.Label;
        TextRenderer.DrawText(e.Graphics, name, Font,
            new Rectangle(left, rect.Top + 5, widthText, 23), text,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, item.DefName, smallFont,
            new Rectangle(left, rect.Top + 27, Math.Max(1, rect.Right - left - 6), 18),
            muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        string cp = item.ClusterCombatPower.HasValue
            ? MP_FormatCp(item.ClusterCombatPower.Value) + " CP" : "—";
        TextRenderer.DrawText(e.Graphics, cp, Font,
            new Rectangle(Math.Max(left, rect.Right - 80), rect.Top + 5, 73, 23),
            item.ClusterCombatPower.HasValue ? Color.FromArgb(172, 213, 222) : muted,
            TextFormatFlags.Right | TextFormatFlags.SingleLine);
        int barWidth = Math.Max(0, rect.Right - left - 11);
        int barY = rect.Bottom - 10;
        using (SolidBrush track = new(Color.FromArgb(48, 65, 74)))
            e.Graphics.FillRectangle(track, left, barY, barWidth, 4);
        int filled = (int)(barWidth *
            Math.Clamp(item.ClusterCombatPower.GetValueOrDefault() / MP_cpCatalogMax, 0f, 1f));
        using (SolidBrush fill = new(Color.FromArgb(107, 166, 181)))
            e.Graphics.FillRectangle(fill, left, barY, filled, 4);
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
        MP_CpLine[] rows = catalog == null ? Array.Empty<MP_CpLine>() :
            document.Items.Where(i => i.Kind == "Building")
                .GroupBy(i => i.DefName, StringComparer.Ordinal)
                .Select(group => new MP_CpLine(catalog.Find("Building", group.Key), group.Count()))
                .Where(line => MP_ClusterCpEligible(line.Item) &&
                    line.Item.ClusterCombatPower.GetValueOrDefault() > 0f)
                .OrderByDescending(line => line.Total)
                .ThenBy(line => line.Item.Label).ToArray();
        float total = rows.Sum(row => row.Total);
        MP_cpTotal.Text = MP_FormatCp(total) + " CP";
        MP_cpBreakdown.BeginUpdate();
        try
        {
            MP_cpBreakdown.Items.Clear();
            if (rows.Length == 0)
                MP_cpBreakdown.Items.Add("无计入 CP 的集群设施");
            else
                MP_cpBreakdown.Items.AddRange(rows);
        }
        finally { MP_cpBreakdown.EndUpdate(); }
    }

    private void MP_DrawClusterCpLine(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        Rectangle bounds = e.Bounds;
        using (SolidBrush bg = new(Color.FromArgb(28, 40, 49)))
            e.Graphics.FillRectangle(bg, bounds);
        if (MP_cpBreakdown.Items[e.Index] is not MP_CpLine row)
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
