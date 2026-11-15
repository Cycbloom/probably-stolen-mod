using System;
using HarmonyLib;

namespace DumpingGroundMap
{
    [HarmonyPatch(typeof(LocHelper), "GetLocalizedUI", new Type[] { typeof(string), typeof(object[]) })]
    internal static class OutingTooltipPatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (!Core.Enabled) return true;
            return Resolve(__0, () => ScavengerOuting.Description, ref __result);
        }

        internal static bool Resolve(string key, Func<string> description, ref string result)
        {
            if (key != ScavengerOuting.TooltipKey) return true;
            result = description();
            return false;
        }
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), "InitUI")]
    internal static class OutingServicePanelPatch
    {
        private static void Prefix() { ScavengerOuting.Sync(); }
    }

    [HarmonyPatch(typeof(PlayerStore), "UpdateServiceCost")]
    internal static class OutingServiceCostPatch
    {
        private static void Postfix() { ScavengerOuting.Sync(); }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetMaxScavAttempts")]
    internal static class OutingAttemptsPatch
    {
        private static void Postfix(ref int __result)
        {
            __result = OutingRules.Attempts(__result, ScavengerOuting.Active);
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetMinorWoundChance")]
    internal static class OutingMinorChancePatch
    {
        private static void Postfix(ref float __result) { if (ScavengerOuting.Active) __result = 0f; }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetMajorWoundChance")]
    internal static class OutingMajorChancePatch
    {
        private static void Postfix(ref float __result) { if (ScavengerOuting.Active) __result = 0f; }
    }

    [HarmonyPatch(typeof(ScavHelper), "RollMinorWound")]
    internal static class OutingMinorRollPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ref bool __result)
        {
            if (!ScavengerOuting.Active) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "RollMajorWound")]
    internal static class OutingMajorRollPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ref bool __result)
        {
            if (!ScavengerOuting.Active) return true;
            __result = false;
            return false;
        }
    }
}
