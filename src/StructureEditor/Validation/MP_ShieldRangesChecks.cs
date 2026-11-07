using System.Xml.Linq;

namespace StructureEditor;

internal static class MP_ShieldRangesChecks
{
    internal static void Run(Action<bool, string> need)
    {
        static XElement Comp(string type, string radiusTag, string value) =>
            new("li", new XAttribute("Class", type), new XElement(radiusTag, value));
        var thing = new XElement("ThingDef",
            new XElement("comps",
                Comp("CompProperties_ProjectileInterceptor", "radius", "11.5"),
                Comp("OtherMod.CompProperties_ShieldEmitter", "shieldRadius", "16"),
                Comp("OtherMod.CompProperties_Explosive", "radius", "200")));
        var radii = MP_ShieldRanges.FromDef(thing);
        need(radii.Count == 2 && Math.Abs(radii[0] - 11.5f) < 0.001f
            && Math.Abs(radii[1] - 16f) < 0.001f,
            "RimWorld and third-party shield components expose radii without reading unrelated effects");
        need(MP_ShieldRanges.FromDef(new XElement("ThingDef")).Count == 0,
            "Ordinary buildings have no shield range");
    }
}
