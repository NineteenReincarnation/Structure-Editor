using Microsoft.Win32;

namespace StructureEditor;

// Paths and local RimWorld discovery. No changes to lookup order or fallback.
internal static partial class Program
{
    public static readonly string DataDirectory = MP_DataDirectories.DataDirectory;
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
