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
        private static readonly MethodInfo eventModifier =
            AccessTools.Method(typeof(ScavHelper), "EventModifier");

        internal static float JunkModifier()
        {
            if (eventModifier != null) return (float)eventModifier.Invoke(null, new object[] { "junk_chance" });
            AfterhourLocation location = AfterhourLocation.Get("dumping_ground");
            return location == null ? 0f : location.GetModifier("junk_chance");
        }

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
