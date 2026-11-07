using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace StructureEditor;

public partial class MainForm
{
    private sealed record MP_LibraryEntry(string File, string Id, string Label, bool HasXml);

    private TabControl MP_libraryTabs;
    private TreeView MP_libraryTree;
    private TextBox MP_librarySearch;
    private PictureBox MP_libraryPreview;
    private Label MP_libraryDetails;
    private Label MP_libraryCount;
    private List<MP_LibraryEntry> MP_libraryEntries = new();
    private bool MP_libraryLoaded;

    // This panel is docked into the existing LEFT library column. No extra
    // dialog and no extra per-Def tree level: double-click a leaf to open it.
    private Control MP_CreateStructureLibraryPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = surface, ForeColor = text,
            ColumnCount = 1, RowCount = 5, Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));

        var row = new Panel { Dock = DockStyle.Fill };
        MP_librarySearch = new TextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "搜索结构 / DefName", BackColor = Color.FromArgb(49, 58, 64),
            ForeColor = text, Margin = Padding.Empty
        };
        var refresh = new Button
        {
            Dock = DockStyle.Right, Width = 54, Text = "刷新",
            BackColor = Color.FromArgb(52, 64, 69), ForeColor = text,
            FlatStyle = FlatStyle.Flat
        };
        row.Controls.Add(MP_librarySearch);
        row.Controls.Add(refresh);
        layout.Controls.Add(row, 0, 0);

        MP_libraryCount = new Label
        {
            Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft,
            Text = "项目结构库 · 双击结构打开"
        };
        layout.Controls.Add(MP_libraryCount, 0, 1);

        MP_libraryTree = new TreeView
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(29, 35, 39),
            ForeColor = text, BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false, FullRowSelect = true, ShowNodeToolTips = true,
            ShowLines = true, Indent = 15
        };
        layout.Controls.Add(MP_libraryTree, 0, 2);

        var previewPanel = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 4),
            BackColor = Color.FromArgb(29, 35, 39)
        };
        MP_libraryPreview = new PictureBox
        {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(42, 48, 51)
        };
        previewPanel.Controls.Add(MP_libraryPreview);
        layout.Controls.Add(previewPanel, 0, 3);

        MP_libraryDetails = new Label
        {
            Dock = DockStyle.Fill, ForeColor = muted, AutoEllipsis = true,
            Text = "单击选择并预览 PNG。双击结构直接打开。"
        };
        layout.Controls.Add(MP_libraryDetails, 0, 4);

        MP_librarySearch.TextChanged += (_, _) => { if (MP_libraryLoaded) MP_RebuildLibraryTree(); };
        refresh.Click += (_, _) => Safe(MP_ReloadLibrary);
        var MP_renameContext = new ContextMenuStrip();
        var MP_renameMenuItem = MP_renameContext.Items.Add("重命名…");
        MP_renameContext.Opening += (_, e) =>
        {
            if (MP_libraryTree.SelectedNode?.Tag is not MP_LibraryEntry) e.Cancel = true;
        };
        MP_renameMenuItem.Click += (_, _) =>
        {
            if (MP_libraryTree.SelectedNode?.Tag is MP_LibraryEntry selected)
                Safe(() => MP_RenameLibraryEntry(selected));
        };
        MP_libraryTree.ContextMenuStrip = MP_renameContext;
        MP_libraryTree.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) MP_libraryTree.SelectedNode = e.Node;
        };
        MP_libraryTree.AfterSelect += (_, e) => MP_ShowLibraryPreview(e.Node?.Tag as MP_LibraryEntry);
        MP_libraryTree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is MP_LibraryEntry entry) Safe(() => OpenWorkspaceFile(entry.File, entry.Id));
        };
        MP_libraryTree.KeyDown += (_, e) =>
        {
            if (MP_libraryTree.SelectedNode?.Tag is not MP_LibraryEntry entry) return;
            if (e.KeyCode == Keys.F2)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                Safe(() => MP_RenameLibraryEntry(entry));
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                Safe(() => OpenWorkspaceFile(entry.File, entry.Id));
            }
        };
        FormClosed += (_, _) => { MP_libraryPreview.Image?.Dispose(); MP_libraryPreview.Image = null; };
        return layout;
    }

    private void MP_ShowStructureLibrary()
    {
        MP_libraryTabs.SelectedIndex = 1;
        if (!MP_libraryLoaded) MP_ReloadLibrary();
        MP_librarySearch.Focus();
    }

    private static bool MP_IsProjectStructure(string file)
    {
        string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Program.StructureLibraryFolder));
        string path = Path.GetFullPath(file);
        return path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private void MP_ReloadLibrary()
    {
        var all = LibraryDefinitions();
        // A JSON project is the editable source of truth when its XML export
        // is present. Keep one leaf per DefName instead of two clickable files.
        MP_libraryEntries = all
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var chosen = group.OrderBy(e => MP_IsProjectStructure(e.File) ? 0 : 1)
                    .ThenBy(e => e.File.EndsWith(".structure.json", StringComparison.OrdinalIgnoreCase) ? 0
                        : e.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase).First();
                return new MP_LibraryEntry(chosen.File, chosen.Id, chosen.Label,
                    group.Any(e => e.File.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
            })
            .OrderBy(e => MP_IsProjectStructure(e.File) ? 0 : 1)
            .ThenBy(e => e.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        MP_libraryLoaded = true;
        MP_RebuildLibraryTree();
    }

    private void MP_RebuildLibraryTree()
    {
        string query = MP_librarySearch.Text.Trim();
        var visible = MP_libraryEntries.Where(e => query.Length == 0
            || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || e.File.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        MP_libraryTree.BeginUpdate();
        try
        {
            MP_libraryTree.Nodes.Clear();
            var projectRoot = new TreeNode("▾ 项目结构库") { Name = "project" };
            var otherRoot = new TreeNode("▾ 其他位置") { Name = "other" };
            foreach (MP_LibraryEntry entry in visible)
            {
                bool project = MP_IsProjectStructure(entry.File);
                TreeNode parent = project ? projectRoot : otherRoot;
                string directory = Path.GetDirectoryName(Path.GetFullPath(entry.File))!;
                string relative = project ? Path.GetRelativePath(Program.StructureLibraryFolder, directory)
                    : Path.GetFileName(directory);
                foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s != "."))
                {
                    TreeNode existing = parent.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == "dir:" + segment);
                    if (existing == null)
                    {
                        existing = new TreeNode("▸ " + segment) { Name = "dir:" + segment };
                        parent.Nodes.Add(existing);
                    }
                    parent = existing;
                }
                parent.Nodes.Add(new TreeNode("▤ " + entry.Label)
                {
                    Name = "item:" + entry.Id, Tag = entry,
                    ToolTipText = entry.Id + " · " + Path.GetFileName(entry.File)
                });
            }
            if (projectRoot.Nodes.Count > 0) MP_libraryTree.Nodes.Add(projectRoot);
            if (otherRoot.Nodes.Count > 0) MP_libraryTree.Nodes.Add(otherRoot);
            if (MP_libraryTree.Nodes.Count == 0)
                MP_libraryTree.Nodes.Add(new TreeNode(query.Length > 0 ? "没有匹配的结构" : "暂无结构文件"));
            MP_libraryTree.ExpandAll();
            MP_libraryCount.Text = $"共 {MP_libraryEntries.Count} 份结构 · 双击打开";
        }
        finally { MP_libraryTree.EndUpdate(); }
        MP_ShowLibraryPreview(null);
    }

    private void MP_ShowLibraryPreview(MP_LibraryEntry entry)
    {
        Image old = MP_libraryPreview.Image;
        MP_libraryPreview.Image = null;
        old?.Dispose();
        if (entry == null)
        {
            MP_libraryDetails.Text = "单击结构预览 PNG，双击直接打开。\n优先读取仓库 structures/ 目录。";
            return;
        }
        try
        {
            string file = Path.GetFullPath(entry.File);
            string basename = Path.GetFileNameWithoutExtension(file);
            if (basename.EndsWith(".structure", StringComparison.OrdinalIgnoreCase))
                basename = basename[..^".structure".Length];
            string folder = Path.GetDirectoryName(file)!;
            string previewFile = new[]
            {
                Path.Combine(folder, basename + ".png"),
                Path.Combine(folder, basename + "_preview.png"),
                Path.Combine(folder, basename + ".preview.png")
            }.FirstOrDefault(File.Exists);
            if (previewFile == null)
            {
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file + "|" + entry.Id)))[..24];
                string cacheDirectory = Path.Combine(Program.DataDirectory, "PreviewCache");
                Directory.CreateDirectory(cacheDirectory);
                string cacheFile = Path.Combine(cacheDirectory, key + ".png");
                if (!File.Exists(cacheFile) || File.GetLastWriteTimeUtc(cacheFile) < File.GetLastWriteTimeUtc(file))
                {
                    if (catalog == null) throw new InvalidOperationException("请先加载 RimWorld 游戏素材。");
                    StructureDocument doc = StructureDocument.Load(file, catalog, entry.Id);
                    using var renderer = new StructureCanvas
                    {
                        Catalog = catalog, Document = doc, AsyncTextures = false,
                        ShowGrid = false, ShowProximityRanges = false
                    };
                    using Bitmap snapshot = renderer.ExportPreview(420);
                    snapshot.Save(cacheFile, ImageFormat.Png);
                }
                previewFile = cacheFile;
            }
            using (Image image = Image.FromFile(previewFile))
                MP_libraryPreview.Image = new Bitmap(image); // release the source PNG
            string kind = entry.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "JSON 工程" : "XML";
            MP_libraryDetails.Text = $"{entry.Label}\n{entry.Id}\n{kind}" + (entry.HasXml ? " · 含 XML" : "");
        }
        catch (Exception ex)
        {
            MP_libraryDetails.Text = $"{entry.Label}\n预览不可用：{ex.Message}";
        }
    }
    private void MP_RenameLibraryEntry(MP_LibraryEntry entry)
    {
        namingSwitchDelay.Stop();
        CommitInspectorEdit();
        CommitMetadata();
        EnsureWorkspaceDocument();
        CaptureWorkspaceDocument();

        var allDefinitions = LibraryDefinitions();
        string[] relatedXml = allDefinitions
            .Where(x => x.Id == entry.Id && x.File.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.File).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var original = MP_StructureRenamer.Prepare(entry.File, entry.Id, entry.Id, entry.Label, relatedXml);

        using Form dialog = new()
        {
            Text = "重命名结构 · 同步关联 XML",
            Font = Font, ForeColor = text, BackColor = surface,
            Size = new Size(660, 390), MinimumSize = new Size(540, 330),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Padding = new Padding(14, 12, 14, 10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 24, 30, 24, 30, 34, 0, 42 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));

        Label caption(string value) => new()
        {
            Text = value, Dock = DockStyle.Fill,
            ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft
        };
        TextBox field(string value) => new()
        {
            Text = value, Dock = DockStyle.Fill, BackColor = Color.FromArgb(49, 58, 64),
            ForeColor = text, BorderStyle = BorderStyle.FixedSingle
        };

        var nameField = field(entry.Label);
        var idField = field(entry.Id);
        layout.Controls.Add(caption("显示名称（结构库中的名称及 XML label）"), 0, 0);
        layout.Controls.Add(nameField, 0, 1);
        layout.Controls.Add(caption("结构标识 DefName（同时更新 JSON/XML 及对应文件名）"), 0, 2);
        layout.Controls.Add(idField, 0, 3);
        layout.Controls.Add(caption($"关联 XML（找到 {original.XmlPaths.Count} 份，将逐个修改其中的目标 DefName）"), 0, 4);
        var paths = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(29, 35, 39),
            ForeColor = muted, Text = original.XmlPaths.Count == 0
                ? "尚未找到已导出的 XML。重命名 JSON 后，下次保存仍使用配置的 XML 输出目录。"
                : string.Join(Environment.NewLine, original.XmlPaths)
        };
        layout.Controls.Add(paths, 0, 5);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 5, 0, 0)
        };
        Button confirm = MakeButton("同步重命名", 0, 0, 112);
        Button cancel = MakeButton("取消", 0, 0, 80);
        confirm.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 6);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string nextId = idField.Text.Trim();
        string nextLabel = nameField.Text.Trim();
        if (nextId == entry.Id && nextLabel == entry.Label) return;
        if (nextId != entry.Id && allDefinitions.Any(x => x.Id.Equals(nextId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("结构库已经存在该 DefName，不能覆盖现有结构：" + nextId);

        var plan = MP_StructureRenamer.Prepare(entry.File, entry.Id, nextId, nextLabel, relatedXml);
        var touched = documents.Where(s => s.File != null
            && plan.Changes.Any(c => SamePath(c.OldPath, s.File))).ToList();
        if (touched.Any(s => s.Dirty || s == activeDocument && dirty))
            throw new InvalidOperationException("关联文件正在工作栏中编辑且尚未保存，请先保存后再重命名。");
        var impacted = touched.Where(s => s.Document.DefName == entry.Id).ToList();

        string backup = MP_StructureRenamer.Apply(plan);
        foreach (WorkspaceDocument session in impacted)
        {
            session.Document.DefName = nextId;
            session.Document.Label = nextLabel;
            session.File = plan.NewPathFor(session.File);
            if (session.SourceFile != null) session.SourceFile = plan.NewPathFor(session.SourceFile);
            if (session.LastXmlPath != null) session.LastXmlPath = plan.NewPathFor(session.LastXmlPath);
            session.SourceId = nextId;
            session.LastSavedId = nextId;
            session.Undo = Array.Empty<StructureDocument>();
            session.Redo = Array.Empty<StructureDocument>();
        }
        // All paths in the Recent and Open Documents settings must follow the
        // rename, otherwise startup could re-open the old deleted filename.
        Program.Paths.RecentDocuments = Program.Paths.RecentDocuments
            .Select(plan.NewPathFor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Program.Paths.LastDocument != null)
            Program.Paths.LastDocument = plan.NewPathFor(Program.Paths.LastDocument);
        foreach (WorkspaceReference reference in Program.Paths.OpenDocuments)
        {
            reference.File = plan.NewPathFor(reference.File);
            if (reference.Definition == entry.Id
                && impacted.Any(s => SamePath(s.File, reference.File)))
                reference.Definition = nextId;
        }

        if (impacted.Contains(activeDocument))
        {
            currentFile = activeDocument.File;
            document = activeDocument.Document;
            dirty = false;
            undo.Clear(); redo.Clear();
            RefreshMetadata();
            canvas.Invalidate();
        }
        RefreshDocumentTabs();
        PersistWorkspace();

        MP_librarySearch.Clear();
        MP_ReloadLibrary();
        MP_SelectRenamedEntry(nextId);
        status.Text = $"已重命名 {entry.Id} → {nextId} · 同步 XML {plan.XmlPaths.Count} 份（原文件已备份）";
    }

    private void MP_SelectRenamedEntry(string id)
    {
        TreeNode find(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is MP_LibraryEntry entry && entry.Id == id) return node;
                TreeNode nested = find(node.Nodes);
                if (nested != null) return nested;
            }
            return null;
        }
        TreeNode selected = find(MP_libraryTree.Nodes);
        if (selected != null)
        {
            selected.EnsureVisible();
            MP_libraryTree.SelectedNode = selected;
        }
    }

}
