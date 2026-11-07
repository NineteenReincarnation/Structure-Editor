namespace StructureEditor;

public partial class MainForm
{
    private readonly CheckBox autoExport = new() { Text = "保存时自动导出 XML", AutoSize = true, Checked = true };
    private readonly TextBox xmlPathBox = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label exportResult = new() { AutoEllipsis = true };
    private void MP_RecordExportTarget(string path)
    {
        if (currentFile == null || string.IsNullOrWhiteSpace(path)) return;
        Program.Paths.MP_XmlExportPaths ??= new(StringComparer.OrdinalIgnoreCase);
        Program.Paths.MP_XmlExportPaths[Path.GetFullPath(currentFile)] = Path.GetFullPath(path);
        if (activeDocument != null) activeDocument.LastXmlPath = Path.GetFullPath(path);
        Program.SavePaths();
    }

    private void RefreshExportLocation()
    {
        xmlPathBox.Text = StructureStorage.XmlTarget(document, saveLocationChanged ? null : currentFile, OutputFolder);
    }
    private void SetXmlDirectory()
    {
        CommitMetadata();
        using FolderBrowserDialog dialog = new() { Description = "选择 XML 导出目录", SelectedPath = Path.GetDirectoryName(StructureStorage.XmlTarget(document, currentFile, OutputFolder)), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Snapshot(); document.XmlExportDirectory = dialog.SelectedPath; dirty = true; UpdateTitle(); RefreshExportLocation();
        exportResult.Text = "下次保存时将更新以上 XML 文件。";
    }
    private void ExportToConfiguredLocation()
    {
        if (namingSwitchDelay.Enabled) ResolveNamingSelection();
        CommitInspectorEdit(); CommitMetadata();
        string path = StructureStorage.XmlTarget(document, saveLocationChanged ? null : currentFile, OutputFolder);
        CheckWorkspaceExportTarget(path);
        StructureStorage.ExportXml(document, catalog, path, document.ExportProfile);
        MP_RecordExportTarget(path);
        exportResult.ForeColor = muted; exportResult.Text = "XML 已更新：\n" + path; status.Text = "已导出布局：" + path;
        RefreshExportLocation();
    }
}
