using System.Globalization;

namespace DumpingGroundMap
{
    internal sealed class MilestoneNote
    {
        internal readonly int Threshold;
        internal readonly string Key, TitleKey;
        private readonly string bodyKey;

        internal MilestoneNote(int threshold, string key, string titleKey, string textKey)
        {
            Threshold = threshold; Key = key; TitleKey = titleKey; bodyKey = textKey;
        }

        internal bool Due(int points, bool received) { return points >= Threshold && !received; }
        internal bool GivesToken(bool legacyTokenReceived)
        {
            return Threshold == PerkCatalog.Perk100Threshold && !legacyTokenReceived;
        }

        internal string Title { get { return Copy.Get(TitleKey); } }
        internal string Body(bool includeToken)
        {
            string text = Copy.Get(bodyKey, OutingRules.Fee, OutingRules.ExtraAttempts);
            return includeToken ? Copy.Get("milestone_token_intro") + text : text;
        }

    }

    internal static class MilestoneNotes
    {
        internal static readonly MilestoneNote[] All =
        {
            new MilestoneNote(PerkCatalog.Perk20Threshold, "scav_note_20_given", "milestone_20_title", "milestone_20_body"),
            new MilestoneNote(PerkCatalog.Perk40Threshold, "scav_note_40_given", "milestone_40_title", "milestone_40_body"),
            new MilestoneNote(PerkCatalog.Perk100Threshold, "scav_note_100_given", "milestone_100_title", "milestone_100_body")
        };
    }
}
