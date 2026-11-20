using System;

namespace DumpingGroundMap
{
    /// <summary>Leave visual layout to the optional window controller, without a DLL dependency.</summary>
    internal static class ContainerWindowScaleBridge
    {
        internal const string ModId = "local.container.window.scale";
        internal const string HarmonyId = ModId;
        internal const string GroundKey = "scale_scavenging_ground";
        private static bool? previous;
        private static bool warned;

        internal static bool ControlsGround
        {
            get
            {
                bool controls = false;
                try
                {
                    if (ModHelper.IsModRunning(ModId))
                    {
                        // Read the real native option each time: do not cache a disabled
                        // partner or overwrite its config when settings/load order change.
                        ModConfig config = ModHelper.GetConfig(ModId);
                        ConfigEntry<bool> option = config == null ? null : config.Get(GroundKey) as ConfigEntry<bool>;
                        controls = option == null || option.Value;
                        if (option == null) Warn("Ground option not registered; yielding layout until it is available");
                    }
                }
                catch (Exception ex)
                {
                    // An unknown controller state must not overwrite its user's layout.
                    controls = true;
                    Warn("Ground option unavailable; yielding layout: " + ex.Message);
                }
                if (controls != previous)
                {
                    if (controls || previous == true)
                        ModLogger.Info("[compat:container-window-scale] Ground layout "
                            + (controls ? "delegated to Container Window Scale" : "returned to Scrapyard Expansion"));
                    previous = controls;
                }
                return controls;
            }
        }

        private static void Warn(string message)
        {
            if (warned) return;
            warned = true;
            ModLogger.Warning("[compat:container-window-scale] " + message);
        }

        internal static void Reset()
        {
            previous = null;
            warned = false;
        }
    }
}
