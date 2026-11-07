using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Linq;
using StructureEditor.Compatibility;

namespace StructureEditor;

public partial class MainForm : Form
{
    private readonly StructureCanvas canvas = new();
    private readonly TextBox search = new(), idBox = new(), labelBox = new();
    private readonly ComboBox kindBox = new(), packBox = new(), groupBox = new(), scopeBox = new(), materialBox = new(), rotationBox = new(), categoryBox = new(), dormancyBox = new();
    private readonly Label dormancyInfo = new();
    private readonly CheckBox dormant = new();
    private readonly ComboBox profileBox = new();
    private readonly CheckBox mpCompatibility = new();
    private readonly Label selectedName = new(), selectedInfo = new(), libraryInfo = new(), summary = new();
    private readonly ListBox palette = new();
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Stack<StructureDocument> undo = new(), redo = new();
    private readonly Color surface = Color.FromArgb(38, 44, 48), text = Color.FromArgb(224, 231, 232), muted = Color.FromArgb(152, 170, 176);
    private Catalog catalog;
    private StructureDocument document = new();
    private string currentFile;
    private bool dirty, updating, filtering, saveLocationChanged;
    private List<PlacedItem> copied = new();
    private CatalogItem shownItem;

    public MainForm()
    {
        Text = "Structure Editor · 结构工坊";
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = surface; ForeColor = text;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Math.Min(1500, Screen.PrimaryScreen!.WorkingArea.Width - 80), Math.Min(940, Screen.PrimaryScreen.WorkingArea.Height - 60));
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 640); KeyPreview = true;
        BuildUi();
        RefreshMetadata(); ShowSelection();
        InitializeBackgroundLoading();
        KeyDown += (_, e) =>
        {
            // List boxes and closed DropDownList controls can classify letters as input keys,
            // bypassing ProcessCmdKey. KeyPreview routes those same mode keys once at form level.
            if (!InputOwnsShortcut(ShortcutInput, e.KeyData) && HandleModeShortcut(e.KeyData))
            { e.Handled = true; e.SuppressKeyPress = true; }
        };
        canvas.BeforeChange += Snapshot;
        canvas.Changed += () => { dirty = true; UpdateTitle(); UpdateSummary(); };
        canvas.SelectionChanged += ShowSelection;
        canvas.Hint += message => status.Text = message;
        FormClosing += (_, e) => { try { if (!AskSaveWorkspace()) e.Cancel = true; } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } };
        Load += (_, _) => { UpdatePaletteDensity(); FitWorkingArea(); };
        Shown += (_, _) => { if (Program.IsGame(Program.Paths.GameRoot)) LoadCatalog(); else { canvas.Document=document; RefreshMetadata(); status.Text="请在 编辑 → 首选项 中选择 RimWorld 游戏目录，加载本机素材。"; } };
    }
    private void BuildUi() => BuildWorkspaceUi();
    private Button MakeButton(string caption, int x, int y, int width)
    {
        Button button = new() { Text = caption, Location = new Point(x, y), Size = new Size(width, 29), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(51, 62, 66), ForeColor = text, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = Color.FromArgb(81, 99, 105); return button;
    }
    private void AddTool(ToolStrip strip, string caption, Action action)
    {
        ToolStripButton button = new(caption) { ForeColor = text, DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += (_, _) => Safe(action); strip.Items.Add(button);
    }
    private void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "结构工坊", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async void LoadCatalog() => await LoadCatalogAsync();
    private string StructuresFolder => Program.DefaultProjects;
    private string OutputFolder => string.IsNullOrWhiteSpace(Program.Paths.SaveDirectory) ? StructuresFolder : Program.Paths.SaveDirectory;
    private string SelectedCategory => categoryBox.SelectedIndex == 0 ? "MechCluster" : "General";
    private string FilterKind => kindBox.SelectedIndex is >= 0 and <= 2 ? new[] { "Building", "Pawn", "Terrain" }[kindBox.SelectedIndex] : null;
    private bool ClusterScope => scopeBox.SelectedIndex == 4;
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
        palette.BeginUpdate(); palette.Items.Clear();
        foreach (CatalogItem item in catalog.Query(FilterKind, pack, group, query, ClusterScope).Where(MatchesScope)
            .OrderBy(i => scopeBox.SelectedIndex == 3 ? Program.Paths.RecentAssets.IndexOf(CatalogCollections.Key(i)) : CatalogGrouping.Order(i.Group)).ThenBy(i => i.Pack == "Core" ? 0 : 1).ThenBy(i => i.Label)) palette.Items.Add(item);
        palette.EndUpdate(); libraryInfo.Text = $"当前 {palette.Items.Count} 项"+(scopeBox.SelectedIndex==5?" · 玩家模组":ClusterScope?" · 机械集群":"")+"\n"+(catalog.RuntimeSnapshotUsed?"目录来源：游戏内实际加载的 Def":"目录来源：本机 XML 与素材");
    }
    private void DrawPalette(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || catalog == null) return;
        int Px(float value) => (int)Math.Round(value * DeviceDpi / 96f);
        CatalogItem item = (CatalogItem)palette.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using SolidBrush bg = new(selected ? Color.FromArgb(53, 79, 82) : surface); e.Graphics.FillRectangle(bg, e.Bounds);
        string texturePath = item.Icon ?? catalog.TextureFor(item);
        Bitmap image = asyncThumbnails ? catalog.Textures.Request(texturePath) : catalog.Textures.Get(texturePath);
        if (image != null)
        {
            Rectangle src = item.Linked && item.Icon == null ? LinkedGraphics.Source(image.Width, image.Height, 0) : new Rectangle(0, 0, image.Width, image.Height);
            int iconSize = Px(47); float ratio = Math.Min((float)iconSize / src.Width, (float)iconSize / src.Height);
            Rectangle dest = new(e.Bounds.X + Px(4) + (int)((iconSize - src.Width * ratio) / 2), e.Bounds.Y + Px(6) + (int)((iconSize - src.Height * ratio) / 2), (int)(src.Width * ratio), (int)(src.Height * ratio));
            e.Graphics.DrawImage(image, dest, src, GraphicsUnit.Pixel);
        }
        string displayName = ClusterScope && !string.IsNullOrEmpty(item.ClusterWikiName) ? item.ClusterWikiName : item.Label;
        if (Program.Paths.Favorites.Contains(CatalogCollections.Key(item))) displayName = "★ " + displayName;
        TextRenderer.DrawText(e.Graphics, displayName, Font, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(9), Math.Max(1, e.Bounds.Width - Px(61)), Px(22)), text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, item.DefName, smallFont, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(32), Math.Max(1, e.Bounds.Width - Px(61)), Px(22)), muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        string category = ClusterScope ? item.ClusterGroup + (item.ClusterWikiEntry ? "" : " / 模组扩展") : item.Group;
        TextRenderer.DrawText(e.Graphics, category + " · " + item.Pack, smallFont, new Rectangle(e.Bounds.X + Px(58), e.Bounds.Y + Px(51), Math.Max(1, e.Bounds.Width - Px(61)), Px(18)), muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
    private void SelectBrush()
    {
        if (palette.SelectedItem is not CatalogItem item) return;
        RememberAsset(item);
        bool keepRectangle = canvas.Tool == CanvasTool.Rectangle && item.Kind == "Terrain";
        canvas.Brush = item; canvas.BrushRotation = item.Kind == "Pawn" ? 2 : 0; canvas.Tool = keepRectangle ? CanvasTool.Rectangle : CanvasTool.Place; canvas.ClearSelection();
        ShowItem(item, null); canvas.Invalidate();
    }
    private void ShowSelection()
    {
        if (canvas.Selected != null && catalog != null) ShowItem(catalog.Find(canvas.Selected.Kind, canvas.Selected.DefName), canvas.Selected);
        else if (canvas.Brush != null) ShowItem(canvas.Brush, null);
        else { selectedName.Text = "未选择对象"; selectedInfo.Text = "从左侧选择素材，或按 V 选择已有对象。"; }
        UpdateSummary();
    }
    private void ShowItem(CatalogItem item, PlacedItem placed)
    {
        if (item == null) return;
        updating = true;
        try
        {
            selectedName.Text = item.Label + "\n" + item.DefName;
            selectedInfo.Text = $"来源：{item.Pack}\n占地 {item.Width} × {item.Height}  ·  {(placed == null ? "当前笔刷" : $"已选 {canvas.Selection.Count} 个对象")}";
            if (shownItem != item)
            {
                materialBox.BeginUpdate(); materialBox.Items.Clear();
                foreach (MaterialItem m in catalog.MaterialsFor(item).OrderBy(m => m.DefName == "Steel" ? 0 : 1).ThenBy(m => m.Label)) materialBox.Items.Add(m);
                materialBox.EndUpdate(); shownItem = item;
            }
            MaterialItem preferred = materialBox.Items.Cast<MaterialItem>().FirstOrDefault(m => m.DefName == (placed?.Stuff ?? canvas.BrushStuff));
            if (materialBox.Items.Count > 0) materialBox.SelectedItem = preferred ?? materialBox.Items[0];
            materialBox.Enabled = materialBox.Items.Count > 0;
            canvas.BrushStuff = (materialBox.SelectedItem as MaterialItem)?.DefName;
            rotationBox.SelectedIndex = placed?.Rotation ?? canvas.BrushRotation; rotationBox.Enabled = item.Rotatable || canvas.Selection.Count > 1;
            var supported = placed == null ? new List<PlacedItem>() : canvas.Selection.Where(i => catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true).ToList();
            bool mp = document.ExportProfile == "MechProtocol" || document.ExportProfile == "Framework" && document.EnableMPCompatibility;
            dormancyBox.Enabled = mp && (placed == null ? item.CanBeDormant : supported.Count > 0);
            var states = supported.Select(i => i.StartDormant).Distinct().ToList();
            bool? setting = placed == null ? canvas.BrushDormant : supported.FirstOrDefault()?.StartDormant;
            dormancyBox.SelectedIndex = states.Count > 1 ? 3 : setting == true ? 1 : setting == false ? 2 : 0;
            dormancyInfo.Text = !mp ? "逐对象休眠是 MP 专用兼容功能。\n可在“导出”页启用 MP 扩展。" : dormancyBox.Enabled ? "MP 兼容初态；受攻击或触发器影响仍会唤醒。" : "此对象没有休眠组件，无法设置休眠。";
        }
        finally { updating = false; }
    }
    private void CommitInspectorEdit()
    {
        if (updating) return;
        canvas.FinishEdit();
        ShowSelection();
    }
    private void Snapshot()
    {
        if (updating) return;
        undo.Push(document.Copy()); redo.Clear();
        int cost = 0;
        var kept = undo.Take(64).TakeWhile((d, n) => { cost += d.Items.Count; return n < 2 || cost <= 250000; }).Reverse().ToList();
        undo.Clear(); foreach (var d in kept) undo.Push(d);
    }
    private void Undo() { CommitInspectorEdit(); if (undo.Count == 0) return; redo.Push(document.Copy()); document = undo.Pop(); RestoreDocument(); }
    private void Redo() { CommitInspectorEdit(); if (redo.Count == 0) return; undo.Push(document.Copy()); document = redo.Pop(); RestoreDocument(); }
    private void RestoreDocument() { canvas.Document = document; canvas.ClearSelection(); dirty = true; RefreshMetadata(); canvas.Invalidate(); }
    private void RefreshMetadata()
    {
        updating = true; idBox.Text = document.DefName; labelBox.Text = document.Label; categoryBox.SelectedIndex = document.Category == "MechCluster" ? 0 : 1; dormant.Checked = document.StartDormant;
        profileBox.SelectedIndex = document.ExportProfile == "MechProtocol" ? 2 : document.ExportProfile == "VanillaPrefab" ? 1 : 0;
        mpCompatibility.Checked = document.EnableMPCompatibility; mpCompatibility.Enabled = document.ExportProfile == "Framework";
        autoExport.Checked = document.AutoExportOnSave; exportResult.Text = "";
        dormant.Enabled = document.ExportProfile == "MechProtocol" || document.ExportProfile == "Framework" && document.EnableMPCompatibility; updating = false;
        UpdateTitle(); UpdateSummary();
        RefreshSaveLocation(); LoadNamingFields();
    }
    private void CommitMetadata()
    {
        if (updating) return;
        string profile = profileBox.SelectedIndex == 2 ? "MechProtocol" : profileBox.SelectedIndex == 1 ? "VanillaPrefab" : "Framework";
        if (document.DefName == idBox.Text && document.Label == labelBox.Text && document.Category == SelectedCategory && document.StartDormant == dormant.Checked && document.ExportProfile == profile && document.EnableMPCompatibility == mpCompatibility.Checked && document.AutoExportOnSave == autoExport.Checked) return;
        Snapshot(); document.DefName = idBox.Text.Trim(); document.Label = labelBox.Text.Trim(); document.Category = SelectedCategory; document.StartDormant = dormant.Checked; document.ExportProfile = profile; document.EnableMPCompatibility = mpCompatibility.Checked;
        document.AutoExportOnSave = autoExport.Checked;
        dirty = true; UpdateTitle(); canvas.Invalidate(); RefreshMetadata(); ShowSelection();
    }
    private void UpdateTitle() { Text = "Structure Editor · 结构工坊 — " + document.Label + (dirty ? " *" : ""); RefreshDocumentTabs(); RefreshNamingPreview(); }
    private void UpdateSummary()
    {
        summary.Text = $"建筑 {document.Items.Count(i => i.Kind == "Building")}   单位 {document.Items.Count(i => i.Kind == "Pawn")}\n地板 {document.Items.Count(i => i.Kind == "Terrain")}   总计 {document.Items.Count}";
        if (selectionCount != null) selectionCount.Text = canvas.Selection.Count == 0 ? "未选择" : $"已选 {canvas.Selection.Count} 个";
    }
    private bool AskSave()
    {
        CommitInspectorEdit();
        CommitMetadata(); if (!dirty) return true;
        DialogResult result = MessageBox.Show(this, "保存“" + document.Label + "”的修改？", "结构工坊", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || result == DialogResult.Yes && SaveDocument(false);
    }
    private void NewDocument() => AddWorkspaceDocument(new());
    private void OpenDocument()
    {
        using OpenFileDialog dialog = new() { Title = "打开结构 / XML", Filter = "结构工程与布局|*.structure.json;*.json;*.xml|结构工程|*.structure.json|布局 XML|*.xml", InitialDirectory = OutputFolder, Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var errors = new List<string>();
        foreach (string path in dialog.FileNames)
            try { OpenWorkspaceFile(path); } catch (Exception ex) { errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
    }
    private void ExportPreview()
    {
        using SaveFileDialog dialog = new() { Filter = "PNG 图片|*.png", FileName = document.DefName + "_preview.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using Bitmap image = canvas.ExportPreview(); image.Save(dialog.FileName, ImageFormat.Png); status.Text = "预览已导出。";
    }
    private void SetPaths()
    {
        using FolderBrowserDialog game = new() { Description = "选择 RimWorld 游戏主目录（包含 Assembly-CSharp.dll）", SelectedPath = Program.Paths.GameRoot };
        if (game.ShowDialog(this) != DialogResult.OK) return;
        Program.Paths.GameRoot = game.SelectedPath; Program.SavePaths(); LoadCatalog();
    }
    private void AddMod()
    {
        using FolderBrowserDialog mod = new() { Description = "选择另一个 Mod 的根目录" };
        if (mod.ShowDialog(this) != DialogResult.OK) return;
        if (!Program.Paths.ExtraMods.Contains(mod.SelectedPath)) Program.Paths.ExtraMods.Add(mod.SelectedPath); Program.SavePaths(); LoadCatalog();
    }
}
