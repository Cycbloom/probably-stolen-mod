using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DumpingGroundMap;
using HarmonyLib;

internal static class SoundFeedbackTests
{
    private const string Owner = "test.dumping-ground.sound";
    private static int checks, saves, notices, queues, spawned, warnings;
    private static bool failAudio;
    private static string store;
    private static long stored;
    private static readonly List<string> played = new List<string>();

    internal static int Run()
    {
        var harmony = new Harmony(Owner);
        bool enabled = Core.Enabled;
        FieldInfo total = AccessTools.Field(typeof(Reputation), "totalValue");
        FieldInfo loaded = AccessTools.Field(typeof(Reputation), "loadedStore");
        FieldInfo current = AccessTools.Field(typeof(Picker), "current");
        object oldTotal = total.GetValue(null), oldLoaded = loaded.GetValue(null), oldCurrent = current.GetValue(null);
        string[] fields = { "tripHit", "hitDay", "adaptiveHitDay", "hitRegion", "newsDay", "newsFired" };
        var oldStash = new Dictionary<string, object>();
        foreach (string name in fields) oldStash[name] = AccessTools.Field(typeof(StashFind), name).GetValue(null);
        try
        {
            // Capture our Unity boundary; native ECall methods cannot run in standalone CLR.
            Patch(harmony, typeof(SoundFeedback), "PlayNative", "Native");
            Patch(harmony, typeof(ModLog), "Log", "Ignore");
            Patch(harmony, typeof(ModLog), "Warning", "Warning");
            Patch(harmony, typeof(Reputation), "StoreKey", "StoreKey");
            Patch(harmony, typeof(Reputation), "StoredValue", "ReadValue");
            Patch(harmony, typeof(Reputation), "WriteStored", "WriteValue");
            Patch(harmony, typeof(Reputation), "ReadGainMultiplier", "Multiplier");
            Patch(harmony, typeof(Reputation), "ApplyPendingDebugPoints", "Ignore");
            Patch(harmony, typeof(Reputation), "get_HasStore", "HasStore");
            Patch(harmony, typeof(StashFind), "AddJackpot", "Spawn");
            Patch(harmony, typeof(StashFind), "SaveState", "Save");
            Patch(harmony, typeof(StashFind), "NotifyFind", "Notice");
            Patch(harmony, typeof(StashFind), "QueueNewsForTomorrow", "Queue");

            Core.Enabled = false;
            SoundFeedback.Jackpot();
            Check(played.Count == 0, "disabled mod cannot reach native playback");
            Core.Enabled = true;

            // Exercise actual selection, including initialization and locked previews.
            var picker = (Picker)FormatterServices.GetUninitializedObject(typeof(Picker));
            current.SetValue(null, picker); total.SetValue(null, 0L);
            MethodInfo select = AccessTools.Method(typeof(Picker), "Select");
            select.Invoke(picker, new object[] { null });
            Region gate = Region.All[0], locked = null;
            foreach (Region region in Region.All) if (region.RequiredReputation > 0) { locked = region; break; }
            select.Invoke(picker, new object[] { locked });
            Check(played.Count == 0, "opening and locked previews stay silent");
            select.Invoke(picker, new object[] { gate });
            Check(played.Count == 1 && played[0] == "1:click_vintage", "first available selection uses the vanilla click");
            select.Invoke(picker, new object[] { gate });
            AccessTools.Method(typeof(Picker), "Hover").Invoke(picker, new object[] { locked });
            Check(played.Count == 1, "repeat clicks and hovering cannot duplicate selection audio");
            current.SetValue(null, null);
            select.Invoke(picker, new object[] { locked });
            Check(played.Count == 1, "stale picker callbacks are silent");

            played.Clear(); store = "sound-test"; loaded.SetValue(null, store);
            stored = 1900; total.SetValue(null, stored);
            Reputation.Add(100);
            Check(played.Count == 1 && played[0] == "1:module_insert", "earned reputation crossing 20 plays one unlock");
            Reputation.Add(100);
            Check(played.Count == 1, "further earnings above the same threshold stay silent");
            total.SetValue(null, 1900L); stored = 1900;
            Reputation.Add(5000);
            Check(played.Count == 2, "crossing several regional thresholds plays only once");
            total.SetValue(null, 7900L); stored = 7900;
            Reputation.Add(100);
            Check(played.Count == 2, "non-region perk at 80 does not play an area unlock");
            Reputation.Add(0);
            Reputation.SetPoints(100);
            stored = 6000; Reputation.Reload();
            Check(played.Count == 2 && Reputation.Points == 60, "debug changes and reload do not announce unlocks");
            loaded.SetValue(null, "another-save"); total.SetValue(null, 0L); stored = 6400;
            Reputation.Add(100);
            Check(played.Count == 2 && Reputation.Points == 65, "lazy save switch measures growth after reading the save");

            Region prizeRegion = null;
            foreach (Region region in Region.All) if (StashTemplate.For(region.Id) != null) { prizeRegion = region; break; }
            var items = new List<GameItem>();
            MethodInfo award = AccessTools.Method(typeof(StashFind), "TryAwardJackpot");
            AccessTools.Field(typeof(StashFind), "tripHit").SetValue(null, false);
            AccessTools.Field(typeof(StashFind), "hitDay").SetValue(null, -1);
            AccessTools.Field(typeof(StashFind), "newsDay").SetValue(null, -1);
            played.Clear(); spawned = 0;
            object[] args = { items, StashTemplate.For(prizeRegion.Id), prizeRegion, 12 };
            Check(!(bool)award.Invoke(null, args), "zero delivered items is not a jackpot");
            Check(saves == 0 && queues == 0 && notices == 0 && played.Count == 0,
                "failed award cannot save, queue news, notify or play");
            Check(!(bool)AccessTools.Field(typeof(StashFind), "tripHit").GetValue(null)
                && (int)AccessTools.Field(typeof(StashFind), "hitDay").GetValue(null) == -1
                && (int)AccessTools.Field(typeof(StashFind), "newsDay").GetValue(null) == -1,
                "failed award preserves the trip and future-news state");
            spawned = 1;
            Check((bool)award.Invoke(null, args) && items.Count == 1, "a partial successful payout counts as a win");
            Check(saves == 1 && queues == 1 && notices == 1 && played.Count == 1 && played[0] == "3:scanner_activate",
                "paid jackpot saves and announces once on source 3");
            Check((int)AccessTools.Field(typeof(StashFind), "newsDay").GetValue(null) == 13,
                "failed news queue retains tomorrow for the existing fallback");
            Check(!(bool)award.Invoke(null, args) && items.Count == 1 && played.Count == 1,
                "same-trip retries cannot pay or play twice");

            AccessTools.Field(typeof(StashFind), "tripHit").SetValue(null, false);
            int beforeWarnings = warnings; failAudio = true;
            Check((bool)award.Invoke(null, args) && items.Count == 2 && notices == 2 && saves == 2,
                "audio failure cannot cancel an earned prize or its notice");
            Check(warnings == beforeWarnings + 1, "audio failure is diagnosed without escaping");
            Console.WriteLine("PASS: " + checks + " sound-feedback checks (native routes, selection, earned unlocks, save isolation and successful payouts)");
            return checks;
        }
        finally
        {
            harmony.UnpatchAll(Owner);
            Core.Enabled = enabled; total.SetValue(null, oldTotal); loaded.SetValue(null, oldLoaded); current.SetValue(null, oldCurrent);
            foreach (var pair in oldStash) AccessTools.Field(typeof(StashFind), pair.Key).SetValue(null, pair.Value);
        }
    }

