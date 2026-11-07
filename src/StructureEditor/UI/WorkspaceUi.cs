namespace StructureEditor;

public partial class MainForm
{
    private readonly Dictionary<CanvasTool, ToolStripButton> toolButtons = new();
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
    private Label selectionCount;
    private void BuildWorkspaceUi()
    {
        SuspendLayout();
        ToolStripManager.Renderer = new StudioRenderer();
        MenuStrip menu = BuildMenus(); MainMenuStrip = menu;
        void Menu(ToolStripDropDownItem parent,string title,Action action) => AddMenuAction(parent,title,action);
        // The native window caption already carries the product name/icon.
        // Keep this row for actions only, not a second decorative title.
        ApplyBranding();
        MP_GlassLayout heading = new() { Dock = DockStyle.Fill, Height = 43,
            ColumnCount = 2, RowCount = 1, Padding = new Padding(8, 4, 8, 4) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ToolStrip actions = new()
        {
            Dock = DockStyle.Right, GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(27, 37, 45), Padding = new Padding(5, 2, 5, 2),
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            AutoSize = true
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
            return button;
        }
        MP_rangeToggle("殉爆范围", "TurretExplosionRanges", false, SetTurretExplosionRanges);
        MP_rangeToggle("探测范围", "ProximityRanges", true, enabled => canvas.ShowProximityRanges = enabled);
        MP_rangeToggle("护盾范围", "ShieldRanges", false, enabled => canvas.ShowShieldRanges = enabled);
        canvas.ShowProximityRanges = true;
        actions.Items.Add(new ToolStripSeparator());
        AddTool(actions, "保存", () => SaveDocument(false));
        AddTool(actions, "保存位置", SetSaveDirectory);
        AddTool(actions, "导出预览", ExportPreview);
        heading.Controls.Add(actions, 1, 0);

        librarySplit = new() { Size = ClientSize, Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6, SplitterDistance = 292, BackColor = Color.FromArgb(21, 27, 30) };
        inspectorSplit = new() { Size = new(Math.Max(700, ClientSize.Width - 298), ClientSize.Height), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 6, SplitterDistance = Math.Max(400, ClientSize.Width - 604), BackColor = librarySplit.BackColor };
        librarySplit.Panel2.Controls.Add(inspectorSplit);
        librarySplit.Panel1MinSize = 235; librarySplit.Panel2MinSize = 540;
        inspectorSplit.Panel1MinSize = 280; inspectorSplit.Panel2MinSize = 255;

        Panel library = new() { Dock = DockStyle.Fill, BackColor = surface, Padding = new(10) };
        TableLayoutPanel filters = BuildLibraryFilters();
        palette.Dock = DockStyle.Fill; palette.DrawMode = DrawMode.OwnerDrawFixed; palette.ItemHeight = 78; palette.IntegralHeight = false; palette.BorderStyle = BorderStyle.None;
        palette.BackColor = surface; palette.ForeColor = text; palette.DrawItem += DrawPalette; palette.SelectedIndexChanged += (_, _) => SelectBrush();
        libraryInfo.Dock = DockStyle.Bottom; libraryInfo.Height = 43; libraryInfo.ForeColor = muted;
        library.Controls.Add(palette); library.Controls.Add(filters); library.Controls.Add(libraryInfo);
        MP_libraryTabs = new TabControl
        {
            Dock = DockStyle.Fill, BackColor = surface, ForeColor = text,
            Padding = new Point(10, 5), DrawMode = TabDrawMode.OwnerDrawFixed
        };
        MP_libraryTabs.DrawItem += (_, e) =>
        {
            bool active = e.Index == MP_libraryTabs.SelectedIndex;
            using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(e.Bounds,
                active ? Color.FromArgb(86, 114, 129) : Color.FromArgb(49, 65, 77),
                active ? Color.FromArgb(42, 70, 81) : Color.FromArgb(33, 45, 54),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, MP_libraryTabs.TabPages[e.Index].Text, Font,
                e.Bounds, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        TabPage materialsPage = new("素材库") { BackColor = surface, Padding = Padding.Empty };
        materialsPage.Controls.Add(library);
        TabPage structuresPage = new("结构库") { BackColor = surface, Padding = Padding.Empty };
        structuresPage.Controls.Add(MP_CreateStructureLibraryPanel());
        MP_libraryTabs.TabPages.Add(materialsPage);
        MP_libraryTabs.TabPages.Add(structuresPage);
        MP_libraryTabs.SelectedIndexChanged += (_, _) =>
        {
            if (MP_libraryTabs.SelectedIndex == 1 && !MP_libraryLoaded) Safe(MP_ReloadLibrary);
        };
        librarySplit.Panel1.Controls.Add(MP_libraryTabs);

        Panel viewport = new() { Dock = DockStyle.Fill };
        ToolStrip editTools = new() { Dock = DockStyle.Top, Height = 38, AutoSize = false, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.FromArgb(35, 46, 56), Padding = new(4, 4, 0, 4) };
        foreach (var (tool, title) in new[] { (CanvasTool.Select, "选择 V"), (CanvasTool.Place, "放置 B"), (CanvasTool.Rectangle, "矩形 M"), (CanvasTool.Erase, "擦除 E"), (CanvasTool.Hand, "平移 H"), (CanvasTool.Zoom, "缩放 Z") })
        {
            ToolStripButton button = new(title) { DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = text, CheckOnClick = false };
            button.Click += (_, _) => { canvas.Tool = tool; canvas.Focus(); }; editTools.Items.Add(button); toolButtons[tool] = button;
        }
        editTools.Items.Add(new ToolStripSeparator()); AddTool(editTools, "旋转 R", canvas.Rotate);
        ToolStripDropDownButton center = new("居中") { ForeColor = text };
        Menu(center, "适应全部    Home", canvas.CenterView); Menu(center, "居中所选    Ctrl+Home", canvas.CenterSelection); Menu(center, "回到原点", canvas.GoToOrigin);
        editTools.Items.Add(center);
        ToolStripDropDownButton layers = new("图层") { ForeColor = text };
        foreach (var (kind, title) in new[] { ("Building", "建筑"), ("Pawn", "单位"), ("Terrain", "地板") })
        {
            ToolStripMenuItem item = new("显示" + title) { Checked = true, CheckOnClick = true };
            item.CheckedChanged += (_, _) => { if (item.Checked) canvas.VisibleLayers.Add(kind); else canvas.VisibleLayers.Remove(kind); canvas.ClearSelection(); canvas.Invalidate(); };
            layers.DropDownItems.Add(item);
        }
        ToolStripMenuItem grid = new("显示网格 G") { Checked = true, CheckOnClick = true };
        grid.CheckedChanged += (_, _) => { canvas.ShowGrid = grid.Checked; canvas.Invalidate(); }; layers.DropDownItems.Add(grid); editTools.Items.Add(layers);
        TableLayoutPanel layerBar = new() { Dock = DockStyle.Top, Height = 36, BackColor = Color.FromArgb(37, 50, 60), ColumnCount = 3 };
        layerBar.ColumnStyles.Add(new(SizeType.AutoSize)); layerBar.ColumnStyles.Add(new(SizeType.Absolute, 124)); layerBar.ColumnStyles.Add(new(SizeType.Percent, 100));
        layerBar.Controls.Add(new Label { Text = "编辑图层", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(10, 0, 10, 0), ForeColor = muted }, 0, 0);
        MP_editLayerSelector = new() { Dock = DockStyle.Fill, Margin = new(0, 3, 0, 3), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = surface, ForeColor = text };
        MP_editLayerSelector.Items.AddRange(new object[] { "全部", "建筑", "单位", "地形" }); MP_editLayerSelector.SelectedIndex = 0;
        MP_editLayerSelector.SelectedIndexChanged += (_, _) =>
        {
            canvas.EditLayer = new[] { null, "Building", "Pawn", "Terrain" }[MP_editLayerSelector.SelectedIndex];
            selectionStatus.Text = "编辑图层：" + MP_editLayerSelector.Text;
        };
        selectionCount = new() { Text = "未选择", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new(0, 0, 12, 0), ForeColor = muted };
        layerBar.Controls.Add(MP_editLayerSelector, 1, 0); layerBar.Controls.Add(selectionCount, 2, 0);
        viewport.Controls.Add(canvas); viewport.Controls.Add(layerBar); viewport.Controls.Add(editTools); inspectorSplit.Panel1.Controls.Add(viewport);

        TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new(6, 7), DrawMode = TabDrawMode.OwnerDrawFixed, BackColor = surface };
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            using SolidBrush background = new(selected ? Color.FromArgb(53, 86, 84) : surface); e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, e.Bounds, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        TableLayoutPanel Page(string title)
        {
            TabPage tab = new(title) { BackColor = surface, ForeColor = text, Padding = new(9), AutoScroll = true };
            TableLayoutPanel layout = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(3), Margin = Padding.Empty };
            layout.ColumnStyles.Add(new(SizeType.Percent, 100));
            layout.SizeChanged += (_, _) => { var extent = new Size(0, layout.Height + tab.Padding.Vertical); if (tab.AutoScrollMinSize != extent) tab.AutoScrollMinSize = extent; };
            tab.Controls.Add(layout); tabs.TabPages.Add(tab); return layout;
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
        TableLayoutPanel objects = Page("对象"), metadata = Page("结构"), exports = Page("导出"), tips = Page("操作");
        selectedName.Font = new(Font, FontStyle.Bold); Row(objects, selectedName, 65); selectedInfo.ForeColor = muted; Row(objects, selectedInfo, 62);
        materialBox.DropDownStyle = rotationBox.DropDownStyle = ComboBoxStyle.DropDownList;
        Field(objects, "材质", materialBox);
        rotationBox.Items.AddRange(new object[] { "北 / North", "东 / East", "南 / South", "西 / West" }); rotationBox.SelectedIndex = 0;
        Field(objects, "方向", rotationBox);
        dormancyBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dormancyBox.Items.AddRange(new object[] { "跟随结构默认", "初始休眠", "初始唤醒", "保持各自设置（混合）" }); dormancyBox.SelectedIndex = 0;
        Field(objects, "初始状态（MP 专用）", dormancyBox);
        dormancyInfo.ForeColor = muted; Row(objects, dormancyInfo, 58);
        Row(objects, new Label { Text = "修改直接生效 · Ctrl+Z 撤销", ForeColor = muted }, 29);
        Label groupTip = new() { Text = "双击吸取并切换到放置。\n拖动移动，方向键微调。\nShift 框选，Ctrl 增减选择。", ForeColor = muted }; Row(objects, groupTip, 90);
        Row(metadata, BuildClusterNamingPanel());
        structureIdTitle = new() { Text = "结构标识", ForeColor = muted, AutoSize = true }; Row(metadata, structureIdTitle); Row(metadata, idBox, 29);
        structureLabelTitle = new() { Text = "显示名称", ForeColor = muted, AutoSize = true }; Row(metadata, structureLabelTitle); Row(metadata, labelBox, 29);
        profileBox.DropDownStyle=ComboBoxStyle.DropDownList;profileBox.Items.AddRange(new object[]{"Structure Framework（主格式）","原版 PrefabDef（兼容）","MechProtocol（兼容）"});profileBox.SelectedIndex=0;
        Field(exports,"XML 目标格式",profileBox);mpCompatibility.Text="启用 MP 扩展（逐对象休眠）";mpCompatibility.AutoSize=true;Row(exports,mpCompatibility);
        Row(exports,autoExport);
        Row(exports,new Label { Text="XML 输出文件",ForeColor=muted,AutoSize=true });Row(exports,xmlPathBox,108);
        xmlPathBox.BackColor=Color.FromArgb(49,58,64);xmlPathBox.ForeColor=text;
        void ExportButton(string title,Action action)
        {
            Button button=MakeButton(title,0,0,230);button.Click+=(_,_)=>Safe(action);Row(exports,button,34);
        }
        ExportButton("更改 XML 目录…",SetXmlDirectory);
        ExportButton("立即导出 XML",ExportToConfiguredLocation);
        ExportButton("另存为 XML…",()=>ExportLayout(false));
        ExportButton("导出独立结构内容包…",ExportBundle);
        ExportButton("导出 PNG 预览…",ExportPreview);
        exportResult.ForeColor=muted;Row(exports,exportResult,110);
        Row(exports,new Label { Text="保存会更新 JSON 工程和所选格式的 XML。未指定 XML 目录时，与工程保存在一起。",ForeColor=muted },100);
        categoryBox.DropDownStyle = ComboBoxStyle.DropDownList; categoryBox.Items.AddRange(new object[] { "机械集群", "通用结构" }); categoryBox.SelectedIndex = 1;
        Field(metadata, "结构类别", categoryBox); dormant.Text = "默认休眠（MP）"; dormant.AutoSize = true; Row(metadata, dormant);
        Row(metadata, new Label { Text = "保存目录", ForeColor = muted, AutoSize = true }); Row(metadata, saveFolderBox, 74);
        saveFolderBox.BackColor = Color.FromArgb(49, 58, 64); saveFolderBox.ForeColor = text;
        Button chooseFolder = MakeButton("更改保存目录…", 0, 0, 230); chooseFolder.Click += (_, _) => Safe(SetSaveDirectory); Row(metadata, chooseFolder, 34);
        summary.ForeColor = muted; Row(metadata, summary, 62);
        Row(metadata, new Label { Text = "保存：JSON 工程及自动导出 XML。\n在“导出”页设置格式和 XML 目录。\n独立内容包自动声明所需模组。", ForeColor = muted }, 100);
        Row(tips, new Label { Text = "V 选择 / B 放置 / M 矩形 / E 擦除\nI 吸取 / R 旋转 / H 平移 / Z 缩放\n\n双击对象：吸取并继续放置\nShift 拖动：追加框选\nCtrl 单击：增减选择\nDelete / Backspace：删除\n\n中键 / 空格+拖动：平移\n滚轮：以鼠标为中心缩放\n缩放工具：左键放大，Alt+左键缩小\n\nCtrl+N / O：新建 / 打开\nCtrl+S：保存；Ctrl+Shift+S：另存为\nCtrl+Z / Ctrl+Shift+Z：撤销 / 重做\nCtrl+C / X / V：复制 / 剪切 / 粘贴\nCtrl+J：复制为新对象\n粘贴预览：Q 逆时针 / E 顺时针旋转\nCtrl+A：全选；Ctrl+Shift+A：取消\nCtrl+0：适合窗口；Ctrl+1：实际大小\nCtrl+Home：居中所选；Tab：切换编辑图层\n\n方向键：移动 1 格\nShift+方向键：移动 10 格\n\nTab / Shift+Tab：切换编辑图层", ForeColor = muted }, 700);
        inspectorSplit.Panel2.Controls.Add(tabs);
        foreach (Control c in new Control[] { search, idBox, labelBox, kindBox, packBox, groupBox, scopeBox, materialBox, rotationBox, categoryBox, dormancyBox, profileBox }) { c.BackColor = Color.FromArgb(49, 58, 64); c.ForeColor = text; }
        foreach (ComboBox combo in new[] { kindBox, packBox, groupBox, scopeBox, MP_editLayerSelector, materialBox, rotationBox, categoryBox, dormancyBox, profileBox })
        {
            combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = Math.Max(22, Font.Height + 3);
            combo.DrawItem += (_, e) =>
            {
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using SolidBrush background = new(selected ? Color.FromArgb(57, 91, 93) : Color.FromArgb(49, 58, 64)); e.Graphics.FillRectangle(background, e.Bounds);
                string label = e.Index >= 0 ? combo.Items[e.Index]?.ToString() : combo.Text;
                TextRenderer.DrawText(e.Graphics, label, Font, e.Bounds, text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
        profileBox.SelectedIndexChanged+=(_,_)=>{if(!updating)CommitMetadata();};mpCompatibility.CheckedChanged+=(_,_)=>{if(!updating)CommitMetadata();};
        autoExport.CheckedChanged+=(_,_)=>{if(!updating)CommitMetadata();};
        canvas.ToolChanged += RefreshToolDisplay;
        StatusStrip strip = new() { BackColor = Color.FromArgb(23, 29, 33), ForeColor = text, SizingGrip = false };
        selectionStatus = new() { BorderSides = ToolStripStatusLabelBorderSides.Left, Text = "放置" };
        status.Text = "正在读取游戏素材…"; strip.Items.Add(status); strip.Items.Add(selectionStatus);
        TableLayoutPanel workspace = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
        workspace.ColumnStyles.Add(new(SizeType.Percent, 100));
        workspace.RowStyles.Add(new(SizeType.AutoSize)); workspace.RowStyles.Add(new(SizeType.AutoSize)); workspace.RowStyles.Add(new(SizeType.AutoSize)); workspace.RowStyles.Add(new(SizeType.Percent, 100)); workspace.RowStyles.Add(new(SizeType.AutoSize));
        menu.Dock = strip.Dock = DockStyle.Fill; menu.Margin = heading.Margin = librarySplit.Margin = strip.Margin = Padding.Empty;
        workspace.Controls.Add(menu, 0, 0); workspace.Controls.Add(heading, 0, 1); workspace.Controls.Add(BuildDocumentBar(), 0, 2); workspace.Controls.Add(librarySplit, 0, 3); workspace.Controls.Add(strip, 0, 4); Controls.Add(workspace);
        RefreshToolDisplay(); ResumeLayout(true);
    }
    private void RefreshToolDisplay()
    {
        foreach (var pair in toolButtons) pair.Value.Checked = pair.Key == canvas.Tool;
        if (selectionStatus != null) selectionStatus.Text = canvas.HasStamp ? "整组粘贴 · Q/E 旋转 · 左键放置 / Esc 取消" : canvas.Tool switch
        { CanvasTool.Select => "选择 · Shift 框选", CanvasTool.Rectangle => "矩形铺地 · 左键拖动", CanvasTool.Erase => "擦除 · 左键拖动", CanvasTool.Hand => "平移 · 拖动视图", CanvasTool.Zoom => "缩放 · Alt 缩小", _ => "放置 · B" };
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
                Color.FromArgb(59, 78, 91), Color.FromArgb(29, 41, 51),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(fill, area);
            using var line = new Pen(Color.FromArgb(70, 103, 117));
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
        public override Color ToolStripBorder => Color.FromArgb(48, 60, 65);
    }
}
