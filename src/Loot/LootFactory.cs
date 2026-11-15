using System;

namespace DumpingGroundMap
{
    /// <summary>One construction path for basic loot and bonus rewards.</summary>
    internal static class LootFactory
    {
        internal static GameItem Create(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            GameItem item = LootVariants.Create(token);
            if (item == null) return null;
            LootVariants.Apply(token, item);
            WeaponWear.Apply(LootVariants.RealId(token), item);
            return item;
        }
    }

    internal static class LootRollResolver
    {
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
