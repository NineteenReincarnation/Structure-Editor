namespace StructureEditor;

// Application data locations shared by persistence and export services.
// Consumers do not need to know about the Program entry point.
internal static class MP_DataDirectories
{
    internal static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StructureEditor");

    internal static string BackupDirectory => Path.Combine(DataDirectory, "Backups");
}
