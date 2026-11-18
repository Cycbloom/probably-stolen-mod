using System;

namespace DumpingGroundMap
{
    /// <summary>One construction path for basic loot and bonus rewards.</summary>
    internal static class LootFactory
    {
        internal static GameItem Create(string token, string regionId = null, bool regionalEvent = false)
        {
            if (string.IsNullOrEmpty(token)) return null;
            GameItem item = LootVariants.Create(token);
            if (item == null) return null;
            LootVariants.Apply(token, item);
            HydraUsage.Apply(LootVariants.RealId(token), item);
            PumpWater.Apply(LootVariants.RealId(token), regionId, item, regionalEvent && regionId == "water");
            WeaponWear.Apply(LootVariants.RealId(token), item);
            return item;
        }
    }

    internal static class LootRollResolver
    {
        /// <summary>Only a failed foreign factory retries; an event cancellation is final.</summary>
        internal static T CreateWithFallback<T>(EffectiveTable table, int index,
            Func<EffectiveTable, int, string> resolve, Func<string, T> create, out bool usedFallback) where T : class
        {
            usedFallback = false;
            string resolved = resolve(table, index);
            if (string.IsNullOrEmpty(resolved)) return null;
            T item = create(resolved);
            if (item != null || table.Slots[index].Kind != LootKind.Foreign) return item;

            int fallback = table.FallbackOrdinaryIndex();
            if (fallback < 0) return null;
            usedFallback = true;
            resolved = resolve(table, fallback);
            return string.IsNullOrEmpty(resolved) ? null : create(resolved);
        }

        /// <summary>
        /// Preserve a virtual variant only when subscribers leave its real ID unchanged.
        /// A replacement (or cancellation) is used before constructing any GameItem.
        /// </summary>
        internal static string Resolve(string token, string realId, string sourceTable,
            Action<LootRollContext> notify)
        {
            LootRollContext context = new LootRollContext();
            context.groupId = "dumpingGroundTG";
            context.tableId = sourceTable;
            context.itemId = realId;
            notify(context);
            return context.itemId == realId ? token : context.itemId;
        }
    }
}
