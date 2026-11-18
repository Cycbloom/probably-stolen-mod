using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    /// <summary>Convert native category tables/groups into the calculator's integer-weight snapshot.</summary>
    internal sealed class LootDataSnapshot
    {
        internal readonly RegionLootTable Vanilla;
        internal readonly Dictionary<string, RegionLootTable> Regions;

        private LootDataSnapshot(RegionLootTable vanilla, Dictionary<string, RegionLootTable> regions)
        {
            Vanilla = vanilla;
            Regions = regions;
        }

        internal static LootDataSnapshot Bundled() { return Capture(LootDefaults.Table, LootDefaults.Group); }

        internal static LootDataSnapshot Capture(Func<string, List<LootEntry>> table, Func<string, List<LootEntry>> group)
        {
            List<LootEntry> baseline = Validate(table(Core.Id + ".baseline"), Core.Id + ".baseline");
            int[] baselineWeights = IntegerWeights(baseline);
            var vanilla = new RegionLootTable(Ids(baseline), baselineWeights);
            if (vanilla.JunkWeight == 0 || vanilla.Count < 2) throw new ArgumentException("baseline needs junk and non-junk entries");
            var regions = new Dictionary<string, RegionLootTable>(StringComparer.Ordinal);
            foreach (Region region in Region.All)
            {
                string groupId = Core.Id + ".region." + region.Id;
                List<LootEntry> categories = Validate(group(groupId), groupId);
                int[] shares = Allocate(categories, 10000);
                var ids = new List<string>();
                var weights = new List<int>();
                var labels = new List<string>();
                var membership = new List<int>();
                for (int c = 0; c < categories.Count; ++c)
                {
                    string categoryId = categories[c].id;
                    if (!categoryId.StartsWith(groupId + ".", StringComparison.Ordinal))
                        throw new ArgumentException("category must belong to " + groupId + ": " + categoryId);
                    List<LootEntry> entries = Validate(table(categoryId), categoryId);
                    int[] allocated = Allocate(entries, shares[c]);
                    labels.Add(categoryId.Substring(groupId.Length + 1));
                    for (int i = 0; i < entries.Count; ++i)
                    {
                        ids.Add(entries[i].id);
                        weights.Add(allocated[i]);
                        membership.Add(c);
                    }
                }
                var result = new RegionLootTable(ids.ToArray(), weights.ToArray(), labels.ToArray(), membership.ToArray());
                if (result.JunkWeight == 0 || result.Count < 2) throw new ArgumentException(groupId + " needs junk and non-junk entries");
                regions.Add(region.Id, result);
            }
            return new LootDataSnapshot(vanilla, regions);
        }

        private static List<LootEntry> Validate(List<LootEntry> entries, string id)
        {
            if (entries == null || entries.Count == 0 || entries.Count > 4096) throw new ArgumentException("missing, empty or oversized loot list: " + id);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LootEntry entry in entries)
                if (entry == null || string.IsNullOrEmpty(entry.id) || !seen.Add(entry.id)
                    || entry.weight <= 0f || float.IsNaN(entry.weight) || float.IsInfinity(entry.weight))
                    throw new ArgumentException("invalid entry in " + id);
            return entries;
        }

        private static string[] Ids(List<LootEntry> entries)
        {
            var ids = new string[entries.Count];
            for (int i = 0; i < ids.Length; ++i) ids[i] = entries[i].id;
            return ids;
        }

        private static int[] IntegerWeights(List<LootEntry> entries)
        {
            var result = new int[entries.Count];
            double sum = 0d;
            for (int i = 0; i < entries.Count; ++i)
            {
                double weight = entries[i].weight;
                sum += weight;
                if (weight != Math.Floor(weight) || sum > 1000000d) return Allocate(entries, 1000000);
                result[i] = (int)weight;
            }
            return result;
        }

        private static int[] Allocate(List<LootEntry> entries, int total)
        {
            if (entries.Count > total) throw new ArgumentException("category share too small for its item count");
            double sum = 0d;
            int largest = 0;
            for (int i = 0; i < entries.Count; ++i)
            {
                sum += entries[i].weight;
                if (entries[i].weight > entries[largest].weight) largest = i;
            }
            var result = new int[entries.Count];
            int assigned = 0;
            for (int i = 0; i < entries.Count; ++i)
            {
                result[i] = Math.Max(1, (int)Math.Round(total * (double)entries[i].weight / sum, MidpointRounding.AwayFromZero));
                assigned += result[i];
            }
            result[largest] += total - assigned;
            if (result[largest] <= 0) throw new ArgumentException("loot ratios exceed integer snapshot precision");
            return result;
        }
    }
}
