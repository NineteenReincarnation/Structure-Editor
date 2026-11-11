namespace StructureEditor;

internal static class CatalogCollections
{
    private static readonly HashSet<string> common = new(StringComparer.Ordinal) { "Wall", "Door", "Autodoor", "Fence", "Sandbags", "Barricade", "Turret_MiniTurret", "Bed", "DoubleBed", "HospitalBed", "Table1x2c", "DiningChair", "Stool", "Shelf", "ShelfSmall", "ElectricStove", "FueledStove", "Battery", "SolarGenerator", "WoodFiredGenerator", "Heater", "Cooler", "StandingLamp", "SteelTile", "Concrete", "WoodPlankFloor", "PavedTile", "Bridge", "Soil", "MechCapsule", "MechAssembler", "Mech_Militor", "Mech_Scyther", "Mech_Lancer", "Colonist", "Villager" };
    internal static string Key(CatalogItem item) => item.Kind + ":" + item.DefName;
    internal static bool Matches(CatalogItem item, int scope, StudioPaths paths) => scope switch
    {
        1 => common.Contains(item.DefName), 2 => paths.Favorites.Contains(Key(item)), 3 => paths.RecentAssets.Contains(Key(item)), 5 => item.IsMod, 6 => !item.IsMod, _ => true
    };
}
