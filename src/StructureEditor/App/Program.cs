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
            if (args.Contains("--self-test")) { StudioChecks.Run(Paths, args.Contains("--turret-explosions")); return; }
            Application.Run(new MainForm());
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(DataDirectory, "error.log"), e.ToString());
            if (args.Contains("--self-test") || args.Contains("--prepare-branding")) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
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
}
