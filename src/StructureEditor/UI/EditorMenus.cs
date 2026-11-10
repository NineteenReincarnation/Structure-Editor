using System.Diagnostics;

namespace StructureEditor;

public partial class MainForm
{
    private readonly ToolStripRenderer menuRenderer = new ToolStripProfessionalRenderer(new EditorMenuColors());
    private ToolStripMenuItem AddMenuAction(ToolStripDropDownItem parent, string title, Action action, Keys keys = Keys.None)
    {
        parent.DropDown.BackColor = surface; parent.DropDown.ForeColor = text; parent.DropDown.Renderer = menuRenderer;
        ToolStripMenuItem item = new(title) { ForeColor = text, ShortcutKeys = (keys & (Keys.Control | Keys.Alt)) != 0 ? keys : Keys.None, ShortcutKeyDisplayString = keys == Keys.None ? "" : new KeysConverter().ConvertToString(keys) }; item.Click += (_, _) => Safe(action); parent.DropDownItems.Add(item); return item;
    }
    private MenuStrip BuildMenus()
    {
        MenuStrip menu = new() { BackColor = surface, ForeColor = text, Padding = new(8, 3, 0, 3), Renderer = menuRenderer };
        ToolStripMenuItem file = new("文件(&F)"), edit = new("编辑(&E)"), objects = new("对象(&O)"), view = new("视图(&V)"), window = new("窗口(&W)"), help = new("帮助(&H)");
        AddMenuAction(file, "新建工程…", NewDocument, Keys.Control | Keys.N); AddMenuAction(file, "打开…", OpenDocument, Keys.Control | Keys.O);
        ToolStripMenuItem recent = new("最近打开"); file.DropDownItems.Add(recent);
        recent.DropDownOpening += (_, _) => { recent.DropDownItems.Clear(); foreach (string path in Program.Paths.RecentDocuments.Where(File.Exists).Take(12)) AddMenuAction(recent, Path.GetFileName(path), () => OpenDocumentPath(path)); if (recent.DropDownItems.Count == 0) recent.DropDownItems.Add(new ToolStripMenuItem("无最近工程") { Enabled = false }); };
        file.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(file, "保存", () => SaveDocument(false), Keys.Control | Keys.S); AddMenuAction(file, "另存为…", () => SaveDocument(true), Keys.Control | Keys.Shift | Keys.S);
        ToolStripMenuItem export = new("导出"); file.DropDownItems.Add(export);
        AddMenuAction(export,"Structure Framework XML…",ExportLayout);
        AddMenuAction(export,"独立结构内容包…",ExportBundle);
        AddMenuAction(export,"PNG 预览…",ExportPreview);
        AddMenuAction(file, "工程保存目录…", SetSaveDirectory); file.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(file, "关闭工程", CloseWorkspaceDocument, Keys.Control | Keys.W); AddMenuAction(file, "退出", Close, Keys.Alt | Keys.F4);
        var undoAction = AddMenuAction(edit, "撤销", Undo, Keys.Control | Keys.Z); var redoAction = AddMenuAction(edit, "重做", Redo, Keys.Control | Keys.Shift | Keys.Z); edit.DropDownItems.Add(new ToolStripSeparator());
        var cutAction = AddMenuAction(edit, "剪切", CutObjects, Keys.Control | Keys.X); var copyAction = AddMenuAction(edit, "复制", CopyObjects, Keys.Control | Keys.C); var pasteAction = AddMenuAction(edit, "粘贴", PasteObjects, Keys.Control | Keys.V); var duplicateAction = AddMenuAction(edit, "复制为新对象", DuplicateObjects, Keys.Control | Keys.D);
        var deleteAction = AddMenuAction(edit, "删除", canvas.DeleteSelected, Keys.Delete); edit.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(edit, "全选", canvas.SelectAll, Keys.Control | Keys.A); var deselectAction = AddMenuAction(edit, "取消选择", canvas.ClearSelection, Keys.Control | Keys.Shift | Keys.A);
        edit.DropDownOpening += (_, _) => { undoAction.Enabled = undo.Count > 0; redoAction.Enabled = redo.Count > 0; pasteAction.Enabled = copied.Count > 0; foreach (var item in new[] { cutAction, copyAction, duplicateAction, deleteAction, deselectAction }) item.Enabled = canvas.Selection.Count > 0; };
        edit.DropDownItems.Add(new ToolStripSeparator()); ToolStripMenuItem prefs = new("首选项"); edit.DropDownItems.Add(prefs); AddMenuAction(prefs, "游戏目录…", SetPaths); AddMenuAction(prefs, "素材来源…", ShowSources);
        ToolStripMenuItem soundToggle = new("交互音效")
        {
            CheckOnClick = true, Checked = Program.Paths?.MP_UiSoundsEnabled ?? true,
            ForeColor = text, ToolTipText = "柔和的按键反馈音，可随时关闭"
        };
        soundToggle.CheckedChanged += (_, _) =>
        {
            Program.Paths.MP_UiSoundsEnabled = soundToggle.Checked;
            Program.SavePaths();
            if (soundToggle.Checked) MP_UiSound.Play(MP_UiSound.Cue.Confirm);
        };
        prefs.DropDownItems.Add(soundToggle);
        AddMenuAction(edit,"重新读取素材",LoadCatalog,Keys.F5);
        AddMenuAction(objects, "顺时针旋转 90°", canvas.Rotate, Keys.R); AddMenuAction(objects, "吸取为放置笔刷", canvas.Eyedropper, Keys.I); AddMenuAction(objects, "加入 / 取消收藏", ToggleFavorite); AddMenuAction(objects, "居中显示所选", canvas.CenterSelection, Keys.Control | Keys.Home);
        AddMenuAction(view, "适合窗口", canvas.CenterView, Keys.Control | Keys.D0); AddMenuAction(view, "实际大小（100%）", () => canvas.ZoomAtCenter(1, true), Keys.Control | Keys.D1); AddMenuAction(view, "放大", () => canvas.ZoomAtCenter(1.2f), Keys.Control | Keys.Oemplus); AddMenuAction(view, "缩小", () => canvas.ZoomAtCenter(1 / 1.2f), Keys.Control | Keys.OemMinus); AddMenuAction(view, "显示 / 隐藏网格", () => { canvas.ShowGrid = !canvas.ShowGrid; canvas.Invalidate(); }, Keys.G);
        AddMenuAction(window, "素材库", () => librarySplit.Panel1Collapsed = !librarySplit.Panel1Collapsed); AddMenuAction(window, "属性面板", ToggleInspector); AddMenuAction(window, "重置工作区", () => { librarySplit.Panel1Collapsed = false; inspectorSplit.Panel2Collapsed = false; canvas.CenterView(); });
        var explosionRanges = AddMenuAction(view, "殉爆范围", () => MP_ToggleRange("TurretExplosionRanges"));
        explosionRanges.Name = "TurretExplosionRanges";
        explosionRanges.ShortcutKeyDisplayString = "Ctrl+G";
        var detectorRanges = AddMenuAction(view, "探测范围", () => MP_ToggleRange("ProximityRanges"));
        detectorRanges.Name = "ProximityRanges";
        detectorRanges.ShortcutKeyDisplayString = "Ctrl+H";
        var shieldRanges = AddMenuAction(view, "护盾范围", () => MP_ToggleRange("ShieldRanges"));
        shieldRanges.Name = "ShieldRanges";
        shieldRanges.ShortcutKeyDisplayString = "Ctrl+J";
        view.DropDownOpening += (_, _) =>
        {
            explosionRanges.Checked = canvas.ShowTurretExplosionRanges;
            detectorRanges.Checked = canvas.ShowProximityRanges;
            shieldRanges.Checked = canvas.ShowShieldRanges;
        };
        AddMenuAction(help, "使用说明", () => MP_HelpWindows.Documentation(this));
        AddMenuAction(help, "键盘快捷键", () => MP_HelpWindows.Shortcuts(this));
        AddMenuAction(help, "项目主页", MP_HelpWindows.Project);
        AddMenuAction(help, "关于 Structure Editor", () => MP_HelpWindows.About(this));
        var headings = new ToolStripMenuItem[] { file, edit, objects, view, window, help };
        foreach (var heading in headings) heading.ForeColor = text;
        menu.Items.AddRange(headings); return menu;
    }
    private void OpenDocumentPath(string path) => OpenWorkspaceFile(path);
    private void SetTurretExplosionRanges(bool visible)
    {
        canvas.ShowTurretExplosionRanges = visible;
        void Sync(ToolStripItem item)
        {
            if (item.Name == "TurretExplosionRanges")
            {
                if (item is ToolStripButton button) button.Checked = visible;
                if (item is ToolStripMenuItem menu) menu.Checked = visible;
            }
            if (item is ToolStripDropDownItem dropdown) foreach (ToolStripItem child in dropdown.DropDownItems) Sync(child);
        }
        void Visit(Control control)
        {
            if (control is ToolStrip strip) foreach (ToolStripItem item in strip.Items) Sync(item);
            foreach (Control child in control.Controls) Visit(child);
        }
        Visit(this);
    }
    private sealed class EditorMenuColors : ProfessionalColorTable
    {
        private static Color Surface => Color.FromArgb(39, 52, 62);
        private static Color Highlight => Color.FromArgb(51, 87, 94);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color MenuItemPressedGradientBegin => Surface;
        public override Color MenuItemPressedGradientMiddle => Surface;
        public override Color MenuItemPressedGradientEnd => Surface;
        public override Color MenuBorder => Color.FromArgb(81, 99, 105);
        public override Color SeparatorDark => Color.FromArgb(66, 79, 85);
        public override Color SeparatorLight => Surface;
    }
}
