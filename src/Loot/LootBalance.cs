using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    /// <summary>Reweights items inside their categories; keeps area identity and junk independent.</summary>
    internal static class LootBalance
    {
        internal const float Baseline = 12.24f;
        internal static int Stage(int reputation) { return Math.Max(0, Math.Min(3, reputation / 20)); }
        internal static float Target(int reputation) { return Baseline * (1f + 0.1f * Stage(reputation)); }
        internal static float JunkBase(Region region) { return 0.5f; }

        internal static void Apply(Region region, string[] ids, float[] weights, int reputation)
        {
            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            double[] values = new double[ids.Length];
            for (int i = 0; i < ids.Length; ++i)
            {
                values[i] = LootCatalog.ExpectedValueOf(ids[i], region.Id);
                if (ids[i] == "junk" || weights[i] <= 0f) continue;
                int category = region.Loot.CategoryOf(ids[i]);
                List<int> group;
                if (!groups.TryGetValue(category, out group)) groups.Add(category, group = new List<int>());
                group.Add(i);
            }
            float junk = JunkBase(region);
            double target = (Target(reputation) - junk * LootCatalog.ValueOf("junk")) / (1d - junk);
            double low = -80d, high = 80d;
            // Explicit category/fill constraints can put the requested target outside
            // the attainable range. Preserve those constraints and use the nearest EV.
            target = Math.Max(Tilt(region, groups, values, weights, low, null),
                Math.Min(Tilt(region, groups, values, weights, high, null), target));
            for (int n = 0; n < 60; ++n)
            {
                double middle = (low + high) * 0.5d;
                if (Tilt(region, groups, values, weights, middle, null) < target) low = middle;
                else high = middle;
            }
            float[] result = new float[weights.Length];
            Tilt(region, groups, values, weights, (low + high) * 0.5d, result);
            Array.Copy(result, weights, result.Length);
        }

        private static double Tilt(Region region, Dictionary<int, List<int>> groups,
            double[] values, float[] original, double slope, float[] result)
        {
            double expected = 0d, total = 0d;
            foreach (KeyValuePair<int, List<int>> pair in groups)
            {
                // Pump container size odds remain 5/45/45/5 at every reputation tier.
                bool fixedSize = region.Id == "water" && pair.Key == 1;
                double share = 0d, maximum = double.NegativeInfinity;
                foreach (int i in pair.Value)
                {
                    share += original[i];
                    maximum = Math.Max(maximum, Math.Log(original[i]) + (fixedSize ? 0d : slope * values[i] / 50d));
                }
                double sum = 0d, cost = 0d;
                foreach (int i in pair.Value)
                {
                    double w = Math.Exp(Math.Log(original[i]) + (fixedSize ? 0d : slope * values[i] / 50d) - maximum);
                    sum += w;
                    cost += w * values[i];
                }
                expected += share * cost / sum;
                total += share;
                if (result == null) continue;
                foreach (int i in pair.Value)
                {
                    double w = Math.Exp(Math.Log(original[i]) + (fixedSize ? 0d : slope * values[i] / 50d) - maximum);
                    result[i] = (float)(share * w / sum);
                }
            }
            return total > 0d ? expected / total : 0d;
        }
    }
}
