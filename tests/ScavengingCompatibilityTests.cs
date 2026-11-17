using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DumpingGroundMap;
using HarmonyLib;

internal static class ScavengingCompatibilityTests
{
    private static int checks, captures, events, counted;
    private static string replacement;
    private static bool cancel;
    private static List<LootEntry> captured;
    private const string Owner = "test.dumping-ground.compatibility";

    internal static int Run()
    {
        ProjectionRules();
        ExtraDrawRules();
        ScopeRules();
        FurnaceLayout();
        foreach (bool partnerFirst in new[] { true, false }) PatchRules(partnerFirst);
        Console.WriteLine("PASS: " + checks + " scavenging compatibility checks (regional snapshots, extra-draw filter, native hooks, both load orders)");
        return checks;
    }

    /// <summary>
    /// 技能额外抽取只走普通/外来槽：不能出现贵重品、80 档补给箱或 100 档钥匙卡。
    /// </summary>
    private static void ExtraDrawRules()
    {
        Check(!ExtraDrawScope.TakeExtra(), "draws outside a search stay base draws");
        ExtraDrawScope.BeginSearch();
        Check(!ExtraDrawScope.TakeExtra(), "first draw in a search is the base draw");
        Check(ExtraDrawScope.TakeExtra(), "second draw in the same search is an extra draw");
        Check(ExtraDrawScope.TakeExtra(), "later draws stay extra");
        ExtraDrawScope.EndSearch();
        Check(!ExtraDrawScope.TakeExtra(), "scope ends with the search");
        ExtraDrawScope.BeginSearch();
        ExtraDrawScope.TakeExtra();
        ExtraDrawScope.BeginSearch();
        Check(!ExtraDrawScope.TakeExtra(), "nested search starts its own base draw");
        ExtraDrawScope.EndSearch();
        Check(ExtraDrawScope.TakeExtra(), "outer search keeps counting after a nested search");
        ExtraDrawScope.EndSearch();

        foreach (Region region in Region.All)
        {
            EffectiveTable table = LootCalculator.Build(region, LootPreset.For(0).Settings, true, 0.5f, 100);
            for (int step = 0; step <= 20; ++step)
            {
                int index = table.PickOrdinaryIndex(step / 20f);
                Check(index >= 0, "every region has an ordinary slot for extra draws");
                LootKind kind = table.Slots[index].Kind;
                Check(kind == LootKind.Ordinary || kind == LootKind.Foreign,
                    "extra draws never pick valuables, supply crates or keycards");
            }
            Check(table.FallbackOrdinaryIndex() >= 0, "extra draw fallback finds an ordinary slot");
        }
    }

    private static void Check(bool condition, string message)
    {
        ++checks;
        if (!condition) throw new Exception("Scavenging compatibility: " + message);
    }

    private static void ProjectionRules()
    {
        foreach (Region region in Region.All)
        foreach (bool unlocked in new[] { false, true })
        {
            EffectiveTable table = LootCalculator.Build(region, LootPreset.For(0).Settings, unlocked, 0.5f, unlocked ? 100 : 0);
            foreach (string source in new[] { "dumpingGroundMedical", "householdTable", "makeshiftWeaponTable" })
            {
                List<LootEntry> projected = RegionalPoolProjection.Build(table, source, 1000f);
                double expectedTotal = 0d;
                Dictionary<string, double> expected = new Dictionary<string, double>();
                for (int i = 0; i < table.Count; ++i)
                {
                    string originalSource = ScavLoot.SourceTable(LootVariants.RealId(table.Items[i]));
                    bool matches = originalSource == source || (source == "dumpingGroundMedical" && originalSource == "medicalTable");
                    if (table.Slots[i].Kind != LootKind.Ordinary || !matches || table.Probs[i] <= 0f) continue;
                    string real = LootVariants.RealId(table.Items[i]);
                    double weight;
                    expected.TryGetValue(real, out weight);
                    expected[real] = weight + table.Probs[i];
                    expectedTotal += table.Probs[i];
                }
                Check(projected.Count == expected.Count, "projection contains only active ordinary items in this region/category");
                double sum = 0d;
                foreach (LootEntry entry in projected)
                {
                    Check(expected.ContainsKey(entry.id), "projection does not import vanilla items or locked valuables");
                    Check(Math.Abs(entry.weight / 1000d - expected[entry.id] / expectedTotal) < 0.00001d, "regional relative odds preserved");
                    sum += entry.weight;
                }
                Check(projected.Count == 0 || Math.Abs(sum - 1000d) < 0.01d, "skill overlay uses the native table's weight scale");
            }
        }
        EffectiveTable water = LootCalculator.Build(Region.All[7], LootPreset.For(0).Settings, false);
        int half = Array.IndexOf(water.Items, WaterSupplyPack.HalfTest);
        Check(half >= 0, "water fixture contains the partial supply pack");
        Check(RegionalPoolProjection.RestoreVariant(water, half, "water_test_container", () => { throw new Exception("unnecessary reroll"); }) == WaterSupplyPack.HalfTest, "unchanged real ID preserves partial pack without rerolling");
        Check(RegionalPoolProjection.RestoreVariant(water, half, "skill_new_item", () => 0f) == "skill_new_item", "new skill candidate survives");
        Check(RegionalPoolProjection.RestoreVariant(water, half, null, () => 0f) == null, "subscriber cancellation survives");
        Check(RegionalPoolProjection.Build(water, "householdTable", 0f).Count == 0, "empty native table safely bypasses overlay");
    }

