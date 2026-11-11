using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace StructureEditor;

// The structure library is WPF-first. Legacy TreeView controls and their
// duplicated selection / layout / native preview logic are no longer created.
public partial class MainForm
{
    private sealed record MP_LibraryEntry(string File, string Id, string Label, bool HasXml);
    private MP_SectionTabs MP_libraryTabs;
    private MP_FluidStructureLibrary MP_structureView;
    private List<MP_LibraryEntry> MP_libraryEntries = new();
    private bool MP_libraryLoaded;
    private readonly System.Windows.Forms.Timer MP_libraryPreviewDelay =
        new() { Interval = 130 };
    private MP_LibraryEntry MP_pendingLibraryPreview;
    private CancellationTokenSource MP_libraryPreviewCancellation = new();
    private readonly SemaphoreSlim MP_libraryPreviewGate = new(1, 1);

    private void MP_CancelLibraryPreview()
    {
        MP_libraryPreviewCancellation.Cancel();
        MP_libraryPreviewCancellation.Dispose();
        MP_libraryPreviewCancellation = new CancellationTokenSource();
    }

    private Control MP_CreateStructureLibraryPanel()
    {
        MP_structureView = new MP_FluidStructureLibrary(
            (file, id) =>
            {
                MP_CancelLibraryPreview();
                MP_pendingLibraryPreview = MP_libraryEntries.FirstOrDefault(item =>
                    item.Id == id && SamePath(item.File, file));
                MP_libraryPreviewDelay.Stop();
                MP_libraryPreviewDelay.Start();
            },
            (file, id) => Safe(() => OpenWorkspaceFile(file, id)),
            (file, id) =>
            {
                var entry = MP_libraryEntries.FirstOrDefault(item =>
                    item.Id == id && SamePath(item.File, file));
                if (entry != null) Safe(() => MP_RenameLibraryEntry(entry));
            },
            delta => MP_CycleLibraryPage(delta));
        MP_libraryPreviewDelay.Tick += (_, _) =>
        {
            MP_libraryPreviewDelay.Stop();
            if (!IsDisposed && !Disposing)
                _ = MP_ShowLibraryPreviewAsync(MP_pendingLibraryPreview);
        };
        Disposed += (_, _) =>
        {
            MP_libraryPreviewDelay.Stop();
            MP_libraryPreviewDelay.Dispose();
            MP_libraryPreviewCancellation.Cancel();
            MP_libraryPreviewCancellation.Dispose();
        };
        return new System.Windows.Forms.Integration.ElementHost
        {
            Child = MP_structureView, Dock = DockStyle.Fill,
            BackColor = surface, TabStop = true
        };
    }

    private void MP_ShowStructureLibrary()
    {
        MP_libraryTabs.SelectedIndex = 1;
        if (!MP_libraryLoaded) MP_ReloadLibrary();
        MP_structureView?.FocusSearch();
    }

