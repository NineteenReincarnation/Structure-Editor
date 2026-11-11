using System.Globalization;
using System.Xml.Linq;

namespace StructureEditor;

// Reads only advertised, static shield radii from the effective ThingDef.
// Runtime-only shields without a radius in XML deliberately have no preview.
internal static class MP_ShieldRanges
{
    private static readonly string[] RadiusTags =
    {
        "radius", "shieldRadius", "shieldRange", "maxRadius", "radiusMax",
        "shieldRadiusMax", "shieldRadiusBase", "range"
    };

    internal static List<float> FromDef(XElement def)
    {
        var ranges = new List<float>();
        foreach (XElement comp in def.Element("comps")?.Elements("li") ?? Enumerable.Empty<XElement>())
        {
            string className = ((string)comp.Attribute("Class") ?? "").Split('.').Last();
            string resolvedComp = (string)comp.Element("compClass") ?? "";
            // RimWorld: CompProperties_ProjectileInterceptor.radius.
            // Other mods frequently use a Shield/Barrier/ForceField component
            // with shieldRadius/shieldRange; only use a field when the component
            // itself advertises one of these mechanics.
            bool shield = new[] { className, resolvedComp }.Any(name =>
                name.Contains("ProjectileInterceptor", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Shield", StringComparison.OrdinalIgnoreCase)
                || name.Contains("ForceField", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Barrier", StringComparison.OrdinalIgnoreCase));
            if (!shield) continue;
            foreach (string tag in RadiusTags)
            {
                if (!float.TryParse((string)comp.Element(tag), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float radius)
                    || !float.IsFinite(radius) || radius <= 0 || radius > 500) continue;
                if (!ranges.Any(v => Math.Abs(v - radius) < 0.001f)) ranges.Add(radius);
                // Some mods define both min and max as attributes; use the
                // explicit primary radius, not multiple accidental radii.
                break;
            }
        }
        return ranges;
    }
}
