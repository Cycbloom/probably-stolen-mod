using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal sealed class LootTableCache
    {
        private readonly Dictionary<string, EffectiveTable> tables = new Dictionary<string, EffectiveTable>();
        private LootSettings settings;
        private bool unlocked;
        private bool initialized;
        private float junkScale;
        private int stage;
        private ForeignLootTable foreign;

        internal void Clear() { tables.Clear(); initialized = false; }

        internal EffectiveTable Get(Region region, LootSettings snapshot, bool perk80, float eventJunkScale = 1f, int reputation = 0,
            RegionalEvent regionalEvent = null, ForeignLootTable foreignLoot = null)
        {
            eventJunkScale = ProbabilityMath.NormalizeJunkScale(eventJunkScale);
            int nextStage = LootBalance.Stage(reputation);
            if (!initialized || !snapshot.Equals(settings) || unlocked != perk80 || junkScale != eventJunkScale || stage != nextStage
                || !ReferenceEquals(foreignLoot, foreign))
            {
                tables.Clear();
                settings = snapshot;
                unlocked = perk80;
                junkScale = eventJunkScale;
                stage = nextStage;
                foreign = foreignLoot;
                initialized = true;
            }
            EffectiveTable table;
            if (!tables.TryGetValue(region.Id, out table) || table.RegionalEvent != regionalEvent)
            {
                table = LootCalculator.Build(region, snapshot, perk80, junkScale, reputation, foreignLoot);
                table = RegionalEventRules.Apply(region, table, regionalEvent);
                tables[region.Id] = table;
            }
            return table;
        }
    }
}
