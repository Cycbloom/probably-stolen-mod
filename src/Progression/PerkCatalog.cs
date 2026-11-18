using System;

namespace DumpingGroundMap
{
    internal sealed class PerkDefinition
    {
        internal readonly int Threshold;
        internal readonly string NameKey, DescriptionKey;
        internal string Name { get { return Copy.Get(NameKey); } }
        internal string Description { get { return Copy.Get(DescriptionKey); } }
        internal PerkDefinition(int threshold, string nameKey, string descriptionKey)
        {
            Threshold = threshold; NameKey = nameKey; DescriptionKey = descriptionKey;
        }
    }

    internal static class PerkCatalog
    {
        /// <summary>五档门槛与名称。改档名只改这几行，其它地方不要再写死。</summary>
        internal const int Perk20Threshold = 20;
        internal const string Perk20NameKey = "perk_20_name";

        internal const int Perk40Threshold = 40;
        internal const string Perk40NameKey = "perk_40_name";

        /// <summary>60 档：提高隐藏储藏与小奖机会。</summary>
        internal const int Perk60Threshold = 60;
        internal const string Perk60NameKey = "perk_60_name";

        /// <summary>80 档：开放贵重品。</summary>
        internal const int Perk80Threshold = 80;
        internal const string Perk80NameKey = "perk_80_name";

        /// <summary>100 档：补给箱、钥匙卡、信物与次日同行夜间服务。</summary>
        internal const int Perk100Threshold = 100;
        internal const string Perk100NameKey = "perk_100_name";


        internal static readonly PerkDefinition[] All =
        {
            new PerkDefinition(Perk20Threshold, Perk20NameKey, "perk_20_description"),
            new PerkDefinition(Perk40Threshold, Perk40NameKey, "perk_40_description"),
            new PerkDefinition(Perk60Threshold, Perk60NameKey, "perk_60_description"),
            new PerkDefinition(Perk80Threshold, Perk80NameKey, "perk_80_description"),
            new PerkDefinition(Perk100Threshold, Perk100NameKey, "perk_100_description"),
        };

        internal static PerkDefinition For(int threshold)
        {
            for (int i = All.Length - 1; i >= 0; --i)
                if (threshold >= All[i].Threshold) return All[i];
            return All[0];
        }

        internal static float MinorMultiplier(int points) { return points < Perk60Threshold ? 0.1f : 1f; }
    }
}
