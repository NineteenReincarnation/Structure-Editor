using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StructureEditor.Compatibility
{
    // Shared by the game and desktop editor. These are identifiers only, never combat parameters.
    public static class MP_ClusterNaming
    {
        public const string DefaultDefName = "MP_Cluster_ScytherForge_T01_V01";
        public const string LegacyStarterDefName = "MP_PurgeStarterCluster";
        public const string ScytherForgeCore = "ScytherForge";
        public const string ScytherForgeLabel = "飞螳铸巢";
        private static readonly string[] Roman = { "", "I", "II", "III", "IV", "V" };
        private static readonly string[] Descriptions = { "", "初级集群", "常规集群", "强化集群", "重型集群", "要塞集群" };
        private static readonly Regex CorePattern = new Regex(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);
        private static readonly Regex IdPattern = new Regex(@"^MP_Cluster_([A-Z][A-Za-z0-9]*)_T(0[1-5])_V([0-9]{2})$", RegexOptions.CultureInvariant);
        public static string ResolveLegacy(string id) => id == LegacyStarterDefName ? DefaultDefName : id;
        public static bool TryParse(string id, out string core, out int tier, out int variant)
        {
            core = null; tier = variant = 0;
            Match match = IdPattern.Match(id ?? "");
            if (!match.Success) return false;
            core = match.Groups[1].Value;
            tier = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            variant = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return variant >= 1 && variant <= 99;
        }
        public static string DefName(string core, int tier, int variant)
        {
            if (!CorePattern.IsMatch(core ?? "")) throw new ArgumentException("Core 必须是英文 PascalCase 标识，例如 ScytherForge，不含空格、下划线或中文。");
            CheckNumbers(tier, variant);
            return "MP_Cluster_" + core + "_T" + tier.ToString("00", CultureInfo.InvariantCulture) + "_V" + variant.ToString("00", CultureInfo.InvariantCulture);
        }
        private static void CheckNumbers(int tier, int variant)
        {
            if (tier < 1 || tier > 5) throw new ArgumentOutOfRangeException(nameof(tier), "集群等级为 I～V（01～05）。");
            if (variant < 1 || variant > 99) throw new ArgumentOutOfRangeException(nameof(variant), "布局变体编号为 01～99。");
        }
        public static string TierName(int tier) { CheckNumbers(tier, 1); return Roman[tier] + "：" + Descriptions[tier]; }
        public static string VariantName(int variant)
        {
            CheckNumbers(1, variant); string value = "";
            while (variant > 0) { variant--; value = (char)('A' + variant % 26) + value; variant /= 26; }
            return value;
        }
        public static string Label(string coreLabel, int tier, int variant)
        {
            CheckNumbers(tier, variant);
            if (string.IsNullOrWhiteSpace(coreLabel) || coreLabel.Contains("\n") || coreLabel.Contains("\r")) throw new ArgumentException("请填写核心设施显示名称。");
            return coreLabel.Trim() + "·" + Roman[tier] + "-" + VariantName(variant);
        }
        public static string CoreLabel(string label, int tier, int variant)
        {
            CheckNumbers(tier, variant);
            string suffix = "·" + Roman[tier] + "-" + VariantName(variant);
            return label != null && label.EndsWith(suffix, StringComparison.Ordinal) ? label.Substring(0, label.Length - suffix.Length) : null;
        }
        public static string Validate(string id, string label)
        {
            string core; int tier, variant;
            if (!TryParse(id, out core, out tier, out variant)) return "机械集群 DefName 必须为 MP_Cluster_[Core]_T01～T05_V01～V99，例如 " + DefaultDefName;
            string coreLabel = CoreLabel(label, tier, variant);
            if (string.IsNullOrWhiteSpace(coreLabel) || coreLabel.Contains("\n") || coreLabel.Contains("\r") || label != Label(coreLabel, tier, variant)) return "显示名称必须为 [核心设施名称]·[I～V]-[布局变体]，并与 DefName 编号对应。";
            if (core == ScytherForgeCore && coreLabel != ScytherForgeLabel) return "ScytherForge 的核心设施显示名称统一为“飞螳铸巢”。";
            if (coreLabel == ScytherForgeLabel && core != ScytherForgeCore) return "飞螳铸巢的 Core 标识统一为 ScytherForge。";
            return null;
        }
        public static int NextVariant(IEnumerable<string> identifiers, string core, int tier)
        {
            DefName(core, tier, 1);
            var used = new HashSet<int>();
            foreach (string id in identifiers)
            {
                string parsedCore; int parsedTier, parsedVariant;
                if (TryParse(ResolveLegacy(id), out parsedCore, out parsedTier, out parsedVariant) && parsedCore == core && parsedTier == tier) used.Add(parsedVariant);
            }
            for (int variant = 1; variant <= 99; variant++) if (!used.Contains(variant)) return variant;
            throw new InvalidOperationException("该核心类型和等级的 01～99 布局编号均已使用，请选择其他等级或整理结构库。");
        }
    }
}
