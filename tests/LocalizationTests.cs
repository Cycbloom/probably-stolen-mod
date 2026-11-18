using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DumpingGroundMap;
using HarmonyLib;

// Unity's selected-locale service cannot run in a standalone CLR process.
// Use the game's actual CSV value parser, then replace only Copy.Get while
// exercising our data/providers under two selected locales in the same process.
internal sealed class LocalizationTests : IDisposable
{
    private const string Owner = "test.dumping-ground.localization";
    private static readonly Dictionary<string, Dictionary<string, string>> tables =
        new Dictionary<string, Dictionary<string, string>>();
    private static string locale;
    private static int checks;
    private static bool ready;
    private readonly Harmony harmony = new Harmony(Owner);

    internal LocalizationTests()
    {
        MethodInfo getter = AccessTools.Method(typeof(Copy), "Get");
        harmony.Patch(getter, prefix: new HarmonyMethod(typeof(LocalizationTests), "Get"));
        // Static data must initialize before localization assets are available.
        ready = false;
        Check(Region.All.Length == 8 && RegionalEvent.All.Length == 8, "region/event data initializes without translations");
        Check(PerkCatalog.All.Length == 5 && MilestoneNotes.All.Length == 3, "perk/note data initializes without translations");
        foreach (Region region in Region.All)
        {
            Check(region.Loot.Count > 0, "loot data initializes without translations");
            Check(StashTemplate.For(region.Id) != null, "stash data initializes without translations");
            Check(StashFind.PlaceTalk.For(region.Id).Keys.Length > 0, "place data initializes without translations");
        }
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../.."));
        tables.Clear();
        tables.Add("zh", Read(Path.Combine(root, "Localization/zh.csv")));
        tables.Add("en", Read(Path.Combine(root, "Localization/en.csv")));
        ready = true;
        locale = "zh";
    }

    private static bool Get(string key, object[] args, ref string __result)
    {
        if (!ready) throw new Exception("Localization was requested before assets loaded: " + key);
        if (string.IsNullOrEmpty(key)) { __result = string.Empty; return false; }
        string value;
        if (!tables[locale].TryGetValue(key, out value)) throw new Exception("Missing localization key: " + key);
        __result = args == null || args.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, args);
        return false;
    }

    private static Dictionary<string, string> Read(string path)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        MethodInfo parse = AccessTools.Method(typeof(ModLoader), "ParseCsvValue");
        foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            int comma = line.IndexOf(',');
            Check(comma > 0, "CSV row has a key");
            string key = line.Substring(0, comma).Trim();
            string value = (string)parse.Invoke(null, new object[] { line.Substring(comma + 1) });
            table.Add(key, value);
            Check(value.Length > 0 && !value.Contains("\\n"), "native parser decodes value/newlines: " + key);
        }
        return table;
    }

    internal int Run()
    {
        Check(tables["zh"].Count == tables["en"].Count, "locale key counts match");
        foreach (string key in tables["zh"].Keys) Check(tables["en"].ContainsKey(key), "locale keys match: " + key);
        Region gate = Region.All[0];
        PerkDefinition perk = PerkCatalog.For(100);
        string[] legacyZh = { "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下" };
        string[] legacyEn = { "Top left", "Top middle", "Top right", "Middle left", "Center", "Middle right", "Bottom left", "Bottom middle", "Bottom right" };
        foreach (string language in new[] { "zh", "en", "zh" })
        {
            locale = language;
            Check(gate.Name == tables[language]["region_gate_name"], "same Region responds to locale change");
            Check(perk.Name == tables[language][PerkCatalog.Perk100NameKey], "same Perk responds to locale change");
            string[] alignments = Tuning.GroundAlignLabels;
            string[] specialties = Tuning.SpecialtyDisplayLabels;
            for (int i = 0; i < alignments.Length; ++i)
            {
                Check(alignments[i] == tables[language]["ground_align_" + i], "alignment display is refreshed");
                Check(Tuning.GroundAlignIndex(legacyZh[i]) == i && Tuning.GroundAlignIndex(legacyEn[i]) == i,
                    "both legacy config languages preserve every alignment");
            }
            for (int i = 0; i < specialties.Length; ++i)
                Check(specialties[i] == tables[language]["specialty_tier_" + i], "specialty display is refreshed");
            foreach (Region region in Region.All)
            {
                Check(region.Name.Length > 0 && region.Detail.Length > 0, "regional text resolves");
                for (int i = 0; i < region.Loot.CategoryCount; ++i)
                    Check(region.Loot.CategoryName(i).Length > 0, "category text resolves");
                Check(RegionalEvent.For(region.Id).Tooltip.Contains("\n"), "event text resolves with newlines");
                StashTemplate stash = StashTemplate.For(region.Id);
                Check(string.Format(CultureInfo.InvariantCulture, stash.Notice, "PLACE").Contains("PLACE"), "stash placeholder works");
                Check(stash.NewsName.Length > 0 && stash.NewsDescription.Length > 0 && stash.DisplayName.Length > 0, "news text resolves");
                StashFind.PlaceTalk talk = StashFind.PlaceTalk.For(region.Id);
                for (int i = 0; i < talk.Keys.Length; ++i) Check(talk.Text(i) == tables[language][talk.Keys[i]], "place text resolves");
            }
            foreach (PerkDefinition item in PerkCatalog.All)
                Check(item.Name.Length > 0 && item.Description.Length > 0, "perk text resolves");
            foreach (MilestoneNote note in MilestoneNotes.All)
                Check(note.Title.Length > 0 && !note.Body(true).Contains("{0}"), "letter and service placeholders resolve");
            bool enabled = Core.Enabled;
            Core.Enabled = true;
            try
            {
                MethodInfo prefix = AccessTools.Method(typeof(ModConfigHeaderPatch), "Prefix");
                object[] args = { Tuning.GroundSection, "section_" + Tuning.GroundSection };
                prefix.Invoke(null, args);
                Check((string)args[0] == tables[language][Tuning.GroundSection], "section header resolves at render time");
                args = new object[] { "Other mod", "section_other" };
                prefix.Invoke(null, args);
                Check((string)args[0] == "Other mod", "unrelated headers remain untouched");
            }
            finally { Core.Enabled = enabled; }
        }
        Check(Copy.Get("milestone_100_body", 20, 2).Contains("20 信用点"), "Chinese format arguments reach translations");
        Console.WriteLine("PASS: " + checks + " localization checks (native CSV parser, delayed lookup, zh/en/zh, settings and legacy alignment)");
        return checks;
    }

    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception(message);
    }

    public void Dispose() { harmony.UnpatchAll(Owner); }
}