    private static void ScopeRules()
    {
        var original = new List<LootEntry> { new LootEntry("toothpaste", 100f, "test") };
        List<LootEntry> snapshot;
        string token;
        Check(!RegionalRollScope.TrySnapshot("householdTable", out snapshot), "no scope outside a delegated roll");
        using (RegionalRollScope.Enter("householdTable", "toothpaste", original))
        {
            Check(RegionalRollScope.TrySnapshot("householdTable", out snapshot), "matching table gets a snapshot");
            snapshot[0].weight = 1f;
            Check(original[0].weight == 100f, "snapshot mutations cannot alter the source");
            Check(!RegionalRollScope.TryTake("householdTable", "anotherGroup", out token), "unrelated groups are untouched");
            try
            {
                using (RegionalRollScope.Enter("makeshiftWeaponTable", "box_cutter", original))
                {
                    Check(!RegionalRollScope.TrySnapshot("householdTable", out snapshot), "nested roll hides parent context");
                    throw new InvalidOperationException("synthetic roll failure");
                }
            }
            catch (InvalidOperationException) { }
            Check(RegionalRollScope.TrySnapshot("householdTable", out snapshot), "exception restores parent scope");
            Check(RegionalRollScope.TryTake("householdTable", "dumpingGroundTG", out token) && token == "toothpaste", "one draw consumes its preset");
            Check(!RegionalRollScope.TryTake("householdTable", "dumpingGroundTG", out token), "nested event draws cannot consume it twice");
            Check(!RegionalRollScope.TrySnapshot("householdTable", out snapshot), "event listeners see the normal registry after consumption");
        }
        Check(!RegionalRollScope.TrySnapshot("householdTable", out snapshot), "scope is gone after returning");
    }

    private static void FurnaceLayout()
    {
        foreach (float[] size in new[] {
            new[] {1280f, 720f}, new[] {1920f, 1080f}, new[] {2560f, 1440f},
            new[] {960f, 540f}, new[] {1280f, 1024f}, new[] {800f, 600f} })
        {
            float scale = Math.Min(size[0] / 1920f, size[1] / 1080f);
            float left = FurnaceLayoutRules.ReserveLeft(8f, size[0], size[1], 531f, 373f, 8f);
            // Independently apply the third-party Tick visibility formula.
            float body = Math.Min(360f * scale, Math.Min(left - 12f * scale, 373f * 96f / 88f));
            Check(body >= 64f * scale, "left-aligned ground leaves furnace visible at " + size[0] + "x" + size[1]);
            Check(left + 531f <= size[0] - 8f, "ground remains inside canvas after reserving furnace space");
            float right = size[0] - 531f - 8f;
            Check(FurnaceLayoutRules.ReserveLeft(right, size[0], size[1], 531f, 373f, 8f) == right, "sufficient right-aligned space is preserved");
        }
        float reported = FurnaceLayoutRules.ReserveLeft(8f, 1280f, 720f, 531f, 373f, 8f);
        Check(Math.Abs(reported - 256f) < 0.01f, "reported 1280x720 layout reserves full furnace lane");
        Check(FurnaceLayoutRules.ReserveLeft(8f, 500f, 300f, 490f, 250f, 8f) <= 10f, "tiny canvases never push ground outside viewport");
    }

    private static HarmonyMethod Method(string name) { return new HarmonyMethod(AccessTools.Method(typeof(ScavengingCompatibilityTests), name)); }

    private static HarmonyMethod WithRealOrdering(Type patch, string name, string probe)
    {
        var method = new HarmonyMethod(AccessTools.Method(patch, name));
        method.method = AccessTools.Method(typeof(ScavengingCompatibilityTests), probe);
        return method;
    }

