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
        ToolStripMenuItem export = new("导出"); file.DropDownItems.Add(export); AddMenuAction(export, "当前目标格式 XML…", () => ExportLayout(false)); AddMenuAction(export, "独立结构内容包…", ExportBundle); AddMenuAction(export,"原版 PrefabDef XML…",()=>ExportLayout(false,"VanillaPrefab")); AddMenuAction(export, "PNG 预览…", ExportPreview); export.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(export, "MechProtocol 兼容 XML…", () => ExportLayout(true));
        AddMenuAction(file, "工程保存目录…", SetSaveDirectory); file.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(file, "关闭工程", CloseWorkspaceDocument, Keys.Control | Keys.W); AddMenuAction(file, "退出", Close, Keys.Alt | Keys.F4);
        var undoAction = AddMenuAction(edit, "撤销", Undo, Keys.Control | Keys.Z); var redoAction = AddMenuAction(edit, "重做", Redo, Keys.Control | Keys.Shift | Keys.Z); edit.DropDownItems.Add(new ToolStripSeparator());
        var cutAction = AddMenuAction(edit, "剪切", CutObjects, Keys.Control | Keys.X); var copyAction = AddMenuAction(edit, "复制", CopyObjects, Keys.Control | Keys.C); var pasteAction = AddMenuAction(edit, "粘贴", PasteObjects, Keys.Control | Keys.V); var duplicateAction = AddMenuAction(edit, "复制为新对象", DuplicateObjects, Keys.Control | Keys.J);
        var deleteAction = AddMenuAction(edit, "删除", canvas.DeleteSelected, Keys.Delete); edit.DropDownItems.Add(new ToolStripSeparator()); AddMenuAction(edit, "全选", canvas.SelectAll, Keys.Control | Keys.A); var deselectAction = AddMenuAction(edit, "取消选择", canvas.ClearSelection, Keys.Control | Keys.Shift | Keys.A);
        edit.DropDownOpening += (_, _) => { undoAction.Enabled = undo.Count > 0; redoAction.Enabled = redo.Count > 0; pasteAction.Enabled = copied.Count > 0; foreach (var item in new[] { cutAction, copyAction, duplicateAction, deleteAction, deselectAction }) item.Enabled = canvas.Selection.Count > 0; };
        edit.DropDownItems.Add(new ToolStripSeparator()); ToolStripMenuItem prefs = new("首选项"); edit.DropDownItems.Add(prefs); AddMenuAction(prefs, "游戏目录…", SetPaths); AddMenuAction(prefs, "素材来源…", ShowSources);
        AddMenuAction(edit,"重新读取素材",LoadCatalog,Keys.F5);
        AddMenuAction(objects, "顺时针旋转 90°", canvas.Rotate, Keys.R); AddMenuAction(objects, "吸取为放置笔刷", canvas.Eyedropper, Keys.I); AddMenuAction(objects, "加入 / 取消收藏", ToggleFavorite); AddMenuAction(objects, "居中显示所选", canvas.CenterSelection, Keys.Control | Keys.Home);
        AddMenuAction(view, "适合窗口", canvas.CenterView, Keys.Control | Keys.D0); AddMenuAction(view, "实际大小（100%）", () => canvas.ZoomAtCenter(1, true), Keys.Control | Keys.D1); AddMenuAction(view, "放大", () => canvas.ZoomAtCenter(1.2f), Keys.Control | Keys.Oemplus); AddMenuAction(view, "缩小", () => canvas.ZoomAtCenter(1 / 1.2f), Keys.Control | Keys.OemMinus); AddMenuAction(view, "显示 / 隐藏网格", () => { canvas.ShowGrid = !canvas.ShowGrid; canvas.Invalidate(); }, Keys.G);
        AddMenuAction(window, "素材库", () => librarySplit.Panel1Collapsed = !librarySplit.Panel1Collapsed); AddMenuAction(window, "属性面板", ToggleInspector); AddMenuAction(window, "重置工作区", () => { librarySplit.Panel1Collapsed = false; inspectorSplit.Panel2Collapsed = false; canvas.CenterView(); });
        var explosionRanges = AddMenuAction(view, "显示炮塔损毁爆炸范围", () => SetTurretExplosionRanges(!canvas.ShowTurretExplosionRanges));
        explosionRanges.Name = "TurretExplosionRanges";
        AddMenuAction(help, "使用说明", () => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "使用说明.md")) { UseShellExecute = true }));
        AddMenuAction(help, "键盘快捷键", () => MessageBox.Show(this, "V 选择 / B 放置 / M 矩形 / E 擦除\nI 吸取 / R 旋转 / H 平移 / Z 缩放\n空格+拖动：临时平移；滚轮：缩放\nCtrl+0 适合窗口 / Ctrl+1 实际大小\nCtrl+J 复制 / Ctrl+Shift+Z 重做\nTab 切换编辑图层；Shift+Tab 反向切换\n粘贴预览：Q 逆时针 / E 顺时针旋转\nShift 框选 / Ctrl 单击增减选择\nDelete / Backspace 删除；Esc 取消", "键盘快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information));
        AddMenuAction(help, "项目主页", () => Process.Start(new ProcessStartInfo("https://github.com/NineteenReincarnation") { UseShellExecute = true }));
        AddMenuAction(help, "关于 Structure Editor", () => MessageBox.Show(this, "Structure Editor · 结构工坊\n版本 " + Application.ProductVersion + "\nRimWorld 结构布局编辑器\n作者：NineteenReincarnation\n素材从本机游戏及额外来源读取。", "关于 Structure Editor", MessageBoxButtons.OK, MessageBoxIcon.Information));
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
        private static Color Surface => Color.FromArgb(36, 44, 49);
        private static Color Highlight => Color.FromArgb(57, 91, 93);
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
