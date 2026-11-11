namespace StructureEditor;

// Catalog collection workflow: user filters, favorites and source dialogs.
// UI state lives here, never inside the catalog model.
public partial class MainForm
{
    private void RefreshPacks()
    {
        if (catalog == null || filtering) return;
        string selected = packBox.SelectedIndex > 0 ? packBox.SelectedItem?.ToString() : null;
        filtering = true;
        try
        {
            packBox.Items.Clear(); packBox.Items.Add("所有来源");
            foreach (string pack in catalog.Items.Where(MatchesScope).Select(i => i.Pack).Distinct().OrderBy(p => p)) packBox.Items.Add(pack);
            packBox.SelectedIndex = selected != null && packBox.Items.Contains(selected) ? packBox.Items.IndexOf(selected) : 0;
        }
        finally { filtering = false; }
    }
    private bool MatchesScope(CatalogItem item) => CatalogCollections.Matches(item, scopeBox.SelectedIndex, Program.Paths);
    private void ToggleFavorite()
    {
        CatalogItem item = canvas.Selected != null ? catalog?.Find(canvas.Selected.Kind, canvas.Selected.DefName) : canvas.Brush;
        if (item == null) { status.Text = "先选择一个素材或画布对象，再加入收藏。"; return; }
        string key = CatalogCollections.Key(item);
        if (!Program.Paths.Favorites.Remove(key)) Program.Paths.Favorites.Add(key);
        Program.SavePaths(); RefreshGroups(); FilterPalette(); status.Text = Program.Paths.Favorites.Contains(key) ? "已收藏：" + item.Label : "已取消收藏：" + item.Label;
    }
    private void RememberAsset(CatalogItem item)
    {
        string key = CatalogCollections.Key(item); Program.Paths.RecentAssets.Remove(key); Program.Paths.RecentAssets.Insert(0, key);
        Program.Paths.RecentAssets = Program.Paths.RecentAssets.Take(32).ToList(); Program.SavePaths();
    }
    private void ShowSources()
    {
        using Form dialog = new() { Text = "素材来源", StartPosition = FormStartPosition.CenterParent, Size = new(650, 400), MinimumSize = new(500, 300), Font = Font, BackColor = surface, ForeColor = text };
        ListBox list = new() { Dock = DockStyle.Fill, BackColor = surface, ForeColor = text, HorizontalScrollbar = true, BorderStyle = BorderStyle.None };
        foreach (string path in Program.Paths.ExtraMods) list.Items.Add(path);
        CheckBox auto = new() { Dock = DockStyle.Top, Height = 38, Checked = Program.Paths.AutoLoadActiveMods, Text = "自动读取玩家当前启用的本地与创意工坊模组", Padding = new(12,0,0,0), AutoSize = false };
        Label note = new() { Dock = DockStyle.Top, Height = 65, Padding = new(12), Text = "基础游戏、DLC 自动读取；已启用模组按 ModsConfig 顺序读取。\n下面管理额外目录；玩家模组在素材库中有独立集合。" };
        FlowLayoutPanel controls = new() { Dock = DockStyle.Bottom, Height = 50, Padding = new(10), FlowDirection = FlowDirection.RightToLeft };
        Button done = MakeButton("完成", 0, 0, 85), add = MakeButton("添加目录…", 0, 0, 100), remove = MakeButton("移除来源", 0, 0, 100), config = MakeButton("游戏模组配置…",0,0,130);
        controls.Controls.AddRange(new Control[] { done, remove, add, config }); done.Click += (_, _) => dialog.Close();
        string configPath = Program.Paths.ModsConfigPath;
        config.Click += (_,_) => { using OpenFileDialog file = new() { Title = "选择玩家的 ModsConfig.xml", Filter = "模组配置|ModsConfig.xml|XML|*.xml", FileName = configPath ?? ModSources.DefaultConfig }; if(file.ShowDialog(dialog)==DialogResult.OK) configPath=file.FileName; };
        add.Click += (_, _) => { using FolderBrowserDialog folder = new() { Description = "选择模组根目录" }; if (folder.ShowDialog(dialog) == DialogResult.OK && !list.Items.Contains(folder.SelectedPath)) list.Items.Add(folder.SelectedPath); };
        remove.Click += (_, _) => { if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex); };
        dialog.Controls.Add(list); dialog.Controls.Add(auto); dialog.Controls.Add(note); dialog.Controls.Add(controls); dialog.ShowDialog(this);
        var sources = list.Items.Cast<string>().ToList();
        if (!sources.SequenceEqual(Program.Paths.ExtraMods) || auto.Checked != Program.Paths.AutoLoadActiveMods || configPath != Program.Paths.ModsConfigPath) { Program.Paths.ExtraMods = sources; Program.Paths.AutoLoadActiveMods=auto.Checked; Program.Paths.ModsConfigPath=configPath; Program.SavePaths(); if (Program.IsGame(Program.Paths.GameRoot)) LoadCatalog(); }
    }
}
