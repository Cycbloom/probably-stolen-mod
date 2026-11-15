using System.Globalization;

namespace DumpingGroundMap
{
    internal sealed class MilestoneNote
    {
        internal readonly int Threshold;
        internal readonly string Key, TitleZh, TitleEn;
        private readonly string bodyZh, bodyEn;

        internal MilestoneNote(int threshold, string key, string titleZh, string titleEn, string zh, string en)
        {
            Threshold = threshold; Key = key; TitleZh = titleZh; TitleEn = titleEn;
            bodyZh = zh; bodyEn = en;
        }

        internal bool Due(int points, bool received) { return points >= Threshold && !received; }
        internal bool GivesToken(bool legacyTokenReceived)
        {
            return Threshold == PerkCatalog.Perk100Threshold && !legacyTokenReceived;
        }

        internal string Body(bool chinese, bool includeToken)
        {
            string text = string.Format(CultureInfo.InvariantCulture, chinese ? bodyZh : bodyEn,
                OutingRules.Fee, OutingRules.ExtraAttempts);
            if (includeToken)
                text = (chinese ? "这枚信物给你留着，往后你就是自己人了。\n\n"
                    : "Keep this token. From now on, you're one of us.\n\n") + text;
            return text;
        }
    }

    internal static class MilestoneNotes
    {
        internal static readonly MilestoneNote[] All =
        {
            new MilestoneNote(PerkCatalog.Perk20Threshold, "scav_note_20_given", "顺路来坐坐", "We'll Drop By",
                "店主：\n这阵子你在场子里忙活，大伙儿都看在眼里。我们商量好了，往后每天多叫一个拾荒的伙计上你店里看看。有好东西给你留着，你也多照应照应。",
                "Shopkeeper,\nWe've seen you putting in the work at the dump. From now on, we'll send one extra scavenger by your shop each day. We'll save you the good finds; you look after our people."),
            new MilestoneNote(PerkCatalog.Perk40Threshold, "scav_note_40_given", "走的时候招呼一声", "Give Us a Shout Before You Leave",
                "店主：\n拆解堆、医疗废弃点和废武器堆的路，你也熟了。往后走的时候，地上还有带不完的东西，就花点工钱招呼我们一声。我们先把值钱的搬回你店里；店里实在塞不下的，也只能留下。",
                "Shopkeeper,\nYou know the way to the scrap pile, medical waste and weapon dump now. If there's more on the ground than you can carry, hire us when you leave. We'll take the valuable stuff to your shop first. Anything your shop can't hold has to stay behind."),
            new MilestoneNote(PerkCatalog.Perk100Threshold, "scav_note_100_given", "明天跟我们一起", "Come With Us Tomorrow",
                "店主：\n大伙儿都认你这个朋友了。以后关店时，花 {0} 信用点约个「与拾荒客同行」，第二天咱们一起下场。我们带你多翻 {1} 回，也帮你看着危险的路，拾荒时轻伤、重伤都不用担心。就约第二天这一天；想再同行，头晚再招呼一声。",
                "Shopkeeper,\nYou're one of our own now. Book a Scavenger Outing for {0} credits when you close, and we'll head out together tomorrow. We'll help you search {1} more times and watch the dangerous paths, so you won't suffer minor or major scavenging wounds. The booking lasts that next day only; ask again the night before another outing.")
        };
    }
}
