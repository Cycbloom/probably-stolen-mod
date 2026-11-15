using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DumpingGroundMap;

internal static class RulesTests
{
    private static int assertions;

    private static int Main(string[] args)
    {
        string binary = Path.GetFullPath(args[0]);
        string managed = Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        {
            string name = new AssemblyName(request.Name).Name + ".dll";
            string path = name == "DumpingGroundMap.dll" ? binary : Path.Combine(managed, name);
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        DataAndProbabilityRules();
        CacheRules();
        EventRules();
        StateRules();
        Console.WriteLine("PASS: " + assertions + " assertions (data, probabilities, board coverage, cache, event replacement, run identity, restoration)");
    }

    private static void Check(bool condition, string message)
    {
        ++assertions;
        if (!condition) throw new Exception(message);
    }

    private static void Near(float actual, float expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.00002f, message + ": " + actual + " vs " + expected);
    }

    private static void DataAndProbabilityRules()
    {
        foreach (Region region in Region.All)
        {
            foreach (string id in region.Loot.Items)
                Check(LootCatalog.ValueOf(id) > 0f, "Missing value: " + id);
            foreach (ValuableDefinition entry in LootCatalog.Valuables)
            {
                Check(entry.WeightFor(region.Id) > 0, "Invalid regional weight: " + entry.Id);
                Check(LootCatalog.ValueOf(entry.Id) > 0f, "Missing valuable value: " + entry.Id);
            }
            foreach (float junk in new[] { 0f, 50f, 100f })
            foreach (float specialty in new[] { 0f, 25f, 50f, 75f, 100f })
            foreach (float high in new[] { 0f, 3f, 30f })
            foreach (bool unlocked in new[] { false, true })
            {
                LootSettings settings = new LootSettings(junk, high, specialty, 5f, 5f);
                EffectiveTable table = LootCalculator.Build(region, settings, unlocked);
                float sum = 0f;
                HashSet<int> shown = new HashSet<int>();
                foreach (LootNoticeGroup group in LootNoticeModel.Groups(table))
                    foreach (int slot in group.Slots)
                        Check(shown.Add(slot), "A board slot appears twice");
                float displayed = 0f;
                foreach (int slot in shown) displayed += table.Probs[slot];
                displayed += table.ProbabilityOf(LootKind.SupplyCrate) + table.ProbabilityOf(LootKind.Keycard);
                Near(displayed, 1f, "Board omitted a positive slot: " + region.Id);
                for (int i = 0; i < table.Count; ++i)
                {
                    Check(!float.IsNaN(table.Probs[i]) && table.Probs[i] >= 0f, "Invalid probability");
                    sum += table.Probs[i];
                    Check(LootCatalog.ValueOf(table.Items[i]) > 0f, "Unvalued final slot");
                }
                Near(sum, 1f, "Probability mass");
                if (!unlocked)
                {
                    Near(table.ProbabilityOf(LootKind.Valuable), 0f, "Locked valuable pool");
                    Near(table.ProbabilityOf(LootKind.SupplyCrate), 0f, "Locked crate pool");
                    Near(table.ProbabilityOf(LootKind.Keycard), 0f, "Locked keycard pool");
                }
                if (junk == 0f) Near(table.JunkChance, 0f, "Zero junk setting");
                if (region.Id == "gate") Near(table.ProbabilityOf(LootKind.SupplyCrate), 0f, "Gate has no crate");
                else if (unlocked)
                {
                    float crate = table.ProbabilityOf(LootKind.SupplyCrate);
                    Check(crate > 0f && crate <= 0.050001f, "Reserved shares respect the available probability mass");
                    Near(crate, table.ProbabilityOf(LootKind.Keycard), "Equal reserved settings scale together");
                    if (table.JunkChance + high / 100f + 0.1f <= 0.995f)
                        Near(crate, 0.05f, "Unconstrained crate absolute probability");
                }
                Check(table.PickIndex(0f, 0f) >= 0 && table.PickIndex(1f, 1f) >= 0, "Sampling edges");
            }

            EffectiveTable normal = LootCalculator.Build(region, new LootSettings(50f, 3f, 75f, 0.5f, 0.5f), true);
            int[] samples = new int[normal.Count];
            const int count = 10000;
            for (int i = 0; i < count; ++i) ++samples[normal.PickIndex(1f, (i + 0.5f) / count)];
            for (int i = 0; i < normal.Count; ++i)
                Check(Math.Abs(samples[i] / (float)count - normal.Probs[i]) < 0.00021f, "Sampling distribution mismatch");
            bool ordinaryExtractor = false, valuableExtractor = false;
            foreach (LootNoticeGroup group in LootNoticeModel.Groups(normal))
                foreach (int slot in group.Slots)
                    if (normal.Items[slot] == "module_extractor")
                    {
                        if (group.Valuable) valuableExtractor = true;
                        else ordinaryExtractor = true;
                    }
            Check(ordinaryExtractor && valuableExtractor, "Ordinary/valuable duplicate must be shown separately");
        }
        bool rejected = false;
        try { new RegionLootTable(new[] { "junk" }, new[] { 0 }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Reject non-positive weights");
    }

    private static void CacheRules()
    {
        Region region = Region.All[1];
        LootTableCache cache = new LootTableCache();
        LootSettings original = new LootSettings(50f, 3f, 75f, 0.5f, 0.5f);
        EffectiveTable locked = cache.Get(region, original, false);
        Check(ReferenceEquals(locked, cache.Get(region, original, false)), "Stable cache");
        EffectiveTable unlocked = cache.Get(region, original, true);
        Check(!ReferenceEquals(locked, unlocked), "80-point threshold invalidates cache");
        Check(unlocked.ProbabilityOf(LootKind.Valuable) > 0f, "Unlock applied");
        Check(cache.Get(region, original, false).ProbabilityOf(LootKind.Valuable) == 0f, "Reload below threshold");
        foreach (LootSettings changed in new[] {
            new LootSettings(49f,3f,75f,0.5f,0.5f), new LootSettings(50f,4f,75f,0.5f,0.5f),
            new LootSettings(50f,3f,100f,0.5f,0.5f), new LootSettings(50f,3f,75f,1f,0.5f),
            new LootSettings(50f,3f,75f,0.5f,1f) })
        {
            EffectiveTable before = cache.Get(region, original, true);
            Check(!ReferenceEquals(before, cache.Get(region, changed, true)), "Every calculation input invalidates cache");
        }
    }

    private static void EventRules()
    {
        string result = LootRollResolver.Resolve("virtual_bad", "real", "medicalTable", c => {
            Check(c.itemId == "real" && c.groupId == "dumpingGroundTG" && c.tableId == "medicalTable", "Event context");
            c.itemId = "replacement";
        });
        Check(result == "replacement", "Subscriber replacement must win");
        Check(LootRollResolver.Resolve("virtual_bad", "real", "table", c => { }) == "virtual_bad", "Keep unchanged variant");
        Check(LootRollResolver.Resolve("virtual_bad", "real", "table", c => c.itemId = null) == null, "Subscriber can cancel");
    }

    private static void StateRules()
    {
        GroundExpand.BeginScavenge();
        GroundExpand.BeginScavenge();
        GroundExpand.EndScavenge();
        Check(GroundExpand.SearchActive, "Nested ground search remains active until outer call exits");
        Exception originalError = new Exception("test search failure");
        Check(ReferenceEquals(originalError, ScavengeGroundScopePatch.Finalizer(originalError, true)), "Preserve search exception");
        Check(!GroundExpand.SearchActive, "Finalizer clears failed search scope");
        GroundExpand.BeginScavenge();
        GroundExpand.ResetForSetup();
        Check(!GroundExpand.SearchActive, "Scene setup resets search scope");
        Near(PerkCatalog.MinorMultiplier(59), 0.1f, "Below tier 60");
        Near(PerkCatalog.MinorMultiplier(60), 1f, "At tier 60");
        Check(PerkCatalog.For(100).DescriptionZh.Contains("尚未开放"), "Tier 100 cannot advertise an unimplemented helper");
        bool wentOut = true;
        bool original = wentOut;
        RestoreScope outer = new RestoreScope(() => wentOut = original);
        wentOut = false;
        RestoreScope inner = null; // Nested call sees the already suppressed field and skips.
        if (inner != null) inner.Restore();
        outer.Restore();
        Check(wentOut, "Nested suppression restores original field");
        int restored = 0;
        RestoreScope scope = new RestoreScope(() => ++restored);
        scope.Restore(); scope.Restore();
        Check(restored == 1, "Postfix/finalizer restoration is idempotent");
        object owner = new object();
        RunIdentity identity = new RunIdentity(owner, "run_a", 1);
        Check(identity.Matches(owner, "run_a", 1), "Same save owner");
        Check(!identity.Matches(new object(), "run_a", 1), "Reload replaces owner even in same run");
        Check(!identity.Matches(owner, "run_b", 1) && !identity.Matches(owner, "run_a", 2), "Cross-run/slot save rejected");
    }
}
