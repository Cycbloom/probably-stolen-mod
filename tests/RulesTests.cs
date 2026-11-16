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
        try
        {
            if (args.Length > 3 && args[3] == "events") WriteRegionalEventReport(args[2]);
            else if (args.Length > 2) WriteBalanceReport(args[2]);
            else Run();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteRegionalEventReport(string path)
    {
        System.Text.StringBuilder csv = new System.Text.StringBuilder();
        csv.AppendLine("reputation,region,native_junk_weight,base_ev,event_ev,uplift,uplift_percent,junk_before,junk_after");
        foreach (int points in new[] { 0, 20, 40, 60, 80 })
        foreach (Region region in Region.All)
        foreach (float scale in new[] { 1f, 0.5f })
        {
            if (points < region.RequiredReputation) continue;
            EffectiveTable before = LootCalculator.Build(region, LootPreset.For(0).Settings, points >= 80, scale, points);
            EffectiveTable after = RegionalEventRules.Apply(region, before, RegionalEvent.For(region.Id));
            csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2:F2},{3:F6},{4:F6},{5:F6},{6:F4},{7:F6},{8:F6}",
                points, region.Id, scale, before.Ev, after.Ev, after.EventEv, after.EventEv / before.Ev * 100f,
                before.JunkChance, after.JunkChance));
        }
        File.WriteAllText(path, csv.ToString(), new System.Text.UTF8Encoding(true));
        Console.WriteLine("Exported regional event expectations: " + path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteBalanceReport(string path)
    {
        System.Text.StringBuilder csv = new System.Text.StringBuilder();
        csv.AppendLine("reputation,region,specialty,target_ev,actual_ev,junk_probability,item,category,probability,expected_item_value");
        foreach (int points in new[] { 0, 20, 40, 60 })
        foreach (Region region in Region.All)
        {
            if (region.RequiredReputation > points) continue;
            foreach (float specialty in new[] { 75f, 100f })
            {
                EffectiveTable table = LootCalculator.Build(region, new LootSettings(100f, 0f, specialty, 0f, 0f), false, 1f, points);
                for (int i = 0; i < table.Count; ++i)
                {
                    if (table.Probs[i] <= 0f) continue;
                    csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:F6},{4:F6},{5:F6},{6},{7},{8:F8},{9:F6}",
                        points, region.Id, specialty, LootBalance.Target(points), table.Ev, table.JunkChance,
                        table.Items[i], region.Loot.CategoryOf(table.Items[i]), table.Probs[i],
                        LootCatalog.ExpectedValueOf(table.Items[i], region.Id)));
                }
            }
        }
        File.WriteAllText(path, csv.ToString(), new System.Text.UTF8Encoding(true));
        Console.WriteLine("Exported balance probabilities: " + path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        DataAndProbabilityRules();
        CacheRules();
        EventAdjustedLootRules();
        EventRules();
        StateRules();
        OutingAndMilestoneRules();
        JackpotWindowRules();
        DailyAndMinorWindowRules();
        JackpotNewsRules();
        ScavengerTrafficRulesTests();
        OverviewLayoutRules();
        MapSelectionRules();
        PumpWaterRules();
        ReputationBalanceRules();
        ExpectedRewardBreakdownRules();
        RegionalSupplyRules();
        SettingsProfileRules();
        RegionalEventRulesTests();
        Console.WriteLine("PASS: " + assertions + " assertions (loot/events, state, outings, milestones, jackpot modes/news, traffic)");
    }

    private static void Check(bool condition, string message)
    {
        ++assertions;
        if (!condition) throw new Exception(message);
    }

    private static void OverviewLayoutRules()
    {
        foreach (float[] canvas in new[] {
            new[] { 1920f, 1080f }, new[] { 1920f, 810f },
            new[] { 1920f, 1440f }, new[] { 1280f, 720f }, new[] { 960f, 540f } })
        {
            UnityEngine.Vector2 map = Picker.OverviewSize(canvas[0], canvas[1]);
            Check(map.x > 0f && map.y > 0f, "Map has visible dimensions");
            Near(map.x / map.y, 2f, "Overview artwork keeps its 2:1 aspect");
            Check(map.x + 60f <= canvas[0] - 64f, "Overview fits horizontal margins");
            float buttons = Math.Min(164f, map.x * 0.32f) + Math.Min(130f, map.x * 0.22f)
                + Math.Min(248f, map.x * 0.38f) + 24f;
            Check(buttons <= map.x, "All three actions fit even on short screens");
            Check(map.y + 340f <= canvas[1] - 48f, "Overview keeps event details and actions within the screen");
        }
        UnityEngine.Vector2 wide = Picker.OverviewSize(1920f, 1080f);
        Check(wide.x * wide.y >= 4f * 680f * 340f, "Widescreen artwork has at least four times the previous area");
    }

    private static void MapSelectionRules()
    {
        Check(Region.All.Length == 8, "Overview has eight complete search destinations");
        HashSet<string> ids = new HashSet<string>();
        foreach (Region region in Region.All)
        {
            Check(ids.Add(region.Id), "Destination ids are unique");
            int ground = MapLayout.Pick(region.X, region.Y);
            Check(ground >= 0 && Region.All[ground] == region, "Ground anchor selects its own area: " + region.Id);
            int roof = MapLayout.Pick(region.X, region.Y + 0.12f);
            Check(roof >= 0 && Region.All[roof] == region, "Landmark body selects its area: " + region.Id);
            int nearby = MapLayout.Pick(region.X + 0.04f, region.Y - 0.035f);
            Check(nearby >= 0 && Region.All[nearby] == region, "Nearby ground belongs to the landmark: " + region.Id);
            Check(StashTemplate.For(region.Id) != null, "Every area has a hidden stash: " + region.Id);
            UnityEngine.Rect frame = MapArt.Frame(region.Id);
            Check(frame.width > 0f && frame.height > 0f && frame.xMin >= 0f && frame.yMin >= 0f
                && frame.xMax <= 1f && frame.yMax <= 1f, "Each landmark has a valid nonempty atlas frame: " + region.Id);
            Check(region.X - MapArt.Width(region.Id) * 0.5275f >= 0f
                && region.X + MapArt.Width(region.Id) * 0.5275f <= 1f
                && region.Y + MapArt.Height(region.Id) * 1.055f <= 1f,
                "Hovered artwork remains within the overview: " + region.Id);
        }
        foreach (float[] outside in new[] {
            new[] { -0.01f, 0.5f }, new[] { 1.01f, 0.5f }, new[] { 0.5f, -0.01f },
            new[] { 0.5f, 1.01f }, new[] { 0.02f, 0.5f }, new[] { 0.98f, 0.5f },
            new[] { 0.5f, 0.02f }, new[] { 0.98f, 0.98f } })
            Check(MapLayout.Pick(outside[0], outside[1]) == -1, "Perimeter and off-map clicks cannot choose a destination");
        Check(MapLayout.Pick(0.70f, 0.12f) == -1, "Remote foreground scrap is inert");
        Check(UnlockCost.For("electronics") == 60 && UnlockCost.For("vehicles") == 40,
            "Electronics and vehicles have different unlock batches");
        Check(UnlockCost.For("water") == 20, "Pump opens with the first reputation tier");
        foreach (string id in new[] { "electronics", "vehicles", "water" })
        {
            Check(!ReferenceEquals(ScavLoot.Get(id), ScavLoot.Vanilla), "New areas have independent loot tables");
            Check(LootCatalog.CrateFor(id) != null && LootCatalog.CardFor(id) != null, "New areas have matching supply crates and cards");
        }
    }

    private static void Near(float actual, float expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.00002f, message + ": " + actual + " vs " + expected);
    }

    private static void PumpWaterRules()
    {
        int[] grades = new int[6];
        for (int roll = 0; roll < 100; ++roll) ++grades[PumpWater.GradeForRoll(roll)];
        Check(grades[0] < grades[1] && grades[1] < grades[2] && grades[2] < grades[3],
            "Water quality grows more common toward ghostwater from the clean end");
        Check(grades[3] > grades[4] && grades[4] > grades[5],
            "Water quality grows rarer beyond ghostwater toward the dirty end");
        Check(grades[2] == grades[4] && grades[1] == grades[5],
            "Native grades equally far from ghostwater have equal chances");
        foreach (int capacity in new[] { 250000, 450000, 750000, 2000000, 6000000 })
        {
            int[] buckets = new int[9];
            for (int i = 0; i < 1000; ++i)
            {
                int parts = PumpWater.FillParts(capacity, (i + 0.5f) / 1000f);
                Check(parts >= capacity / 10 && parts <= capacity, "Water is present and fits the native capacity");
                ++buckets[Math.Min(8, (int)Math.Round(parts * 100d / capacity) / 10 - 1)];
            }
            for (int i = 1; i <= 4; ++i)
                Check(buckets[i] > buckets[i - 1], "Water volume becomes more common toward mid-fill");
            for (int i = 5; i < buckets.Length; ++i)
                Check(buckets[i] < buckets[i - 1], "Water volume becomes rarer toward a full container");
            Near(PumpWater.FillParts(capacity, 0.5f) / (float)capacity, 0.55f, "Median fill stays near half-full");
            Check(PumpWater.FillParts(capacity, 0f) == capacity / 10, "Minimum fill is ten percent");
            Check(PumpWater.FillParts(capacity, 1f) == capacity, "Maximum fill never overflows");
        }
        RegionLootTable pump = ScavLoot.Get("water");
        Check(pump.Weights[Array.IndexOf(pump.Items, "bottled_water")]
            > pump.Weights[Array.IndexOf(pump.Items, "small_bottled_water")]
            && pump.Weights[Array.IndexOf(pump.Items, "large_bottled_water")]
            > pump.Weights[Array.IndexOf(pump.Items, "water_jug")], "Middle-sized water containers are more common");
        Check(Array.IndexOf(pump.Items, "empty_beer_bottle") < 0
            && Array.IndexOf(pump.Items, "wine_bottle") < 0, "Pump never substitutes non-water junk bottles");
        HashSet<string> treatment = new HashSet<string> { WaterSupplyPack.HalfTest, WaterSupplyPack.HalfTablets, "hydroreed_filter",
            "water_filter", "full_water_test_strip_box", "full_water_purification_tablet_box", "uv_filter",
            "water_filter_adv", "portable_water_purifier" };
        StashTemplate jackpot = StashTemplate.For("water");
        foreach (RewardGroup group in jackpot.Groups)
        foreach (string id in group.Ids)
            Check(treatment.Contains(id), "Pump jackpot batches only give water treatment or testing supplies: " + id);
        foreach (string id in jackpot.ValuableIds)
            Check(treatment.Contains(id), "Pump jackpot valuables are equipment, never bottles: " + id);
        Array minor = (Array)typeof(StashFind.MinorBox).GetField("All", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        int minorCount = 0;
        foreach (StashFind.MinorBox box in minor)
        {
            if (box.RegionId != "water") continue;
            ++minorCount;
            Check(treatment.Contains(box.ItemId), "Pump minor batches never contain bottles, jugs or ration packs");
            Check(LootCatalog.ValueOf(box.ItemId) == box.UnitPrice, "Minor batch quantity uses the item base price");
        }
        Check(minorCount >= 4, "Pump keeps varied minor rewards");
        Check(PumpWater.IsContainer("mini_bottle") && !PumpWater.IsContainer("water_tablet_container")
            && !PumpWater.IsContainer("water_filter") && !PumpWater.IsContainer("mini_alcohol_bottle"),
            "Only refillable native water containers get randomized contents");
        foreach (string loose in new[] { "water_tablet", "water_test_strip", "water_tablet_container", "water_test_container" })
            Check(Array.IndexOf(pump.Items, loose) < 0, "Pump never rolls loose consumables or empty packaging: " + loose);
        foreach (Region region in Region.All)
        {
            foreach (string token in region.Loot.Items)
            {
                string container = WaterSupplyPack.ContainerId(token);
                if (container == null) continue;
                int contents = WaterSupplyPack.Count(token);
                Check(contents >= 5, "Supply packs always contain at least half their native contents");
                float unitPrice = container == "water_test_container" ? 3f : 6f;
                Near(LootCatalog.ValueOf(token), 1f + contents * unitPrice, "Pack valuation includes the actual contents");
            }
        }
        Check(WaterSupplyPack.Count("full_water_test_strip_box") == 10
            && WaterSupplyPack.Count("full_water_purification_tablet_box") == 9,
            "Full pack quantities match native prebuilt boxes");
        Check(LootVariants.RealId(WaterSupplyPack.HalfTest) == "water_test_container"
            && LootVariants.RealId(WaterSupplyPack.HalfTablets) == "water_tablet_container",
            "Half packs expose native container ids to loot subscribers");
    }

    private static void ReputationBalanceRules()
    {
        int[] thresholds = { 0, 20, 40, 60 };
        float[] targets = { 12.24f, 13.464f, 14.688f, 15.912f };
        for (int stage = 0; stage < thresholds.Length; ++stage)
        {
            int opened = 0;
            foreach (Region region in Region.All)
            {
                if (region.RequiredReputation > thresholds[stage]) continue;
                ++opened;
                foreach (float specialty in new[] { 0f, 25f, 50f, 75f, 100f })
                {
                    EffectiveTable table = LootCalculator.Build(region, new LootSettings(100f, 0f, specialty, 0f, 0f), false, 1f, thresholds[stage]);
                    Check(Math.Abs(table.Ev - targets[stage]) < 0.01f,
                        "Common tier target: " + region.Id + " rep=" + thresholds[stage] + " specialty=" + specialty + " actual=" + table.Ev);
                    Check(table.JunkChance >= 0.44999f && table.JunkChance <= 0.55001f, "Baseline junk stays within 45-55 percent");
                    EffectiveTable reducedJunk = LootCalculator.Build(region, new LootSettings(50f, 0f, specialty, 0f, 0f), false, 1f, thresholds[stage]);
                    Check(Math.Abs(reducedJunk.Ev - (targets[stage] * 1.5f - 1.5f)) < 0.01f,
                        "Custom junk settings retain parity across all open destinations");
                    if (region.Id == "water" && specialty > 0f)
                    {
                        float[] sizes = new float[4];
                        string[] ids = { "small_bottled_water", "bottled_water", "large_bottled_water", "water_jug" };
                        float containers = 0f, treatment = 0f, household = 0f;
                        for (int i = 0; i < table.Count; ++i)
                        {
                            int size = Array.IndexOf(ids, table.Items[i]);
                            if (size >= 0) { sizes[size] += table.Probs[i]; containers += table.Probs[i]; }
                            if (region.Loot.CategoryOf(table.Items[i]) == 2) treatment += table.Probs[i];
                            if (region.Loot.CategoryOf(table.Items[i]) == 3) household += table.Probs[i];
                        }
                        if (specialty >= 75f) Check(containers > treatment, "Pump containers outnumber all treatment supplies");
                        if (specialty == 100f)
                        {
                            Near(containers, 0.31f, "All nine percentage points removed from household goods go to water containers");
                            Near(household, 0.09f, "Pump household share is half the previous eighteen percent");
                            Near(treatment, 0.10f, "Treatment share is preserved");
                        }
                        Near(sizes[0] / containers, 0.05f, "Small bottles retain their size share");
                        Near(sizes[1] / containers, 0.45f, "Standard bottles retain their size share");
                        Near(sizes[2] / containers, 0.45f, "Large bottles retain their size share");
                        Near(sizes[3] / containers, 0.05f, "Water jugs retain their size share");
                    }
                }
            }
            Check(opened == 2 + stage * 2, "Each tier opens exactly two more destinations");
        }
        Near(LootBalance.Target(100), targets[3], "Base reward stops growing after reputation 60");
        Near(LootBalance.Target(-20), targets[0], "Negative reputation cannot lower the starting tier");
        LootTableCache cache = new LootTableCache();
        LootSettings settings = new LootSettings(100f, 0f, 100f, 0f, 0f);
        Region gate = Region.All[0];
        EffectiveTable before = cache.Get(gate, settings, false, 1f, 19);
        EffectiveTable after = cache.Get(gate, settings, false, 1f, 20);
        Check(!ReferenceEquals(before, after) && after.Ev > before.Ev, "Crossing a reputation tier refreshes an old destination");
        Check(ReferenceEquals(after, cache.Get(gate, settings, false, 1f, 39)), "Reputation within a tier reuses the table");
        EffectiveTable at40 = cache.Get(gate, settings, false, 1f, 40);
        EffectiveTable at60 = cache.Get(gate, settings, false, 1f, 60);
        Check(!ReferenceEquals(after, at40) && !ReferenceEquals(at40, at60), "Both later base tiers invalidate the old cached table");
        Near(at60.Ev, targets[3], "Oldest destination reaches the final base target");
        Near(LootCalculator.Build(Region.All[5], settings, false, 1f, 0).Ev, targets[3],
            "Locked electronics preview uses its opening tier rather than an unreachable starting tier");
        Near(cache.Get(gate, settings, false, 1f, 0).Ev, targets[0], "Loading a lower-reputation save removes the old tier");
        Check(Array.IndexOf(ScavLoot.Get("scrap").Items, "blank_module") < 0
            && Array.IndexOf(ScavLoot.Get("vehicles").Items, "blank_module") < 0,
            "Modules are no longer shared by all three industrial themes");
        foreach (string id in new[] { "scrap", "vehicles" })
        foreach (RewardGroup group in StashTemplate.For(id).Groups)
        foreach (string item in group.Ids)
            Check(item.IndexOf("module", StringComparison.Ordinal) < 0,
                "Industrial jackpot batches preserve material/tool identity: " + id + "/" + item);
    }

    private static void ExpectedRewardBreakdownRules()
    {
        foreach (Region region in Region.All)
        foreach (int points in new[] { 0, 60, 80 })
        foreach (float junk in new[] { 0f, 50f, 100f, 150f })
        foreach (float scale in new[] { 0.5f, 1f, 2f })
        {
            LootSettings settings = new LootSettings(junk, 3f, 75f, 0.5f, 0.5f);
            EffectiveTable basis = LootCalculator.Build(region, settings, false, scale, points);
            EffectiveTable withRares = LootCalculator.Build(region, settings, true, scale, points);
            MainExpectation parts = MainExpectation.From(withRares, region.Id);
            Check(Math.Abs(parts.Total - withRares.Ev) < 0.001f, "Three displayed EV terms sum to actual main-roll EV");
            Check(Math.Abs(parts.Base - basis.Ev) < 0.001f, "Base display is the no-rare counterfactual under the same settings");
            Check(Math.Abs(parts.Valuable + parts.Supply - (withRares.Ev - basis.Ev)) < 0.001f,
                "Rare and crate uplifts exclude the ordinary items they replace");
            MainExpectation noRares = MainExpectation.From(basis, region.Id);
            Near(noRares.Valuable, 0f, "No valuable share gives zero valuable uplift");
            Near(noRares.Supply, 0f, "No crate/card share gives zero crate/card uplift");
        }
        Near(RewardExpectation.MeanMinorCount(200f, 10f), 12f, "Cheap minor rewards respect the twelve-item cap");
        Near(RewardExpectation.MeanMinorCount(200f, 20f), 10f, "Symmetric amount interval averages ten items");
        Near(RewardExpectation.MeanMinorCount(200f, 60f), 3.4f, "Minor count integrates rounded quantity thresholds");
        Near(RewardExpectation.MeanMinorCount(200f, 500f), 1f, "Expensive minor rewards keep the one-item floor");
        Near(StashFind.MinorBox.ExpectedValue("unknown_area", 200f), 0f, "An area without minor rewards has zero payout");
        Check(StashFind.MinorBox.ExpectedValue("gate", 200f) > 0f, "Gate now has useful general minor rewards");
        float gateJackpot = 80f + 201.5f + 38400f / 124f;
        Check(Math.Abs(RewardExpectation.MeanJackpotValue(StashTemplate.For("gate")) - gateJackpot) < 0.001f,
            "Jackpot payout includes group count means and weighted valuable selection");
        RewardExpectation rewards = new RewardExpectation(20f, 10f, 500f, 200f);
        Near(rewards.JackpotEv, 100f, "Jackpot EV is chance times conditional payout");
        Near(rewards.MinorEv, 16f, "Minor EV accounts for jackpot precedence");
        Near(new RewardExpectation(100f, 100f, 500f, 200f).MinorEv, 0f, "Guaranteed jackpot suppresses the minor reward");
        Near(new RewardExpectation(0f, 10f, 500f, 200f).MinorEv, 20f, "Unavailable jackpot leaves the full minor chance");
        Near(new RewardExpectation(0f, 0f, 500f, 200f).JackpotEv, 0f, "Disabled jackpot contributes zero");
    }

    private static void RegionalSupplyRules()
    {
        Dictionary<string, string> nativeCards = new Dictionary<string, string> {
            { "loot_crate_service", "ser_keycard" }, { "loot_crate_engineering", "eng_keycard" },
            { "loot_crate_medical", "med_keycard" }, { "loot_crate_security", "sec_keycard" } };
        HashSet<string> crateProfiles = new HashSet<string>();
        HashSet<string> minorProfiles = new HashSet<string>();
        foreach (Region region in Region.All)
        {
            SupplyEntry[] supplies = SupplyPool.For(region.Id);
            int totalWeight = 0;
            string profile = "";
            foreach (SupplyEntry entry in supplies)
            {
                Check(entry.Weight > 0 && nativeCards[entry.CrateId] == entry.CardId,
                    "Regional crates use their genuinely compatible native card");
                totalWeight += entry.Weight;
                profile += entry.CrateId + ":" + entry.Weight + ";";
            }
            Check(totalWeight == 100 && supplies.Length > 0, "Every area has a complete weighted crate/card pool");
            Check(crateProfiles.Add(profile), "Every area's supply type distribution is distinct");
            EffectiveTable table = LootCalculator.Build(region, new LootSettings(100f, 0f, 100f, 0.5f, 0.5f), true, 1f, 80);
            Near(table.ProbabilityOf(LootKind.SupplyCrate), 0.005f, "More crate types do not multiply the configured total chance");
            Near(table.ProbabilityOf(LootKind.Keycard), 0.005f, "More card types do not multiply the configured total chance");
            foreach (SupplyEntry entry in supplies)
            {
                float crate = 0f, card = 0f;
                for (int i = 0; i < table.Count; ++i)
                {
                    if (table.Items[i] == entry.CrateId) crate += table.Probs[i];
                    if (table.Items[i] == entry.CardId) card += table.Probs[i];
                }
                Near(crate, 0.005f * entry.Weight / 100f, "Crates follow their regional pool weight");
                Near(card, crate, "Compatible card types follow the same pool proportions");
            }
            string[] minor = StashFind.MinorBox.ItemsFor(region.Id);
            Check(minor.Length >= 4, "Every area, including Gate, has a varied minor-reward pool");
            foreach (string item in minor)
                Check(Array.IndexOf(region.Loot.Items, item) >= 0, "Minor rewards belong to the area's ordinary theme: " + region.Id + "/" + item);
            Array.Sort(minor, StringComparer.Ordinal);
            Check(minorProfiles.Add(string.Join(";", minor)), "No two areas share an identical minor-reward pool");
        }
    }

    private static void SettingsProfileRules()
    {
        Near(Tuning.ClampJunk(-1f), 0f, "Junk slider clamps its lower bound");
        Near(Tuning.ClampJunk(125f), 125f, "Junk settings above one hundred remain usable");
        Near(Tuning.ClampJunk(151f), 150f, "Junk slider clamps at one hundred fifty");
        Near(Tuning.ClampJunk(float.NaN), 100f, "Invalid junk setting falls back to the new default");
        LootPreset normal = LootPreset.For(0), enhanced = LootPreset.For(1);
        Near(normal.Junk, 100f, "Default profile restores the standard junk baseline");
        Near(normal.High, Tuning.DefaultHighPercent, "Default profile restores valuable chance");
        Near(normal.Crate, 1f, "Default profile restores one percent crate chance");
        Near(normal.Card, 1f, "Default profile restores one percent card chance");
        Near(normal.Minor, Tuning.DefaultMinorPercent, "Default profile restores minor reward chance");
        Near(normal.MinorValue, Tuning.DefaultMinorValue, "Default profile restores minor reward value");
        Near(normal.StashCap, Tuning.DefaultStashCap, "Default profile restores jackpot cap");
        Check(enhanced.Junk < normal.Junk && enhanced.SpecialtyPercent > normal.SpecialtyPercent,
            "Enhanced profile improves ordinary loot and regional focus");
        Check(enhanced.High > normal.High && enhanced.Crate >= normal.Crate && enhanced.Card >= normal.Card
            && enhanced.Minor > normal.Minor && enhanced.MinorValue > normal.MinorValue && enhanced.StashCap > normal.StashCap,
            "Enhanced profile improves valuables and bonuses without reducing crate or card chance");
        foreach (Region region in Region.All)
        {
            EffectiveTable highJunk = LootCalculator.Build(region, new LootSettings(150f, 0f, 75f, 0f, 0f), false, 1f, 60);
            Near(highJunk.JunkChance, 0.75f, "150 percent multiplier means 75 percent ordinary junk");
            EffectiveTable ordinary = LootCalculator.Build(region, normal.Settings, false, 1f, 60);
            EffectiveTable boosted = LootCalculator.Build(region, enhanced.Settings, false, 1f, 60);
            Check(boosted.Ev > ordinary.Ev, "Enhanced profile yields more basic value before rare unlocks");
            Near(boosted.ProbabilityOf(LootKind.Valuable), 0f, "Preset never bypasses the valuable reputation gate");
            Near(boosted.ProbabilityOf(LootKind.SupplyCrate), 0f, "Preset never bypasses the crate reputation gate");
        }
    }

    private static void RegionalEventRulesTests()
    {
        for (int seed = 1; seed <= 160; ++seed)
        foreach (int week in new[] { 0, 7, 28, 700 })
        foreach (int mask in new[] { 0, 5, 15, 63, 255 })
        {
            int[] plan = RegionalEventSchedule.Plan(week, week, mask, seed, null);
            int[] repeated = RegionalEventSchedule.Plan(week, week, mask, seed, null);
            int[] counts = new int[7];
            for (int i = 0; i < plan.Length; ++i)
            {
                Check(plan[i] == repeated[i], "Weekly plan is deterministic for the saved seed");
                if ((mask & (1 << i)) == 0) Check(plan[i] == -1, "Locked regions are not scheduled");
                else
                {
                    Check(plan[i] >= week && plan[i] < week + 7, "Every eligible region has one event within the week");
                    ++counts[plan[i] - week];
                }
            }
            foreach (int count in counts) Check(count <= 2, "At most two regional events share a date");
            int[] retained = RegionalEventSchedule.Plan(week, week + 4, mask, seed, plan);
            for (int i = 0; i < plan.Length; ++i) Check(retained[i] == plan[i], "Loading mid-week never moves an existing date");
        }
        int[] early = RegionalEventSchedule.Plan(7, 7, 5, 1234, null);
        int[] unlocked = RegionalEventSchedule.Plan(7, 11, 255, 1234, early);
        for (int i = 0; i < unlocked.Length; ++i)
        {
            if (early[i] >= 0) Check(unlocked[i] == early[i], "Midweek unlock leaves old events fixed");
            else Check(unlocked[i] == -1 || unlocked[i] >= 11, "New unlocks start tomorrow or later");
        }
        int[] lastDay = RegionalEventSchedule.Plan(7, 13, 255, 99, null);
        Check(Array.FindAll(lastDay, d => d == 13).Length == 2, "Partial week respects the two-event capacity");
        Near(RegionalEventRules.WoundChance(10f, true), 8f, "Gate event reduces relative wound risk");
        Near(RegionalEventRules.WoundChance(0f, true), 0f, "Regional event preserves injury immunity");
        Near(RegionalEventRules.WoundChance(10f, false), 10f, "Inactive regional event leaves wound chance alone");

        HashSet<string> definitions = new HashSet<string>();
        foreach (RegionalEvent effect in RegionalEvent.All) Check(definitions.Add(effect.RegionId), "Each area has one distinct event definition");
        Check(definitions.Count == Region.All.Length, "All eight regions have an event");
        foreach (int points in new[] { 0, 20, 40, 60 })
        foreach (Region region in Region.All)
        {
            if (region.RequiredReputation > points) continue;
            EffectiveTable before = LootCalculator.Build(region, LootPreset.For(0).Settings, false, 1f, points);
            EffectiveTable after = RegionalEventRules.Apply(region, before, RegionalEvent.For(region.Id));
            float growth = after.EventEv / before.Ev;
            Check(growth >= 0.10f && growth <= 0.20f, "Default event yields are within the 10-20 percent ordinary-reward budget");
        }
        foreach (Region region in Region.All)
        foreach (int reputation in new[] { 0, 20, 40, 60, 80 })
        foreach (float junk in new[] { 0f, 50f, 100f, 150f })
        foreach (float specialty in new[] { 25f, 75f, 100f })
        foreach (float scale in new[] { 0f, 0.5f, 1f, 1.5f })
        {
            RegionalEvent effect = RegionalEvent.For(region.Id);
            EffectiveTable before = LootCalculator.Build(region, new LootSettings(junk, 3f, specialty, 1f, 1f), reputation >= 80, scale, reputation);
            float[] untouched = (float[])before.Probs.Clone();
            EffectiveTable after = RegionalEventRules.Apply(region, before, effect);
            float probability = 0f, expected = 0f;
            for (int i = 0; i < after.Count; ++i)
            {
                Check(after.Items[i] == before.Items[i], "Events preserve slot order for the live probability window");
                Check(after.Probs[i] >= 0f, "Event probabilities remain nonnegative");
                Near(before.Probs[i], untouched[i], "Applying regional effects never mutates the no-event table");
                if (before.Slots[i].Kind != LootKind.Ordinary)
                    Near(after.Probs[i], before.Probs[i], "Regional events preserve each valuable, crate and card probability");
                probability += after.Probs[i];
                expected += after.Probs[i] * after.ExpectedValueAt(i, region.Id);
            }
            Near(probability, 1f, "Regional probabilities are normalized even with native events");
            Near(expected, after.Ev, "Event EV matches the displayed final probabilities and item state");
            Check(after.Ev + 0.0001f >= before.Ev, "An event never reduces expected loot value at extreme settings");
            Check(after.EventEv >= 0f, "Event uplift displays zero rather than negative zero at floating-point boundaries");
            Check(after.JunkChance <= before.JunkChance + 0.00001f, "Regional event never raises junk probability");
            MainExpectation parts = MainExpectation.From(after, region.Id);
            Near(parts.Base + parts.Valuable + parts.Supply + parts.Event, after.Ev, "Four-part EV decomposition includes exactly one regional uplift");
            Near(parts.Event, after.Ev - before.Ev, "Regional uplift uses the same-day no-regional-event counterfactual");
            Check(ReferenceEquals(after, RegionalEventRules.Apply(region, after, effect)), "Regional effects cannot be applied twice");
            if (region.Id == "water")
            {
                float[] sizes = new float[4];
                string[] ids = { "small_bottled_water", "bottled_water", "large_bottled_water", "water_jug" };
                float sum = 0f;
                for (int i = 0; i < ids.Length; ++i)
                {
                    int slot = Array.FindIndex(after.Items, id => id == ids[i]);
                    sizes[i] = after.Probs[slot]; sum += sizes[i];
                }
                if (specialty == 100f && sum > 0f)
                {
                    Near(sizes[0] / sum, 0.05f, "Pump event preserves small-bottle share");
                    Near(sizes[1] / sum, 0.45f, "Pump event preserves standard-bottle share");
                    Near(sizes[2] / sum, 0.45f, "Pump event preserves large-bottle share");
                    Near(sizes[3] / sum, 0.05f, "Pump event preserves jug share");
                }
            }
        }
        foreach (string id in new[] { "small_bottled_water", "bottled_water", "large_bottled_water", "water_jug" })
            Check(PumpWater.ExpectedValue(id, LootCatalog.ValueOf(id), true) > PumpWater.ExpectedValue(id, LootCatalog.ValueOf(id)),
                "Pump event water-state value uses the same shifted fill distribution");
        double ordinaryFill = 0d, eventFill = 0d;
        for (int i = 0; i < 1000; ++i)
        {
            float u = (i + 0.5f) / 1000f;
            int ordinary = PumpWater.FillParts(2000, u), boosted = PumpWater.FillParts(2000, u, true);
            Check(boosted >= ordinary && boosted <= 2000 && boosted >= 200,
                "Event water samples shift up without empty or overflowing containers");
            ordinaryFill += ordinary; eventFill += boosted;
        }
        Check(eventFill > ordinaryFill * 1.1d, "Pump event meaningfully increases mean water volume");
        Region gate = Region.All[0];
        LootTableCache cache = new LootTableCache();
        LootSettings settings = new LootSettings(100f, 3f, 75f, 1f, 1f);
        EffectiveTable normal = cache.Get(gate, settings, true, 0.5f, 80);
        EffectiveTable active = cache.Get(gate, settings, true, 0.5f, 80, RegionalEvent.For(gate.Id));
        Check(!ReferenceEquals(normal, active), "Event start invalidates the area's cached table");
        Check(ReferenceEquals(active, cache.Get(gate, settings, true, 0.5f, 80, RegionalEvent.For(gate.Id))), "Same event reuses the current table");
        EffectiveTable expired = cache.Get(gate, settings, true, 0.5f, 80);
        Near(expired.Ev, normal.Ev, "Event expiry restores the ordinary table");
        Check(expired.RegionalEvent == null, "Expired table carries no stale event");
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
            foreach (float junk in new[] { 0f, 50f, 100f, 150f })
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
                if (unlocked)
                {
                    float crate = table.ProbabilityOf(LootKind.SupplyCrate);
                    Check(crate > 0f && crate <= 0.050001f, "Reserved shares respect the available probability mass");
                    Near(crate, table.ProbabilityOf(LootKind.Keycard), "Equal reserved settings scale together");
                    if (table.JunkChance + high / 100f + 0.1f <= 0.995f)
                        Near(crate, 0.05f, "Unconstrained crate absolute probability");
                }
                Check(table.PickIndex(0f) >= 0 && table.PickIndex(1f) >= 0, "Sampling edges");
            }

            EffectiveTable normal = LootCalculator.Build(region, new LootSettings(50f, 3f, 75f, 0.5f, 0.5f), true);
            int[] samples = new int[normal.Count];
            const int count = 10000;
            for (int i = 0; i < count; ++i) ++samples[normal.PickIndex((i + 0.5f) / count)];
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
            bool extractorInBase = Array.IndexOf(region.Loot.Items, "module_extractor") >= 0;
            Check(valuableExtractor && (!extractorInBase || ordinaryExtractor), "Ordinary/valuable duplicate must be shown separately");
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

    private static void EventAdjustedLootRules()
    {
        Near(ProbabilityMath.JunkScaleFromModifier(-50f), 0.5f, "Fresh dump halves junk weight");
        Near(ProbabilityMath.JunkScaleFromModifier(-50f - 50f), 0f, "Native modifiers add before clamping");
        Near(ProbabilityMath.JunkScaleFromModifier(-150f), 0f, "Junk weight never becomes negative");
        Near(ProbabilityMath.JunkScaleFromModifier(50f), 1.5f, "Positive junk event");
        foreach (Region region in Region.All)
        foreach (bool unlocked in new[] { false, true })
        foreach (float junk in new[] { 0f, 50f, 100f, 150f })
        foreach (float specialty in new[] { 0f, 75f, 100f })
        {
            LootSettings settings = new LootSettings(junk, 30f, specialty, 5f, 5f);
            EffectiveTable basis = LootCalculator.Build(region, settings, unlocked);
            foreach (float scale in new[] { 0f, 0.5f, 1f, 1.5f, 2f })
            {
                EffectiveTable table = LootCalculator.Build(region, settings, unlocked, scale);
                // Independent odds formula: leave every non-junk weight intact.
                float denominator = 1f - basis.JunkChance + basis.JunkChance * scale;
                float expectedValue = 0f, total = 0f;
                for (int i = 0; i < table.Count; ++i)
                {
                    float expected = basis.Probs[i] * (basis.Items[i] == "junk" ? scale : 1f) / denominator;
                    Near(table.Probs[i], expected, "Event-adjusted probability: " + region.Id + "/" + table.Items[i]);
                    Check(table.Probs[i] >= 0f && !float.IsNaN(table.Probs[i]), "Event probabilities are finite and nonnegative");
                    expectedValue += expected * LootCatalog.ExpectedValueOf(table.Items[i], region.Id);
                    total += table.Probs[i];
                }
                Near(total, 1f, "Event table remains normalized");
                Check(Math.Abs(table.Ev - expectedValue) < 0.002f, "Expected value uses event probabilities");
                Near(table.JunkChance, basis.JunkChance * scale / denominator, "Displayed junk chance is normalized, not directly halved");
                Near(table.HighChance, table.ProbabilityOf(LootKind.Valuable), "Valuable subtotal follows the event");
                Near(table.ProbabilityOf(LootKind.SupplyCrate), basis.ProbabilityOf(LootKind.SupplyCrate) / denominator, "Crate share follows the event once");
                Near(table.ProbabilityOf(LootKind.Keycard), basis.ProbabilityOf(LootKind.Keycard) / denominator, "Card share follows the event once");
                foreach (float edge in new[] { 0f, 1f })
                    Check(table.Probs[table.PickIndex(edge)] > 0f, "Sampling edges never select a zero-probability item");
            }
        }

        foreach (Region region in Region.All)
        foreach (float scale in new[] { 0f, 0.5f, 1f, 1.5f, 2f })
        {
            EffectiveTable table = LootCalculator.Build(region, new LootSettings(50f, 3f, 75f, 0.5f, 0.5f), true, scale);
            int[] sampled = new int[table.Count];
            const int samples = 10000;
            for (int i = 0; i < samples; ++i) ++sampled[table.PickIndex((i + 0.5f) / samples)];
            for (int i = 0; i < table.Count; ++i)
                Check(Math.Abs(sampled[i] / (float)samples - table.Probs[i]) < 0.00021f, "Every event-adjusted displayed probability matches sampling");
        }

        LootTableCache cache = new LootTableCache();
        Region area = Region.All[1];
        LootSettings original = new LootSettings(50f, 3f, 75f, 0.5f, 0.5f);
        EffectiveTable normal = cache.Get(area, original, true);
        EffectiveTable reduced = cache.Get(area, original, true, 0.5f);
        Check(!ReferenceEquals(normal, reduced) && reduced.JunkChance < normal.JunkChance, "Starting an event invalidates the cached table");
        Check(ReferenceEquals(reduced, cache.Get(area, original, true, 0.5f)), "Unchanged event reuses the table");
        EffectiveTable increased = cache.Get(area, original, true, 1.5f);
        Check(!ReferenceEquals(reduced, increased) && increased.JunkChance > normal.JunkChance, "Changing an event updates the cached table");
        EffectiveTable restored = cache.Get(area, original, true);
        Near(restored.JunkChance, normal.JunkChance, "Event end restores the configured junk chance without editing settings");
        Near(restored.Ev, normal.Ev, "Event end restores expected value");
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
        Check(PerkCatalog.For(100).DescriptionZh.Contains("夜间服务"), "Tier 100 describes the actual night service");
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

    private static void OutingAndMilestoneRules()
    {
        StoreService service = new StoreService();
        service.isUsing = true;
        OutingRules.Configure(service, 99);
        Check(service.tooltip == ScavengerOuting.TooltipKey, "Native tooltip field must hold a translation key, not literal prose");
        string tooltip = null;
        Check(!OutingTooltipPatch.Resolve(service.tooltip, () => "花 20 信用点", ref tooltip)
            && tooltip == "花 20 信用点", "Our tooltip key resolves before the native translation lookup");
        tooltip = "native result";
        Check(OutingTooltipPatch.Resolve("another_service_tooltip", () => { throw new Exception("unrelated lookup intercepted"); }, ref tooltip)
            && tooltip == "native result", "Other UI translations stay on the native path");
        Check(!service.unlocked && !service.isUsing && service.cost == 20, "Locked service cannot remain selected");
        OutingRules.Configure(service, 100);
        Check(service.unlocked && service.cost == 20 && !service.isMandatory, "100 reputation unlocks optional 20-credit service");
        service.isUsing = true;
        OutingRules.Configure(service, 150);
        Check(service.isUsing && service.cost == 20, "Refreshing the panel preserves an explicit booking");
        int benefitDay = OutingRules.BenefitDay(7);
        Check(benefitDay == 8 && !OutingRules.Active(7, benefitDay), "Booking cannot buff the same night");
        Check(OutingRules.Active(8, benefitDay) && !OutingRules.Active(9, benefitDay), "Benefit lasts exactly the following day");
        Check(!OutingRules.Active(-1, -1) && OutingRules.BenefitDay(-1) == -1, "No active service without a valid day");
        Check(OutingRules.Attempts(5, true) == 7 && OutingRules.Attempts(7, true) == 9, "Service stacks with scavenger start");
        Check(OutingRules.Attempts(5, false) == 5 && OutingRules.Attempts(0, true) == 0, "Expiry and native inability preserved");

        HashSet<string> keys = new HashSet<string>();
        foreach (MilestoneNote note in MilestoneNotes.All)
        {
            Check(keys.Add(note.Key), "Each note has its own per-save receipt");
            Check(!note.Due(note.Threshold - 1, false) && note.Due(note.Threshold, false), "Milestone threshold");
            Check(!note.Due(200, true), "Received note never repeats");
            Check(!note.GivesToken(true), "Legacy token receipt prevents a duplicate");
            Check(note.GivesToken(false) == (note.Threshold == 100), "Token belongs exclusively to 100 reputation");
        }
        Check(!MilestoneNotes.All[0].Body(true, false).Contains("信物"), "20-point letter no longer claims a token");
        Check(MilestoneNotes.All[1].Body(true, false).Contains("搬回你店里"), "40-point letter explains hauling");
        Check(MilestoneNotes.All[2].Body(true, true).Contains("20 信用点")
            && MilestoneNotes.All[2].Body(true, true).Contains("2 回"), "100-point letter follows service price and attempts");
        Check(!MilestoneNotes.All[2].Body(true, false).Contains("这枚信物"), "Upgrade note does not imply a second token");

        List<string> onCounter = new List<string>();
        bool received = false;
        bool full = true;
        Func<string, bool> place = item => {
            if (full && item == "note") return false;
            onCounter.Add(item); return true;
        };
        Action<List<string>> rollback = items => { foreach (string item in items) onCounter.Remove(item); };
        Check(!MilestoneDelivery.Give(true, () => "token", () => "note", place, rollback, () => received = true),
            "Full counter defers the token-and-note bundle");
        Check(onCounter.Count == 0 && !received, "Failed note rolls token back without marking receipt");
        full = false;
        Check(MilestoneDelivery.Give(true, () => "token", () => "note", place, rollback, () => received = true)
            && received && onCounter.Count == 2, "Retry delivers and marks both items");
        onCounter.Clear(); received = false;
        Check(MilestoneDelivery.Give(false, () => { throw new Exception("duplicate token"); }, () => "note", place,
            rollback, () => received = true) && onCounter.Count == 1, "Legacy player receives only the new letter");
        onCounter.Clear();
        bool failed = false;
        try { MilestoneDelivery.Give(true, () => "token", () => "note", place, rollback, () => { throw new Exception("save receipt failed"); }); }
        catch (Exception) { failed = true; }
        Check(failed && onCounter.Count == 0, "Receipt failure also rolls the whole bundle back");
    }

    private static PlayerStore TrafficSave(int nightDay)
    {
        PlayerStore store = (PlayerStore)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PlayerStore));
        store.startScenarioId = ScavengerStart.ScenarioId;
        store.healthData = new HealthData();
        store.healthData.wentToAfterhoursLastNight = true;
        store.afterhourState = new Dictionary<string, AfterhourLocationState>();
        store.afterhourState.Add(ScavengerStart.DumpingGroundId,
            new AfterhourLocationState { visits = 1, lastVisitDay = nightDay });
        store.nightLogs = new List<string>();
        return store;
    }

    private static void ScavengerTrafficRulesTests()
    {
        foreach (int night in new[] { 1, 2, 12, 99 })
        {
            PlayerStore store = TrafficSave(night);
            Check(ScavengerTrafficRules.ProtectsNightReport(store, night), "Dump visit suppresses the false penalty report that night");
            Check(ScavengerTrafficRules.ProtectsMorning(store, night + 1), "The same saved visit protects tomorrow's traffic");
            Check(!ScavengerTrafficRules.ProtectsMorning(store, night)
                && !ScavengerTrafficRules.ProtectsMorning(store, night + 2), "Protection belongs only to the following morning");
            Check(!ScavengerTrafficRules.ProtectsNightReport(store, night + 1), "Yesterday's visit cannot suppress tonight's unrelated report");

            // Recreate only fields the native save persists, without any MapUI instance.
            PlayerStore reloaded = TrafficSave(store.afterhourState[ScavengerStart.DumpingGroundId].lastVisitDay);
            Check(ScavengerTrafficRules.ProtectsMorning(reloaded, night + 1), "Morning reload preserves the exemption without transient map history");
            foreach (int unpenalizedCount in new[] { 0, 1, 6, 7 })
            {
                RestoreScope scope = ScavengerCustomerPatch.BeginProtectedCount(reloaded, night + 1);
                Check(scope != null && !reloaded.healthData.wentToAfterhoursLastNight, "Native customer count sees the exempt state before its own clamp");
                Check(ScavengerCustomerPatch.BeginProtectedCount(reloaded, night + 1) == null, "Nested counts do not overwrite the restoration state");
                int count = Math.Max(0, Math.Min(6, unpenalizedCount - (reloaded.healthData.wentToAfterhoursLastNight ? 1 : 0)));
                Check(count == Math.Max(0, Math.Min(6, unpenalizedCount)), "Exemption respects both native clamp boundaries");
                scope.Restore(); scope.Restore();
                Check(reloaded.healthData.wentToAfterhoursLastNight, "Counting restores real night activity even after repeated cleanup");
            }

            const string warning = "Missed one customer after going out last night.";
            // Match the serialized output of native AddNightLog without its newer-runtime TrimStart(char) dependency.
            reloaded.nightLogs.Add("<color=#9c3738>" + warning + "</color>");
            reloaded.nightLogs.Add(warning);
            reloaded.nightLogs.Add("<color=#9c3738>Other night activity report.</color>");
            Check(ScavengerTrafficRules.RemoveMisleadingReport(reloaded, night + 1, warning) == 2,
                "Old saved penalty reports are removed only when the matching night was exempt");
            Check(reloaded.nightLogs.Count == 1 && reloaded.nightLogs[0].Contains("Other night activity"), "Unrelated night reports remain intact");
            Check(ScavengerTrafficRules.RemoveMisleadingReport(reloaded, night + 1, warning) == 0, "Repeated report repair is idempotent");
        }

        PlayerStore ordinary = TrafficSave(1);
        ordinary.startScenarioId = "another_start";
        ordinary.nightLogs.Add("<color=#9c3738>Penalty</color>");
        Check(!ScavengerTrafficRules.ProtectsMorning(ordinary, 2)
            && !ScavengerTrafficRules.ProtectsNightReport(ordinary, 1), "Other starts retain native customer penalties and report");
        Check(ScavengerTrafficRules.RemoveMisleadingReport(ordinary, 2, "Penalty") == 0, "No cleanup of another start's legitimate penalty");
        PlayerStore elsewhere = TrafficSave(1);
        elsewhere.afterhourState.Clear();
        elsewhere.afterhourState.Add("commissary", new AfterhourLocationState { visits = 1, lastVisitDay = 1 });
        Check(!ScavengerTrafficRules.ProtectsMorning(elsewhere, 2)
            && !ScavengerTrafficRules.ProtectsNightReport(elsewhere, 1), "Other destinations do not gain the dump exemption");
        Check(ScavengerCustomerPatch.BeginProtectedCount(elsewhere, 2) == null
            && elsewhere.healthData.wentToAfterhoursLastNight, "Ineligible counting leaves night activity unchanged");
        PlayerStore neverVisited = TrafficSave(0);
        neverVisited.afterhourState[ScavengerStart.DumpingGroundId].visits = 0;
        Check(!ScavengerTrafficRules.ProtectsMorning(neverVisited, 1), "Default native zero-day visit data never grants a free customer");
        PlayerStore stayedHome = TrafficSave(1);
        stayedHome.healthData.wentToAfterhoursLastNight = false;
        Check(!ScavengerTrafficRules.ProtectsMorning(stayedHome, 2), "Staying home does not add a second exemption");
        Check(!ScavengerTrafficRules.ProtectsMorning(null, 2)
            && !ScavengerTrafficRules.ProtectsNightReport(null, 1), "No save means no exemption");
    }

    private static void JackpotNewsRules()
    {
        FieldInfo singleton = typeof(StoreStation).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = singleton.GetValue(null);
        FieldInfo playerSingleton = typeof(PlayerStore).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previousPlayer = playerSingleton.GetValue(null);
        PlayerStore player = (PlayerStore)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PlayerStore));
        player.startingPerks = new List<StartingPerk>();
        playerSingleton.SetValue(null, player);
        StoreStation station = (StoreStation)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StoreStation));
        singleton.SetValue(null, station);
        try
        {
            foreach (int hitDay in new[] { 1, 2, 12, 99 })
            {
                int today = hitDay + 1;
                station.dayCounter = today;
                StoreEventManager manager = new StoreEventManager();
                station.storeEventManager = manager;
                StoreEvent ev = new StoreEvent();
                ev.identifier = "dgm_stash_test";
                ev.newsName = "Jackpot news";
                ev.displayName = "Food surplus";
                ev.duration = ev.totalDuration = 1;
                ev.cooldown = 60;
                StashFind.QueueNewsForToday(manager, ev);
                Check(manager.futurEvents.Count == 1 && ReferenceEquals(manager.futurEvents[0], ev), "News enters the native queue");
                Check(manager.futurEventsDay[0] == 0, "Opening-day news is due immediately, never delayed by the day number");
                Check(ev.startDay == today && ev.IsKnown, "Native queue assigns today's calendar date and reveals the event");
                Check((int)StoreEventManager.GetEventDayType(ev, today) == 4, "Calendar sees a single-day event, not an out-of-range blank continuation");
                Check(ev.GetDisplayName() == "Food surplus", "Calendar retains the event title");
                Check(manager.GetActiveEventOnDay(today, false, false).Contains(ev), "Calendar includes the jackpot on the following day");
                Check(!manager.GetActiveEventOnDay(today + 1, false, false).Contains(ev), "Jackpot market event does not spill into a later day");
                Check(manager.GetActiveEvents().Count == 0, "The normal opening-day handler remains responsible for activation");
                manager.HandleFuturEvent();
                Check(manager.futurEvents.Count == 0 && manager.futurEventsDay.Count == 0, "Native opening-day handling consumes the news immediately");
                Check(manager.GetActiveEvents().Contains(ev) && !ev.isHiddenFromNewsPaper, "News is active and eligible for the native newspaper that day");
                Check(ev.startDay == today && (int)StoreEventManager.GetEventDayType(ev, today) == 4, "Activation preserves the titled single-day calendar entry");
                manager.HandleFuturEvent();
                Check(manager.GetActiveEvents().Count == 1, "Repeated future-queue handling cannot duplicate the same news");
            }

            station.dayCounter = 2;
            StoreEventManager legacyManager = new StoreEventManager();
            station.storeEventManager = legacyManager;
            StoreEvent legacy = new StoreEvent();
            legacy.identifier = "dgm_stash_refuse";
            legacy.duration = legacy.totalDuration = 1;
            legacyManager.QueueFuturEvent(legacy, 2, true);
            legacyManager.futurEventsDay[0] = 1; // 旧版第 2 天 OnDayStart 已减过一次。
            StoreEvent unrelated = new StoreEvent();
            unrelated.identifier = "another_mod_event";
            unrelated.duration = 1;
            legacyManager.QueueFuturEvent(unrelated, 5, true);
            Check(StashFind.RepairLegacyNews(legacyManager, 1, 2, "refuse") == 0, "Legacy repair never publishes before its receipt date");
            Check(StashFind.RepairLegacyNews(legacyManager, 2, 2, "gate") == 0, "Legacy repair is constrained to the saved jackpot region");
            Check(StashFind.RepairLegacyNews(legacyManager, 2, 2, "refuse") == 1, "The reported day-2 save repairs its delayed jackpot news");
            Check(legacy.startDay == 2 && legacyManager.GetActiveEvents().Contains(legacy), "Repaired news is active today for calendar and newspaper");
            Check(legacyManager.futurEvents.Count == 1 && ReferenceEquals(legacyManager.futurEvents[0], unrelated)
                && legacyManager.futurEventsDay[0] == 5, "Repair preserves unrelated events and their countdowns");
            Check(StashFind.RepairLegacyNews(legacyManager, 2, 2, "refuse") == 0
                && legacyManager.GetActiveEvents().Count == 1, "Repeated load cannot duplicate repaired news");

            StoreEventManager currentManager = new StoreEventManager();
            StoreEvent current = new StoreEvent();
            current.identifier = "dgm_stash_refuse";
            current.duration = 1;
            StashFind.QueueNewsForToday(currentManager, current);
            Check(StashFind.RepairLegacyNews(currentManager, 2, 2, "refuse") == 0
                && currentManager.futurEvents.Count == 1, "Correctly scheduled news is never treated as a legacy error");
        }
        finally
        {
            singleton.SetValue(null, previous);
            playerSingleton.SetValue(null, previousPlayer);
        }
    }

    private static void JackpotWindowRules()
    {
        Check(Tuning.DefaultStashWindow, "Existing saves keep adaptive mode by default");
        float window = JackpotChanceRules.Start;
        int phase = 0;
        foreach (float expected in new[] { 0.1f, 0.2f, 0.4f, 0.8f, 0.85f })
        {
            JackpotChanceRules.AfterMiss(ref window, ref phase, 1f, true);
            Near(window, expected, "Adaptive slow-start then linear growth");
        }
        JackpotChanceRules.AfterHit(ref window, ref phase, 1f, true);
        Near(window, 0.425f, "Adaptive win halves chance");
        JackpotChanceRules.AfterMiss(ref window, ref phase, 1f, true);
        Near(window, 0.475f, "After a win growth stays linear");
        for (int i = 0; i < 100; ++i) JackpotChanceRules.AfterMiss(ref window, ref phase, 1f, true);
        Near(window, 1f, "Adaptive window stops at cap");

        foreach (float cap in new[] { 0.1f, 1f, 5f })
        {
            window = 0.075f; phase = 0;
            Near(JackpotChanceRules.Current(window, cap, false), cap, "Fixed mode starts at the effective cap");
            for (int i = 0; i < 20; ++i)
            {
                JackpotChanceRules.AfterMiss(ref window, ref phase, cap, false);
                JackpotChanceRules.AfterHit(ref window, ref phase, cap, false);
                Near(JackpotChanceRules.Current(window, cap, false), cap, "Fixed mode never grows or halves");
            }
            Near(window, 0.075f, "Fixed mode freezes saved window progress");
            Check(phase == 0, "Fixed mode freezes the adaptive phase");
            Near(JackpotChanceRules.Current(window, cap, true), 0.075f, "Re-enable resumes paused progress");
        }
        Near(JackpotChanceRules.Current(0.8f, 0.1f, true), 0.1f, "Low-reputation effective cap applies in adaptive mode");
        Near(JackpotChanceRules.Current(0.8f, 0.1f, false), 0.1f, "Low-reputation effective cap applies in fixed mode");
        window = 0.05f; phase = 0;
        JackpotChanceRules.AfterHit(ref window, ref phase, 1f, true);
        Near(window, 0.05f, "Adaptive win retains minimum chance");
    }

    private static void DailyAndMinorWindowRules()
    {
        foreach (float cap in new[] { 0.1f, 1f, 2f, 5f })
        {
            JackpotDailyState state = new JackpotDailyState();
            Check(state.Advance(10, cap, true, -1), "Daily window initializes on the current date");
            float start = state.Chance;
            for (int day = 1; day <= 5; ++day)
            {
                Check(state.Advance(10 + day, cap, true, -1), "A new date advances the jackpot window once");
                Near(state.Chance, start + (cap - start) * day / 5f, "Five missed days linearly reach the jackpot cap");
                for (int search = 0; search < 20; ++search)
                    Check(!state.Advance(10 + day, cap, true, -1), "Repeated searches and previews on the same date never grow the jackpot window");
            }
            Near(state.Chance, cap, "Five missed natural days reach each configured cap");
            Check(!state.Advance(15, cap, true, 15), "Winning does not change the current day's fixed probability");
            state.Advance(16, cap, true, 15);
            Near(state.Chance, Math.Max(JackpotChanceRules.Start, cap * 0.5f), "A winning day halves the next day's jackpot probability");
            float reduced = state.Chance;
            for (int day = 1; day <= 5; ++day)
            {
                state.Advance(16 + day, cap, true, 15);
                Near(state.Chance, reduced + (cap - reduced) * day / 5f, "Recovery after a win also takes five missed days");
            }
            float saved = state.Chance;
            int savedMissDays = state.MissDays;
            state.Advance(100, cap, false, -1);
            Near(state.Chance, saved, "Disabling the daily window freezes its saved probability");
            Check(state.MissDays == savedMissDays, "Disabled calendar days do not accumulate missed-day growth");
            state.Advance(100, cap, true, -1);
            Near(state.Chance, saved, "Re-enabling does not catch up paused days");
            state.Advance(1000000, cap, true, -1);
            Near(state.Chance, cap, "Long calendar gaps safely catch up to the cap");
        }
        JackpotDailyState pendingWin = new JackpotDailyState();
        pendingWin.Advance(1, 1f, true, -1);
        pendingWin.Advance(6, 1f, true, -1);
        pendingWin.Advance(8, 1f, true, 7);
        Near(pendingWin.Chance, 0.5f, "Catch-up applies a pending winning day at its correct date");
        pendingWin.Advance(10000, 1f, true, 7);
        Near(pendingWin.Chance, 1f, "Catch-up continues after a pending win");
        pendingWin.Advance(10000, 5f, true, 7);
        Near(pendingWin.Chance, 1f, "Increasing the cap preserves current odds");
        pendingWin.Advance(10005, 5f, true, 7);
        Near(pendingWin.Chance, 5f, "New cap is reached within a fresh five-day cycle");
        pendingWin.Advance(10005, 0.1f, true, 7);
        Near(pendingWin.Chance, 0.1f, "Lower cap immediately limits the daily window");
        JackpotDailyState changingCap = new JackpotDailyState();
        changingCap.Advance(1, 1f, true, -1);
        changingCap.Advance(6, 1f, true, -1);
        changingCap.Advance(7, 2f, true, 6);
        Near(changingCap.Chance, 0.5f, "Changing cap on a new day still consumes yesterday's win");

        Check(Tuning.DefaultMinorWindow, "The new minor window is enabled by default");
        float minor = MinorChanceRules.StartFor(3f); int phase = 0;
        Near(minor, 0.3f, "Minor default window starts at 0.3 percent");
        foreach (float expected in new[] { 0.6f, 1.2f, 2.4f, 2.5f, 2.6f })
        {
            MinorChanceRules.AfterSearch(ref minor, ref phase, 3f, true, false);
            Near(minor, expected, "Minor misses first double then add 0.1 percentage points per search");
        }
        MinorChanceRules.AfterSearch(ref minor, ref phase, 3f, true, true);
        Near(minor, 1.3f, "Minor win halves its independent window");
        MinorChanceRules.AfterSearch(ref minor, ref phase, 3f, true, false);
        Near(minor, 1.4f, "Minor recovery remains additive after a win");
        float paused = minor; int pausedPhase = phase;
        MinorChanceRules.AfterSearch(ref minor, ref phase, 3f, false, false);
        MinorChanceRules.AfterSearch(ref minor, ref phase, 3f, false, true);
        Near(minor, paused, "Fixed minor mode pauses both miss and win updates");
        Check(phase == pausedPhase, "Fixed minor mode preserves its phase");
        Near(MinorChanceRules.Current(minor, 3f, false), 3f, "Fixed minor mode uses the cap directly");
        MinorChanceRules.AfterSearch(ref minor, ref phase, 0f, true, false);
        Near(minor, paused, "Zero minor cap pauses window progress");
        Near(MinorChanceRules.Current(minor, 0f, true), 0f, "Zero cap disables minor awards");
        foreach (float cap in new[] { 0.1f, 1f, 3f, 5f, 10f })
        {
            minor = MinorChanceRules.StartFor(cap); phase = 0;
            for (int search = 0; search < 200; ++search)
            {
                float current = MinorChanceRules.Current(minor, cap, true);
                Check(current >= 0f && current <= cap, "Minor window always respects its configured cap");
                Near(current * PerkCatalog.MinorMultiplier(0), current / 10f, "Low reputation scales current minor odds, not just its cap");
                MinorChanceRules.AfterSearch(ref minor, ref phase, cap, true, search % 23 == 0);
            }
        }
        Near(MinorChanceRules.Current(float.NaN, 3f, true), 0.3f, "Invalid stored minor state falls back to its starting probability");
        RewardExpectation parts = new RewardExpectation(1f, 0.6f, 1000f, 200f);
        Near(parts.JackpotEv, 10f, "Daily jackpot EV uses current actual odds");
        Near(parts.MinorEv, 200f * 0.006f * 0.99f, "Minor EV uses current window odds and jackpot precedence");
    }
}
