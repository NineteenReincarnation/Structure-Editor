using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StructureEditor;

public sealed class CatalogSource
{
    public string PackageId { get; set; }
    public string Name { get; set; }
    public string Root { get; set; }
    public bool Official { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public string Display => Official ? Name : Name + " [" + PackageId + "]";
    public override string ToString() => Display;
}

internal static class ModSources
{
    internal static string DefaultConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config", "ModsConfig.xml");
    internal static List<string> ActivePackages(StudioPaths paths)
    {
        string file = string.IsNullOrWhiteSpace(paths.ModsConfigPath) ? DefaultConfig : paths.ModsConfigPath;
        if (!File.Exists(file)) return new();
        try { return XDocument.Load(file).Root?.Element("activeMods")?.Elements("li").Select(x => x.Value.Trim()).ToList() ?? new(); }
        catch { return new(); }
    }
    internal static List<CatalogSource> Installed(StudioPaths paths)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(paths.GameRoot))
        {
            roots.Add(Path.Combine(paths.GameRoot, "Mods"));
            string steamApps = Directory.GetParent(paths.GameRoot)?.Parent?.FullName;
            if (steamApps != null) roots.Add(Path.Combine(steamApps, "workshop", "content", "294100"));
        }
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam != null)
        {
            roots.Add(Path.Combine(steam, "steamapps", "workshop", "content", "294100"));
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraries)) foreach (Match m in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                roots.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "workshop", "content", "294100"));
        }
        List<CatalogSource> result = new();
        foreach (string root in roots.Where(Directory.Exists))
            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                var source = Read(folder); if (source != null && !result.Any(x => x.Root.Equals(source.Root, StringComparison.OrdinalIgnoreCase))) result.Add(source);
            }
        return result;
    }
    internal static CatalogSource Read(string folder)
    {
        try
        {
            string about = Path.Combine(folder, "About", "About.xml");
            if (!File.Exists(about)) return null;
            XElement data = XDocument.Load(about).Root;
            string package = ((string)data.Element("packageId"))?.Trim();
            if (string.IsNullOrEmpty(package)) return null;
            return new() { Root = Path.GetFullPath(folder), PackageId = package, Name = ((string)data.Element("name"))?.Trim() ?? Path.GetFileName(folder),
                Dependencies = data.Element("modDependencies")?.Elements("li").Select(e => ((string)e.Element("packageId"))?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new() };
        }
        catch { return null; }
    }
    internal static List<CatalogSource> Resolve(StudioPaths paths, List<string> warnings)
    {
        List<CatalogSource> sources = new();
        foreach (string name in new[] { "Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" })
        {
            string root = Path.Combine(paths.GameRoot, "Data", name);
            if (Directory.Exists(root)) sources.Add(new() { Root = root, Official = true, Name = name, PackageId = name == "Core" ? "ludeon.rimworld" : "ludeon.rimworld." + name.ToLowerInvariant() });
        }
        void Add(CatalogSource source)
        {
            if (source == null) return;
            int existing = sources.FindIndex(s => s.PackageId.Equals(source.PackageId, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) { if (!sources[existing].Official) sources[existing] = source; } else sources.Add(source);
        }
        if (paths.AutoLoadActiveMods) Add(Read(Path.Combine(paths.GameRoot, "Mods", "StructureFramework")));
        if (paths.AutoLoadActiveMods)
        {
            var installed = Installed(paths);
            foreach (string package in ActivePackages(paths).Where(id => !id.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)))
            {
                var source = installed.FirstOrDefault(s => s.PackageId.Equals(package, StringComparison.OrdinalIgnoreCase))
                    ?? installed.FirstOrDefault(s => (s.PackageId + "_steam").Equals(package, StringComparison.OrdinalIgnoreCase));
                if (source != null) Add(source); else warnings.Add("未找到已启用模组：" + package);
            }
        }
        foreach (string root in paths.ExtraMods.Concat(string.IsNullOrWhiteSpace(paths.ModRoot) ? Array.Empty<string>() : new[] { paths.ModRoot }).Where(Directory.Exists))
        {
            var source = Read(root);
            Add(source ?? new() { Root = Path.GetFullPath(root), PackageId = "", Name = Path.GetFileName(root) });
        }
        return sources;
    }
}
