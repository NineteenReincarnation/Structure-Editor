using System.Drawing.Imaging;

namespace StructureEditor;

// Native file dialogs and explicit user document actions.
public partial class MainForm
{
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
