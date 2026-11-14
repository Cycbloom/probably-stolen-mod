using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// Turns the fixed per-area weight tables plus the player's settings into a
    /// runtime probability table, and computes expected value from the same
    /// unitValue figures used for the balance design.
    /// </summary>
    internal static class Tuning
    {
        internal const float DefaultJunkPercent = 50f;
        internal const float DefaultHighPercent = 0f;
        internal const int DefaultSpecialtyTier = 2;

        internal const float JunkMin = 0f;
        internal const float JunkMax = 100f;
        internal const float HighMin = 0f;
        internal const float HighMax = 30f;

        private const string SectionName = "拾荒掉落 / Loot";

        /// <summary>
        /// One shared tier for every area. Each area still applies it to its
        /// own signature pool, so the same tier yields different goods.
        /// </summary>
        internal const string SpecialtyKey = "specialty";

        /// <summary>Dropdown labels and the percentage each tier stands for.</summary>
        private static readonly string[] SpecialtyLabels = { "较少", "中等", "较多", "全部" };
        private static readonly float[] SpecialtyPercents = { 25f, 50f, 75f, 100f };

        private static ConfigEntry<float> junkEntry;
        private static ConfigEntry<float> highEntry;
        private static ConfigEntry<string> specialtyEntry;
        private static readonly Dictionary<string, EffectiveTable> cache =
            new Dictionary<string, EffectiveTable>();
        private static readonly Dictionary<string, float> baseEvCache =
            new Dictionary<string, float>();
        private static bool hasLive;
        private static float liveJunk;
        private static float liveHigh;
        private static bool initialized;

        private static float EntryJunk()
        {
            if (junkEntry == null) return DefaultJunkPercent;
            float value = junkEntry.Value;
            if (float.IsNaN(value)) return DefaultJunkPercent;
            return Mathf.Round(Mathf.Clamp(value, JunkMin, JunkMax));
        }

        private static float EntryHigh()
        {
            if (highEntry == null) return DefaultHighPercent;
            float value = highEntry.Value;
            if (float.IsNaN(value)) return DefaultHighPercent;
            return Mathf.Round(Mathf.Clamp(value, HighMin, HighMax) * 2f) / 2f;
        }

        private static int EntrySpecialtyTier()
        {
            if (specialtyEntry == null) return DefaultSpecialtyTier;
            int tier = Array.IndexOf(SpecialtyLabels, specialtyEntry.Value);
            return tier < 0 ? DefaultSpecialtyTier : tier;
        }

        /// <summary>
        /// 25/50/75/100 percent, picked from four fixed tiers. Each area applies it
        /// to its own signature list, junk excluded.
        /// </summary>
        internal static float SpecialtyPercent
        {
            get { return SpecialtyPercents[EntrySpecialtyTier()]; }
        }

        /// <summary>0-based tier index: 0 = 较少, 1 = 中等, 2 = 较多, 3 = 全部.</summary>
        internal static int SpecialtyTier
        {
            get { return EntrySpecialtyTier(); }
        }

        /// <summary>
        /// While a slider is being dragged the values live here instead of in the
        /// config entries, so the file is written once when the drag ends.
        /// </summary>
        internal static float JunkPercent { get { return hasLive ? liveJunk : EntryJunk(); } }

        internal static float HighPercent { get { return hasLive ? liveHigh : EntryHigh(); } }

        /// <summary>True while a slider drag is holding unsaved values in memory.</summary>
        internal static bool HasLive { get { return hasLive; } }

        internal static float ClampJunk(float value)
        {
            return float.IsNaN(value) ? DefaultJunkPercent : Mathf.Clamp(value, JunkMin, JunkMax);
        }

        internal static float ClampHigh(float value)
        {
            return float.IsNaN(value) ? DefaultHighPercent : Mathf.Clamp(value, HighMin, HighMax);
        }

        /// <summary>
        /// Vanilla reference: the untouched weight tables (junk at 100%, no
        /// specialty, no high-value share), i.e. 12.26 in every area. This is a
        /// fixed yardstick and does not follow the player's current settings.
        /// </summary>
        internal static float BaseEv(Region region)
        {
            float ev;
            if (baseEvCache.TryGetValue(region.Id, out ev)) return ev;
            ev = Build(region, 100f, 0f, 0f).Ev;
            baseEvCache[region.Id] = ev;
            return ev;
        }

        internal static void Init()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                ModConfig config = ModHelper.GetConfig(Core.Id);

                junkEntry = config.Define("junk_percent", DefaultJunkPercent, SectionName,
                    delegate { return Copy.Text("垃圾出现率倍率（%，0–100）", "Junk chance multiplier (%, 0-100)"); },
                    0f, 100f,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒出多少垃圾，只由这条决定。\n50%：入口空地的垃圾约 49% → 约 24%。\n100%：原版水平（约 49%）；0%：不出垃圾。\n注意：50% 不是「一半掉落都是垃圾」，\n而是把原本的垃圾量减半。",
                            "Sets how much junk shows up each search; it only changes junk.\n50%: Main Gate junk goes from about 49% to about 24%.\n100%: vanilla level (about 49%). 0%: no junk at all.\nNote: 50% does not mean half of all drops are junk -\nit halves the original junk amount.");
                    },
                    false);

                highEntry = config.Define("high_percent", DefaultHighPercent, SectionName,
                    delegate { return Copy.Text("高档物资出现率（%，0–30）", "High-value find chance (%, 0-30)"); },
                    0f, 30f,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒单独判断：这一趟会不会出高档物资。\n没出不累积，下次概率不变，没有保底。\n8% 约每 12 次出 1 次；0% 不出；最高 30%。\n高档物资换自普通物品，垃圾不受影响。",
                            "Rolled once per search:\nit decides only whether this haul includes a high-value item.\nNo pity: a miss does not raise the chance next time.\n8%: about 1 in 12 searches. 0%: never. Up to 30%.\nHigh-value items replace normal items; junk is not affected.");
                    },
                    false);

                specialtyEntry = config.Define(SpecialtyKey, SpecialtyLabels[DefaultSpecialtyTier],
                    SectionName,
                    delegate { return Copy.Text("区域专精度", "Region specialty"); },
                    SpecialtyLabels,
                    delegate
                    {
                        return Copy.Text(
                            "决定除垃圾外，捡到的东西多偏向本区特产。\n档位越高，本区特产越多，通用物品越少。\n「全部」：除垃圾外只出本区特产。\n如报废终端区：「较少」仍有材料、食品，\n「全部」只出模组和电子件。\n每个区都有自己的一批特产；垃圾不受影响。",
                            "Controls how strongly, junk aside, a haul leans toward this region's own goods.\nHigher tiers mean more local goods and fewer generic ones.\nAll: nothing but local goods and junk.\nExample, Dead Terminals: Rare still brings materials and food;\nAll brings only modules and electronics.\nEach region has its own goods; junk is not affected.");
                    },
                    false);

                MigrateSpecialtyTier(config);

                junkEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(ClampJunk(value));
                    if (snapped != value) junkEntry.Set(snapped);
                    Invalidated();
                };
                highEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(ClampHigh(value) * 2f) / 2f;
                    if (snapped != value) highEntry.Set(snapped);
                    Invalidated();
                };
                specialtyEntry.OnChanged += delegate(string value)
                {
                    Invalidated();
                };
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] Could not register settings: " + ex);
            }
        }

        private static void Invalidated()
        {
            cache.Clear();
            NativeSettings.RefreshIfOpen();
        }

        /// <summary>
        /// 0.3.12 turned the 0-100 specialty number into four named tiers. A stored
        /// number is not one of the choice labels, so the config reader would fall
        /// back to the default; read the raw value once and map it to the nearest
        /// tier (0-37 = 较少, 38-62 = 中等, 63-87 = 较多, 88-100 = 全部).
        /// </summary>
        private static void MigrateSpecialtyTier(ModConfig config)
        {
            if (specialtyEntry == null || config == null) return;
            string raw;
            if (!TryReadRaw(config, SpecialtyKey, out raw)) return;
            float percent;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                return;
            int tier = DefaultSpecialtyTier;
            float best = float.MaxValue;
            for (int i = 0; i < SpecialtyPercents.Length; ++i)
            {
                float distance = Mathf.Abs(percent - SpecialtyPercents[i]);
                if (distance < best)
                {
                    best = distance;
                    tier = i;
                }
            }
            specialtyEntry.Set(SpecialtyLabels[tier]);
        }

        private static bool TryReadRaw(ModConfig config, string key, out string value)
        {
            value = null;
            try
            {
                FieldInfo field = typeof(ModConfig).GetField("rawValues",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) return false;
                Dictionary<string, string> raw =
                    field.GetValue(config) as Dictionary<string, string>;
                return raw != null && raw.TryGetValue(key, out value) && value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void SetJunkPercent(float value)
        {
            if (junkEntry == null) return;
            hasLive = false;
            junkEntry.Set(ClampJunk(value));
        }

        internal static void SetHighPercent(float value)
        {
            if (highEntry == null) return;
            hasLive = false;
            highEntry.Set(ClampHigh(value));
        }

        internal static void SetSpecialtyTier(int tier)
        {
            if (specialtyEntry == null) return;
            hasLive = false;
            specialtyEntry.Set(SpecialtyLabels[Mathf.Clamp(tier, 0, SpecialtyLabels.Length - 1)]);
        }

        /// <summary>
        /// Slider preview: keeps the value in memory only, so a drag can refresh
        /// numbers and EV every frame without touching the config file.
        /// </summary>
        internal static void SetJunkPercentLive(float value)
        {
            EnsureLive();
            liveJunk = ClampJunk(value);
            Invalidated();
        }

        internal static void SetHighPercentLive(float value)
        {
            EnsureLive();
            liveHigh = ClampHigh(value);
            Invalidated();
        }

        private static void EnsureLive()
        {
            if (hasLive) return;
            hasLive = true;
            liveJunk = EntryJunk();
            liveHigh = EntryHigh();
        }

        /// <summary>
        /// Writes the dragged value through ConfigEntry.Set once, which saves the
        /// config file; called on pointer-release, panel close and preset clicks.
        /// </summary>
        internal static void CommitLive()
        {
            if (!hasLive) return;
            float junk = liveJunk;
            float high = liveHigh;
            hasLive = false;
            if (junkEntry != null && junkEntry.Value != junk) junkEntry.Set(junk);
            if (highEntry != null && highEntry.Value != high) highEntry.Set(high);
            Invalidated();
        }

        internal static void ResetDefaults()
        {
            hasLive = false;
            SetJunkPercent(DefaultJunkPercent);
            SetHighPercent(DefaultHighPercent);
            SetSpecialtyTier(DefaultSpecialtyTier);
        }

        internal static void ApplyPreset(int preset)
        {
            float junk = DefaultJunkPercent;
            float high = DefaultHighPercent;
            int tier = DefaultSpecialtyTier;
            switch (preset)
            {
                case 1: // every area drops only its own specialties
                    tier = SpecialtyLabels.Length - 1;
                    break;
                case 2: // gamble on high-value finds
                    high = 8f;
                    break;
            }
            hasLive = false;
            SetJunkPercent(junk);
            SetHighPercent(high);
            SetSpecialtyTier(tier);
        }

        internal static EffectiveTable TableFor(Region region)
        {
            Init();
            EffectiveTable table;
            if (cache.TryGetValue(region.Id, out table)) return table;
            table = Build(region, JunkPercent, HighPercent, SpecialtyPercent);
            cache[region.Id] = table;
            return table;
        }

        /// <summary>
        /// Rolls one item for the area, honouring the vanilla junk event.
        /// </summary>
        internal static string Pick(Region region, float junkScale)
        {
            EffectiveTable table = TableFor(region);
            int index = table.PickIndex(junkScale);
            if (index < 0) return null;
            return table.Items[index];
        }

        private static EffectiveTable Build(Region region, float junkPercent,
            float highPercent, float specialtyPercent)
        {
            RegionLootTable baseTable = region.Loot;
            string[] ids = baseTable.Items;
            int count = ids.Length;
            int junkIndex = Array.IndexOf(ids, "junk");
            int regionIndex = RegionIndex(region);
            // One shared tier for every area; each area applies it to its own
            // specialty pool below, so the same value still yields different goods.
            float specialty = Mathf.Clamp(specialtyPercent, 0f, 100f) / 100f;

            // 1) The specialty share only reshapes the non-junk part: inside that
            //    part, everything that is not this area's specialty is scaled by
            //    (1 - specialty). Junk is its own tier and keeps its weight, so the
            //    slider never removes junk from the table.
            float[] weights = new float[count];
            float signatureTotal = 0f;
            for (int i = 0; i < count; ++i)
            {
                float weight = baseTable.Weights[i];
                if (i == junkIndex)
                {
                    weights[i] = weight;
                    continue;
                }
                if (IsSignature(regionIndex, ids[i])) signatureTotal += weight;
                else weight *= 1f - specialty;
                weights[i] = weight;
            }
            if (signatureTotal <= 0f)
            {
                // Nothing marked as a specialty in this table: ignore the slider.
                for (int i = 0; i < count; ++i)
                {
                    if (i == junkIndex) continue;
                    weights[i] = baseTable.Weights[i];
                }
            }

            float otherTotal = 0f;
            for (int i = 0; i < count; ++i)
                if (i != junkIndex) otherTotal += weights[i];

            // 2) Junk probability = the base table's junk share times the junk
            //    multiplier. Neither the specialty slider nor the high-value
            //    carve-out touches it, so the junk slider is the only control.
            float baseTotal = baseTable.Total;
            float junkBase = junkIndex >= 0 && baseTotal > 0f
                ? baseTable.Weights[junkIndex] / (float)baseTotal
                : 0f;
            float junkProb = Mathf.Clamp(junkBase * junkPercent / 100f, 0f, 0.995f);

            // 3) The high-value share is carved out of the non-junk items only.
            float highChance = Mathf.Clamp(highPercent, 0f, 30f) / 100f;
            if (highChance > 0.995f - junkProb) highChance = Mathf.Max(0f, 0.995f - junkProb);
            if (otherTotal <= 0f) junkProb = 1f - highChance;
            float normalProb = Mathf.Max(0f, 1f - junkProb - highChance);

            bool withHigh = highChance > 0f;
            int highCount = withHigh ? HighValue.Length : 0;

            string[] items = new string[count + highCount];
            float[] probs = new float[count + highCount];
            bool[] isHigh = new bool[count + highCount];
            bool[] isJunk = new bool[count + highCount];

            for (int i = 0; i < count; ++i)
            {
                items[i] = ids[i];
                if (i == junkIndex) probs[i] = junkProb;
                else probs[i] = otherTotal > 0f ? normalProb * (weights[i] / otherTotal) : 0f;
                isJunk[i] = i == junkIndex;
            }

            if (withHigh)
            {
                int[] highWeights = HighWeights[RegionIndex(region)];
                float highTotal = 0f;
                for (int j = 0; j < HighValue.Length; ++j) highTotal += highWeights[j];
                if (highTotal <= 0f) highTotal = 1f;
                for (int j = 0; j < HighValue.Length; ++j)
                {
                    int slot = count + j;
                    items[slot] = HighValue[j];
                    probs[slot] = highChance * highWeights[j] / highTotal;
                    isHigh[slot] = true;
                }
            }

            // 4) Expected value of one roll.
            float expected = 0f;
            for (int i = 0; i < items.Length; ++i)
                expected += probs[i] * ValueOf(items[i]);

            float finalJunk = junkIndex >= 0 ? probs[junkIndex] : 0f;
            return new EffectiveTable(items, probs, isHigh, isJunk, highChance, finalJunk, expected);
        }

        private static int RegionIndex(Region region)
        {
            for (int i = 0; i < Region.All.Length; ++i)
                if (Region.All[i].Id == region.Id) return i;
            return 0;
        }

        internal static float ValueOf(string itemId)
        {
            float value;
            return UnitValues.TryGetValue(itemId, out value) ? value : 0f;
        }

        private static int CategoryOf(string itemId)
        {
            int category;
            return Categories.TryGetValue(itemId, out category) ? category : 0;
        }

        /// <summary>
        /// Which categories count as "this area's own goods", in Region.All order.
        /// Everything else (junk included) is what the specialty slider pushes out.
        /// </summary>
        private static readonly int[][] SignatureCategories =
        {
            new[] { 1, 4, 6 }, // Main Gate: materials, tools, household goods
            new[] { 1, 4, 5 }, // Scrap Pile: materials, tools, the odd module
            new[] { 2, 3, 6 }, // Refuse Lane: food, medical, household goods
            new[] { 5, 7 },    // Dead Terminals: modules and electronics
            new[] { 3 },       // Medical Waste: medicine only
            new[] { 8, 9, 10 },// Weapon Dump: melee, guns, ammunition
            new[] { 10, 11 }   // Parts Crates: gun mods and ammunition
        };

        private static bool IsSignature(int regionIndex, string itemId)
        {
            int category = CategoryOf(itemId);
            if (category == 0) return false;
            int[] set = SignatureCategories[regionIndex];
            for (int i = 0; i < set.Length; ++i)
                if (set[i] == category) return true;
            return false;
        }

        /// <summary>
        /// High-value pool (unitValue 250–400). Not part of the base tables; they
        /// only enter when the high-value chance is raised above zero.
        /// </summary>
        private static readonly string[] HighValue =
        {
            "blue_blood_bag", "water_filter_adv",
            "portable_water_purifier", "bottled_water_premium", "black_injector",
            "rare_electronic", "module_extractor_advanced"
        };

        /// <summary>
        /// Per-area high-value mix, in Region.All order (gate, scrap, refuse,
        /// terminal, medical, weapons, parts).
        /// </summary>
        private static readonly int[][] HighWeights =
        {
            new[] { 2, 2, 1, 2, 1, 1, 1 },
            new[] { 1, 1, 1, 1, 1, 3, 4 },
            new[] { 3, 3, 3, 3, 1, 1, 1 },
            new[] { 1, 1, 1, 1, 3, 3, 2 },
            new[] { 4, 1, 1, 1, 5, 1, 1 },
            new[] { 1, 2, 2, 2, 3, 1, 3 },
            new[] { 1, 2, 2, 2, 2, 2, 3 }
        };

        /// <summary>
        /// Same figures as the balance document (GameItem.unitValue, boxes folded
        /// into their contents, random modules counted at 70).
        /// </summary>
        private static readonly Dictionary<string, float> UnitValues =
            new Dictionary<string, float>
            {
                { "junk", 3f },
                { "glass_shard", 2f },
                { "system_module_ruined", 30f },
                { "pipe_weapon", 15f },
                { "empty_beer_bottle", 5f },
                { "scratcher", 20f },
                { "scrap_metal", 10f },
                { "nuts_metal", 14f },
                { "flux_agent", 4f },
                { "printer_plastic", 12f },
                { "wire", 16f },
                { "gun_part", 25f },
                { "common_electronic", 33f },
                { "kotton_fabric", 65f },
                { "random_performance_module", 70f },
                { "random_efficiency_module", 70f },
                { "random_quality_module", 70f },
                { "system_module_performance", 70f },
                { "system_module_eco", 100f },
                { "system_module_fineness", 100f },
                { "screwdriver", 25f },
                { "wire_cutter", 40f },
                { "module_extractor", 100f },
                { "welder", 80f },
                { "turbo_booster", 35f },
                { "crowbar", 75f },
                { "caffeine_pill", 10f },
                { "toothpaste", 15f },
                { "water_filter", 25f },
                { "shampoo", 30f },
                { "paper_towel", 35f },
                { "toilet_paper", 25f },
                { "full_water_test_strip_box", 31f },
                { "full_water_purification_tablet_box", 55f },
                { "glass_shard_shiv", 8f },
                { "box_cutter", 28f },
                { "kitchen_knife", 45f },
                { "galaxy_blend", 25f },
                { "energy_drink", 18f },
                { "soda_red", 15f },
                { "cat_bar", 15f },
                { "li_eat_snackbar", 17f },
                { "red_beer", 18f },
                { "processed_milk", 16f },
                { "processed_juice", 14f },
                { "processed_meat", 25f },
                { "zerochew", 8f },
                { "cup_noodle", 20f },
                { "nudka", 85f },
                { "beis_icecream", 40f },
                { "bandage_item", 8f },
                { "hemostatic_bandage_item", 16f },
                { "topical_bandage_item", 16f },
                { "blood_bag", 33f },
                { "rubbing_alcohol", 35f },
                { "salve", 80f },
                { "surgery_tool", 200f },
                { "birth_control_pill", 15f },
                { "fanta_pill", 12f },
                { "phagimycin_pill", 25f },
                { "zyanide_pill", 33f },
                { "smelling_salt", 15f },
                { "dream_dust", 18f },
                { "nightmare_dust", 36f },
                { "common_medical", 40f },
                { "metal_ingot", 50f },
                { "system_module_corrupt", 45f },
                { "blank_module", 60f },
                { "combat_knife", 75f },
                { "kitchen_cleaver", 65f },
                { "stun_baton", 165f },
                { "hatchet", 55f },
                { "skinning_knife", 100f },
                { "combat_machete", 185f },
                { "printed_gun", 40f },
                { "handmade_pistol", 120f },
                { "heavy_handmade_pistol", 160f },
                { "revolver", 240f },
                { "small_pistol_ammo", 20f },
                { "small_pistol_ammo_p", 26f },
                { "small_pistol_ammo_nl", 14f },
                { "heavy_pistol_ammo", 30f },
                { "heavy_pistol_ammo_p", 36f },
                { "heavy_pistol_ammo_nl", 24f },
                { "lou_etched_round", 30f },
                { "rds", 60f },
                { "rds_makeshift", 25f },
                { "silencer", 55f },
                { "silencer2", 90f },
                { "silencer_makeshift", 15f },
                { "barrel", 60f },
                { "compensator", 50f },
                { "mag_10mm", 30f },
                { "mag_10mm_high", 40f },
                { "mag_10mm_ext", 50f },
                { "mag_22", 15f },
                { "mag_22_high", 25f },
                { "mag_22_ext", 35f },
                { "mag_10mm_smg", 55f },
                { "mag_10mm_smg_high", 70f },
                { "mag_10mm_smg_box", 85f },
                { "laser", 50f },
                { "laser2", 100f },
                { "laser_toy", 18f },
                { "bipod", 40f },
                { "gl", 150f },
                { "grip", 40f },
                { "stock", 40f },
                { "short_stock", 35f },
                { "internal_springs", 40f },
                { "internal_springs_improved", 20f },
                { "trigger_group", 50f },
                { "match_barrel", 75f },
                { "pistol_switch", 40f },
                { "ammo_display", 70f },
                { "grip_gel", 70f },
                { "blue_blood_bag", 250f },
                { "water_filter_adv", 350f },
                { "portable_water_purifier", 325f },
                { "bottled_water_premium", 325f },
                { "black_injector", 350f },
                { "rare_electronic", 350f },
                { "module_extractor_advanced", 350f }
            };

        /// <summary>
        /// Item categories: 1 materials, 2 food, 3 medical, 4 tools, 5 modules,
        /// 6 household goods, 7 electronics, 8 melee weapons, 9 guns, 10 ammo,
        /// 11 gun mods. SignatureCategories uses these ids.
        /// </summary>
        private static readonly Dictionary<string, int> Categories =
            new Dictionary<string, int>
            {
                { "glass_shard", 1 }, { "scrap_metal", 1 }, { "nuts_metal", 1 },
                { "flux_agent", 1 },
                { "gun_part", 1 }, { "kotton_fabric", 1 }, { "metal_ingot", 1 },
                { "empty_beer_bottle", 2 }, { "galaxy_blend", 2 }, { "energy_drink", 2 },
                { "soda_red", 2 }, { "cat_bar", 2 }, { "li_eat_snackbar", 2 },
                { "red_beer", 2 }, { "processed_milk", 2 }, { "processed_juice", 2 },
                { "processed_meat", 2 }, { "zerochew", 2 }, { "cup_noodle", 2 },
                { "nudka", 2 }, { "beis_icecream", 2 },
                { "bandage_item", 3 },
                { "hemostatic_bandage_item", 3 }, { "topical_bandage_item", 3 },
                { "blood_bag", 3 }, { "rubbing_alcohol", 3 }, { "salve", 3 },
                { "surgery_tool", 3 }, { "birth_control_pill", 3 }, { "fanta_pill", 3 },
                { "phagimycin_pill", 3 }, { "zyanide_pill", 3 }, { "smelling_salt", 3 },
                { "dream_dust", 3 }, { "nightmare_dust", 3 }, { "common_medical", 3 },
                { "screwdriver", 4 }, { "wire_cutter", 4 }, { "module_extractor", 4 },
                { "welder", 4 }, { "turbo_booster", 4 },
                { "system_module_ruined", 5 }, { "random_performance_module", 5 },
                { "random_efficiency_module", 5 }, { "random_quality_module", 5 },
                { "system_module_performance", 5 }, { "system_module_eco", 5 },
                { "system_module_fineness", 5 }, { "system_module_corrupt", 5 },
                { "blank_module", 5 },
                { "caffeine_pill", 6 }, { "toothpaste", 6 }, { "water_filter", 6 },
                { "shampoo", 6 }, { "paper_towel", 6 }, { "toilet_paper", 6 },
                { "full_water_test_strip_box", 6 },
                { "full_water_purification_tablet_box", 6 },
                { "wire", 7 }, { "printer_plastic", 7 },
                { "common_electronic", 7 }, { "rare_electronic", 7 },
                { "pipe_weapon", 8 }, { "glass_shard_shiv", 8 }, { "box_cutter", 8 },
                { "kitchen_knife", 8 }, { "crowbar", 8 }, { "combat_knife", 8 },
                { "kitchen_cleaver", 8 }, { "hatchet", 8 }, { "skinning_knife", 8 },
                { "stun_baton", 8 }, { "combat_machete", 8 },
                { "printed_gun", 9 }, { "handmade_pistol", 9 },
                { "heavy_handmade_pistol", 9 }, { "revolver", 9 },
                { "small_pistol_ammo", 10 }, { "small_pistol_ammo_p", 10 },
                { "small_pistol_ammo_nl", 10 }, { "heavy_pistol_ammo", 10 },
                { "heavy_pistol_ammo_p", 10 }, { "heavy_pistol_ammo_nl", 10 },
                { "lou_etched_round", 10 },
                { "rds", 11 }, { "rds_makeshift", 11 }, { "silencer", 11 },
                { "silencer2", 11 }, { "silencer_makeshift", 11 }, { "barrel", 11 },
                { "compensator", 11 }, { "mag_10mm", 11 }, { "mag_10mm_high", 11 },
                { "mag_10mm_ext", 11 }, { "mag_22", 11 }, { "mag_22_high", 11 },
                { "mag_22_ext", 11 }, { "mag_10mm_smg", 11 }, { "mag_10mm_smg_high", 11 },
                { "mag_10mm_smg_box", 11 }, { "laser", 11 }, { "laser2", 11 },
                { "laser_toy", 11 }, { "bipod", 11 }, { "gl", 11 }, { "grip", 11 },
                { "stock", 11 }, { "short_stock", 11 }, { "internal_springs", 11 },
                { "internal_springs_improved", 11 }, { "trigger_group", 11 },
                { "match_barrel", 11 }, { "pistol_switch", 11 }, { "ammo_display", 11 },
                { "grip_gel", 11 }
            };
    }

    /// <summary>
    /// One area's runtime distribution: final probabilities plus the derived
    /// expected value. Rebuilt whenever a setting changes.
    /// </summary>
    internal sealed class EffectiveTable
    {
        internal readonly string[] Items;
        internal readonly float[] Probs;
        internal readonly bool[] IsHigh;
        internal readonly float HighChance;
        internal readonly float JunkChance;
        internal readonly float Ev;

        private readonly bool[] isJunk;
        private readonly int[] order;
        private readonly int junkIndex;

        internal EffectiveTable(string[] items, float[] probs, bool[] isHigh, bool[] isJunk,
            float highChance, float junkChance, float ev)
        {
            Items = items;
            Probs = probs;
            IsHigh = isHigh;
            this.isJunk = isJunk;
            HighChance = highChance;
            JunkChance = junkChance;
            Ev = ev;
            junkIndex = Array.IndexOf(items, "junk");

            order = new int[items.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                int compare = probs[b].CompareTo(probs[a]);
                return compare != 0 ? compare : string.CompareOrdinal(items[a], items[b]);
            });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return order[i]; }
        internal float ProbabilityAt(int i) { return Probs[order[i]]; }
        internal string ItemAt(int i) { return Items[order[i]]; }

        internal int PickIndex(float junkScale)
        {
            float sum = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                sum += i == junkIndex ? Probs[i] * junkScale : Probs[i];
            }
            if (sum <= 0f) return -1;

            float roll = UnityEngine.Random.value * sum;
            int last = -1;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                last = i;
                roll -= i == junkIndex ? Probs[i] * junkScale : Probs[i];
                if (roll < 0f) return i;
            }
            return last;
        }
    }
}
