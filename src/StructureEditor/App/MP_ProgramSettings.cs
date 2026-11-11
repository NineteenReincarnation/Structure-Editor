using System.Text.Json;

namespace StructureEditor;

// Persist settings at the same location and with the same JSON representation.
internal static partial class Program
{
    public static void SavePaths()
    {
        if (selfTest) return;
        Directory.CreateDirectory(DataDirectory);
        string temp=SettingsFile+".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(Paths, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp,SettingsFile,true);
    }
}
