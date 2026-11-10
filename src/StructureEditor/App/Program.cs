using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Win32;

namespace StructureEditor;

internal static class Program
{
    public static StudioPaths Paths;
    private static bool selfTest;
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StructureEditor");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string DefaultProjects => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Structure Editor");
    // Prefer the repository's editable structures/ folder when running from app/.
    // A standalone package also supports app/structures/ as a fallback.
    public static string StructureLibraryFolder
    {
        get
        {
            var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
            if (baseDir.Name.Equals("app", StringComparison.OrdinalIgnoreCase)
                && baseDir.Parent is not null)
            {
                string repoFolder = Path.Combine(baseDir.Parent.FullName, "structures");
                if (Directory.Exists(repoFolder)) return repoFolder;
            }
            for (DirectoryInfo current = baseDir; current is not null; current = current.Parent)
            {
                string candidate = Path.Combine(current.FullName, "structures");
                if (Directory.Exists(candidate)) return candidate;
            }
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "structures"));
        }
    }
    public static string TestDirectory => Path.Combine(DataDirectory, "Validation");
    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(DataDirectory);
        try { Paths = File.Exists(SettingsFile) ? JsonSerializer.Deserialize<StudioPaths>(File.ReadAllText(SettingsFile)) ?? new() : new(); }
        catch { Paths = new(); }
        if (string.IsNullOrWhiteSpace(Paths.GameRoot)) Paths.GameRoot = FindGame();
        int gameArgument = Array.IndexOf(args, "--game");
        if (gameArgument >= 0 && gameArgument + 1 < args.Length) Paths.GameRoot = args[gameArgument + 1];
        selfTest = args.Contains("--self-test");
        if (args.Contains("--core-only")) { Paths.ModRoot = null; Paths.ExtraMods.Clear(); Paths.LastDocument = null; Paths.AutoLoadActiveMods = false; }
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string file = Path.Combine(Paths.GameRoot ?? "", name.Name + ".dll");
            return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
        };
        ApplicationConfiguration.Initialize();
        try
        {
            int brandingArgument = Array.IndexOf(args, "--prepare-branding");
            if (brandingArgument >= 0)
            {
                if (brandingArgument + 1 >= args.Length) throw new ArgumentException("--prepare-branding 需要一个输出目录。");
                int iconArgument = Array.IndexOf(args, "--icon-image");
                if (iconArgument >= 0 && iconArgument + 1 >= args.Length) throw new ArgumentException("--icon-image 需要一个 PNG 文件路径。");
                AppBranding.Prepare(Path.GetFullPath(args[brandingArgument + 1]), iconArgument < 0 ? null : Path.GetFullPath(args[iconArgument + 1])); return;
            }
            if (args.Contains("--catalog-cache-test"))
            {
                MP_CatalogStartupCacheChecks.Run();
                return;
            }
            if (args.Contains("--ui-smoke-test"))
            {
                if (Math.Abs(MP_Motion.EaseOut(0f)) > 0.00001f ||
                    Math.Abs(MP_Motion.EaseOut(1f) - 1f) > 0.00001f ||
                    Math.Abs(MP_Motion.EaseOut(.5f) - .875f) > 0.00001f ||
                    MP_Motion.Blend(Color.Black, Color.White, 1f) != Color.FromArgb(255, 255, 255))
                    throw new InvalidOperationException("Motion easing/color contract failed.");
                using var form = new MainForm();
                form.CreateControl();
                static IEnumerable<Control> Descendants(Control parent)
                {
                    foreach (Control child in parent.Controls)
                    {
                        yield return child;
                        foreach (Control descendant in Descendants(child)) yield return descendant;
                    }
                }
                var wpfHosts = Descendants(form)
                    .OfType<System.Windows.Forms.Integration.ElementHost>().ToArray();
                if (wpfHosts.Count(host => host.Child is MP_CpMotionView) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidAssetPalette) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidStructureLibrary) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidCpSummary) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidDocumentBar) != 1
                    || wpfHosts.Count(host => host.Child is MP_FluidTabBar) != 2)
                    throw new InvalidOperationException("WPF editor presentation hosts are missing.");
                // Template realization must have a public zero-argument
                // constructor so virtualized lists work after catalog load.
                _ = new MP_FluidAssetRow();
                // 278px split panel at 150% DPI leaves about 185 DIPs.
                // Every navigation tab must remain reachable at that width.
                double[] fitted = MP_TabLayout.Fit(new[] { 74d, 74d, 86d }, 185d);
                if (fitted.Length != 3 || fitted.Any(w => w < 55)
                    || fitted.Sum() > 185.01d)
                    throw new InvalidOperationException("WPF navigation tabs overflow narrow panels.");
                var pages = Descendants(form).OfType<TabPage>().ToArray();
                if (pages.Length != 5 || !pages.Any(page => page.Text == "集群 CP")
                    || pages.Any(page => page.Parent is not TabControl))
                    throw new InvalidOperationException("Workspace tab pages were not attached to TabControls.");
                var tabs = Descendants(form).OfType<MP_SectionTabs>().ToArray();
                if (tabs.Length != 2 || tabs.Any(tab => tab.SelectedIndex < 0)
                    || !pages.Any(page => page.Text == "工程")
                    || pages.Any(page => page.Text is "导出" or "结构" or "操作"))
                    throw new InvalidOperationException("Workspace section tabs failed to initialize.");
                var documentStrip = (ToolStrip)typeof(MainForm).GetField("documentBar",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(form)!;
                var documentHost = (Panel)typeof(MainForm).GetField("MP_documentHost",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(form)!;
                if (documentStrip.Parent != documentHost
                    || documentStrip.Items.Cast<ToolStripItem>().Any(item =>
                        item.Text is "新建" or "打开…"))
                    throw new InvalidOperationException("Document tabs must be hosted separately from New/Open.");
                if (Descendants(form).OfType<ToolStrip>()
                    .SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                    .Count(item => item.Text == "新建") != 1)
                    throw new InvalidOperationException("New/Open quick actions must not be duplicated.");
                return;
            }
            if (args.Contains("--self-test")) { StudioChecks.Run(Paths, args.Contains("--turret-explosions")); return; }
            Application.Run(new MainForm());
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(DataDirectory, "error.log"), e.ToString());
            if (args.Contains("--self-test") || args.Contains("--ui-smoke-test") || args.Contains("--catalog-cache-test") || args.Contains("--prepare-branding")) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            else MessageBox.Show(e.Message + "\n\n详细信息：" + Path.Combine(DataDirectory,"error.log"), "Structure Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    public static void SavePaths()
    {
        if (selfTest) return;
        Directory.CreateDirectory(DataDirectory);
        string temp=SettingsFile+".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(Paths, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp,SettingsFile,true);
    }
    public static bool IsGame(string path) => !string.IsNullOrEmpty(path) && Directory.Exists(Path.Combine(path,"Data","Core","Defs"));
    private static string FindGame()
    {
        List<string> roots=new();
        string steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string;
        if(steam!=null)
        {
            roots.Add(steam);
            string libraries=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            if(File.Exists(libraries)) foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(libraries),"\"path\"\\s+\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\","\\"));
        }
        return roots.Select(p=>Path.Combine(p,"steamapps","common","RimWorld")).FirstOrDefault(IsGame);
    }
}
public class StudioPaths
{
    public List<WorkspaceReference> OpenDocuments { get; set; } = new();
    public bool AutoLoadActiveMods { get; set; } = true;
    public string ModsConfigPath { get; set; }
    public string GameRoot { get; set; }
    // Read old preferences without requiring the old mod or exposing it as a built-in dependency.
    public string ModRoot { get; set; }
    public List<string> ExtraMods { get; set; } = new();
    public string SaveDirectory { get; set; }
    public string LastDocument { get; set; }
    public string LastDefinition { get; set; }
    public bool UseRuntimeCatalog { get; set; } = true;
    public List<string> Favorites { get; set; } = new();
    public List<string> RecentAssets { get; set; } = new();
    public List<string> RecentDocuments { get; set; } = new();
    // Local-only exact paths for manual XML exports. Do not embed machine paths in shared projects.
    public Dictionary<string, string> MP_XmlExportPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
