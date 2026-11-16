using System;

namespace DumpingGroundMap
{
    internal sealed class PerkDefinition
    {
        internal readonly int Threshold;
        internal readonly string NameZh, NameEn, DescriptionZh, DescriptionEn;
        internal PerkDefinition(int threshold, string zh, string en, string descriptionZh, string descriptionEn)
        {
            Threshold = threshold; NameZh = zh; NameEn = en;
            DescriptionZh = descriptionZh; DescriptionEn = descriptionEn;
        }
    }

    internal static class PerkCatalog
    {
        /// <summary>五档门槛与名称。改档名只改这几行，其它地方不要再写死。</summary>
        internal const int Perk20Threshold = 20;
        internal const string Perk20NameZh = "拾荒老手";
        internal const string Perk20NameEn = "Scavenger Veteran";

        internal const int Perk40Threshold = 40;
        internal const string Perk40NameZh = "熟门熟路";
        internal const string Perk40NameEn = "Knows the Back Ways";

        /// <summary>60 档：提高隐藏储藏与小奖机会。</summary>
        internal const int Perk60Threshold = 60;
        internal const string Perk60NameZh = "眼力毒辣";
        internal const string Perk60NameEn = "Keen Eye";

        /// <summary>80 档：开放贵重品。</summary>
        internal const int Perk80Threshold = 80;
        internal const string Perk80NameZh = "识货行家";
        internal const string Perk80NameEn = "Connoisseur";

        /// <summary>100 档：补给箱、钥匙卡、信物与次日同行夜间服务。</summary>
        internal const int Perk100Threshold = 100;
        internal const string Perk100NameZh = "自己人";
        internal const string Perk100NameEn = "One of Us";


        internal static readonly PerkDefinition[] All =
        {
            new PerkDefinition(Perk20Threshold, Perk20NameZh, Perk20NameEn,
                "拆解堆、废泵站开放；所有已开放区域的基础收益目标比起步提高10%。每天多一名拾荒者来你店里。",
                "Scrap Pile and Pump Station open. All open areas gain a +10% base reward target. One extra scavenger visits your shop each day."),
            new PerkDefinition(Perk40Threshold, Perk40NameZh, Perk40NameEn,
                "报废车场、医疗废弃点开放；所有已开放区域的基础收益目标比起步提高20%。离开时可付费请帮手搬运。",
                "Vehicle Graveyard and Medical Waste open. All open areas gain a +20% base reward target. Paid carrying help is available when you leave."),
            new PerkDefinition(Perk60Threshold, Perk60NameZh, Perk60NameEn,
                "电子垃圾坡、废武器堆开放；所有区域的基础收益目标比起步提高30%。隐藏储藏与小奖机会提高，奖励收益另算。",
                "E-Waste Slope and Weapon Dump open. All areas gain a +30% base reward target. Hidden-stash and minor-reward chances improve separately."),
            new PerkDefinition(Perk80Threshold, Perk80NameZh, Perk80NameEn,
                "拾荒时可能遇到贵重品。",
                "While scavenging you may come across valuable goods."),
            new PerkDefinition(Perk100Threshold, Perk100NameZh, Perk100NameEn,
                "拾荒时可翻到补给箱与对应钥匙卡。拾荒者把你当成自己人，并送来一枚信物。解锁夜间服务「与拾荒客同行」：每次 20 信用点，次日当天多 2 次拾荒，拾荒时不会受轻伤或重伤。",
                "Supply crates and matching keycards can now be found while scavenging. The scavengers welcome you as one of their own with a token. Unlocks Scavenger Outing: pay 20 credits for 2 extra searches and no minor or major scavenging wounds the next day."),
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