    private static bool MP_IsProjectStructure(string file)
    {
        string directory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Program.StructureLibraryFolder));
        return Path.GetFullPath(file).StartsWith(directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private void MP_ReloadLibrary()
    {
        MP_libraryPreviewDelay.Stop();
        MP_CancelLibraryPreview();
        // One editable structure per DefName. Prefer JSON over exported XML.
        MP_libraryEntries = LibraryDefinitions()
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var chosen = group.OrderBy(item => MP_IsProjectStructure(item.File) ? 0 : 1)
                    .ThenBy(item =>
                        item.File.EndsWith(".structure.json", StringComparison.OrdinalIgnoreCase) ? 0 :
                        item.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenBy(item => item.File, StringComparer.OrdinalIgnoreCase).First();
                return new MP_LibraryEntry(chosen.File, chosen.Id, chosen.Label,
                    group.Any(item => item.File.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
            })
            .OrderBy(item => MP_IsProjectStructure(item.File) ? 0 : 1)
            .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        MP_libraryLoaded = true;
        MP_structureView?.SetEntries(MP_libraryEntries.Select(item =>
            new MP_StructureViewEntry(item.File, item.Id, item.Label, item.HasXml)).ToArray());
    }

    // Preview parsing/rendering and disk I/O run on a dedicated STA worker.
    // StructureCanvas is a WinForms control and must never be constructed on
    // an arbitrary MTA thread. One worker at a time prevents a fast sequence
    // of clicks from decoding many full previews concurrently.
    private async Task MP_ShowLibraryPreviewAsync(MP_LibraryEntry entry)
    {
        if (entry == null || MP_structureView == null) return;
        CancellationToken token = MP_libraryPreviewCancellation.Token;
        Catalog previewCatalog = catalog;
        bool acquired = false;
        try
        {
            await MP_libraryPreviewGate.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            byte[] bytes = await MP_RenderLibraryPreviewStaAsync(
                entry.File, entry.Id, previewCatalog, token);
            if (!token.IsCancellationRequested && !IsDisposed && !Disposing)
                MP_structureView?.SetPreviewBytes(entry.Id, bytes);
        }
        catch (OperationCanceledException) { /* A newer selection won. */ }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !IsDisposed && !Disposing)
                MP_structureView?.SetPreviewStatus(entry.Id, "无法生成预览");
        }
        finally
        {
            if (acquired) MP_libraryPreviewGate.Release();
        }
    }

    private static Task<byte[]> MP_RenderLibraryPreviewStaAsync(
        string path, string id, Catalog catalogSnapshot, CancellationToken token)
    {
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(MP_ReadOrGeneratePreview(
                    path, id, catalogSnapshot, token));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "MP_StructurePreview",
            Priority = ThreadPriority.BelowNormal
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static byte[] MP_ReadOrGeneratePreview(
        string input, string id, Catalog catalogSnapshot, CancellationToken token)
    {
        string file = Path.GetFullPath(input);
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
            string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(file + "|" + id)))[..24];
            string cacheFolder = Path.Combine(Program.DataDirectory, "PreviewCache");
            Directory.CreateDirectory(cacheFolder);
            string cacheFile = Path.Combine(cacheFolder, hash + ".png");
            if (!File.Exists(cacheFile) ||
                File.GetLastWriteTimeUtc(cacheFile) < File.GetLastWriteTimeUtc(file))
            {
                token.ThrowIfCancellationRequested();
                if (catalogSnapshot == null)
                    throw new InvalidOperationException("请先加载 RimWorld 游戏素材。");
                StructureDocument target = StructureDocument.Load(file, catalogSnapshot, id);
                token.ThrowIfCancellationRequested();
                using var renderer = new StructureCanvas
                {
                    Catalog = catalogSnapshot, Document = target, AsyncTextures = false,
                    ShowGrid = false, ShowProximityRanges = false
                };
                using Bitmap bitmap = renderer.ExportPreview(420);
                token.ThrowIfCancellationRequested();
                bitmap.Save(cacheFile, ImageFormat.Png);
            }
            previewFile = cacheFile;
        }
        token.ThrowIfCancellationRequested();
        return File.ReadAllBytes(previewFile);
    }

    private void MP_RenameLibraryEntry(MP_LibraryEntry entry)
    {
        namingSwitchDelay.Stop();
        CommitInspectorEdit();
        CommitMetadata();
        EnsureWorkspaceDocument();
        CaptureWorkspaceDocument();

        var allDefinitions = LibraryDefinitions();
        // Combine discovered exports with the last exact output path stored
        // locally for manually named or externally placed XML files.
        IEnumerable<string> tracked = documents
            .Where(s => s.Document.DefName == entry.Id && s.File != null && SamePath(s.File, entry.File))
            .Select(s => s.LastXmlPath)
            .Where(x => !string.IsNullOrWhiteSpace(x));
        if (Program.Paths.MP_XmlExportPaths != null
            && Program.Paths.MP_XmlExportPaths.TryGetValue(Path.GetFullPath(entry.File), out string localExport))
            tracked = tracked.Append(localExport);
        string[] relatedXml = allDefinitions
            .Where(x => x.Id == entry.Id && x.File.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.File).Concat(tracked)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        if (Program.Paths.MP_XmlExportPaths != null)
        {
            var revisedLinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Program.Paths.MP_XmlExportPaths)
                revisedLinks[plan.NewPathFor(pair.Key)] = plan.NewPathFor(pair.Value);
            Program.Paths.MP_XmlExportPaths = revisedLinks;
        }
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

        MP_structureView?.ClearSearch();
        MP_ReloadLibrary();
        MP_SelectRenamedEntry(nextId);
        status.Text = $"已重命名 {entry.Id} → {nextId} · 同步 XML {plan.XmlPaths.Count} 份（原文件已备份）";
    }


    private void MP_SelectRenamedEntry(string id)
        => MP_structureView?.SelectById(id);
}
