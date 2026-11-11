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
    private bool MatchesScope(CatalogItem item) => CatalogCollections.Matches(
        item, scopeBox.SelectedIndex, Program.Paths.Favorites, Program.Paths.RecentAssets);
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

    // Catalog search, list painting and brush selection share the same workspace state.
    private void RefreshGroups()
    {
        if (catalog == null || filtering) return;
        filtering = true;
        try
        {
            string selected = groupBox.SelectedIndex > 0 ? groupBox.SelectedItem?.ToString() : null;
            string pack = packBox.SelectedIndex > 0 ? packBox.SelectedItem?.ToString() : null;
            groupBox.BeginUpdate(); groupBox.Items.Clear(); groupBox.Items.Add(ClusterScope ? "全部集群分类" : "全部分类");
            IEnumerable<string> groups = ClusterScope ? MechClusterCatalog.Groups : catalog.Query(FilterKind, pack, null, null).Where(MatchesScope).Select(i => i.Group).Distinct().OrderBy(CatalogGrouping.Order).ThenBy(g => g);
            foreach (string group in groups) groupBox.Items.Add(group);
            groupBox.SelectedIndex = selected != null && groupBox.Items.Contains(selected) ? groupBox.Items.IndexOf(selected) : 0; groupBox.EndUpdate();
        }
        finally { filtering = false; }
    }
    private void FilterPalette()
    {
        if (catalog == null || filtering) return;
        string query = search.Text.Trim(); string pack = packBox.SelectedIndex > 0 ? packBox.SelectedItem?.ToString() : null;
        string group = groupBox.SelectedIndex > 0 ? groupBox.SelectedItem?.ToString() : null;
        var results = MP_CatalogPaletteQuery.Select(
            catalog.Query(FilterKind, pack, group, query, ClusterScope),
            scopeBox.SelectedIndex, Program.Paths.Favorites, Program.Paths.RecentAssets);
        // Avoid clearing/repainting the owner-drawn list when the result set is identical.
        // This matters when users cycle layers or repeatedly change selection.
        bool identical = palette.Items.Count == results.Length;
        for (int i = 0; identical && i < results.Length; i++)
            identical = ReferenceEquals(palette.Items[i], results[i]);
        if (identical)
        {
            libraryInfo.Text = $"{results.Length} 个素材";
            MP_assetView?.SetItems(catalog, results);
            return;
        }
        CatalogItem previous = palette.SelectedItem as CatalogItem;
        (palette as MP_MotionListBox)?.ResetMotion();
        palette.BeginUpdate();
        try
        {
            MP_rebuildingPalette = true;
            palette.Items.Clear();
            palette.Items.AddRange(results);
            if (previous != null && results.Contains(previous)) palette.SelectedItem = previous;
        }
        finally
        {
            MP_rebuildingPalette = false;
            palette.EndUpdate();
        }
        libraryInfo.Text = $"{results.Length} 个素材";
        MP_assetView?.SetItems(catalog, results);
    }
    private void DrawPalette(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || catalog == null) return;
        int Px(float value) => (int)Math.Round(value * DeviceDpi / 96f);
        CatalogItem item = (CatalogItem)palette.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        MP_MotionListBox motionList = palette as MP_MotionListBox;
        Color background = MP_Motion.Blend(surface, MP_StudioTheme.Hover,
            motionList?.Hover(e.Index) ?? 0);
        background = MP_Motion.Blend(background, MP_StudioTheme.Active,
            motionList?.Selected(e.Index) ?? (selected ? 1f : 0f));
        using SolidBrush bg = new(background); e.Graphics.FillRectangle(bg, e.Bounds);
        string texturePath = item.Icon ?? catalog.TextureFor(item);
        Bitmap image = asyncThumbnails ? catalog.Textures.Request(texturePath) : catalog.Textures.Get(texturePath);
        if (image != null)
        {
            Rectangle src = item.Linked && item.Icon == null && LinkedGraphics.IsAtlas(image, texturePath)
                ? LinkedGraphics.Source(image.Width, image.Height, 0)
                : new Rectangle(0, 0, image.Width, image.Height);
            int iconSize = Px(47); float ratio = Math.Min((float)iconSize / src.Width, (float)iconSize / src.Height);
            Rectangle dest = new(e.Bounds.X + Px(4) + (int)((iconSize - src.Width * ratio) / 2), e.Bounds.Y + Px(6) + (int)((iconSize - src.Height * ratio) / 2), (int)(src.Width * ratio), (int)(src.Height * ratio));
            e.Graphics.DrawImage(image, dest, src, GraphicsUnit.Pixel);
        }
        string displayName = ClusterScope && !string.IsNullOrEmpty(item.ClusterWikiName) ? item.ClusterWikiName : item.Label;
        if (Program.Paths.Favorites.Contains(CatalogCollections.Key(item))) displayName = "★ " + displayName;
        TextRenderer.DrawText(e.Graphics, displayName, Font, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(9), Math.Max(1, e.Bounds.Width - Px(61)), Px(22)), selected ? MP_StudioTheme.ActiveText : text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, item.DefName, smallFont, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(32), Math.Max(1, e.Bounds.Width - Px(61)), Px(22)), selected ? MP_StudioTheme.ActiveText : muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        string category = ClusterScope ? item.ClusterGroup + (item.ClusterWikiEntry ? "" : " / 模组扩展") : item.Group;
        TextRenderer.DrawText(e.Graphics, category + " · " + item.Pack, smallFont, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(51), Math.Max(1, e.Bounds.Width - Px(61)), Px(18)), muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
    private void SelectBrush()
    {
        if (MP_rebuildingPalette || palette.SelectedItem is not CatalogItem item) return;
        // Clear the separate CP-list highlight when switching to another brush.
        MP_cpMotionView?.ClearSelection();
        MP_ActivatePaletteBrush(item);
    }

    // One selection path for the ordinary palette and the mech-cluster CP list.
    private void MP_ActivatePaletteBrush(CatalogItem item)
    {
        if (item == null) return;
        RememberAsset(item);
        MP_assetView?.MarkSelected(item);
        bool keepRectangle = canvas.Tool == CanvasTool.Rectangle && item.Kind == "Terrain";
        canvas.Brush = item;
        canvas.BrushRotation = item.Kind == "Pawn" ? 2 : 0;
        canvas.Tool = keepRectangle ? CanvasTool.Rectangle : CanvasTool.Place;
        canvas.ClearSelection();
        ShowItem(item, null);
        canvas.Invalidate();
    }
}
