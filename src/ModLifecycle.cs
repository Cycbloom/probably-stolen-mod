using System;

namespace DumpingGroundMap
{
    /// <summary>Owns subscriptions and the order of run/visit initialization.</summary>
    internal static class ModLifecycle
    {
        private static bool hooked;

        internal static void Enable()
        {
            if (hooked) return;
            Tuning.Init();
            ScavengerStart.Hook();
            ModHook.OnGameLoadedLate += Loaded;
            ModHook.OnNewGameStarted += NewGame;
            ModHook.OnShutterOpenedLate += ScavengerMilestones.OnShutterOpenedLate;
            ModHook.OnGoingSleepEarly += ScavengerOuting.BeforeSleep;
            ModHook.OnHandlingNightlyServicesEarly += ScavengerOuting.AfterServicePayments;
            ModHook.OnAfterhourEventStarted += AfterhourEventChanged;
            ModHook.OnAfterhourEventEnded += AfterhourEventChanged;
            Tuning.Changed += RefreshViews;
            hooked = true;
            // Enabling after a save has already loaded must not retain another run's cache.
            // OnEnable precedes localization asset loading. Restore run data now;
            // the native service/UI hooks resolve their text when next opened.
            if (PlayerStore.HasInstance && EmporiumEntry.Instance != null) RestoreRun(false);
            else ResetMemory();
        }

        internal static void Disable()
        {
            if (hooked)
            {
                ModHook.OnGameLoadedLate -= Loaded;
                ModHook.OnNewGameStarted -= NewGame;
                ModHook.OnShutterOpenedLate -= ScavengerMilestones.OnShutterOpenedLate;
                ModHook.OnGoingSleepEarly -= ScavengerOuting.BeforeSleep;
                ModHook.OnHandlingNightlyServicesEarly -= ScavengerOuting.AfterServicePayments;
                ModHook.OnAfterhourEventStarted -= AfterhourEventChanged;
                ModHook.OnAfterhourEventEnded -= AfterhourEventChanged;
                Tuning.Changed -= RefreshViews;
                hooked = false;
            }
            try
            {
                ScavengerStart.Unhook();
                ScavengerOuting.Remove();
                ReputationPopBridge.Shutdown();
            }
            finally
            {
                try { NativeSettings.Reset(); }
                finally { ResetMemory(); }
            }
        }

        private static void ResetMemory()
        {
            ResetVisit();
            DeferredSave.Cancel();
            Reputation.ResetSession();
            StashFind.ResetSession();
            RegionalEvents.ResetSession();
        }

        private static void ResetVisit()
        {
            Picker.Close();
            Picker.ResetChoice();
            StashFind.ResetTrip();
            ScavHelperService.Reset();
            GroundExpand.ResetSearch();
        }

        private static void Loaded()
        {
            RestoreRun(true);
        }

        private static void RestoreRun(bool updateViews)
        {
            DeferredSave.Cancel();
            ResetVisit();
            Reputation.Reload();
            ScavengerTrafficRules.RepairSavedReport();
            StashFind.Reload();
            StashFind.SyncDailyWindow();
            RegionalEvents.ResetSession();
            RegionalEvents.Sync();
            if (updateViews)
            {
                ScavengerOuting.Sync();
                RefreshViews();
            }
        }

        private static void NewGame(string scenarioId)
        {
            DeferredSave.Cancel();
            ResetVisit();
            Reputation.NewGame(scenarioId);
            StashFind.NewGame(scenarioId);
            ScavengerStart.OnNewGameStarted(scenarioId);
            // Explicit debug requests take precedence over the scenario's default starting value.
            Reputation.ApplyPendingDebugPoints(false);
            StashFind.SyncDailyWindow();
            RegionalEvents.ResetSession();
            RegionalEvents.Sync();
            ScavengerOuting.Sync();
            RefreshViews();
        }

        private static void AfterhourEventChanged(string locationId, string eventId)
        {
            if (locationId == "dumping_ground") RefreshViews();
        }

        private static void RefreshViews()
        {
            RegionalEvents.Sync();
            Picker.RefreshIfOpen();
            NativeSettings.RefreshIfOpen();
            LootNoticeView.RefreshIfOpen();
        }
    }
}
