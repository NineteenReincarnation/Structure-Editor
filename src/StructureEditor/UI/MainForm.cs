using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Linq;
using StructureEditor.Compatibility;

namespace StructureEditor;

public partial class MainForm : Form
{
    private readonly StructureCanvas canvas = new();
    private readonly TextBox search = new(), idBox = new(), labelBox = new();
    private readonly ComboBox kindBox = new MP_DarkComboBox(), packBox = new MP_DarkComboBox(),
        groupBox = new MP_DarkComboBox(), scopeBox = new MP_DarkComboBox(),
        materialBox = new MP_DarkComboBox(), rotationBox = new MP_DarkComboBox(),
        categoryBox = new MP_DarkComboBox(), dormancyBox = new MP_DarkComboBox();
    private readonly Label dormancyInfo = new();
    private readonly CheckBox dormant = new();
    private readonly ComboBox profileBox = new MP_DarkComboBox();
    private readonly CheckBox mpCompatibility = new();
    private readonly Label selectedName = new(), selectedInfo = new(), libraryInfo = new(), summary = new();
    private Label MP_rotationLabel, MP_materialLabel, MP_dormancyLabel;
    private StructureDocument MP_summaryDocument;
    private int MP_summaryCount = -1;
    private string MP_summaryText;
    private readonly ListBox palette = new();
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Stack<StructureDocument> undo = new(), redo = new();
    private readonly Color surface = Color.FromArgb(33, 43, 52), text = Color.FromArgb(224, 234, 240), muted = Color.FromArgb(156, 175, 188);
    private Catalog catalog;
    private StructureDocument document = new();
    private string currentFile;
    private bool dirty, updating, filtering, saveLocationChanged;
    private List<PlacedItem> copied = new();
    private CatalogItem shownItem;
    private bool MP_rebuildingPalette;

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
        canvas.Changed += () => { MP_summaryDocument = null; dirty = true; UpdateTitle(); UpdateSummary(); MP_ScheduleExportReview(); };
        canvas.SelectionChanged += ShowSelection;
        canvas.Hint += message => status.Text = message;
        FormClosing += (_, e) => { try { if (!AskSaveWorkspace()) e.Cancel = true; } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } };
        FormClosed += (_, _) => { MP_exportReviewTimer.Stop(); MP_exportReviewTimer.Dispose(); };
        Load += (_, _) => { UpdatePaletteDensity(); FitWorkingArea(); };
        Shown += (_, _) => { if (Program.IsGame(Program.Paths.GameRoot)) LoadCatalog(); else { canvas.Document=document; RefreshMetadata(); status.Text="请在 编辑 → 首选项 中选择 RimWorld 游戏目录，加载本机素材。"; } };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MP_WindowGlass.Apply(Handle);
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
    private string StructuresFolder => Directory.Exists(Program.StructureLibraryFolder) ? Program.StructureLibraryFolder : Program.DefaultProjects;
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
        var results = catalog.Query(FilterKind, pack, group, query, ClusterScope).Where(MatchesScope)
            .OrderBy(i => scopeBox.SelectedIndex == 3 ? Program.Paths.RecentAssets.IndexOf(CatalogCollections.Key(i)) : CatalogGrouping.Order(i.Group))
            .ThenBy(i => i.Pack == "Core" ? 0 : 1).ThenBy(i => i.Label).ToArray();
        // Avoid clearing/repainting the owner-drawn list when the result set is identical.
        // This matters when users cycle layers or repeatedly change selection.
        bool identical = palette.Items.Count == results.Length;
        for (int i = 0; identical && i < results.Length; i++)
            identical = ReferenceEquals(palette.Items[i], results[i]);
        if (identical)
        {
            libraryInfo.Text = $"{results.Length} 个素材";
            return;
        }
        CatalogItem previous = palette.SelectedItem as CatalogItem;
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
        if (MP_rebuildingPalette || palette.SelectedItem is not CatalogItem item) return;
        RememberAsset(item);
        bool keepRectangle = canvas.Tool == CanvasTool.Rectangle && item.Kind == "Terrain";
        canvas.Brush = item; canvas.BrushRotation = item.Kind == "Pawn" ? 2 : 0; canvas.Tool = keepRectangle ? CanvasTool.Rectangle : CanvasTool.Place; canvas.ClearSelection();
        ShowItem(item, null); canvas.Invalidate();
    }
    private void ShowSelection()
    {
        if (canvas.Selected != null && catalog != null) ShowItem(catalog.Find(canvas.Selected.Kind, canvas.Selected.DefName), canvas.Selected);
        else if (canvas.Brush != null) ShowItem(canvas.Brush, null);
        else
        {
            selectedName.Text = "未选择对象";
            selectedInfo.Text = "";
            MP_materialLabel.Visible = materialBox.Visible = false;
            MP_rotationLabel.Visible = rotationBox.Visible = false;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = false;
        }
        UpdateSummary();
    }
    private void ShowItem(CatalogItem item, PlacedItem placed)
    {
        if (item == null)
        {
            MP_materialLabel.Visible = materialBox.Visible = false;
            MP_rotationLabel.Visible = rotationBox.Visible = false;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = false;
            return;
        }
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
            bool hasMaterial = materialBox.Items.Count > 0;
            MP_materialLabel.Visible = materialBox.Visible = hasMaterial;
            materialBox.Enabled = hasMaterial;
            canvas.BrushStuff = hasMaterial
                ? (materialBox.SelectedItem as MaterialItem)?.DefName : null;
            bool canRotate = placed != null && canvas.Selection.Count > 1
                ? canvas.Selection.Any(selected => catalog.Find(selected.Kind, selected.DefName)?.Rotatable == true)
                : item.Rotatable;
            MP_rotationLabel.Visible = rotationBox.Visible = canRotate;
            rotationBox.Enabled = canRotate;
            if (canRotate) rotationBox.SelectedIndex = placed?.Rotation ?? canvas.BrushRotation;
            var supported = placed == null ? new List<PlacedItem>() : canvas.Selection.Where(i => catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true).ToList();
            bool mp = document.ExportProfile == "MechProtocol"
                || ((document.ExportProfile == "Framework" || document.ExportProfile == "Universal") && document.EnableMPCompatibility);
            bool canDormant = placed == null ? item.CanBeDormant : supported.Count > 0;
            bool showDormancy = mp && canDormant;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = showDormancy;
            dormancyBox.Enabled = showDormancy;
            var states = supported.Select(i => i.StartDormant).Distinct().ToList();
            bool? setting = placed == null ? canvas.BrushDormant : supported.FirstOrDefault()?.StartDormant;
            dormancyBox.SelectedIndex = states.Count > 1 ? 3 : setting == true ? 1 : setting == false ? 2 : 0;
            dormancyInfo.Text = ""; dormancyInfo.Visible = false;
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
        profileBox.SelectedIndex = document.ExportProfile switch
        {
            "MechProtocol" => 2, "VanillaPrefab" => 1,
            "Universal" => 3, _ => 0
        };
        mpCompatibility.Checked = document.EnableMPCompatibility;
        bool supportsExtension = document.ExportProfile is "Framework" or "Universal";
        mpCompatibility.Enabled = supportsExtension;
        mpCompatibility.Visible = supportsExtension;
        autoExport.Checked = document.AutoExportOnSave;
        exportResult.Text = "";
        exportResult.Visible = false;
        dormant.Enabled = document.ExportProfile == "MechProtocol"
            || ((document.ExportProfile == "Framework" || document.ExportProfile == "Universal")
                && document.EnableMPCompatibility);
        dormant.Visible = dormant.Enabled;
        updating = false;
        UpdateTitle(); UpdateSummary();
        RefreshSaveLocation(); LoadNamingFields();
    }
    private void CommitMetadata()
    {
        if (updating) return;
        string profile = profileBox.SelectedIndex switch
        {
            2 => "MechProtocol", 1 => "VanillaPrefab",
            3 => "Universal", _ => "Framework"
        };
        // Switching away from an extension-capable output clears only its
        // enable flag. Original per-object dormancy remains in the JSON.
        bool compatible = (profile == "Framework" || profile == "Universal")
            && mpCompatibility.Checked;
        if (document.DefName == idBox.Text && document.Label == labelBox.Text
            && document.Category == SelectedCategory
            && document.StartDormant == dormant.Checked && document.ExportProfile == profile
            && document.EnableMPCompatibility == compatible
            && document.AutoExportOnSave == autoExport.Checked) return;
        Snapshot();
        document.DefName = idBox.Text.Trim(); document.Label = labelBox.Text.Trim();
        document.Category = SelectedCategory; document.StartDormant = dormant.Checked;
        document.ExportProfile = profile; document.EnableMPCompatibility = compatible;
        document.AutoExportOnSave = autoExport.Checked;
        dirty = true; UpdateTitle(); canvas.Invalidate(); RefreshMetadata(); ShowSelection();
        MP_ScheduleExportReview();
    }
    private void UpdateTitle() { Text = "Structure Editor · 结构工坊 — " + document.Label + (dirty ? " *" : ""); RefreshDocumentTabs(); RefreshNamingPreview(); }
    private void UpdateSummary()
    {
        if (!ReferenceEquals(MP_summaryDocument, document) || MP_summaryCount != document.Items.Count)
        {
            int buildings = 0, pawns = 0, terrain = 0;
            foreach (PlacedItem item in document.Items)
            {
                if (item.Kind == "Building") buildings++;
                else if (item.Kind == "Pawn") pawns++;
                else if (item.Kind == "Terrain") terrain++;
            }
            MP_summaryText = $"建筑 {buildings}   单位 {pawns}\n地板 {terrain}   总计 {document.Items.Count}";
            MP_summaryDocument = document;
            MP_summaryCount = document.Items.Count;
        }
        if (summary.Text != MP_summaryText) summary.Text = MP_summaryText;
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
