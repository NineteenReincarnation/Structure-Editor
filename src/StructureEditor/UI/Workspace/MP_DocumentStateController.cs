namespace StructureEditor;

// Undo/redo, metadata and document state coordination.
public partial class MainForm
{
    private void Snapshot()
    {
        if (updating) return;
        MP_history.Snapshot(document);
    }
    private void Undo() { CommitInspectorEdit(); StructureDocument previous = MP_history.Undo(document); if (previous == null) return; document = previous; RestoreDocument(); }
    private void Redo() { CommitInspectorEdit(); StructureDocument next = MP_history.Redo(document); if (next == null) return; document = next; RestoreDocument(); }
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
}
