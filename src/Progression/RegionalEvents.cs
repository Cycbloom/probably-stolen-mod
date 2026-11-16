using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Schedules are stored with the run; UI, rolls and wound checks share this state.</summary>
    internal static class RegionalEvents
    {
        private const string WeekKey = "regional_event_week";
        private const string DaysKey = "regional_event_days";
        private const string SeedKey = "regional_event_seed";
        private static PlayerStore owner;
        private static int observedDay = -1, observedMask = -1;
        private static int[] days;
        private static bool failed;

        internal static void ResetSession()
        {
            owner = null; observedDay = observedMask = -1; days = null; failed = false;
        }

        internal static void Sync()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance || StoreStation.Instance == null || EmporiumEntry.Instance == null) return;
            PlayerStore current = PlayerStore.Instance;
            if (!ReferenceEquals(owner, current)) { ResetSession(); owner = current; }
            int today = StoreStation.GetDayCounter();
            if (today < 1) return;
            int mask = 0;
            for (int i = 0; i < Region.All.Length; ++i) if (Region.All[i].Unlocked) mask |= 1 << i;
            if (today == observedDay && mask == observedMask && days != null) return;
            try
            {
                // Native IsScheduledOn uses dayCounter % 7, Sunday = 0.
                int week = today - today % 7;
                int savedWeek = ModHelper.GetModDataInt(Core.Id, WeekKey, -1);
                string stored = ModHelper.GetModData(Core.Id, DaysKey, "");
                int[] previous = savedWeek == week ? Parse(stored) : null;
                int seed = ModHelper.GetModDataInt(Core.Id, SeedKey, 0);
                if (seed == 0)
                {
                    uint hash = 2166136261u;
                    foreach (char c in current.runID ?? "dumping-ground") hash = unchecked((hash ^ c) * 16777619u);
                    seed = (int)(hash & 0x7fffffffu);
                    if (seed == 0) seed = 1;
                    ModHelper.SetModDataInt(Core.Id, SeedKey, seed);
                }
                // A mid-week unlock joins from tomorrow; existing dates never move.
                days = RegionalEventSchedule.Plan(week, previous == null ? today : today + 1, mask, seed, previous);
                string encoded = Encode(days);
                if (savedWeek != week || encoded != stored)
                {
                    ModHelper.SetModDataInt(Core.Id, WeekKey, week);
                    ModHelper.SetModData(Core.Id, DaysKey, encoded);
                    DeferredSave.Run("regional event schedule");
                }
                observedDay = today; observedMask = mask; failed = false;
            }
            catch (Exception ex)
            {
                days = null;
                if (!failed) Debug.LogWarning("[DumpingGroundMap] regional events: " + ex.Message);
                failed = true;
            }
        }

        internal static RegionalEvent ActiveFor(Region region)
        {
            if (!Core.Enabled || region == null || !region.Unlocked || !PlayerStore.HasInstance || EmporiumEntry.Instance == null) return null;
            Sync();
            if (days == null || observedDay != StoreStation.GetDayCounter()) return null;
            int i = Array.IndexOf(Region.All, region);
            return i >= 0 && days[i] == observedDay ? RegionalEvent.For(region.Id) : null;
        }

        internal static string TodaySummary()
        {
            List<string> names = new List<string>();
            foreach (Region region in Region.All)
            {
                RegionalEvent effect = ActiveFor(region);
                if (effect != null) names.Add(region.Name + " · " + effect.Title);
            }
            return names.Count > 0 ? Copy.Text("今日事件：", "Today: ") + string.Join(" / ", names.ToArray())
                : Copy.Text("今日没有区域事件", "No regional events today");
        }

        internal static string Details(Region region, EffectiveTable table)
        {
            RegionalEvent effect = table.RegionalEvent;
            if (effect == null) return "";
            return Copy.Text("今日：", "Today: ") + effect.Title + Copy.Text("（今日结束）", " (ends today)")
                + "\n" + effect.Description + "\n"
                + Copy.Text("主掉落期望 ", "Main-drop EV ") + table.Ev.ToString("0.00", CultureInfo.InvariantCulture)
                + Copy.Text("（事件 ", " (event ") + table.EventEv.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                + Copy.Text(" 信用点/件）", " credits/item)");
        }

        private static int[] Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] parts = raw.Split(',');
            if (parts.Length != Region.All.Length) return null;
            int[] result = new int[parts.Length];
            for (int i = 0; i < result.Length; ++i)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i])) return null;
            return result;
        }
        private static string Encode(int[] values)
        {
            return string.Join(",", Array.ConvertAll(values, n => n.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
