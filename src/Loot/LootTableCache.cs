using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal sealed class LootTableCache
    {
        private readonly Dictionary<string, EffectiveTable> tables = new Dictionary<string, EffectiveTable>();
        private LootSettings settings;
        private bool unlocked;
        private bool initialized;

        internal void Clear() { tables.Clear(); initialized = false; }

        internal EffectiveTable Get(Region region, LootSettings snapshot, bool perk80)
        {
            if (!initialized || !snapshot.Equals(settings) || unlocked != perk80)
            {
                tables.Clear();
                settings = snapshot;
                unlocked = perk80;
                initialized = true;
            }
            EffectiveTable table;
            if (!tables.TryGetValue(region.Id, out table))
            {
                table = LootCalculator.Build(region, snapshot, perk80);
                tables.Add(region.Id, table);
            }
            return table;
        }
    }
}
