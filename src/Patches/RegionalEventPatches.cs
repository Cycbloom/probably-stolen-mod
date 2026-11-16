using HarmonyLib;

namespace DumpingGroundMap
{
    [HarmonyPatch(typeof(StoreStation), "StartDay")]
    internal static class RegionalEventDayPatch
    {
        private static void Postfix()
        {
            if (!Core.Enabled) return;
            StashFind.SyncDailyWindow();
            RegionalEvents.Sync();
            Picker.RefreshIfOpen();
            NativeSettings.RefreshIfOpen();
            LootNoticeView.RefreshIfOpen();
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetMinorWoundChance")]
    internal static class RegionalEventMinorWoundPatch
    {
        private static void Postfix(ref float __result)
        {
            Region region = Picker.ChosenRegion;
            if (region != null && region.Id == "gate" && Picker.IsAtDump(MapUIManager.Instance))
                __result = RegionalEventRules.WoundChance(__result, RegionalEvents.ActiveFor(region) != null);
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "GetMajorWoundChance")]
    internal static class RegionalEventMajorWoundPatch
    {
        private static void Postfix(ref float __result)
        {
            Region region = Picker.ChosenRegion;
            if (region != null && region.Id == "gate" && Picker.IsAtDump(MapUIManager.Instance))
                __result = RegionalEventRules.WoundChance(__result, RegionalEvents.ActiveFor(region) != null);
        }
    }
}
