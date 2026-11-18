using System;
using System.Collections.Generic;
using System.Reflection;
using DumpingGroundMap;
using HarmonyLib;

internal static class DebugToolsTests
{
    private const string Owner = "test.dumping-ground.debug";
    private static readonly Dictionary<string, Action> buttons = new Dictionary<string, Action>();
    private static readonly List<string> info = new List<string>();
    private static readonly List<string> warnings = new List<string>();
    private static int checks, additions, removals;
    private static bool validRegistration = true;

    internal static int Run(Dictionary<string, Dictionary<string, string>> actualLocales)
    {
        var harmony = new Harmony(Owner);
        bool enabled = Core.Enabled;
        FieldInfo total = AccessTools.Field(typeof(Reputation), "totalValue");
        object originalTotal = total.GetValue(null);
        PropertyInfo chosen = AccessTools.Property(typeof(Picker), "ChosenRegion");
        object originalChosen = chosen.GetValue(null, null);
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ModHelper), "AddDebugFunction"), prefix: new HarmonyMethod(typeof(DebugToolsTests), "Add"));
            harmony.Patch(AccessTools.Method(typeof(ModHelper), "RemoveDebugFunctions"), prefix: new HarmonyMethod(typeof(DebugToolsTests), "Remove"));
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Info"), prefix: new HarmonyMethod(typeof(DebugToolsTests), "Info"));
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Warning"), prefix: new HarmonyMethod(typeof(DebugToolsTests), "Warning"));
            // Detect unintended persistence access even when it would otherwise succeed.
            harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerStore), "Instance"), prefix: new HarmonyMethod(typeof(DebugToolsTests), "NoStore"));
            AssetDiagnostics.Clear();
            Core.Enabled = true;
            DebugTools.Register();
            Check(buttons.Count == 2 && validRegistration, "two callbacks belong to our mod's native Special column");
            DebugTools.Register();
            Check(additions == 2, "repeated registration cannot duplicate callbacks");
            Action assets = buttons["DGM Check Assets"];
            Action state = buttons["DGM Dump State"];

            assets();
            Check(warnings.Count == 1 && warnings[0].Contains("not ready"), "pre-load check explicitly reports unavailable snapshot");
            Check(!AssetDiagnostics.Ready && info.Count == 0, "checking before load does not invent a loaded state");
            var own = new LoadedMod { Manifest = new ModManifest { ID = Core.Id }, Localization = actualLocales };
            AssetDiagnostics.OnLoaded(own);
            Check(AssetDiagnostics.Ready, "our completed load enables rechecking");
            warnings.Clear(); info.Clear();
            AssetDiagnostics.OnLoaded(new LoadedMod { Manifest = new ModManifest { ID = "other.mod" } });
            assets();
            Check(warnings.Count == 1 && warnings[0].Contains("missing paper sprites (12)")
                && info.Count == 1 && info[0].Contains("en=" + actualLocales["en"].Count),
                "foreign notifications cannot replace the snapshot used by the button");
            own.Localization = null;
            warnings.Clear(); info.Clear();
            assets();
            Check(warnings.Count == 3 && info[0].Contains("en=0, zh=0"), "recheck inspects current loaded dictionaries without disk access");

            total.SetValue(null, 750L);
            chosen.SetValue(null, null, null);
            info.Clear(); warnings.Clear();
            state();
            Check(info.Count == 1 && info[0].Contains("cached mod state") && info[0].Contains("reputation=7, total_value=750"),
                "state button reports cached progress without accessing PlayerStore");
            Check(info[0].Contains("chosen_region=none, unlocked=[gate, refuse]"), "unselected region and locked areas are clear");
            total.SetValue(null, 6000L);
            chosen.SetValue(null, Region.All[1], null);
            info.Clear();
            state();
            Check(info[0].Contains("chosen_region=scrap")
                && info[0].Contains("unlocked=[gate, scrap, refuse, med, weapons, electronics, vehicles, water]"),
                "region report follows current thresholds and selection");
            Check((long)total.GetValue(null) == 6000L && ReferenceEquals(chosen.GetValue(null, null), Region.All[1]),
                "diagnostics do not mutate progress or selection");

            Core.Enabled = false;
            info.Clear(); warnings.Clear();
            state(); assets();
            Check(info.Count == 0 && warnings.Count == 0, "retained callbacks stay inert while disabled");
            AssetDiagnostics.Clear();
            DebugTools.Unregister();
            Check(buttons.Count == 0 && removals == 1 && !AssetDiagnostics.Ready, "cleanup removes only our buttons and releases snapshot");
            Core.Enabled = true;
            DebugTools.Register();
            Check(buttons.Count == 2 && additions == 4, "registering after cleanup recreates exactly two buttons");
            info.Clear(); warnings.Clear();
            buttons["DGM Check Assets"]();
            Check(warnings.Count == 1 && warnings[0].Contains("not ready"), "reenabling cannot reuse a previous session's assets");
            Console.WriteLine("PASS: " + checks + " debug-tool checks (native callback registration, read-only state, asset recheck and cleanup)");
            return checks;
        }
        finally
        {
            DebugTools.Unregister();
            AssetDiagnostics.Clear();
            total.SetValue(null, originalTotal);
            chosen.SetValue(null, originalChosen, null);
            Core.Enabled = enabled;
            harmony.UnpatchAll(Owner);
        }
    }

    private static bool Add(string __0, string __1, string __2, Action __3, bool __4)
    {
        validRegistration &= __0 == Core.Id && __1 == ModHelper.DEBUG_CATEGORY_SPECIAL && !__4;
        ++additions;
        buttons.Add(__2, __3);
        return false;
    }
    private static bool Remove(string __0, ref int __result)
    {
        if (__0 != Core.Id) throw new Exception("Attempted to remove another mod's buttons");
        ++removals; __result = buttons.Count; buttons.Clear(); return false;
    }
    private static bool Info(object __0) { info.Add((string)__0); return false; }
    private static bool Warning(object __0) { warnings.Add((string)__0); return false; }
    private static bool NoStore() { throw new Exception("Diagnostics must never access PlayerStore.Instance"); }
    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception(message);
    }
}
