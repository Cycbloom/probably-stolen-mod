using System;
using System.Reflection;
using HarmonyLib;

namespace DumpingGroundMap
{
    /// <summary>Game event adapter; reflection stays at the integration boundary.</summary>
    internal static class NativeLootEvents
    {
        private static readonly MethodInfo fireLootRolled =
            AccessTools.Method(typeof(ModHook), "FireOnLootRolled");

        internal static string Resolve(string token)
        {
            if (fireLootRolled == null)
                throw new MissingMethodException("ModHook.FireOnLootRolled is required for scavenging compatibility.");
            string realId = LootVariants.RealId(token);
            return LootRollResolver.Resolve(token, realId, ScavLoot.SourceTable(realId), Notify);
        }

        private static void Notify(LootRollContext context)
        {
            fireLootRolled.Invoke(null, new object[] { context });
        }
    }
}
