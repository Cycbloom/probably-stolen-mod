using System;
using UnityEngine;

namespace DumpingGroundMap
{
    internal static class OutingRules
    {
        internal const int Fee = 20;
        internal const int ExtraAttempts = 2;

        internal static bool Unlocked(int reputation) { return reputation >= PerkCatalog.Perk100Threshold; }
        internal static int BenefitDay(int bookingDay) { return bookingDay < 0 ? -1 : checked(bookingDay + 1); }
        internal static bool Active(int today, int benefitDay) { return today >= 0 && today == benefitDay; }
        internal static int Attempts(int original, bool active)
        {
            // Preserve native conditions that prevent scavenging entirely.
            return active && original > 0 ? original + ExtraAttempts : original;
        }

        internal static void Configure(StoreService service, int reputation)
        {
            service.cost = Fee;
            service.tooltip = ScavengerOuting.TooltipKey;
            service.unlocked = Unlocked(reputation);
            service.introduced = service.unlocked;
            service.isMandatory = false;
            service.totalCooldown = service.currentCooldown = 0;
            if (!service.unlocked) service.isUsing = false;
        }
    }

    /// <summary>A one-night booking on the native end-of-day service sheet.</summary>
    internal static class ScavengerOuting
    {
        internal const string ServiceId = Core.Id + ".scavenger_outing";
        internal const string BenefitDayKey = "scav_outing_day";
        internal const string TooltipKey = Core.Id + ".scavenger_outing_tooltip";

        internal static string Description
        {
            get
            {
                return Copy.Get("scavengerouting_pay_20_credits_to_explore_with_scavengers");
            }
        }

        internal static bool Active
        {
            get
            {
                if (!Core.Enabled || !PlayerStore.HasInstance) return false;
                int today = Today();
                return OutingRules.Active(today, ModHelper.GetModDataInt(Core.Id, BenefitDayKey, -1));
            }
        }

        internal static int Today()
        {
            try { return StoreStation.GetDayCounter(); }
            catch { return -1; }
        }

        internal static StoreService Sync()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance) return null;
            PlayerStore store = PlayerStore.Instance;
            if (store.storeService == null) return null;
            StoreService service = store.storeService.Find(s => s != null && s.id == ServiceId);
            if (service == null)
            {
                service = new StoreService();
                service.id = ServiceId;
                store.storeService.Add(service);
            }
            OutingRules.Configure(service, Reputation.Points);
            service.displayName = Copy.Get("scavengerouting_scavenger_outing");
            // Native BuildServiceRow resolves tooltip through LocHelper.GetLocalizedUI.
            service.description = Description;
            return service;
        }

        /// <summary>Runs before the native loop that charges selected, unlocked services.</summary>
        internal static void BeforeSleep() { Sync(); }

        /// <summary>The native payment loop has finished when this hook is fired.</summary>
        internal static void AfterServicePayments()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance) return;
            PlayerStore store = PlayerStore.Instance;
            StoreService service = store.storeService.Find(s => s != null && s.id == ServiceId);
            if (service == null || !service.IsActive()) return;
            // No second charge: PlayerStore.EndNight already paid this exact row.
            service.isUsing = false;
            int benefitDay = OutingRules.BenefitDay(Today());
            if (benefitDay < 0) return;
            ModHelper.SetModDataInt(Core.Id, BenefitDayKey, benefitDay);
            store.AddNightLog(Copy.Get("scavengerouting_paid_20_credits_for_a_scavenger_outing"));
            Debug.Log("[DumpingGroundMap] scavenger outing booked for day " + benefitDay);
        }

        internal static void Remove()
        {
            if (!PlayerStore.HasInstance) return;
            PlayerStore store = PlayerStore.Instance;
            if (store.storeService != null) store.storeService.RemoveAll(s => s != null && s.id == ServiceId);
        }
    }
}
