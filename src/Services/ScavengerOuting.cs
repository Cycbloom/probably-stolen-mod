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
            service.displayName = Copy.Text("与拾荒客同行", "Scavenger Outing");
            service.description = service.tooltip = Copy.Text(
                "花 20 信用点，约拾荒客明天一起探险。次日当天拾荒次数 +2，拾荒时不会受轻伤或重伤。每次预约只管一天，结算后自动取消勾选；想再同行，头晚重新预约。",
                "Pay 20 credits to explore with scavengers tomorrow. Gain 2 extra scavenging attempts and no minor or major scavenging wounds that day. Each booking lasts one day and unticks after settlement; book again the night before another outing.");
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
            store.AddNightLog(Copy.Text(
                "已付 20 信用点约好拾荒客同行。今天多 2 次拾荒，拾荒时不会受轻伤或重伤。",
                "Paid 20 credits for a scavenger outing. Today grants 2 extra searches and no minor or major scavenging wounds."));
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
