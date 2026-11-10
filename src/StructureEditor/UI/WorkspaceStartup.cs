using System.Diagnostics;
using StructureEditor.Compatibility;

namespace StructureEditor;

public partial class MainForm
{
    private CancellationTokenSource catalogLoad;
    private readonly System.Windows.Forms.Timer textureRefresh = new() { Interval = 80 };
    private int pendingTextureRefresh;
    private bool asyncThumbnails;
    private Control libraryFilters;
    internal bool IsLoadingCatalog { get; private set; }
    internal long CatalogLoadMilliseconds { get; private set; }

    private void InitializeBackgroundLoading()
    {
        textureRefresh.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref pendingTextureRefresh, 0) == 0) return;
            palette.Invalidate(); MP_cpMotionView?.RefreshImages(); canvas.RefreshTextures();
        };
    }
    private void TextureArrived() => Interlocked.Exchange(ref pendingTextureRefresh, 1);

    internal async Task LoadCatalogAsync()
    {
        catalogLoad?.Cancel(); catalogLoad?.Dispose(); catalogLoad = new();
        var cancellation = catalogLoad.Token;
        var paths = new StudioPaths { GameRoot = Program.Paths.GameRoot, ModRoot = Program.Paths.ModRoot, ExtraMods = new(Program.Paths.ExtraMods), AutoLoadActiveMods = Program.Paths.AutoLoadActiveMods, ModsConfigPath = Program.Paths.ModsConfigPath, UseRuntimeCatalog = Program.Paths.UseRuntimeCatalog };
        var originalDocument = document;
        bool restore = canvas.Document == null && currentFile == null && !dirty;
        string initial = Program.Paths.LastDocument;
        IsLoadingCatalog = true; palette.Enabled = false; if (libraryFilters != null) libraryFilters.Enabled = false;
        libraryInfo.Text = "正在加载素材…";
        status.Text = "正在后台读取本机素材…";
        var timer = Stopwatch.StartNew();
        IProgress<string> progress = new Progress<string>(message => { if (!IsDisposed && !Disposing && !cancellation.IsCancellationRequested) status.Text = message; });
        Catalog loaded = null;
        try
        {
            var result = await Task.Run(() =>
            {
                Catalog source = new();
                try
                {
                    source.Load(paths, progress, cancellation);
                    StructureDocument restored = null; string warning = null;
                    string file = initial;
                    if (restore && !string.IsNullOrWhiteSpace(file))
                    {
                        if (!File.Exists(file) && Path.GetFileNameWithoutExtension(file) == MP_ClusterNaming.LegacyStarterDefName)
                            file = Path.Combine(Path.GetDirectoryName(file)!, MP_ClusterNaming.DefaultDefName + ".xml");
                        if (File.Exists(file)) try { restored = StructureDocument.Load(file,source,Program.Paths.LastDefinition); } catch (Exception ex) { warning = "上次工程未恢复：" + ex.Message; }
                    }
                    // Return the loaded catalog immediately. Thumbnail decoding now
                    // happens on demand via TextureLibrary.Request, not during startup.
                    return (Source: source, Restored: restored, File: file, Warning: warning);
                }
                catch { source.Dispose(); throw; }
            }, cancellation);
            loaded = result.Source;
            if (IsDisposed || Disposing || cancellation.IsCancellationRequested) { loaded.Dispose(); loaded = null; return; }
            var previous = catalog;
            if (previous?.Textures != null) previous.Textures.TextureReady -= TextureArrived;
            catalog = loaded; loaded = null; asyncThumbnails = true;
            catalog.Textures.TextureReady += TextureArrived;
            canvas.AsyncTextures = true; canvas.Catalog = catalog; ApplyBranding(catalog.Textures);
            if (!borrowedCatalog) previous?.Dispose();
            bool restoreWorkspace = restore && ReferenceEquals(originalDocument, document) && currentFile == null && !dirty;
            if (restoreWorkspace && result.Restored != null)
            { document = result.Restored; currentFile = result.File; }
            filtering = true;
            try
            {
                string pack = packBox.SelectedIndex > 0 ? packBox.SelectedItem?.ToString() : null;
                packBox.Items.Clear(); packBox.Items.Add("所有来源");
                foreach (string name in catalog.Items.Select(i => i.Pack).Distinct().OrderBy(p => p)) packBox.Items.Add(name);
                packBox.SelectedIndex = pack != null && packBox.Items.Contains(pack) ? packBox.Items.IndexOf(pack) : 0;
            }
            finally { filtering = false; }
            canvas.Document = document; RefreshMetadata(); RefreshPacks(); RefreshGroups(); FilterPalette(); MP_RefreshClusterCpCatalog(); canvas.CenterView();
            if (restoreWorkspace) RestoreWorkspace();
            textureRefresh.Start(); CatalogLoadMilliseconds = timer.ElapsedMilliseconds;
            status.Text = result.Warning ?? $"已读取 {catalog.Items.Count} 项素材 · " +
                (catalog.MP_UsedWarmCache ? "缓存加速" : "已更新缓存") + " · 预览按需加载";
            File.WriteAllText(Path.Combine(Program.DataDirectory, "startup.log"),
                $"Catalog ready: {CatalogLoadMilliseconds} ms; cache hit: {catalog.MP_UsedWarmCache}; " +
                $"cache check: {catalog.MP_CacheCheckMilliseconds} ms; " +
                $"texture source index: {catalog.MP_TextureIndexMilliseconds} ms; " +
                $"definition resolution: {catalog.MP_DefParseMilliseconds} ms; " +
                $"items: {catalog.Items.Count}; texture sources: {catalog.Textures.SourceCount}.\n");
            if (catalog.Warnings.Count > 0) File.WriteAllLines(Path.Combine(Program.DataDirectory, "catalog-warnings.log"), catalog.Warnings);
        }
        catch (OperationCanceledException) { loaded?.Dispose(); }
        catch (Exception ex)
        {
            loaded?.Dispose();
            if (!IsDisposed && !Disposing && !cancellation.IsCancellationRequested)
            { status.Text = "素材加载失败：" + ex.Message; libraryInfo.Text = "加载失败，请检查游戏目录。"; File.WriteAllText(Path.Combine(Program.DataDirectory, "catalog-error.log"), ex.ToString()); }
        }
        finally
        {
            if (!IsDisposed && !Disposing && !cancellation.IsCancellationRequested)
            { IsLoadingCatalog = false; palette.Enabled = catalog != null; if (libraryFilters != null) libraryFilters.Enabled = true; }
        }
    }

    private void FitWorkingArea(bool center = true)
    {
        if (WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        int margin = Math.Max(16, DeviceDpi / 6);
        var available = new Size(Math.Max(300, area.Width - margin * 2), Math.Max(300, area.Height - margin * 2));
        MinimumSize = new(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        Location = center
            ? new(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2))
            : new(Math.Clamp(Left, area.Left + margin, area.Right - margin - Width), Math.Clamp(Top, area.Top + margin, area.Bottom - margin - Height));
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) { UpdatePaletteDensity(); FitWorkingArea(false); } }));
    }
    private void UpdatePaletteDensity()
    {
        palette.ItemHeight = Math.Max(48, (int)Math.Round(78 * DeviceDpi / 96f));
        if (MP_cpCatalogList != null)
            MP_cpCatalogList.ItemHeight = Math.Max(48, (int)Math.Round(52 * DeviceDpi / 96f));
    }
    private void StopBackgroundLoading()
    {
        var loading = catalogLoad; catalogLoad = null; loading?.Cancel(); loading?.Dispose(); textureRefresh.Stop(); textureRefresh.Dispose();
        if (catalog?.Textures != null) catalog.Textures.TextureReady -= TextureArrived;
    }
}