    private static void Patch(Harmony harmony, Type type, string target, string prefix)
    {
        try { harmony.Patch(AccessTools.Method(type, target), prefix: new HarmonyMethod(typeof(SoundFeedbackTests), prefix)); }
        catch (Exception ex) { throw new Exception("Cannot patch test boundary " + type.Name + "." + target, ex); }
    }
    private static bool Native(string __0, bool __1, ref bool __result)
    {
        if (failAudio) throw new InvalidOperationException("test unavailable audio");
        played.Add((__1 ? "3:" : "1:") + __0); __result = true; return false;
    }
    private static bool Ignore() { return false; }
    private static bool Warning() { ++warnings; return false; }
    private static bool StoreKey(ref string __result) { __result = store; return false; }
    private static bool ReadValue(ref long __result) { __result = stored; return false; }
    private static bool WriteValue(long __0, ref long __result) { stored = __0; __result = stored; return false; }
    private static bool Multiplier(ref string difficultyId, ref float __result) { difficultyId = "normal"; __result = 1f; return false; }
    private static bool HasStore(ref bool __result) { __result = true; return false; }
    private static bool Spawn(List<GameItem> __0, ref int __result)
    {
        for (int i = 0; i < spawned; ++i) __0.Add(null);
        __result = spawned; return false;
    }
    private static bool Save() { ++saves; return false; }
    private static bool Notice() { ++notices; return false; }
    private static bool Queue(ref bool __result) { ++queues; __result = false; return false; }
    private static void Check(bool value, string message)
    {
        ++checks; if (!value) throw new Exception(message);
    }
}
