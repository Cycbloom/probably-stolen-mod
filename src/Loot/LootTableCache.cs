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

        internal void Clear() { tables.Clear(); initialized = false; }

        internal EffectiveTable Get(Region region, LootSettings snapshot, bool perk80, float eventJunkScale = 1f)
        {
            eventJunkScale = ProbabilityMath.NormalizeJunkScale(eventJunkScale);
            if (!initialized || !snapshot.Equals(settings) || unlocked != perk80 || junkScale != eventJunkScale)
            {
                tables.Clear();
                settings = snapshot;
                unlocked = perk80;
                junkScale = eventJunkScale;
                initialized = true;
            }
            EffectiveTable table;
            if (!tables.TryGetValue(region.Id, out table))
            {
                table = LootCalculator.Build(region, snapshot, perk80, junkScale);
                tables.Add(region.Id, table);
            }
            return table;
        }
    }
}
