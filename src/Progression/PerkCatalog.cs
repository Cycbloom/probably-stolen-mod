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

        /// <summary>80 档：贵重品出现率 80 门控，高级区可翻到补给箱与对应钥匙卡。</summary>
        internal const int Perk80Threshold = 80;
        internal const string Perk80NameZh = "识货行家";
        internal const string Perk80NameEn = "Connoisseur";

        /// <summary>100 档：信物与次日同行夜间服务。</summary>
        internal const int Perk100Threshold = 100;
        internal const string Perk100NameZh = "自己人";
        internal const string Perk100NameEn = "One of Us";


        internal static readonly PerkDefinition[] All =
        {
            new PerkDefinition(Perk20Threshold, Perk20NameZh, Perk20NameEn,
                "你结交了许多拾荒者朋友。每天会多一名拾荒者来你店里。",
                "You've made a lot of scavenger friends. One more scavenger visits your shop each day."),
            new PerkDefinition(Perk40Threshold, Perk40NameZh, Perk40NameEn,
                "你对这片场子越来越熟。三个高级拾荒场对你开放：拆解堆、医疗废弃点、废武器堆。离开时还可以花钱请拾荒者帮手，把拾荒时得到的东西搬回店里。",
                "You know this place better and better. Three high-tier scavenging grounds are open to you: Scrap Pile, Medical Waste, Weapon Dump. When you leave, you can also pay a scavenger helper to carry what you found back to the store."),
            new PerkDefinition(Perk60Threshold, Perk60NameZh, Perk60NameEn,
                "拾荒时更有机会找到隐藏的储藏。第二天你会在报纸上看到自己的事迹。",
                "You have a better chance of finding a hidden stash while scavenging. The next day you'll see your deed in the newspaper."),
            new PerkDefinition(Perk80Threshold, Perk80NameZh, Perk80NameEn,
                "拾荒时可能遇到贵重品。偶尔还能翻到补给箱和对应的钥匙卡。",
                "While scavenging you may come across valuable goods. Now and then you can even turn up a supply crate and its matching keycard."),
            new PerkDefinition(Perk100Threshold, Perk100NameZh, Perk100NameEn,
                "拾荒者把你当成自己人，并送来一枚信物。解锁夜间服务「与拾荒客同行」：每次 20 信用点，次日当天多 2 次拾荒，拾荒时不会受轻伤或重伤。",
                "The scavengers welcome you as one of their own with a token. Unlocks Scavenger Outing: pay 20 credits for 2 extra searches and no minor or major scavenging wounds the next day."),
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
