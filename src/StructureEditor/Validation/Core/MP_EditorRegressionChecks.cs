namespace StructureEditor;

// Offline checks run alongside the existing UI smoke test on Windows CI.
// Never touches the user's structure library, game assets, or preferences.
internal static class MP_EditorRegressionChecks
{
    internal static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "MP_EditorChecks_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            MP_DocumentHistoryChecks.Run();
            MP_CatalogPaletteQueryChecks.Run();
            var source = new StructureDocument
            {
                DefName = "MP_EditorSmoke",
                Label = "自动回归结构",
                AutoExportOnSave = false,
                Items = new()
                {
                    new() { Kind = "Building", DefName = "Wall", Stuff = "Steel",
                        X = -4, Z = 2, Rotation = 3, StartDormant = false },
                    new() { Kind = "Pawn", DefName = "Mech_Scyther",
                        X = 5, Z = -2, Rotation = 2, StartDormant = true },
                    new() { Kind = "Terrain", DefName = "MechanoidPlatform" }
                }
            };
            var copy = source.Copy();
            copy.Items[0].X = 42;
            if (source.Items[0].X != -4)
                throw new InvalidOperationException("Document copy mutated source.");
            string json = Path.Combine(folder, "case.structure.json");
            source.Save(json);
            Validate(StructureDocument.Load(json), "JSON");
            string xml = Path.Combine(folder, "case.xml");
            source.Save(xml);
            Validate(StructureDocument.Load(xml), "XML");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static void Validate(StructureDocument doc, string format)
    {
        if (doc.DefName != "MP_EditorSmoke"
            || doc.Label != "自动回归结构"
            || doc.Items.Count != 3
            || doc.Items[0].Stuff != "Steel"
            || doc.Items[0].X != -4
            || doc.Items[0].Z != 2
            || doc.Items[0].Rotation != 3
            || doc.Items[0].StartDormant != false
            || doc.Items[1].StartDormant != true
            || doc.Items[1].Rotation != 2
            || doc.Items[2].Kind != "Terrain")
            throw new InvalidOperationException(
                format + " document roundtrip lost object properties.");
    }
}
