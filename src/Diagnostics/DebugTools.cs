using System.Collections.Generic;
using System.Globalization;

namespace DumpingGroundMap
{
    /// <summary>Read-only diagnostics exposed through the game's optional F9 menu.</summary>
    internal static class DebugTools
    {
        private static bool registered;

        internal static void Register()
        {
            if (registered) return;
            // Stable developer labels: registration precedes localization asset loading.
            ModHelper.AddDebugFunction(Core.Id, ModHelper.DEBUG_CATEGORY_SPECIAL, "DGM Check Assets", AssetDiagnostics.Recheck);
            ModHelper.AddDebugFunction(Core.Id, ModHelper.DEBUG_CATEGORY_SPECIAL, "DGM Dump State", DumpState);
            registered = true;
        }

        internal static void Unregister()
        {
            ModHelper.RemoveDebugFunctions(Core.Id);
            registered = false;
        }

        private static void DumpState()
        {
            if (!Core.Enabled) return;
            // Inspect only our cached state. Never create PlayerStore, reload, save,
            // award milestones, or build a loot table just to print diagnostics.
            var unlocked = new List<string>();
            int points = Reputation.Points;
            foreach (Region region in Region.All)
                if (points >= region.RequiredReputation) unlocked.Add(region.Id);
            Region chosen = Picker.ChosenRegion;
            ModLogger.Info(string.Format(CultureInfo.InvariantCulture,
                "[debug] cached mod state: assets_notified={0}, reputation={1}, total_value={2}, chosen_region={3}, unlocked=[{4}]",
                AssetDiagnostics.Ready, points, Reputation.TotalValue, chosen == null ? "none" : chosen.Id,
                string.Join(", ", unlocked.ToArray())));
        }
    }
}
