using System;

namespace DumpingGroundMap
{
    /// <summary>Uses the game's saved visit history, never the map window's transient state.</summary>
    internal static class ScavengerTrafficRules
    {
        internal static bool ProtectsMorning(PlayerStore store, int today)
        {
            return today > 1 && ProtectsNightReport(store, today - 1);
        }

        internal static bool ProtectsNightReport(PlayerStore store, int nightDay)
        {
            if (nightDay < 1 || store == null || store.startScenarioId != ScavengerStart.ScenarioId
                || store.healthData == null || !store.healthData.wentToAfterhoursLastNight
                || store.afterhourState == null) return false;
            AfterhourLocationState visit;
            return store.afterhourState.TryGetValue(ScavengerStart.DumpingGroundId, out visit)
                && visit != null && visit.visits > 0 && visit.lastVisitDay == nightDay;
        }

        internal static int RemoveMisleadingReport(PlayerStore store, int today, string warning)
        {
            if (!ProtectsMorning(store, today) || store.nightLogs == null || string.IsNullOrEmpty(warning))
                return 0;
            string colored = "<color=#9c3738>" + warning + "</color>";
            return store.nightLogs.RemoveAll(line => line == warning || line == colored);
        }

        internal static void RepairSavedReport()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance) return;
            PlayerStore store = PlayerStore.Instance;
            int today = StoreStation.GetDayCounter();
            if (!ProtectsMorning(store, today)) return;
            RemoveMisleadingReport(store, today,
                LocHelper.GetLocalizedMechanic("mech_nightlog_afterhour_debuff"));
        }
    }
}