    private static void PatchRules(bool partnerFirst)
    {
        var own = new Harmony(Owner);
        var partner = new Harmony(ScavengingExpansionBridge.HarmonyId);
        MethodInfo roll = AccessTools.Method(typeof(LootRegistry), "RollInternal");
        MethodInfo spawn = AccessTools.Method(typeof(ScavengingCompatibilityTests), "SpawnProbe");
        MethodInfo search = AccessTools.Method(typeof(ScavengingCompatibilityTests), "SearchProbe");
        Action installOwn = delegate {
            own.CreateClassProcessor(typeof(RegionalSkillPoolPatch)).Patch();
            own.CreateClassProcessor(typeof(RegionalSkillRollPatch)).Patch();
            own.Patch(spawn, prefix: WithRealOrdering(typeof(DumpingGroundLootPatch), "Prefix", "ReplaceProbe"));
            own.Patch(search, postfix: WithRealOrdering(typeof(ScavengeReputationPatch), "Postfix", "CountProbe"));
        };
        Action installPartner = delegate {
            partner.Patch(roll, prefix: Method("CapturePrefix"), postfix: Method("OverlayPostfix"));
            partner.Patch(spawn, prefix: Method("SpawnCapturePrefix"), postfix: Method("SpawnCapturePostfix"));
            var extra = Method("ExtraLootPostfix"); extra.priority = Priority.Last;
            partner.Patch(search, postfix: extra);
        };
        try
        {
            own.Patch(AccessTools.Method(typeof(LootRegistry), "GetTableEntries"), prefix: Method("NativeEntriesPrefix"));
            own.Patch(AccessTools.Method(typeof(ModHelper), "IsModRunning"), prefix: Method("RunningPrefix"));
            own.Patch(AccessTools.Method(typeof(ModHook), "FireOnLootRolled"), prefix: Method("EventPrefix"));
            own.Patch(AccessTools.Method(typeof(ScavHelper), "EventModifier"), prefix: Method("EventModifierPrefix"));
            if (partnerFirst) { installPartner(); installOwn(); } else { installOwn(); installPartner(); }

            captures = 0;
            Check(SpawnProbe() == "regional" && captures == 1, "discovery state survives the replacement prefix in either load order");
            counted = 0;
            SearchProbe();
            Check(counted == 30, "skill-added loot is counted before reputation in either load order");
            Check(NativeLootEvents.JunkModifier() == -65f, "regional calculation uses the patched native junk modifier");

            EffectiveTable table = LootCalculator.Build(Region.All[7], LootPreset.For(0).Settings, false);
            int index = Array.IndexOf(table.Items, "toothpaste");
            captures = events = 0; replacement = "skill_new_item"; cancel = false;
            Check(ScavengingExpansionBridge.Resolve(table, index) == replacement, "native skill postfix can replace a regional item");
            Check(captures == 1 && events == 1 && captured.Count > 1, "skill receives the regional snapshot and one native loot event");
            Check(captured.TrueForAll(e => e.id != "native_only_item"), "skill never sees the unrelated vanilla pool");
            captures = events = 0; replacement = null;
            Check(ScavengingExpansionBridge.Resolve(table, index) == "toothpaste", "rank-zero/no-overlay path preserves the selected regional item");
            Check(captures == 1 && events == 1, "no-overlay path still fires once");
            cancel = true; replacement = "skill_new_item";
            Check(ScavengingExpansionBridge.Resolve(table, index) == null, "native event cancellation is not replaced by a skill");
        }
        finally { own.UnpatchAll(Owner); partner.UnpatchAll(ScavengingExpansionBridge.HarmonyId); cancel = false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string SpawnProbe() { return "vanilla"; }
    private static bool ReplaceProbe(ref string __result) { __result = "regional"; return false; }
    private static void SpawnCapturePrefix(ref bool __state) { __state = true; }
    private static void SpawnCapturePostfix(bool __state) { if (__state) ++captures; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<int> SearchProbe() { return new List<int> { 10 }; }
    private static void ExtraLootPostfix(List<int> __result) { __result.Add(20); }
    private static void CountProbe(List<int> __result) { foreach (int value in __result) counted += value; }
    private static void CapturePrefix(string tableId, ref List<LootEntry> __state) { ++captures; captured = __state = LootRegistry.GetTableEntries(tableId); }
    private static void OverlayPostfix(ref string __result, List<LootEntry> __state) { if (__state != null && replacement != null && __result != null) __result = replacement; }
    private static bool NativeEntriesPrefix(ref List<LootEntry> __result) { __result = new List<LootEntry> { new LootEntry("native_only_item", 1000f, "test") }; return false; }
    private static bool RunningPrefix(ref bool __result) { __result = true; return false; }
    private static bool EventPrefix(LootRollContext context) { ++events; if (cancel) context.itemId = null; return false; }
    private static bool EventModifierPrefix(ref float __result) { __result = -65f; return false; }
}
