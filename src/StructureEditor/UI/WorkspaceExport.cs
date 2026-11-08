namespace StructureEditor;

public partial class MainForm
{
    private readonly CheckBox autoExport = new() { Text = "保存时自动导出 XML", AutoSize = true, Checked = true };
    private readonly TextBox xmlPathBox = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label exportResult = new() { AutoEllipsis = true };
    private readonly Label MP_compatRecommendation = new() { AutoSize = true };
    private readonly Label MP_exportReviewStatus = new() { AutoSize = true };
    private readonly Label MP_exportDependencySummary = new() { AutoSize = true };
    private readonly TextBox MP_exportReviewDetails = new()
    {
        ReadOnly = true, Multiline = true, WordWrap = true,
        ScrollBars = ScrollBars.Vertical, Height = 103
    };
    private readonly System.Windows.Forms.Timer MP_exportReviewTimer = new() { Interval = 360 };
    private Button MP_exportPrimary;

    private void MP_ScheduleExportReview()
    {
        if (IsDisposed || Disposing || MP_exportReviewStatus == null) return;
        MP_exportReviewTimer.Stop();
        if (catalog == null)
        {
            MP_exportReviewStatus.Text = "正在准备素材与导出检查…";
            if (MP_exportPrimary != null) MP_exportPrimary.Enabled = false;
            return;
        }
        MP_exportReviewTimer.Start();
    }

    private void MP_RefreshExportReview()
    {
        if (IsDisposed || Disposing) return;
        MP_exportReviewTimer.Stop();
        var review = MP_ExportReview.Inspect(document, catalog, document.ExportProfile);
        if (MP_exportReviewStatus.Text != review.Summary)
            MP_exportReviewStatus.Text = review.Summary;
        MP_exportReviewStatus.ForeColor = review.Errors > 0 ? Color.FromArgb(239, 151, 139)
            : review.Warnings > 0 ? Color.FromArgb(218, 185, 120) : Color.FromArgb(129, 206, 166);
        MP_exportStatusDot.SetState(review.Errors, review.Warnings);
        string dependencies = review.Dependencies.Count == 0
            ? "依赖：仅原版基础内容"
            : "依赖：" + string.Join("、", review.Dependencies.Select(x => x.Name ?? x.PackageId).Take(5))
                + (review.Dependencies.Count > 5 ? $" 等 {review.Dependencies.Count} 项" : "");
        if (MP_exportDependencySummary.Text != dependencies)
            MP_exportDependencySummary.Text = dependencies;
        string details = string.Join(Environment.NewLine,
            review.Issues.Select(x => (x.IsError ? "[错误] " : "[提醒] ") + x.Message));
        if (MP_exportReviewDetails.Text != details)
            MP_exportReviewDetails.Text = details;
        MP_exportReviewDetails.Visible = review.Issues.Count != 0;
        MP_exportPrimary.Enabled = review.CanExport;

        if (document.ExportProfile is "Framework" or "Universal")
        {
            bool recommended = MP_ExportReview.MP_CanRecommendDormancy(document, catalog);
            string message = document.EnableMPCompatibility
                ? "休眠拓展已启用"
                : recommended ? "检测到可休眠对象" : "";
            if (MP_compatRecommendation.Text != message)
                MP_compatRecommendation.Text = message;
            MP_compatRecommendation.Visible = recommended && !document.EnableMPCompatibility;
        }
        else
        {
            if (MP_compatRecommendation.Text.Length != 0) MP_compatRecommendation.Text = "";
            MP_compatRecommendation.Visible = false;
        }
    }

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
        string nextPath = StructureStorage.XmlTarget(document,
            saveLocationChanged ? null : currentFile, OutputFolder);
        if (xmlPathBox.Text != nextPath) xmlPathBox.Text = nextPath;
        MP_ScheduleExportReview();
    }
    private void SetXmlDirectory()
    {
        CommitMetadata();
        using FolderBrowserDialog dialog = new() { Description = "选择 XML 导出目录", SelectedPath = Path.GetDirectoryName(StructureStorage.XmlTarget(document, currentFile, OutputFolder)), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Snapshot(); document.XmlExportDirectory = dialog.SelectedPath; dirty = true; UpdateTitle(); RefreshExportLocation();
        exportResult.Text = "XML 输出目录已更新。"; exportResult.Visible = true;
    }
    private void ExportToConfiguredLocation()
    {
        if (namingSwitchDelay.Enabled) ResolveNamingSelection();
        CommitInspectorEdit(); CommitMetadata();
        string path = StructureStorage.XmlTarget(document, saveLocationChanged ? null : currentFile, OutputFolder);
        CheckWorkspaceExportTarget(path);
        StructureStorage.ExportXml(document, catalog, path, document.ExportProfile);
        MP_RecordExportTarget(path);
        exportResult.ForeColor = muted; exportResult.Text = "XML 已导出"; exportResult.Visible = true; status.Text = "已导出布局：" + path;
        RefreshExportLocation();
    }
}
