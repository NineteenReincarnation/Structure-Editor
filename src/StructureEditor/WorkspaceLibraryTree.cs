namespace StructureEditor;

public partial class MainForm
{
    private sealed record MP_LibraryEntry(string File, string Id, string Label);

    // A file browser rather than a long, flat ToolStrip menu. The source list is
    // rebuilt whenever the window is opened or Refresh is pressed.
    private void MP_OpenStructureLibrary()
    {
        using Form dialog = new()
        {
            Text = "结构库 · 浏览文件", Font = Font, StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(570, 420), Size = new Size(790, 650),
            BackColor = surface, ForeColor = text
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        Panel searchRow = new() { Dock = DockStyle.Fill };
        TextBox filter = new()
        {
            Dock = DockStyle.Fill, PlaceholderText = "搜索结构名称、DefName 或文件路径",
            BackColor = Color.FromArgb(49, 58, 64), ForeColor = text, BorderStyle = BorderStyle.FixedSingle
        };
        Button refresh = new()
        {
            Dock = DockStyle.Right, Width = 92, Text = "刷新", FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 62, 66), ForeColor = text
        };
        searchRow.Controls.Add(filter);
        searchRow.Controls.Add(refresh);
        layout.Controls.Add(searchRow, 0, 0);

        TreeView tree = new()
        {
            Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true,
            ShowNodeToolTips = true, ShowLines = true, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(30, 36, 41), ForeColor = text,
            LineColor = Color.FromArgb(97, 112, 120)
        };
        layout.Controls.Add(tree, 0, 1);

        Label details = new()
        {
            Dock = DockStyle.Fill, Text = "从左侧树状目录选择结构文件中的 Def。",
            ForeColor = muted, AutoEllipsis = true, Padding = new Padding(4, 7, 4, 0)
        };
        layout.Controls.Add(details, 0, 2);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
        };
        Button open = new() { Text = "打开结构", Width = 110, Enabled = false, DialogResult = DialogResult.None };
        Button cancel = new() { Text = "取消", Width = 88, DialogResult = DialogResult.Cancel };
        foreach (Button button in new[] { open, cancel })
        {
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(51, 62, 66);
            button.ForeColor = text;
            buttons.Controls.Add(button);
        }
        layout.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(layout);
        dialog.CancelButton = cancel;

        List<MP_LibraryEntry> entries = new();
        MP_LibraryEntry selected = null;
        void Select(TreeNode node)
        {
            selected = node?.Tag as MP_LibraryEntry;
            open.Enabled = selected != null;
            details.Text = selected == null
                ? "展开文件夹与 XML / JSON 文件，然后选择一个结构。"
                : $"{selected.Label}  ·  {selected.Id}\n{selected.File}";
        }
        void Populate()
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                string query = filter.Text.Trim();
                var visible = entries.Where(e => query.Length == 0
                    || e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.File.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string[] sourceFolders = StructureFolders().Append(Program.DefaultProjects).Append(desktop)
                    .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Length).ToArray();
                Dictionary<string, TreeNode> rootNodes = new(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, TreeNode> fileNodes = new(StringComparer.OrdinalIgnoreCase);
                foreach (MP_LibraryEntry entry in visible.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Label))
                {
                    string file = Path.GetFullPath(entry.File);
                    string directory = Path.GetDirectoryName(file)!;
                    string root = sourceFolders.FirstOrDefault(dir =>
                        directory.Equals(dir, StringComparison.OrdinalIgnoreCase)
                        || directory.StartsWith(Path.TrimEndingDirectorySeparator(dir) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase)) ?? directory;
                    if (!rootNodes.TryGetValue(root, out TreeNode parent))
                    {
                        string title = Path.GetFileName(root);
                        if (title.Equals("Structures", StringComparison.OrdinalIgnoreCase))
                            title = Path.GetFileName(Path.GetDirectoryName(root)) + " / Structures";
                        parent = new TreeNode("▸ " + title) { ToolTipText = root };
                        rootNodes.Add(root, parent);
                        tree.Nodes.Add(parent);
                    }
                    string relative = Path.GetRelativePath(root, directory);
                    if (relative != ".")
                    {
                        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "."))
                        {
                            TreeNode next = parent.Nodes.Cast<TreeNode>()
                                .FirstOrDefault(n => n.Name == "folder:" + segment);
                            if (next == null)
                            {
                                next = new TreeNode("▸ " + segment) { Name = "folder:" + segment };
                                parent.Nodes.Add(next);
                            }
                            parent = next;
                        }
                    }
                    if (!fileNodes.TryGetValue(file, out TreeNode fileNode))
                    {
                        fileNode = new TreeNode("▤ " + Path.GetFileName(file)) { ToolTipText = file };
                        fileNodes.Add(file, fileNode);
                        parent.Nodes.Add(fileNode);
                    }
                    fileNode.Nodes.Add(new TreeNode(entry.Label + "  ·  " + entry.Id)
                    {
                        Tag = entry, ToolTipText = entry.File
                    });
                }
                if (tree.Nodes.Count == 0)
                    tree.Nodes.Add(new TreeNode(query.Length == 0 ? "暂无可用结构" : "没有匹配的结构"));
                foreach (TreeNode node in tree.Nodes) node.Expand();
                if (query.Length > 0) tree.ExpandAll();
            }
            finally { tree.EndUpdate(); }
            Select(null);
        }
        void Reload()
        {
            entries = LibraryDefinitions().Select(x => new MP_LibraryEntry(x.File, x.Id, x.Label)).ToList();
            Populate();
            details.Text = $"找到 {entries.Count} 个结构定义 · 可展开目录选择或搜索";
        }
        tree.AfterSelect += (_, e) => Select(e.Node);
        tree.NodeMouseDoubleClick += (_, e) =>
        {
            Select(e.Node);
            if (selected != null) dialog.DialogResult = DialogResult.OK;
        };
        filter.TextChanged += (_, _) => Populate();
        refresh.Click += (_, _) => Safe(Reload);
        open.Click += (_, _) =>
        {
            if (selected != null) dialog.DialogResult = DialogResult.OK;
        };
        Reload();
        if (dialog.ShowDialog(this) == DialogResult.OK && selected != null)
            OpenWorkspaceFile(selected.File, selected.Id);
    }
}
