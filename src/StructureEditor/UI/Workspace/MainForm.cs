using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Linq;
using StructureEditor.Compatibility;

namespace StructureEditor;

public partial class MainForm : Form
{
    private readonly StructureCanvas canvas = new();
    private readonly TextBox search = new(), idBox = new(), labelBox = new();
    private readonly ComboBox kindBox = new MP_DarkComboBox(), packBox = new MP_DarkComboBox(),
        groupBox = new MP_DarkComboBox(), scopeBox = new MP_DarkComboBox(),
        materialBox = new MP_DarkComboBox(), rotationBox = new MP_DarkComboBox(),
        dormancyBox = new MP_DarkComboBox();
    private readonly Label dormancyInfo = new();
    private readonly CheckBox dormant = new();
    private readonly Label selectedName = new(), selectedInfo = new(), libraryInfo = new();
    private readonly MP_ExportIndicator MP_exportStatusDot = new();
    private Label MP_rotationLabel, MP_materialLabel, MP_dormancyLabel;
    private readonly ListBox palette = new MP_MotionListBox();
    private MP_FluidAssetPalette MP_assetView;
    private readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Stack<StructureDocument> undo = new(), redo = new();
    private readonly Color surface = MP_StudioTheme.Panel,
        text = MP_StudioTheme.Text,
        muted = MP_StudioTheme.Secondary;
    private Catalog catalog;
    private StructureDocument document = new();
    private string currentFile;
    private bool dirty, updating, filtering, saveLocationChanged;
    private List<PlacedItem> copied = new();
    private CatalogItem shownItem;
    private bool MP_rebuildingPalette;

    public MainForm()
    {
        Text = "Structure Editor · 结构工坊";
        Font = MP_LiltTheme.CreateGdiFont(10);
        BackColor = surface; ForeColor = text;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Math.Min(1500, Screen.PrimaryScreen!.WorkingArea.Width - 80), Math.Min(940, Screen.PrimaryScreen.WorkingArea.Height - 60));
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 640); KeyPreview = true;
        BuildUi();
        MP_InitializeKeyboardNavigation();
        RefreshMetadata(); ShowSelection();
        InitializeBackgroundLoading();
        KeyDown += (_, e) =>
        {
            // List boxes and closed DropDownList controls can classify letters as input keys,
            // bypassing ProcessCmdKey. KeyPreview routes those same mode keys once at form level.
            if (!InputOwnsShortcut(ShortcutInput, e.KeyData) && HandleModeShortcut(e.KeyData))
            { e.Handled = true; e.SuppressKeyPress = true; }
        };
        canvas.BeforeChange += Snapshot;
        canvas.Changed += () =>
        {
            dirty = true;
            UpdateTitle();
            MP_ScheduleExportReview();
            MP_RefreshClusterCpSummary();
        };
        canvas.SelectionChanged += ShowSelection;
        canvas.Hint += message => status.Text = message;
        FormClosing += (_, e) => { try { if (!AskSaveWorkspace()) e.Cancel = true; } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } };
        Disposed += (_, _) =>
        {
            MP_exportReviewTimer.Stop(); MP_exportReviewTimer.Dispose();
        };
        Load += (_, _) => { UpdatePaletteDensity(); FitWorkingArea(); };
        Shown += (_, _) => { if (Program.IsGame(Program.Paths.GameRoot)) LoadCatalog(); else { canvas.Document=document; RefreshMetadata(); status.Text="请在 编辑 → 首选项 中选择 RimWorld 游戏目录，加载本机素材。"; } };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MP_WindowGlass.Apply(Handle);
    }

    private void BuildUi() => BuildWorkspaceUi();
    private Button MakeButton(string caption, int x, int y, int width)
    {
        Button button = new MP_AnimatedButton
        {
            Text = caption, Location = new Point(x, y), Size = new Size(width, 29),
            FlatStyle = FlatStyle.Flat, BackColor = MP_StudioTheme.Panel,
            ForeColor = text, UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = MP_StudioTheme.Accent;
        button.FlatAppearance.MouseOverBackColor = MP_StudioTheme.Hover;
        button.FlatAppearance.MouseDownBackColor = MP_StudioTheme.Active;
        return button;
    }
    private void AddTool(ToolStrip strip, string caption, Action action)
    {
        ToolStripButton button = new(caption)
        {
            ForeColor = text, DisplayStyle = ToolStripItemDisplayStyle.Text,
            Padding = new Padding(5, 1, 5, 1),
            Margin = new Padding(2, 0, 2, 0)
        };
        button.Click += (_, _) =>
        {
            MP_UiSound.Play(MP_UiSound.Cue.Confirm);
            Safe(action);
        };
        strip.Items.Add(button);
    }
    private void Safe(Action action)
    {
        try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "结构工坊", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async void LoadCatalog() => await LoadCatalogAsync();
    private string StructuresFolder => Directory.Exists(Program.StructureLibraryFolder) ? Program.StructureLibraryFolder : Program.DefaultProjects;
    private string OutputFolder => string.IsNullOrWhiteSpace(Program.Paths.SaveDirectory) ? StructuresFolder : Program.Paths.SaveDirectory;
    private string FilterKind => kindBox.SelectedIndex is >= 0 and <= 2 ? new[] { "Building", "Pawn", "Terrain" }[kindBox.SelectedIndex] : null;
    private bool ClusterScope => scopeBox.SelectedIndex == 4;
}
