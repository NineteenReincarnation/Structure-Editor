using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Win32;

namespace StructureEditor;

internal static partial class Program
{
    public static StudioPaths Paths;
    private static bool selfTest;
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
            if (args.Contains("--ui-smoke-test")) { MP_UiSmokeChecks.Run(); return; }
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
}
