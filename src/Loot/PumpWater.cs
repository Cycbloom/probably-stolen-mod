using System;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Loose pump-station containers carry water; sealed rations and other areas keep native behavior.</summary>
    internal static class PumpWater
    {
        internal static bool IsContainer(string id)
        {
            return id == "small_bottled_water" || id == "bottled_water"
                || id == "large_bottled_water" || id == "water_jug" || id == "mini_bottle";
        }

        // Native grades: 0 pure, 1 high quality, 2 base, 3 ghostwater, 4 rust, 5 gutterflow.
        // A discrete bell curve peaks at ghostwater, falling toward both extremes.
        internal static int GradeForRoll(int roll)
        {
            if (roll < 1) return 0;
            if (roll < 7) return 1;
            if (roll < 31) return 2;
            if (roll < 70) return 3;
            if (roll < 94) return 4;
            return 5;
        }

        // Native capacities and AddWater both use microlitre parts, not millilitres.
        // Truncated normal over 10-100%, mean 55%, sigma 18 percentage points.
        // One-percent fill steps keep the sampler bounded and avoid clamping mass at the tails.
        private static readonly double[] fillCumulative = BuildFillDistribution(55d);
        private static readonly double[] surplusCumulative = BuildFillDistribution(65d);

        private static double[] BuildFillDistribution(double center)
        {
            double[] cumulative = new double[91];
            double total = 0d;
            for (int i = 0; i < cumulative.Length; ++i)
            {
                double z = (i + 10d - center) / 18d;
                total += Math.Exp(-0.5d * z * z);
                cumulative[i] = total;
            }
            for (int i = 0; i < cumulative.Length; ++i) cumulative[i] /= total;
            return cumulative;
        }

        internal static int FillParts(int capacity, float roll, bool surplus = false)
        {
            if (capacity <= 0) return 0;
            double u = Math.Max(0d, Math.Min(1d, roll));
            int percent = 100;
            double[] distribution = surplus ? surplusCumulative : fillCumulative;
            for (int i = 0; i < distribution.Length; ++i)
            {
                if (u <= distribution[i]) { percent = i + 10; break; }
            }
            return Math.Min(capacity, Math.Max(1, (int)Math.Round(capacity * (percent / 100d))));
        }

        internal static float ExpectedValue(string id, float shellValue, bool surplus = false)
        {
            int capacity = id == "small_bottled_water" ? 450 : id == "bottled_water" ? 750
                : id == "large_bottled_water" ? 2000 : id == "water_jug" ? 6000 : 250;
            // Native water base price is 40/L. Purity modifies that value, then shell price is added.
            double[] purity = { 2d, 1.5d, 1d, 0.67d, 0.25d, 0.1d };
            int[] chances = { 1, 6, 24, 39, 24, 6 };
            double result = 0d, previous = 0d;
            double[] distribution = surplus ? surplusCumulative : fillCumulative;
            for (int i = 0; i < distribution.Length; ++i)
            {
                double probability = distribution[i] - previous;
                previous = distribution[i];
                double water = Math.Round(capacity * (i + 10d) / 100d / 1000d * 40d, MidpointRounding.AwayFromZero);
                for (int grade = 0; grade < chances.Length; ++grade)
                    result += probability * chances[grade] / 100d * Math.Round(shellValue + water * purity[grade]);
            }
            return (float)result;
        }

        internal static void Apply(string id, string regionId, GameItem item, bool surplus = false)
        {
            if (regionId != "water" || item == null || !IsContainer(id)) return;
            int capacity = WaterHelper.GetTotalCapacity(item);
            if (capacity <= 0) return;
            int amount = FillParts(capacity, UnityEngine.Random.value, surplus);
            int grade = GradeForRoll(UnityEngine.Random.Range(0, 100));
            WaterHelper.EmptyContainer(item);
            WaterHelper.AddWater(item, grade, amount, false, 0, 1, true);
            SpriteHelper.UpdateWaterContainerSprite(item);
            WaterHelper.UpdateWaterBottleSound(item);
        }
    }
}
