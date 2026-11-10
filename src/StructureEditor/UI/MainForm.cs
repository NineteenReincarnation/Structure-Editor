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
        dormancyBox = new MP_DarkComboBox();
    private readonly Label dormancyInfo = new();
    private readonly CheckBox dormant = new();
    private readonly Label selectedName = new(), selectedInfo = new(), libraryInfo = new();
    private readonly MP_ExportIndicator MP_exportStatusDot = new();
    private Label MP_rotationLabel, MP_materialLabel, MP_dormancyLabel;
    private readonly ListBox palette = new MP_MotionListBox();
    private MP_FluidAssetPalette MP_assetView;
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Stack<StructureDocument> undo = new(), redo = new();
    private readonly Color surface = MP_StudioTheme.Panel,
        text = MP_StudioTheme.Text,
        muted = MP_StudioTheme.Secondary;
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
        Font = MP_LiltTheme.CreateGdiFont(10);
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
        canvas.Changed += () =>
        {
            dirty = true;
            UpdateTitle();
            MP_ScheduleExportReview();
            MP_RefreshClusterCpSummary();
        };
        canvas.SelectionChanged += ShowSelection;
        canvas.Hint += message => status.Text = message;
        FormClosing += (_, e) => { try { if (!AskSaveWorkspace()) e.Cancel = true; } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } };
        Disposed += (_, _) =>
        {
            MP_exportReviewTimer.Stop(); MP_exportReviewTimer.Dispose();
        };
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
        Button button = new MP_AnimatedButton
        {
            Text = caption, Location = new Point(x, y), Size = new Size(width, 29),
            FlatStyle = FlatStyle.Flat, BackColor = MP_StudioTheme.Panel,
            ForeColor = text, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = MP_StudioTheme.Accent;
        button.FlatAppearance.MouseOverBackColor = MP_StudioTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = MP_StudioTheme.Active;
        return button;
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
    private void ShowSelection()
    {
        if (canvas.Selected != null && catalog != null) ShowItem(catalog.Find(canvas.Selected.Kind, canvas.Selected.DefName), canvas.Selected);
        else if (canvas.Brush != null) ShowItem(canvas.Brush, null);
        else
        {
            if (selectedName.Text != "未选择对象") selectedName.Text = "未选择对象";
            if (selectedInfo.Text.Length != 0) selectedInfo.Text = "";
            MP_materialLabel.Visible = materialBox.Visible = false;
            MP_rotationLabel.Visible = rotationBox.Visible = false;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = false;
        }
       
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
            string name = item.Label + "\n" + item.DefName;
            if (selectedName.Text != name) selectedName.Text = name;
            string info = $"来源：{item.Pack}\n占地 {item.Width} × {item.Height}  ·  {(placed == null ? "当前笔刷" : $"已选 {canvas.Selection.Count} 个对象")}";
            if (selectedInfo.Text != info) selectedInfo.Text = info;
            if (shownItem != item)
            {
                materialBox.BeginUpdate(); materialBox.Items.Clear();
                foreach (MaterialItem m in catalog.MaterialsFor(item).OrderBy(m => m.DefName == "Steel" ? 0 : 1).ThenBy(m => m.Label)) materialBox.Items.Add(m);
                materialBox.EndUpdate(); shownItem = item;
            }
            MaterialItem preferred = materialBox.Items.Cast<MaterialItem>().FirstOrDefault(m => m.DefName == (placed?.Stuff ?? canvas.BrushStuff));
            if (materialBox.Items.Count > 0)
            {
                object nextMaterial = preferred ?? materialBox.Items[0];
                if (!ReferenceEquals(materialBox.SelectedItem, nextMaterial))
                    materialBox.SelectedItem = nextMaterial;
            }
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
            if (canRotate)
            {
                int rotation = placed?.Rotation ?? canvas.BrushRotation;
                if (rotationBox.SelectedIndex != rotation) rotationBox.SelectedIndex = rotation;
            }
            var supported = placed == null ? new List<PlacedItem>() : canvas.Selection.Where(i => catalog.Find(i.Kind, i.DefName)?.CanBeDormant == true).ToList();
            bool showDormancy = placed == null ? item.CanBeDormant : supported.Count > 0;
            MP_dormancyLabel.Visible = dormancyBox.Visible = dormancyInfo.Visible = showDormancy;
            dormancyBox.Enabled = showDormancy;
            var states = supported.Select(i => i.StartDormant).Distinct().ToList();
            bool? setting = placed == null ? canvas.BrushDormant : supported.FirstOrDefault()?.StartDormant;
            int nextDormancy = states.Count > 1 ? 3 : setting == true ? 1 : setting == false ? 2 : 0;
            if (dormancyBox.SelectedIndex != nextDormancy)
                dormancyBox.SelectedIndex = nextDormancy;
            dormancyInfo.Text = ""; dormancyInfo.Visible = false;
        }
        finally { updating = false; }
    }
    private static void MP_FitInspectorLabel(Label label, int minimumHeight)
    {
        if (label.Width < 40 || label.IsDisposed) return;
        int measured = TextRenderer.MeasureText(label.Text, label.Font,
            new Size(Math.Max(40, label.ClientSize.Width - 6), 10000),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        int required = Math.Max(minimumHeight, measured + 6);
        if (label.Height != required) label.Height = required;
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
        updating = true; idBox.Text = document.DefName; labelBox.Text = document.Label; dormant.Checked = document.StartDormant;
        document.ExportProfile = "Framework";
        autoExport.Checked = document.AutoExportOnSave;
        exportResult.Text = "";
        exportResult.Visible = false;
        dormant.Enabled = true; dormant.Visible = true;
        updating = false;
        UpdateTitle();
        RefreshSaveLocation(); LoadNamingFields(); MP_RefreshClusterCpSummary();
    }
    private void CommitMetadata()
    {
        if (updating) return;
        // XML output is fixed to Structure Framework.
        if (document.DefName == idBox.Text && document.Label == labelBox.Text
            && document.StartDormant == dormant.Checked && document.ExportProfile == "Framework"
            && document.AutoExportOnSave == autoExport.Checked) return;
        Snapshot();
        document.DefName = idBox.Text.Trim(); document.Label = labelBox.Text.Trim();
        document.StartDormant = dormant.Checked;
        document.ExportProfile = "Framework";
        document.AutoExportOnSave = autoExport.Checked;
        dirty = true; UpdateTitle(); canvas.Invalidate(); RefreshMetadata(); ShowSelection();
        MP_ScheduleExportReview();
    }
    private void UpdateTitle()
    {
        string next = "Structure Editor · 结构工坊 — " + document.Label + (dirty ? " *" : "");
        // Moving one item can generate many canvas change notifications.
        // Tab layout and naming preview only need refreshing on a title change.
        if (Text != next)
        {
            Text = next;
            RefreshDocumentTabs();
        }
        // The naming preview also depends on the current file location, not
        // only the visible title (for example after saving a clean document).
        RefreshNamingPreview();
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
