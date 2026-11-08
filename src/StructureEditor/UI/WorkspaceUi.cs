namespace StructureEditor;

public partial class MainForm
{
    private readonly Dictionary<string, ToolStripButton> MP_rangeButtons = new();
    private void MP_ToggleRange(string id)
    {
        if (MP_rangeButtons.TryGetValue(id, out ToolStripButton button) && !button.IsDisposed)
            button.Checked = !button.Checked;
    }
    private readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 160 };
    private readonly Font smallFont = new("Microsoft YaHei UI", 8);
    private ToolStripStatusLabel selectionStatus;
    private SplitContainer librarySplit, inspectorSplit;
    private ComboBox MP_editLayerSelector;
    private void MP_CycleEditLayer(int step)
    {
        if (MP_editLayerSelector?.Items.Count is not > 0) return;
        MP_editLayerSelector.SelectedIndex = (MP_editLayerSelector.SelectedIndex + step +
            MP_editLayerSelector.Items.Count) % MP_editLayerSelector.Items.Count;
    }
    private void BuildWorkspaceUi()
    {
        SuspendLayout();
        ToolStripManager.Renderer = new StudioRenderer();
        MenuStrip menu = BuildMenus(); MainMenuStrip = menu;
        ApplyBranding();
        // A symmetrical header keeps document tabs centered in the actual
        // window, independently of the range actions on the right.
        MP_GlassLayout topBar = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0)); // balance right controls
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        ToolStrip actions = new()
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = true, CanOverflow = false,
            Margin = Padding.Empty, BackColor = Color.FromArgb(35, 47, 57),
            Padding = new Padding(6, 4, 8, 4),
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        ToolStripButton MP_rangeToggle(string label, string name, bool initial, Action<bool> onChange)
        {
            ToolStripButton button = new(label)
            {
                Name = name, ForeColor = text, DisplayStyle = ToolStripItemDisplayStyle.Text,
                CheckOnClick = true, Checked = initial, ToolTipText = $"显示或隐藏{label}"
            };
            button.CheckedChanged += (_, _) => onChange(button.Checked);
            actions.Items.Add(button);
            MP_rangeButtons[name] = button;
            return button;
        }
        MP_rangeToggle("殉爆范围", "TurretExplosionRanges", false, SetTurretExplosionRanges);
        MP_rangeToggle("探测范围", "ProximityRanges", true, enabled => canvas.ShowProximityRanges = enabled);
        MP_rangeToggle("护盾范围", "ShieldRanges", false, enabled => canvas.ShowShieldRanges = enabled);
        canvas.ShowProximityRanges = true;
        actions.Items.Add(new ToolStripSeparator());
        AddTool(actions, "保存", () => SaveDocument(false));
        // Paths and preview export already live in the unified project panel
        // and File menu. Avoid duplicating them on the header.
        topBar.Controls.Add(BuildDocumentBar(), 1, 0);
        topBar.Controls.Add(actions, 2, 0);
        void BalanceHeader()
        {
            int width = actions.GetPreferredSize(Size.Empty).Width;
            if (width <= 0) width = actions.Width;
            if (width > 0 && Math.Abs(topBar.ColumnStyles[0].Width - width) > 1)
                topBar.ColumnStyles[0].Width = width;
        }
        actions.SizeChanged += (_, _) => BalanceHeader();
        BalanceHeader();

        librarySplit = new() { Size = ClientSize, Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 3, SplitterDistance = 278, BackColor = Color.FromArgb(39, 52, 61) };
        inspectorSplit = new() { Size = new(Math.Max(700, ClientSize.Width - 284), ClientSize.Height), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 3, SplitterDistance = Math.Max(330, ClientSize.Width - 638), BackColor = librarySplit.BackColor };
        librarySplit.Panel2.Controls.Add(inspectorSplit);
        librarySplit.Panel1MinSize = 235; librarySplit.Panel2MinSize = 540;
        inspectorSplit.Panel1MinSize = 280; inspectorSplit.Panel2MinSize = 300;

        Panel library = new() { Dock = DockStyle.Fill, BackColor = surface, Padding = new(10) };
        TableLayoutPanel filters = BuildLibraryFilters();
        palette.Dock = DockStyle.Fill; palette.DrawMode = DrawMode.OwnerDrawFixed; palette.ItemHeight = 78; palette.IntegralHeight = false; palette.BorderStyle = BorderStyle.None;
        palette.BackColor = surface; palette.ForeColor = text; palette.DrawItem += DrawPalette; palette.SelectedIndexChanged += (_, _) => SelectBrush();
        libraryInfo.Dock = DockStyle.Bottom; libraryInfo.Height = 43; libraryInfo.ForeColor = muted;
        // Cover the bright native ListBox scrollbar with a dark draggable track.
        // Scrolling and selection still use the original ListBox.
        Panel MP_paletteHost = new() { Dock = DockStyle.Fill, BackColor = surface };
        MP_DarkPaletteScrollbar MP_paletteScroll = new(palette);
        palette.Dock = DockStyle.None;
        palette.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        MP_paletteHost.Resize += (_, _) =>
        {
            // Native scrollbar lives outside the clipped panel;
            // only the custom dark track remains visible.
            palette.Bounds = new Rectangle(0, 0,
                MP_paletteHost.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
                MP_paletteHost.ClientSize.Height);
            MP_paletteScroll.BringToFront();
            MP_paletteScroll.Invalidate();
        };
        MP_paletteHost.Controls.Add(palette);
        MP_paletteHost.Controls.Add(MP_paletteScroll);
        MP_paletteScroll.BringToFront();
        library.Controls.Add(MP_paletteHost); library.Controls.Add(filters); library.Controls.Add(libraryInfo);
        MP_libraryTabs = new MP_SectionTabs
        {
            Dock = DockStyle.Fill, BackColor = surface, ForeColor = text
        };
        TabPage materialsPage = new("素材库") { BackColor = surface, Padding = Padding.Empty };
        materialsPage.Controls.Add(library);
        TabPage structuresPage = new("结构库") { BackColor = surface, Padding = Padding.Empty };
        structuresPage.Controls.Add(MP_CreateStructureLibraryPanel());
        MP_libraryTabs.AddPage(materialsPage);
        MP_libraryTabs.AddPage(structuresPage);
        MP_libraryTabs.SelectedIndexChanged += (_, _) =>
        {
            if (MP_libraryTabs.SelectedIndex == 1 && !MP_libraryLoaded) Safe(MP_ReloadLibrary);
        };
        librarySplit.Panel1.Controls.Add(MP_libraryTabs);

        Panel viewport = new() { Dock = DockStyle.Fill };
        MP_SmoothLayout layerBar = new() { Dock = DockStyle.Top, Height = 36,
            BackColor = Color.FromArgb(37, 50, 60), ColumnCount = 5 };
        layerBar.ColumnStyles.Add(new(SizeType.Absolute, 132));
        layerBar.ColumnStyles.Add(new(SizeType.AutoSize));
        layerBar.ColumnStyles.Add(new(SizeType.Absolute, 124));
        layerBar.ColumnStyles.Add(new(SizeType.Absolute, 110));
        layerBar.ColumnStyles.Add(new(SizeType.Percent, 100));
        ToolStrip documentActions = new()
        {
            Dock = DockStyle.Fill, AutoSize = false, GripStyle = ToolStripGripStyle.Hidden,
            CanOverflow = false, BackColor = layerBar.BackColor,
            Margin = Padding.Empty, Padding = new Padding(4, 3, 0, 3)
        };
        AddTool(documentActions, "新建", NewDocument);
        AddTool(documentActions, "打开…", OpenDocument);
        layerBar.Controls.Add(documentActions, 0, 0);
        layerBar.Controls.Add(new Label { Text = "编辑图层", AutoSize = true,
            Anchor = AnchorStyles.Left, Margin = new(10, 0, 10, 0),
            ForeColor = muted }, 1, 0);
        MP_editLayerSelector = new MP_DarkComboBox() { Dock = DockStyle.Fill, Margin = new(0, 3, 0, 3), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = surface, ForeColor = text };
        MP_editLayerSelector.Items.AddRange(new object[] { "全部", "建筑", "单位", "地形" }); MP_editLayerSelector.SelectedIndex = 0;
        MP_editLayerSelector.SelectedIndexChanged += (_, _) =>
        {
            canvas.EditLayer = new[] { null, "Building", "Pawn", "Terrain" }[MP_editLayerSelector.SelectedIndex];
            selectionStatus.Text = "编辑图层：" + MP_editLayerSelector.Text;
        };
        ToolStrip MP_layerVisibilityBar = new()
        {
            Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            BackColor = layerBar.BackColor, Margin = Padding.Empty,
            Padding = new Padding(2, 3, 0, 3)
        };
        ToolStripDropDownButton MP_visibleLayers = new("显示图层")
        {
            ForeColor = text, ToolTipText = "选择要显示的图层"
        };
        foreach (var (kind, label) in new[] { ("Building", "建筑"), ("Pawn", "单位"), ("Terrain", "地形") })
        {
            ToolStripMenuItem item = new(label) { Checked = true, CheckOnClick = true };
            item.CheckedChanged += (_, _) =>
            {
                if (item.Checked) canvas.VisibleLayers.Add(kind); else canvas.VisibleLayers.Remove(kind);
                canvas.ClearSelection(); canvas.Invalidate();
            };
            MP_visibleLayers.DropDownItems.Add(item);
        }
        MP_visibleLayers.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem MP_gridVisibility = new("网格") { Checked = canvas.ShowGrid, CheckOnClick = true };
        MP_gridVisibility.CheckedChanged += (_, _) =>
        {
            canvas.ShowGrid = MP_gridVisibility.Checked;
            canvas.Invalidate();
        };
        MP_visibleLayers.DropDownItems.Add(MP_gridVisibility);
        MP_visibleLayers.DropDownOpening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in MP_visibleLayers.DropDownItems.OfType<ToolStripMenuItem>())
            {
                if (item == MP_gridVisibility) { item.Checked = canvas.ShowGrid; continue; }
                string kind = item.Text switch { "建筑" => "Building", "单位" => "Pawn", _ => "Terrain" };
                item.Checked = canvas.VisibleLayers.Contains(kind);
            }
        };
        MP_layerVisibilityBar.Items.Add(MP_visibleLayers);
        layerBar.Controls.Add(MP_editLayerSelector, 2, 0);
        layerBar.Controls.Add(MP_layerVisibilityBar, 3, 0);
        viewport.Controls.Add(canvas); viewport.Controls.Add(layerBar); inspectorSplit.Panel1.Controls.Add(viewport);

        MP_SectionTabs tabs = new() { Dock = DockStyle.Fill, BackColor = surface, ForeColor = text };
        TableLayoutPanel Page(string title)
        {
            TabPage tab = new(title) { BackColor = surface, ForeColor = text, Padding = new(9), AutoScroll = true };
            MP_SmoothLayout layout = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(3), Margin = Padding.Empty };
            layout.ColumnStyles.Add(new(SizeType.Percent, 100));
            layout.SizeChanged += (_, _) => { var extent = new Size(0, layout.Height + tab.Padding.Vertical); if (tab.AutoScrollMinSize != extent) tab.AutoScrollMinSize = extent; };
            tab.Controls.Add(layout); tabs.AddPage(tab); return layout;
        }
        void Row(TableLayoutPanel panel, Control control, int height = 0)
        {
            control.Dock = DockStyle.Top; control.Margin = new(0, 0, 0, 10);
            if (height > 0) control.Height = height;
            int row = panel.RowCount++; panel.RowStyles.Add(new(SizeType.AutoSize)); panel.Controls.Add(control, 0, row);
        }
        void Field(TableLayoutPanel panel, string title, Control control)
        {
            Row(panel, new Label { Text = title, ForeColor = muted, AutoSize = true }); Row(panel, control, 29);
        }
        TableLayoutPanel objects = Page("对象"), metadata = Page("工程");
        TableLayoutPanel exports = metadata;
        selectedName.Font = new(Font, FontStyle.Bold);
        Row(objects, selectedName, 49);
        selectedInfo.ForeColor = muted;
        Row(objects, selectedInfo, 40);
        materialBox.DropDownStyle = rotationBox.DropDownStyle = ComboBoxStyle.DropDownList;
        MP_materialLabel = new Label { Text = "材质", ForeColor = muted, AutoSize = true };
        Row(objects, MP_materialLabel);
        Row(objects, materialBox, 35);
        rotationBox.Items.AddRange(new object[] { "北 / North", "东 / East", "南 / South", "西 / West" });
        rotationBox.SelectedIndex = 0;
        MP_rotationLabel = new Label { Text = "方向", ForeColor = muted, AutoSize = true };
        Row(objects, MP_rotationLabel);
        Row(objects, rotationBox, 29);
        dormancyBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dormancyBox.Items.AddRange(new object[] { "跟随结构默认", "初始休眠", "初始唤醒", "保持各自设置（混合）" });
        dormancyBox.SelectedIndex = 0;
        MP_dormancyLabel = new Label { Text = "初始状态", ForeColor = muted, AutoSize = true };
        Row(objects, MP_dormancyLabel);
        Row(objects, dormancyBox, 29);
        dormancyInfo.Visible = false;

        Row(metadata, new Label { Text = "结构", Font = new(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 220, 230), AutoSize = true });
        Row(metadata, BuildClusterNamingPanel());
        structureLabelTitle = new() { Text = "名称", ForeColor = muted, AutoSize = true };
        Row(metadata, structureLabelTitle);
        Row(metadata, labelBox, 29);
        structureIdTitle = new() { Text = "DefName", ForeColor = muted, AutoSize = true };
        Row(metadata, structureIdTitle);
        Row(metadata, idBox, 29);
        categoryBox.DropDownStyle = ComboBoxStyle.DropDownList;
        categoryBox.Items.AddRange(new object[] { "机械集群", "通用结构" });
        categoryBox.SelectedIndex = 1;
        Field(metadata, "类别", categoryBox);
        dormant.Text = "默认休眠"; dormant.AutoSize = true;
        Row(metadata, dormant);
        Row(metadata, new Label { Text = "XML 导出", Font = new(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 220, 230), AutoSize = true });
        profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
        profileBox.Items.AddRange(new object[]
        {
            "Structure Framework（扩展）",
            "原版 PrefabDef（默认）",
            "MechProtocol（专用）",
            "原版基础 + 条件拓展"
        });
        profileBox.SelectedIndex = 1;
        Field(metadata, "目标格式", profileBox);
        mpCompatibility.Text = "MechProtocol · 逐对象休眠";
        mpCompatibility.AutoSize = true;
        Row(metadata, mpCompatibility);
        MP_compatRecommendation.ForeColor = muted;
        Row(metadata, MP_compatRecommendation);
        Row(metadata, autoExport);
        Panel reviewRow = new()
        {
            Height = 27, BackColor = surface, Margin = Padding.Empty
        };
        MP_exportStatusDot.Location = new Point(1, 3);
        MP_exportReviewStatus.ForeColor = muted;
        MP_exportReviewStatus.Location = new Point(24, 2);
        MP_exportReviewStatus.Margin = Padding.Empty;
        reviewRow.Controls.Add(MP_exportStatusDot);
        reviewRow.Controls.Add(MP_exportReviewStatus);
        Row(metadata, reviewRow, 27);
        MP_exportReviewDetails.BackColor = Color.FromArgb(36, 48, 57);
        MP_exportReviewDetails.ForeColor = text;
        MP_exportReviewDetails.BorderStyle = BorderStyle.None;
        Row(metadata, MP_exportReviewDetails, 72);
        MP_exportReviewDetails.Visible = false;

        MP_exportPrimary = MakeButton("立即导出 XML", 0, 0, 230);
        MP_exportPrimary.Click += (_, _) => Safe(ExportToConfiguredLocation);
        Row(metadata, MP_exportPrimary, 36);
        exportResult.ForeColor = muted;
        Row(metadata, exportResult, 34);
        exportResult.Visible = false;

        Button advancedToggle = MakeButton("高级设置  ▸", 0, 0, 230);
        Row(metadata, advancedToggle, 33);
        MP_SmoothLayout advanced = new()
        {
            AutoSize = true, Dock = DockStyle.Top,
            BackColor = surface, ColumnCount = 1, Visible = false,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        advanced.ColumnStyles.Add(new(SizeType.Percent, 100));
        Row(metadata, advanced);
        advancedToggle.Click += (_, _) =>
        {
            advanced.Visible = !advanced.Visible;
            advancedToggle.Text = advanced.Visible ? "高级设置  ▾" : "高级设置  ▸";
        };
        Row(advanced, new Label { Text = "XML 输出位置", ForeColor = muted, AutoSize = true });
        xmlPathBox.Multiline = false;
        xmlPathBox.ScrollBars = ScrollBars.None;
        xmlPathBox.BorderStyle = BorderStyle.None;
        xmlPathBox.BackColor = Color.FromArgb(42, 55, 64);
        xmlPathBox.ForeColor = text;
        Row(advanced, xmlPathBox, 28);
        Button exportFolder = MakeButton("更改 XML 目录…", 0, 0, 230);
        exportFolder.Click += (_, _) => Safe(SetXmlDirectory);
        Row(advanced, exportFolder, 31);

        Row(advanced, new Label { Text = "工程目录", ForeColor = muted, AutoSize = true });
        saveFolderBox.Multiline = false;
        saveFolderBox.ScrollBars = ScrollBars.None;
        saveFolderBox.BorderStyle = BorderStyle.None;
        saveFolderBox.BackColor = Color.FromArgb(42, 55, 64);
        saveFolderBox.ForeColor = text;
        Row(advanced, saveFolderBox, 28);
        Button projectFolder = MakeButton("更改工程目录…", 0, 0, 230);
        projectFolder.Click += (_, _) => Safe(SetSaveDirectory);
        Row(advanced, projectFolder, 31);

        MP_exportDependencySummary.ForeColor = muted;
        Row(advanced, MP_exportDependencySummary);
        Button moreExport = MakeButton("更多导出方式 ▾", 0, 0, 230);
        ContextMenuStrip moreMenu = new() { BackColor = Color.FromArgb(39, 52, 62),
            ForeColor = text, ShowImageMargin = false };
        void More(string title, Action action)
        {
            ToolStripMenuItem item = new(title) { ForeColor = text };
            item.Click += (_, _) => Safe(action);
            moreMenu.Items.Add(item);
        }
        More("另存为 XML…", () => ExportLayout(false));
        More("导出完整 Mod 内容包…", ExportBundle);
        More("导出 PNG 预览…", ExportPreview);
        moreExport.Click += (_, _) => moreMenu.Show(moreExport, new Point(0, moreExport.Height));
        Row(advanced, moreExport, 32);
        MP_exportReviewTimer.Tick += (_, _) => MP_RefreshExportReview();
        inspectorSplit.Panel2.Controls.Add(tabs);
        foreach (Control c in new Control[] { search, idBox, labelBox, kindBox, packBox, groupBox, scopeBox, materialBox, rotationBox, categoryBox, dormancyBox, profileBox }) { c.BackColor = Color.FromArgb(49, 58, 64); c.ForeColor = text; }
        foreach (ComboBox combo in new[] { kindBox, packBox, groupBox, scopeBox, MP_editLayerSelector, materialBox, rotationBox, categoryBox, dormancyBox, profileBox })
        {
            combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = combo == materialBox ? Math.Max(32, Font.Height + 14)
                : Math.Max(22, Font.Height + 3);
            combo.DrawItem += (_, e) =>
            {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using SolidBrush background = new(selected ? Color.FromArgb(57, 91, 93) : Color.FromArgb(49, 58, 64));
                e.Graphics.FillRectangle(background, e.Bounds);
                Rectangle textBounds = new(e.Bounds.X + 5, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 8), e.Bounds.Height);
                if (combo == materialBox && e.Index >= 0 &&
                    combo.Items[e.Index] is MaterialItem material)
                {
                    int iconSize = Math.Min(27, Math.Max(12, e.Bounds.Height - 6));
                    Rectangle iconRect = new(e.Bounds.X + 5,
                        e.Bounds.Y + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
                    Bitmap sprite = catalog?.Textures?.Request(material.Texture);
                    if (sprite != null)
                    {
                        float scale = Math.Min(iconSize / (float)sprite.Width, iconSize / (float)sprite.Height);
                        int w = Math.Max(1, (int)Math.Round(sprite.Width * scale));
                        int h = Math.Max(1, (int)Math.Round(sprite.Height * scale));
                        e.Graphics.DrawImage(sprite, new Rectangle(iconRect.X + (iconSize - w) / 2,
                            iconRect.Y + (iconSize - h) / 2, w, h));
                    }
                    else
                    {
                        using SolidBrush swatch = new(material.Color);
                        e.Graphics.FillRectangle(swatch, iconRect);
                    }
                    using Pen border = new(Color.FromArgb(95, 122, 132));
                    e.Graphics.DrawRectangle(border, iconRect);
                    textBounds = new Rectangle(iconRect.Right + 9, e.Bounds.Y,
                        Math.Max(0, e.Bounds.Right - iconRect.Right - 13), e.Bounds.Height);
                }
                string label = e.Index >= 0 ? combo.Items[e.Index]?.ToString() : combo.Text;
                TextRenderer.DrawText(e.Graphics, label, Font, textBounds, text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); FilterPalette(); };
        kindBox.SelectedIndexChanged += (_, _) => { RefreshGroups(); FilterPalette(); }; packBox.SelectedIndexChanged += (_, _) => { RefreshGroups(); FilterPalette(); };
        groupBox.SelectedIndexChanged += (_, _) => FilterPalette();
        scopeBox.SelectedIndexChanged += (_, _) =>
        {
            filtering = true;
            try { if (ClusterScope) kindBox.SelectedIndex = 0; kindBox.Enabled = !ClusterScope; if (packBox.Items.Count > 0) packBox.SelectedIndex = 0; search.Clear(); if (groupBox.Items.Count > 0) groupBox.SelectedIndex = 0; }
            finally { filtering = false; }
            RefreshPacks(); RefreshGroups(); FilterPalette();
        };
        materialBox.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            string stuff = (materialBox.SelectedItem as MaterialItem)?.DefName;
            canvas.FinishEdit(); canvas.BrushStuff = stuff; canvas.SetSelectionMaterial(stuff);
        };
        rotationBox.SelectedIndexChanged += (_, _) =>
        {
            if (updating || rotationBox.SelectedIndex < 0) return;
            int rotation = rotationBox.SelectedIndex;
            canvas.FinishEdit(); canvas.BrushRotation = rotation; canvas.SetSelectionRotation(rotation); canvas.Invalidate();
        };
        dormancyBox.SelectedIndexChanged += (_, _) =>
        {
            if (updating || !dormancyBox.Enabled || dormancyBox.SelectedIndex == 3) return;
            canvas.BrushDormant = dormancyBox.SelectedIndex == 1 ? true : dormancyBox.SelectedIndex == 2 ? false : null;
            canvas.SetSelectionDormancy(canvas.BrushDormant);
        };
        foreach (Control c in new Control[] { idBox, labelBox, categoryBox, dormant }) c.Leave += (_, _) => CommitMetadata();
        profileBox.SelectedIndexChanged+=(_,_)=>{if(!updating)CommitMetadata();};
        mpCompatibility.CheckedChanged+=(_,_)=>{if(!updating)CommitMetadata();};
        autoExport.CheckedChanged+=(_,_)=>{if(!updating)CommitMetadata();};
        canvas.ToolChanged += RefreshToolDisplay;
        StatusStrip strip = new() { BackColor = Color.FromArgb(23, 29, 33), ForeColor = text, SizingGrip = false };
        selectionStatus = new() { BorderSides = ToolStripStatusLabelBorderSides.None, Text = "放置" };
        status.Text = "正在读取游戏素材…"; strip.Items.Add(status); strip.Items.Add(selectionStatus);
        MP_SmoothLayout workspace = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = surface };
        workspace.ColumnStyles.Add(new(SizeType.Percent, 100));
        workspace.RowStyles.Add(new(SizeType.AutoSize)); workspace.RowStyles.Add(new(SizeType.Absolute, 38)); workspace.RowStyles.Add(new(SizeType.Percent, 100)); workspace.RowStyles.Add(new(SizeType.AutoSize));
        menu.Dock = strip.Dock = DockStyle.Fill; menu.Margin = topBar.Margin = librarySplit.Margin = strip.Margin = Padding.Empty;
        workspace.Controls.Add(menu, 0, 0); workspace.Controls.Add(topBar, 0, 1); workspace.Controls.Add(librarySplit, 0, 2); workspace.Controls.Add(strip, 0, 3); Controls.Add(workspace);
        RefreshToolDisplay(); ResumeLayout(true);
    }
    private void RefreshToolDisplay()
    {
        if (selectionStatus != null) selectionStatus.Text = canvas.HasStamp ? "整组粘贴 · Q/E 旋转 · 左键放置 / Esc 取消" : canvas.Tool switch
        { CanvasTool.Select => "选择 · Shift 框选", CanvasTool.Rectangle => "矩形铺地 · 左键拖动", CanvasTool.Erase => "擦除 · 左键拖动", CanvasTool.Hand => "平移 · 拖动视图", CanvasTool.Zoom => "缩放 · Alt 缩小", _ => "放置" };
    }
    private void ToggleInspector() => inspectorSplit.Panel2Collapsed = !inspectorSplit.Panel2Collapsed;
    private sealed class StudioRenderer : ToolStripProfessionalRenderer
    {
        public StudioRenderer() : base(new StudioColors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { base.OnRenderToolStripBackground(e); return; }
            Rectangle area = e.AffectedBounds;
            if (area.Width <= 0 || area.Height <= 0) return;
            using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(area,
                Color.FromArgb(46, 62, 73), Color.FromArgb(34, 47, 57),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(fill, area);
            using var line = new Pen(Color.FromArgb(54, 73, 83));
            e.Graphics.DrawLine(line, area.Left, area.Bottom - 1, area.Right, area.Bottom - 1);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.FromArgb(224, 231, 232); base.OnRenderItemText(e); }
    }
    private sealed class StudioColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(38, 44, 48);
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color MenuItemSelected => Color.FromArgb(61, 91, 95);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Color MenuItemPressedGradientEnd => MenuItemSelected;
        public override Color ButtonSelectedGradientBegin => MenuItemSelected;
        public override Color ButtonSelectedGradientEnd => MenuItemSelected;
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(54, 103, 96);
        public override Color ButtonCheckedGradientMiddle => ButtonCheckedGradientBegin;
        public override Color ButtonCheckedGradientEnd => ButtonCheckedGradientBegin;
        public override Color MenuBorder => Color.FromArgb(74, 96, 103);
        public override Color ToolStripBorder => Color.FromArgb(47, 63, 73);
    }
}
