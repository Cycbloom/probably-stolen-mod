using System;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Three remaining-use states for the HydraTech purifier, using native durability.</summary>
    internal static class HydraUsage
    {
        internal const string ItemId = "portable_water_purifier";

        internal static void Apply(string id, GameItem item)
        {
            if (id != ItemId || item == null) return;
            int maximum = DurabilityHelper.GetMaxDurability(item);
            if (maximum <= 0) return;
            int tier = UnityEngine.Random.Range(0, 3);
            int remaining = tier == 0 ? Math.Max(1, (int)Math.Round(maximum / 3d))
                : tier == 1 ? Math.Max(1, (int)Math.Round(maximum / 2d)) : maximum;
            // Preserve native maximum, default, pricing mode and normal consumption behavior.
            DurabilityHelper.SetCurrentDurability(item, remaining);
        }
    }
}
