using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using DumpingGroundMap;
using HarmonyLib;

internal static class LootJsonTests
{
    private const string Owner = "test.dumping-ground.loot-json";
    private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
    private static readonly List<string> logs = new List<string>();
    private static readonly List<string> warnings = new List<string>();
    private static int checks;

    internal static int Run()
    {
        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../.."));
        var fingerprints = serializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(repo, "tests/Fixtures/loot-1.0.8-fingerprints.json")));
        VerifyFingerprints(LootDataSnapshot.Bundled(), fingerprints, "bundled defaults");
        var harmony = new Harmony(Owner);
        var originals = new Dictionary<FieldInfo, object>();
        bool enabled = Core.Enabled;
        try
        {
            foreach (string name in new[] { "tables", "groups", "vanillaTables", "vanillaGroups", "journal", "warned", "dirty", "vanillaEverLoaded", "tablesLoadedThisScene", "groupsLoadedThisScene" })
            {
                FieldInfo field = AccessTools.Field(typeof(LootRegistry), name);
                originals.Add(field, field.GetValue(null));
                field.SetValue(null, Activator.CreateInstance(field.FieldType));
            }
            var vanillaTables = (Dictionary<string, List<LootEntry>>)AccessTools.Field(typeof(LootRegistry), "vanillaTables").GetValue(null);
            var vanillaGroups = (Dictionary<string, List<LootEntry>>)AccessTools.Field(typeof(LootRegistry), "vanillaGroups").GetValue(null);
            vanillaTables.Add("junkTable", new List<LootEntry> { new LootEntry("junk", 100f, null) });
            vanillaGroups.Add("dumpingGroundTG", new List<LootEntry> { new LootEntry("junkTable", 100f, null) });
            AccessTools.Field(typeof(LootRegistry), "vanillaEverLoaded").SetValue(null, true);
            AccessTools.Field(typeof(LootRegistry), "dirty").SetValue(null, true);
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Info"), prefix: new HarmonyMethod(typeof(LootJsonTests), "Log"));
            harmony.Patch(AccessTools.Method(typeof(ModLogger), "Warning"), prefix: new HarmonyMethod(typeof(LootJsonTests), "Warning"));
            LoadNativeData(Path.Combine(repo, "LootTables/regions.json"));
            Check(ModHelper.GetLootEntries("junkTable").Count == 1 && ModHelper.GetLootEntries("junkTable")[0].weight == 100f,
                "native JSON does not modify vanilla tables");
            Check(ModHelper.GetLootGroupEntries("dumpingGroundTG").Count == 1 && ModHelper.GetLootGroupEntries("dumpingGroundTG")[0].id == "junkTable",
                "private groups do not enter the vanilla foreign-loot group");
            Check(ModHelper.GetLootEntries(Core.Id + ".baseline")[0].sourceModId == Core.Id, "native loader attributes JSON entries to this mod");
            VerifyFingerprints(LootDataSnapshot.Capture(ModHelper.GetLootEntries, ModHelper.GetLootGroupEntries), fingerprints, "native JSON snapshot");

            Core.Enabled = true;
            ModHook.OnLootTablesLoaded += ScavLoot.OnTablesLoaded;
            Fire();
            Check(logs.Exists(s => s.Contains("[loot] snapshot ready: regions=8, baseline_entries=53, source=registry")), "official loaded event installs the native snapshot");
            var cache = new LootTableCache();
            LootSettings settings = new LootSettings(100f, 0f, 100f, 0f, 0f);
            EffectiveTable before = cache.Get(Region.All[0], settings, false);
            float oldEv = Tuning.BaseEv(Region.All[0]);
            var ownIds = (HashSet<string>)AccessTools.Method(typeof(ForeignLoot), "OwnIds").Invoke(null, null);
            Check(!ownIds.Contains("json_added_item"), "foreign filter starts with original JSON catalog");
            string category = Core.Id + ".region.gate.category_materials";
            int oldWeight = ScavLoot.Get("gate").Weights[ScavLoot.Get("gate").IndexOf("scrap_metal")];
            ModHelper.SetLootWeight(Core.Id, category, "scrap_metal", 10000f);
            ModHelper.AddLootEntry(Core.Id, category, "json_added_item", 50f);
            ModHelper.SetLootWeight(Core.Id, Core.Id + ".baseline", "glass_shard", 10000f);
            Fire();
            RegionLootTable changed = ScavLoot.Get("gate");
            Check(changed.IndexOf("json_added_item") >= 0 && changed.Weights[changed.IndexOf("scrap_metal")] != oldWeight, "native edits reach the calculator's category data");
            Check(changed.CategoryOf("json_added_item") == changed.CategoryOf("scrap_metal"), "new JSON item inherits its category and display grouping");
            Check(!ReferenceEquals(before, cache.Get(Region.All[0], settings, false)), "table generation invalidates existing probability cache");
            Check(Tuning.BaseEv(Region.All[0]) != oldEv, "baseline edit invalidates reference EV cache");
            ownIds = (HashSet<string>)AccessTools.Method(typeof(ForeignLoot), "OwnIds").Invoke(null, null);
            Check(ownIds.Contains("json_added_item"), "foreign catalog is rebuilt from the installed JSON snapshot");
            Check(ScavLoot.SourceTable("scrap_metal") == "materialTable", "native event source-table compatibility remains stable");

            int revision = ScavLoot.Revision;
            Core.Enabled = false;
            Fire();
            Check(ScavLoot.Revision == revision, "loaded callback is inert while disabled");
            Core.Enabled = true;
            warnings.Clear();
            ModHelper.ClearLootGroup(Core.Id, Core.Id + ".region.scrap");
            Fire();
            Check(warnings.Count == 1 && warnings[0].Contains("using bundled defaults"), "incomplete registry emits one actionable fallback warning");
            VerifyFingerprints(LootDataSnapshot.Bundled(), fingerprints, "fallback source");
            Check(Fingerprint(ScavLoot.Get("gate")) == fingerprints["gate"] && Fingerprint(ScavLoot.Vanilla) == fingerprints["baseline"],
                "failed load restores the whole baseline atomically rather than mixing scenes");
            InvalidData();
            Console.WriteLine("PASS: " + checks + " loot-JSON checks (native DTO/conversion/journal, legacy fingerprints, cache refresh, provenance and fallback)");
            return checks;
        }
        finally
        {
            ModHook.OnLootTablesLoaded -= ScavLoot.OnTablesLoaded;
            foreach (var original in originals) original.Key.SetValue(null, original.Value);
            ScavLoot.Reset();
            Core.Enabled = enabled;
            harmony.UnpatchAll(Owner);
        }
    }

    private static void InvalidData()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { LootDataSnapshot.Capture(id => new List<LootEntry> { new LootEntry("junk", bad, Core.Id) }, LootDefaults.Group); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid registry weight rejected: " + bad);
        }
        bool duplicate = false;
        try { LootDataSnapshot.Capture(id => new List<LootEntry> { new LootEntry("junk", 1f, Core.Id), new LootEntry("junk", 2f, Core.Id) }, LootDefaults.Group); }
        catch (ArgumentException) { duplicate = true; }
        Check(duplicate, "duplicate IDs cannot corrupt an integer snapshot");
        LootDataSnapshot scaled = LootDataSnapshot.Capture(LootDefaults.Table, id =>
        {
            var entries = LootDefaults.Group(id);
            var result = new List<LootEntry>();
            foreach (LootEntry entry in entries) result.Add(new LootEntry(entry.id, entry.weight * 0.5f, Core.Id));
            return result;
        });
        Check(Fingerprint(scaled.Regions["gate"]) == Fingerprint(LootDataSnapshot.Bundled().Regions["gate"]), "relative fractional group shares preserve category probabilities");
    }

    private static void Fire() { AccessTools.Method(typeof(ModHook), "FireOnLootTablesLoaded").Invoke(null, null); }
    private static void LoadNativeData(string path)
    {
        // Unity's JSON parser/file-loader cannot run outside the engine. Parse into
        // its actual DTO, then exercise native ToEntries + registration + journal.
        Assembly game = typeof(LootRegistry).Assembly;
        Type dto = game.GetType("ModLootFileJson");
        object data = serializer.Deserialize(File.ReadAllText(path), dto);
        MethodInfo convert = AccessTools.Method(game.GetType("ModLootFile"), "ToEntries");
        foreach (string kind in new[] { "tables", "groups" })
        {
            Array lists = (Array)dto.GetField(kind).GetValue(data);
            Check(lists.Length == (kind == "tables" ? 40 : 8), "native DTO accepts " + kind);
            MethodInfo register = AccessTools.Method(typeof(LootRegistry), kind == "tables" ? "RegisterTable" : "RegisterGroup",
                new[] { typeof(string), typeof(string), typeof(IEnumerable<LootEntry>) });
            foreach (object list in lists)
            {
                string id = (string)list.GetType().GetField("id").GetValue(list);
                object entries = convert.Invoke(null, new[] { list.GetType().GetField("entries").GetValue(list) });
                register.Invoke(null, new[] { (object)Core.Id, id, entries });
            }
        }
    }
    private static bool Log(object __0) { logs.Add(Convert.ToString(__0)); return false; }
    private static bool Warning(object __0) { warnings.Add(Convert.ToString(__0)); return false; }
    private static void VerifyFingerprints(LootDataSnapshot data, Dictionary<string, string> expected, string source)
    {
        Check(Fingerprint(data.Vanilla) == expected["baseline"], source + " preserves legacy baseline exactly");
        foreach (Region region in Region.All) Check(Fingerprint(data.Regions[region.Id]) == expected[region.Id], source + " preserves " + region.Id + " IDs, weights, categories and order");
    }
    private static string Fingerprint(RegionLootTable table)
    {
        var keys = (string[])AccessTools.Field(typeof(RegionLootTable), "categoryKeys").GetValue(table);
        var membership = (int[])AccessTools.Field(typeof(RegionLootTable), "itemCategory").GetValue(table);
        var text = new StringBuilder();
        for (int i = 0; i < table.Count; ++i) text.Append(table.Items[i]).Append(':').Append(table.Weights[i].ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(keys == null ? "" : keys[membership[i]]).Append('\n');
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
    }
    private static void Check(bool value, string message) { ++checks; if (!value) throw new Exception(message); }
}
