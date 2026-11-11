namespace StructureEditor;

internal static class CatalogCollections
{
    private static readonly HashSet<string> common = new(StringComparer.Ordinal) { "Wall", "Door", "Autodoor", "Fence", "Sandbags", "Barricade", "Turret_MiniTurret", "Bed", "DoubleBed", "HospitalBed", "Table1x2c", "DiningChair", "Stool", "Shelf", "ShelfSmall", "ElectricStove", "FueledStove", "Battery", "SolarGenerator", "WoodFiredGenerator", "Heater", "Cooler", "StandingLamp", "SteelTile", "Concrete", "WoodPlankFloor", "PavedTile", "Bridge", "Soil", "MechCapsule", "MechAssembler", "Mech_Militor", "Mech_Scyther", "Mech_Lancer", "Colonist", "Villager" };
    internal static string Key(CatalogItem item) => item.Kind + ":" + item.DefName;
    // The catalog does not depend on Program, StudioPaths or a UI control.
    // Lists are provided by the caller without copying or changing scope rules.
    internal static bool Matches(CatalogItem item, int scope,
        ICollection<string> favorites, ICollection<string> recentAssets) => scope switch
    {
        1 => common.Contains(item.DefName),
        2 => favorites.Contains(Key(item)),
        3 => recentAssets.Contains(Key(item)),
        5 => item.IsMod,
        6 => !item.IsMod,
        _ => true
    };
}
