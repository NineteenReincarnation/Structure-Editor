namespace StructureEditor;

public partial class MainForm
{
    private TableLayoutPanel BuildLibraryFilters()
    {
        TableLayoutPanel filters = new() { Name = "LibraryFilters", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        filters.ColumnStyles.Add(new(SizeType.Percent, 100));
        void Row(Control control)
        {
            control.Dock = DockStyle.Fill; control.Margin = new(0, 0, 0, 8);
            filters.RowStyles.Add(new(SizeType.AutoSize)); filters.Controls.Add(control, 0, filters.RowCount++);
        }
        void Picker(string title, ComboBox combo)
        {
            TableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new(SizeType.AutoSize)); row.ColumnStyles.Add(new(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = muted, Anchor = AnchorStyles.Left, Margin = new(0, 0, 10, 0) }, 0, 0);
            combo.Dock = DockStyle.Fill; combo.Margin = Padding.Empty; combo.DropDownStyle = ComboBoxStyle.DropDownList;
            row.Controls.Add(combo, 1, 0); Row(row);
        }
        // The active left tab already says 素材库. Start with the task users
        // actually perform: search, then progressively narrow the results.
        search.PlaceholderText = "搜索名称 / DefName"; search.BorderStyle = BorderStyle.FixedSingle; Row(search);
        scopeBox.Items.AddRange(new object[] { "全部素材", "常用", "收藏", "最近使用", "机械集群", "玩家模组", "原版与 DLC" }); scopeBox.SelectedIndex = 6;
        kindBox.Items.AddRange(new object[] { "建筑", "单位", "地形", "全部类型" }); kindBox.SelectedIndex = 0;
        Picker("集合", scopeBox); Picker("类型", kindBox); Picker("分类", groupBox); Picker("来源", packBox);
        TableLayoutPanel buttons = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new(SizeType.Percent, 50));
        Button favorite = MakeButton("☆ 收藏", 0, 0, 100), clear = MakeButton("重置筛选", 0, 0, 100);
        favorite.Dock = clear.Dock = DockStyle.Fill; favorite.Margin = new(0, 0, 4, 0); clear.Margin = new(4, 0, 0, 0);
        favorite.Click += (_, _) => ToggleFavorite();
        clear.Click += (_, _) =>
        {
            filtering = true;
            try { scopeBox.SelectedIndex = 0; kindBox.SelectedIndex = 3; kindBox.Enabled = true; search.Clear(); if (packBox.Items.Count > 0) packBox.SelectedIndex = 0; if (groupBox.Items.Count > 0) groupBox.SelectedIndex = 0; }
            finally { filtering = false; }
            RefreshPacks(); RefreshGroups(); FilterPalette();
        };
        buttons.Controls.Add(favorite, 0, 0); buttons.Controls.Add(clear, 1, 0); Row(buttons);
        libraryFilters = filters; return filters;
    }
}
