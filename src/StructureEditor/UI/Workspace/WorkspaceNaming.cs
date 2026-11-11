using StructureEditor.Compatibility;

namespace StructureEditor;

public partial class MainForm
{
    private ComboBox namingCore, namingTier;
    private NumericUpDown namingVariant;
    private TextBox namingLabel, namingId;
    private Label namingStatus;
    private Button applyNaming;
    private GroupBox namingPanel;
    private Button MP_clusterNamingToggle;
    private bool? MP_clusterNamingExpanded;

    private Control BuildClusterNamingToggle()
    {
        MP_clusterNamingToggle = MakeButton("集群命名工具 ▸", 0, 0, 140);
        MP_clusterNamingToggle.Dock = DockStyle.Top;
        MP_clusterNamingToggle.Click += (_, _) =>
        {
            MP_clusterNamingExpanded = !namingPanel.Visible;
            LoadNamingFields();
        };
        return MP_clusterNamingToggle;
    }
    private Label structureIdTitle, structureLabelTitle;
    private bool loadingNaming;
    private readonly System.Windows.Forms.Timer namingSwitchDelay = new() { Interval = 220 };
    private readonly List<string> namingIds = new();

    private Control BuildClusterNamingPanel()
    {
        namingPanel = new() { Text = "集群命名 · MP", ForeColor = text, AutoSize = true, Dock = DockStyle.Top, Padding = new(8) };
        TableLayoutPanel layout = new() { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 55)); layout.ColumnStyles.Add(new(SizeType.Percent, 45));
        namingCore = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        namingCore.Items.AddRange(MPClusterPresets.All); namingCore.SelectedIndex = 0;
        namingTier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        for (int tier = 1; tier <= 5; tier++) namingTier.Items.Add(MP_ClusterNaming.TierName(tier).Replace("：", " ").Replace("集群", ""));
        namingTier.SelectedIndex = 0;
        namingVariant = new() { Minimum = 1, Maximum = 99, Value = 1, Dock = DockStyle.Fill };
        var next = MakeButton("下个空号", 0, 0, 110); next.Dock = DockStyle.Fill;
        next.Click += (_, _) => Safe(() => { ReloadNamingIds(); namingVariant.Value = MP_ClusterNaming.NextVariant(namingIds, ((MPClusterPreset)namingCore.SelectedItem).Core, namingTier.SelectedIndex + 1); NamingChoiceChanged(); });
        namingLabel = new() { ReadOnly = true, Dock = DockStyle.Fill };
        namingId = new() { ReadOnly = true, Multiline = true, WordWrap = true, Height = 56, Dock = DockStyle.Fill, Font = new(Font.FontFamily, 8) };
        namingStatus = new() { AutoSize = true, ForeColor = muted, Dock = DockStyle.Fill };
        applyNaming = MakeButton("重新识别", 0, 0, 100); applyNaming.Dock = DockStyle.Fill;
        var copy = MakeButton("复制名称", 0, 0, 100); copy.Dock = DockStyle.Fill;
        copy.Click += (_, _) => Safe(() => { Clipboard.SetText(namingLabel.Text + Environment.NewLine + namingId.Text); status.Text = "已复制显示名和 DefName"; });
        layout.Controls.Add(namingCore, 0, 0); layout.Controls.Add(namingTier, 1, 0);
        layout.Controls.Add(namingVariant, 0, 1); layout.Controls.Add(next, 1, 1);
        layout.Controls.Add(namingLabel, 0, 2); layout.SetColumnSpan(namingLabel, 2);
        layout.Controls.Add(namingId, 0, 3); layout.SetColumnSpan(namingId, 2);
        layout.Controls.Add(namingStatus, 0, 4); layout.SetColumnSpan(namingStatus, 2);
        layout.Controls.Add(applyNaming, 0, 5); layout.Controls.Add(copy, 1, 5);
        foreach (Control control in new Control[] { namingCore, namingTier, namingVariant, namingLabel, namingId }) { control.BackColor = Color.FromArgb(49, 58, 64); control.ForeColor = text; }
        namingCore.SelectedIndexChanged += (_, _) => NamingChoiceChanged(); namingTier.SelectedIndexChanged += (_, _) => NamingChoiceChanged(); namingVariant.ValueChanged += (_, _) => NamingChoiceChanged();
        namingSwitchDelay.Tick += (_, _) => { namingSwitchDelay.Stop(); Safe(ResolveNamingSelection); };
        namingPanel.Enter += (_, _) => Safe(() => { ReloadNamingIds(); RefreshNamingPreview(); });
        applyNaming.Click += (_, _) => Safe(ResolveNamingSelection);
        namingPanel.Controls.Add(layout); return namingPanel;
    }
    private void ReloadNamingIds()
    {
        namingIds.Clear(); namingIds.AddRange(LibraryDefinitions().Select(d => d.Id));
        namingIds.AddRange(documents.Select(s => s == activeDocument ? document.DefName : s.Document.DefName));
        namingIds.Add(document.DefName);
    }
    private void LoadNamingFields()
    {
        if (namingPanel == null) return;
        namingSwitchDelay.Stop();
        bool isNamedCluster = MP_ClusterNaming.TryParse(document.DefName, out _, out _, out _);
        namingPanel.Visible = MP_clusterNamingExpanded ?? isNamedCluster;
        if (MP_clusterNamingToggle != null)
            MP_clusterNamingToggle.Text = namingPanel.Visible ? "集群命名工具 ▾" : "集群命名工具 ▸";
        idBox.ReadOnly = labelBox.ReadOnly = isNamedCluster
            && MPClusterPresets.All.Any(p => document.DefName.StartsWith("MP_Cluster_" + p.Core + "_", StringComparison.Ordinal));
        if (structureIdTitle != null)
        { structureIdTitle.Visible = structureLabelTitle.Visible = idBox.Visible = labelBox.Visible = !idBox.ReadOnly; }
        if (!namingPanel.Visible) return;
        loadingNaming = true;
        try
        {
            if (MP_ClusterNaming.TryParse(document.DefName, out string core, out int tier, out int variant))
            {
                var preset = MPClusterPresets.All.FirstOrDefault(p => p.Core == core);
                if (preset != null) namingCore.SelectedItem = preset;
                namingTier.SelectedIndex = tier - 1; namingVariant.Value = variant;
            }
            else
            {
                var preset = MPClusterPresets.All.FirstOrDefault(p => document.Items.Any(i => i.DefName == p.Building));
                if (preset != null) namingCore.SelectedItem = preset;
            }
        }
        finally { loadingNaming = false; }
        RefreshNamingPreview();
    }
    private void RefreshNamingPreview()
    {
        if (loadingNaming || namingCore?.SelectedItem is not MPClusterPreset preset || namingTier.SelectedIndex < 0) return;
        int tier = namingTier.SelectedIndex + 1, variant = (int)namingVariant.Value;
        string label = MP_ClusterNaming.Label(preset.Label, tier, variant);
        string id = MP_ClusterNaming.DefName(preset.Core, tier, variant);
        string state = id != document.DefName ? "自动识别…"
            : currentFile == null ? "空草稿 · 未保存"
            : Path.GetExtension(currentFile).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? "JSON · 已加载" : "XML · 已加载";
        if (namingLabel.Text != label) namingLabel.Text = label;
        if (namingId.Text != id) namingId.Text = id;
        if (namingStatus.Text != state) namingStatus.Text = state;
        if (namingStatus.ForeColor != muted) namingStatus.ForeColor = muted;
        if (!applyNaming.Enabled) applyNaming.Enabled = true;
    }
    private void NamingChoiceChanged()
    {
        if (loadingNaming || updating) return;
        RefreshNamingPreview(); namingSwitchDelay.Stop(); namingSwitchDelay.Start();
    }
    private void ResolveNamingSelection()
    {
        try { ActivateNamedCluster(); }
        catch { LoadNamingFields(); throw; }
    }
    private void ActivateNamedCluster()
    {
        namingSwitchDelay.Stop();
        if (loadingNaming || namingCore.SelectedItem is not MPClusterPreset preset) return;
        // Resolve the requested identity before switching commits the previous document's controls.
        int tier = namingTier.SelectedIndex + 1, variant = (int)namingVariant.Value;
        string id = MP_ClusterNaming.DefName(preset.Core, tier, variant);
        EnsureWorkspaceDocument();
        var existing = documents.FirstOrDefault(s => (s == activeDocument ? document : s.Document).DefName == id);
        if (existing != null)
        {
            SwitchWorkspaceDocument(existing); LoadNamingFields(); return;
        }
        var library = LibraryDefinitions();
        var matches = library.Where(e => e.Id == id).ToList();
        if (matches.Count > 0)
        {
            // The editable project is authoritative when its exported XML also exists.
            var match = matches.OrderBy(e => MP_IsProjectStructure(e.File) ? 0 : 1)
                .ThenBy(e => e.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(e => Program.Paths.RecentDocuments.FindIndex(p => SamePath(p, e.File)) is int index && index >= 0 ? index : int.MaxValue)
                .ThenByDescending(e => File.GetLastWriteTimeUtc(e.File))
                .ThenBy(e => e.File, StringComparer.OrdinalIgnoreCase).First();
            OpenWorkspaceFile(match.File, id); return;
        }
        string xmlFolder = document.XmlExportDirectory;
        var sibling = library.FirstOrDefault(e => e.File.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && MP_ClusterNaming.TryParse(e.Id, out string core, out _, out _) && core == preset.Core);
        if (sibling.File != null) xmlFolder = Path.GetDirectoryName(sibling.File);
        AddWorkspaceDocument(new()
        {
            DefName = id, Label = MP_ClusterNaming.Label(preset.Label, tier, variant),
            ExportProfile = "Framework", XmlExportDirectory = xmlFolder
        }, prepared: true);
        status.Text = "空草稿 · 保存后创建文件";
    }
    private void CheckWorkspaceSaveTarget(string path)
    {
        EnsureWorkspaceDocument();
        if (documents.Any(s => s != activeDocument && (SamePath(s.File, path) || s.Document.DefName == document.DefName)))
            throw new InvalidOperationException("工作栏已有同名结构，请切换到对应文档，或分配新编号。");
        if (File.Exists(path) && !SamePath(path, currentFile) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && StructureDocument.Load(path).DefName != document.DefName)
            throw new InvalidOperationException("目标工程属于另一份结构，请更换文件。");
        // A naming tool is not a document category. Only generated cluster
        // identities use their preset naming/uniqueness constraints.
        if (!document.DefName.StartsWith("MP_Cluster_", StringComparison.Ordinal)) return;
        string error = MPClusterPresets.Validate(document.DefName, document.Label);
        if (error != null) throw new InvalidOperationException(error);
        string xmlTarget = StructureStorage.XmlTarget(document, path, OutputFolder);
        foreach (var entry in LibraryDefinitions().Where(e => e.Id == document.DefName))
        {
            bool source = document.DefName == activeDocument.SourceId && SamePath(entry.File, activeDocument.SourceFile);
            bool ownProject = SamePath(entry.File, currentFile) && currentFile?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true;
            bool pairedXml = activeDocument.SourceFile != null && document.DefName == activeDocument.SourceId && SamePath(entry.File, xmlTarget)
                || document.DefName == activeDocument.LastSavedId && SamePath(entry.File, activeDocument.LastXmlPath);
            if (!source && !ownProject && !pairedXml) throw new InvalidOperationException("编号已占用：" + entry.Label + "。请使用下一个空号。");
        }
    }
    private void CheckWorkspaceExportTarget(string xmlPath)
    {
        CheckWorkspaceSaveTarget(StructureStorage.ResolveTarget(currentFile, null, document.DefName, OutputFolder, saveLocationChanged));
        if (documents.Any(s => s != activeDocument && s.Document.DefName == document.DefName &&
            (SamePath(s.File, xmlPath) || SamePath(StructureStorage.XmlTarget(s.Document, s.File, s.SaveDirectory ?? OutputFolder), xmlPath))))
            throw new InvalidOperationException("输出文件正在另一文档中编辑。");
    }
}
