using System;
using System.Collections.Generic;
using System.IO;
using DumpingGroundMap;
using HarmonyLib;

internal static class AssetDiagnosticsTests
{
    private const string Owner = "test.dumping-ground.assets";
    private static int checks;
    private static readonly List<string> warnings = new List<string>();
    private static readonly List<string> info = new List<string>();

    internal static int Run(Dictionary<string, Dictionary<string, string>> actualLocales)
    {
        string paper = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../Paper"));
        var keys = new List<string>();
        foreach (string path in System.IO.Directory.GetFiles(paper, "*.png")) keys.Add(Path.GetFileNameWithoutExtension(path));
        Check(AssetDiagnostics.Check(keys, actualLocales).Count == 0, "shipped artwork and native-parsed CSV snapshots pass");

        var missing = new List<string>(keys);
        missing.Remove("dumping_ground_gate");
        missing.Remove("dumping_ground_landmarks");
        List<string> issues = AssetDiagnostics.Check(missing, actualLocales);
        Check(issues.Count == 1 && issues[0].Contains("(2)"), "missing sprites are grouped");
        Check(issues[0].Contains("Textures/Paper/dumping_ground_gate.png")
            && issues[0].Contains("Textures/Paper/dumping_ground_landmarks.png"), "missing art includes actionable paths");
        missing = new List<string>(keys);
        missing.Remove("dumping_ground_gate");
        missing.Add("DUMPING_GROUND_GATE");
        Check(AssetDiagnostics.Check(missing, actualLocales).Count == 1, "asset filename keys retain native case sensitivity");

        var locales = Clone(actualLocales);
        locales.Remove("en");
        issues = AssetDiagnostics.Check(keys, locales);
        Check(issues.Count == 1 && issues[0].Contains("Localization/en.csv"), "missing English file produces one file warning");
        locales = Clone(actualLocales);
        locales["zh"].Clear();
        issues = AssetDiagnostics.Check(keys, locales);
        Check(issues.Count == 1 && issues[0].Contains("Localization/zh.csv"), "empty Chinese file avoids hundreds of key warnings");
        locales["zh"] = null;
        Check(AssetDiagnostics.Check(keys, locales).Count == 1, "null locale table is treated as missing");
        Check(AssetDiagnostics.Check(null, null).Count == 3, "missing resource directories produce three grouped warnings");

        locales = Clone(actualLocales);
        locales["en"].Remove("region_gate_name");
        locales["en"].Add("test_english_only", "English only");
        issues = AssetDiagnostics.Check(keys, locales);
        Check(issues.Count == 2, "key mismatches are checked in both directions");
        Check(issues[0].Contains("en.csv") && issues[0].Contains("region_gate_name"), "missing English key is named");
        Check(issues[1].Contains("zh.csv") && issues[1].Contains("test_english_only"), "missing Chinese key is named");

        locales = Clone(actualLocales);
        locales["zh"]["region_gate_name"] = null;
        locales["zh"]["region_gate_detail"] = "";
        locales["zh"]["reputation_title"] = " \n ";
        issues = AssetDiagnostics.Check(keys, locales);
        Check(issues.Count == 1 && issues[0].Contains("empty Localization/zh.csv values (3)"), "null, empty and whitespace values are grouped");
        Check(locales["zh"]["region_gate_name"] == null && locales["zh"].Count == actualLocales["zh"].Count,
            "diagnostics never replace or remove loaded data");
        locales = Clone(actualLocales);
        locales.Add("fr", new Dictionary<string, string>());
        Check(AssetDiagnostics.Check(keys, locales).Count == 0, "additional optional locales are not required to be complete");

        for (int i = 0; i < 50; ++i) locales["en"].Add("bulk_" + i.ToString("00"), "extra");
        issues = AssetDiagnostics.Check(keys, locales);
        Check(issues.Count == 1 && issues[0].Contains("(50)"), "large missing-key sets emit one summary");
        Check(issues[0].Contains("bulk_04") && !issues[0].Contains("bulk_05") && issues[0].EndsWith(", ..."),
            "warning samples are sorted, bounded and indicate omitted entries");

        HookBoundary(actualLocales);
        Console.WriteLine("PASS: " + checks + " asset checks (real package keys/locales, missing files, partial values, bounded warnings and callback filtering)");
        return checks;
    }

    private static void HookBoundary(Dictionary<string, Dictionary<string, string>> actualLocales)
    {
        var harmony = new Harmony(Owner);
        bool enabled = Core.Enabled;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Info"), prefix: new HarmonyMethod(typeof(AssetDiagnosticsTests), "InfoPrefix"));
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Warning"), prefix: new HarmonyMethod(typeof(AssetDiagnosticsTests), "WarningPrefix"));
            warnings.Clear(); info.Clear();
            Core.Enabled = true;
            AssetDiagnostics.OnLoaded(null);
            AssetDiagnostics.OnLoaded(new LoadedMod());
            AssetDiagnostics.OnLoaded(new LoadedMod { Manifest = new ModManifest { ID = "other.mod" } });
            Check(warnings.Count == 0 && info.Count == 0, "invalid and unrelated mod notifications are ignored");

            var own = new LoadedMod { Manifest = new ModManifest { ID = Core.Id }, Localization = actualLocales };
            Core.Enabled = false;
            AssetDiagnostics.OnLoaded(own);
            Check(warnings.Count == 0 && info.Count == 0, "disabled mod does not validate or log");
            Core.Enabled = true;
            own.PaperSprites["dumping_ground_gate"] = null;
            AssetDiagnostics.OnLoaded(own);
            Check(warnings.Count == 1 && warnings[0].Contains("missing paper sprites (12)"), "null sprite entries cannot mask failed loads");
            Check(info.Count == 1 && info[0].Contains("paper sprites=0") && info[0].Contains("warnings=1"), "completion summary reports actual loaded results");
            Check(info[0].Contains("en=" + actualLocales["en"].Count) && info[0].Contains("zh=" + actualLocales["zh"].Count),
                "completion uses loaded locale counts without hardcoding 351");

            warnings.Clear(); info.Clear();
            own.PaperSprites = null;
            own.Localization = null;
            AssetDiagnostics.OnLoaded(own);
            Check(warnings.Count == 3 && info.Count == 1, "a failed snapshot reports errors without throwing");
            Check(info[0].Contains("en=0, zh=0, warnings=3"), "missing tables have zero counts in completion summary");
        }
        finally { AssetDiagnostics.Clear(); Core.Enabled = enabled; harmony.UnpatchAll(Owner); }
    }

    private static Dictionary<string, Dictionary<string, string>> Clone(Dictionary<string, Dictionary<string, string>> source)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in source) result.Add(locale.Key, new Dictionary<string, string>(locale.Value));
        return result;
    }
    private static bool InfoPrefix(object __0) { info.Add((string)__0); return false; }
    private static bool WarningPrefix(object __0) { warnings.Add((string)__0); return false; }
    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception(message);
    }
}
