using System;

namespace DumpingGroundMap
{
    /// <summary>Pure weekly planning: one visit per eligible area, at most two per day.</summary>
    internal static class RegionalEventSchedule
    {
        internal static int[] Plan(int weekStart, int firstAvailable, int mask, int seed, int[] previous)
        {
            int[] result = new int[Region.All.Length];
            int[] counts = new int[7];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = previous != null && i < previous.Length ? previous[i] : -1;
                if (result[i] >= weekStart && result[i] < weekStart + 7 && counts[result[i] - weekStart] < 2)
                    ++counts[result[i] - weekStart];
                else result[i] = -1;
            }
            int[] order = new int[result.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            uint state = unchecked((uint)seed ^ ((uint)weekStart * 2654435761u));
            for (int i = order.Length - 1; i > 0; --i)
            {
                int j = Next(ref state, i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }
            foreach (int i in order)
            {
                if ((mask & (1 << i)) == 0 || result[i] >= 0) continue;
                int low = Math.Max(0, firstAvailable - weekStart);
                if (low >= 7) continue;
                // Fill empty dates before assigning a second event to a date.
                int minimum = 2;
                for (int d = low; d < 7; ++d) minimum = Math.Min(minimum, counts[d]);
                if (minimum >= 2) continue;
                int[] choices = new int[7]; int count = 0;
                for (int d = low; d < 7; ++d) if (counts[d] == minimum) choices[count++] = d;
                int day = choices[Next(ref state, count)];
                result[i] = weekStart + day;
                ++counts[day];
            }
            return result;
        }
        private static int Next(ref uint state, int max)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (int)((state >> 8) % (uint)max);
        }
    }
}
