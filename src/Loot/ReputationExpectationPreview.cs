using System;

namespace DumpingGroundMap
{
    /// <summary>Permanent progression preview: no calendar events or player-state mutations.</summary>
    internal sealed class ReputationExpectationPreview
    {
        internal static readonly int[] Milestones = { 0, 20, 40, 60, 80, 100 };
        private LootSettings settings;
        private ForeignLootTable foreign;
        private MainExpectation[] averages;

        internal static int OpenCount(int points)
        {
            int count = 0;
            foreach (Region region in Region.All)
                if (region.RequiredReputation <= points) ++count;
            return count;
        }

        internal MainExpectation Get(int index, LootSettings snapshot, ForeignLootTable foreignLoot = null)
        {
            if (index < 0 || index >= Milestones.Length) throw new ArgumentOutOfRangeException("index");
            if (averages == null || !settings.Equals(snapshot) || !ReferenceEquals(foreign, foreignLoot))
            {
                settings = snapshot;
                foreign = foreignLoot;
                averages = new MainExpectation[Milestones.Length];
                for (int i = 0; i < Milestones.Length; ++i)
                {
                    int points = Milestones[i];
                    MainExpectation sum = new MainExpectation();
                    foreach (Region region in Region.All)
                    {
                        if (region.RequiredReputation > points) continue;
                        EffectiveTable table = LootCalculator.Build(region, snapshot,
                            points >= PerkCatalog.Perk80Threshold, 1f, points, foreignLoot);
                        MainExpectation parts = MainExpectation.From(table, region.Id);
                        sum.Base += parts.Base;
                        sum.Valuable += parts.Valuable;
                        sum.Supply += parts.Supply;
                        sum.Total += parts.Total;
                    }
                    int count = Math.Max(1, OpenCount(points));
                    sum.Base /= count;
                    sum.Valuable /= count;
                    sum.Supply /= count;
                    sum.Total /= count;
                    averages[i] = sum;
                }
            }
            return averages[index];
        }
    }
}
