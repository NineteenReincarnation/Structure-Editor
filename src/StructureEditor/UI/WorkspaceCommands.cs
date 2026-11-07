namespace StructureEditor;

public partial class MainForm
{
    private bool borrowedCatalog;
    private void CopyObjects()
    {
        if (canvas.Selection.Count == 0) return;
        copied = canvas.CopySelection(); status.Text = $"已复制 {copied.Count} 个对象；Ctrl+V 放置整组。";
    }
    private void CutObjects() { if (canvas.Selection.Count == 0) return; CopyObjects(); canvas.DeleteSelected(); }
    private void PasteObjects() { if (copied.Count == 0) return; canvas.SetStamp(copied); canvas.Focus(); status.Text = $"粘贴 {copied.Count} 个对象：Q 逆时针 / E 顺时针旋转，左键放置，Esc 退出。"; }
    private void DuplicateObjects() { CopyObjects(); PasteObjects(); }
    protected virtual Control ShortcutInput
    {
        get
        {
            Control input = this;
            while (true)
            {
                Control child = input.Controls.Cast<Control>().FirstOrDefault(c => c.ContainsFocus);
                if (child == null) return input;
                input = child;
            }
        }
    }
    internal static bool InputOwnsShortcut(Control input, Keys keyData) => input switch
    {
        TextBoxBase text => !text.ReadOnly || (keyData & (Keys.Control | Keys.Alt)) != 0,
        ComboBox combo => combo.DropDownStyle != ComboBoxStyle.DropDownList || combo.DroppedDown,
        UpDownBase => true,
        _ => false
    };
    private bool HandleModeShortcut(Keys keyData)
    {
        if (canvas.HasStamp && (keyData == Keys.Q || keyData == Keys.E))
        {
            canvas.Rotate(keyData == Keys.Q ? -1 : 1); canvas.Focus(); return true;
        }
        switch (keyData)
        {
            case Keys.B: canvas.Tool = CanvasTool.Place; break;
            case Keys.V: canvas.Tool = CanvasTool.Select; break;
            case Keys.M: canvas.Tool = CanvasTool.Rectangle; break;
            case Keys.E: canvas.Tool = CanvasTool.Erase; break;
            case Keys.R: canvas.Rotate(); break;
            case Keys.I: canvas.Eyedropper(); break;
            case Keys.G: canvas.ShowGrid = !canvas.ShowGrid; canvas.Invalidate(); break;
            case Keys.H: canvas.Tool = CanvasTool.Hand; break;
            case Keys.Z: canvas.Tool = CanvasTool.Zoom; break;
            default: return false;
        }
        canvas.Focus(); return true;
    }
    private static bool HandleTextShortcut(TextBoxBase input, Keys keyData)
    {
        if (!input.ShortcutsEnabled) return false;
        switch (keyData)
        {
            case Keys.Control | Keys.C:
            case Keys.Control | Keys.Insert: input.Copy(); return true;
            case Keys.Control | Keys.A: input.SelectAll(); return true;
            case Keys.Control | Keys.X:
            case Keys.Shift | Keys.Delete: if (!input.ReadOnly) input.Cut(); return true;
            case Keys.Control | Keys.V:
            case Keys.Shift | Keys.Insert: if (!input.ReadOnly) input.Paste(); return true;
            case Keys.Control | Keys.Z: if (!input.ReadOnly) input.Undo(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z:
                if (!input.ReadOnly && input is RichTextBox rich) rich.Redo();
                return true;
            default: return false;
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.W)) { Safe(CloseWorkspaceDocument); return true; }
        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            EnsureWorkspaceDocument(); int step = (keyData & Keys.Shift) != 0 ? -1 : 1;
            Safe(() => SwitchWorkspaceDocument(documents[(documents.IndexOf(activeDocument) + step + documents.Count) % documents.Count])); return true;
        }
        if (keyData == Keys.F5) { LoadCatalog(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { Safe(() => SaveDocument(false)); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { Safe(() => SaveDocument(true)); return true; }
        Control input = ShortcutInput;
        // Calling the Form base method here still dispatches MenuStrip Ctrl+C/V/Z,
        // so explicitly complete native text edits before the menu sees the command.
        if (input is TextBoxBase textInput && HandleTextShortcut(textInput, keyData)) return true;
        if (InputOwnsShortcut(input, keyData)) return base.ProcessCmdKey(ref msg, keyData);
        if (HandleModeShortcut(keyData)) return true;
        if (keyData == (Keys.Control | Keys.N)) { Safe(NewDocument); return true; }
        if (keyData == (Keys.Control | Keys.O)) { Safe(OpenDocument); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
        if (keyData == (Keys.Control | Keys.A)) { canvas.SelectAll(); canvas.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { CopyObjects(); return true; }
        if (keyData == (Keys.Control | Keys.X)) { CutObjects(); return true; }
        if (keyData == (Keys.Control | Keys.V)) { PasteObjects(); return true; }
        if (keyData == (Keys.Control | Keys.J)) { DuplicateObjects(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.A)) { canvas.ClearSelection(); return true; }
        if (keyData == Keys.Escape) { canvas.Cancel(); return true; }
        if ((keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            && MP_editLayerSelector != null && !InputOwnsShortcut(input, keyData)
            && input is not (ComboBox or TreeView or ListBox or ListView or Button or TextBoxBase or UpDownBase))
        {
            MP_CycleEditLayer(keyData == Keys.Tab ? 1 : -1);
            canvas.Focus();
            return true;
        }
        Keys plainKey = keyData & Keys.KeyCode;
        if ((keyData & (Keys.Control | Keys.Alt)) == 0 && plainKey is Keys.Delete or Keys.Back or Keys.X)
        { canvas.DeleteSelected(); return true; }
        if (keyData == Keys.Home) { canvas.CenterView(); return true; }
        if (keyData == (Keys.Control | Keys.Home)) { canvas.CenterSelection(); return true; }
        if (keyData == (Keys.Control | Keys.D0)) { canvas.CenterView(); return true; }
        if (keyData == (Keys.Control | Keys.D1)) { canvas.ZoomAtCenter(1, true); return true; }
        if (canvas.Focused)
        {
            int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
            if ((keyData & ~Keys.Shift) == Keys.Left) { canvas.MoveSelected(-step, 0); return true; }
            if ((keyData & ~Keys.Shift) == Keys.Right) { canvas.MoveSelected(step, 0); return true; }
            if ((keyData & ~Keys.Shift) == Keys.Up) { canvas.MoveSelected(0, step); return true; }
            if ((keyData & ~Keys.Shift) == Keys.Down) { canvas.MoveSelected(0, -step); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { StopBackgroundLoading(); searchDelay.Dispose(); namingSwitchDelay.Dispose(); smallFont.Dispose(); if (!borrowedCatalog) catalog?.Dispose(); }
        base.Dispose(disposing);
    }
    // Render and exercise the application's own controls in a headless verification process.
    internal void BindForChecks(Catalog source, StructureDocument structure)
    {
        borrowedCatalog = true; catalog = source; document = structure; canvas.Catalog = source; canvas.Document = document;
        packBox.Items.Add("所有来源"); packBox.SelectedIndex = 0; RefreshMetadata(); FilterPalette(); ShowSelection(); canvas.CenterView();
        status.Text = $"已读取 {catalog.Items.Count} 个素材  ·  Shift 框选 / M 矩形铺地";
    }
}
