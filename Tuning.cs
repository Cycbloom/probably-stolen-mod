using System;
using System.Collections.Generic;
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
        internal const float DefaultJunkPercent = 100f;
        internal const float DefaultHighPercent = 0f;
        internal const int DefaultPity = 0;
        internal const float DefaultSpecialty = 0f;

        internal const float JunkMin = 0f;
        internal const float JunkMax = 200f;
        internal const float HighMin = 0f;
        internal const float HighMax = 30f;
        internal const int PityMin = 0;
        internal const int PityMax = 20;
        internal const float SpecialtyMin = 0f;
        internal const float SpecialtyMax = 100f;

        private const string SectionName = "拾荒掉落 / Loot";

        /// <summary>
        /// One shared slider for all four areas. Each area still applies it to its
        /// own signature pool, so the same percentage yields different goods.
        /// </summary>
        internal const string SpecialtyKey = "specialty";

        private static ConfigEntry<float> junkEntry;
        private static ConfigEntry<float> highEntry;
        private static ConfigEntry<int> pityEntry;
        private static ConfigEntry<float> specialtyEntry;
        private static readonly Dictionary<string, EffectiveTable> cache =
            new Dictionary<string, EffectiveTable>();
        private static bool hasLive;
        private static float liveJunk;
        private static float liveHigh;
        private static int livePity;
        private static float liveSpecialty;
        private static int sinceHigh;
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

        private static int EntryPity()
        {
            return pityEntry == null ? DefaultPity : Mathf.Clamp(pityEntry.Value, PityMin, PityMax);
        }

        private static float EntrySpecialty()
        {
            if (specialtyEntry == null) return DefaultSpecialty;
            float value = specialtyEntry.Value;
            if (float.IsNaN(value)) return DefaultSpecialty;
            return Mathf.Round(Mathf.Clamp(value, SpecialtyMin, SpecialtyMax));
        }

        /// <summary>
        /// 0 = every area keeps its whole mixed table; 100 = only the items that
        /// belong to each area's own pool survive (junk excluded).
        /// </summary>
        internal static float SpecialtyPercent
        {
            get { return hasLive ? liveSpecialty : EntrySpecialty(); }
        }

        /// <summary>
        /// While a slider is being dragged the values live here instead of in the
        /// config entries, so the file is written once when the drag ends.
        /// </summary>
        internal static float JunkPercent { get { return hasLive ? liveJunk : EntryJunk(); } }

        internal static float HighPercent { get { return hasLive ? liveHigh : EntryHigh(); } }

        internal static int Pity { get { return hasLive ? livePity : EntryPity(); } }

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

        internal static int ClampPity(int value)
        {
            return Mathf.Clamp(value, PityMin, PityMax);
        }

        internal static float ClampSpecialty(float value)
        {
            return float.IsNaN(value) ? DefaultSpecialty : Mathf.Clamp(value, SpecialtyMin, SpecialtyMax);
        }

        internal static float BaseEv(Region region)
        {
            RegionLootTable table = region.Loot;
            float total = 0f;
            float weighted = 0f;
            for (int i = 0; i < table.Items.Length; ++i)
            {
                total += table.Weights[i];
                weighted += table.Weights[i] * ValueOf(table.Items[i]);
            }
            return total > 0f ? weighted / total : 0f;
        }

        internal static void Init()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                ModConfig config = ModHelper.GetConfig(Core.Id);

                junkEntry = config.Define("junk_percent", DefaultJunkPercent, SectionName,
                    delegate { return Copy.Text("垃圾出现率倍率（%，0–200）", "Junk chance multiplier (%, 0-200)"); },
                    0f, 200f,
                    delegate
                    {
                        return Copy.Text(
                            "按概率缩放垃圾出现率：50 表示该区垃圾概率减半，让出的份额按原比例分给其它物品。范围 0–200%，步长 1%。",
                            "Scales the junk chance itself: 50 halves it in every area, and the freed share is split among the other items in their original proportions. Range 0-200%, step 1%.");
                    },
                    false);

                highEntry = config.Define("high_percent", DefaultHighPercent, SectionName,
                    delegate { return Copy.Text("高档物资出现率（%，0–30）", "High-value find chance (%, 0-30)"); },
                    0f, 30f,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒抽到高档物资的总概率，份额从全部普通物品按比例让出；各区的具体高档物品分布不同。范围 0–30%，步长 0.5%。",
                            "Total chance per search to find a high-value item. The share is taken proportionally from all normal items; each area draws from its own high-value mix. Range 0-30%, step 0.5%.");
                    },
                    false);

                pityEntry = config.Define("high_pity", DefaultPity, SectionName,
                    delegate { return Copy.Text("高档保底（次，0–20）", "High-value pity (searches, 0-20)"); },
                    (int?)0, (int?)20,
                    delegate
                    {
                        return Copy.Text(
                            "连续这么多次没出高档时，下一次必出一件；出任意高档即重新计数。0 为关闭，需要高档出现率大于 0。范围 0–20 次。",
                            "After this many searches without a high-value find, the next one is guaranteed. Resets on any high-value find. 0 disables it; needs a high-value chance above 0. Range 0-20 searches.");
                    },
                    false);

                specialtyEntry = config.Define(SpecialtyKey, DefaultSpecialty, SectionName,
                    delegate { return Copy.Text("区域专精度（%，0–100）", "Region specialty (%, 0-100)"); },
                    0f, 100f,
                    delegate
                    {
                        return Copy.Text(
                            "这一趟捡到的东西里，除了垃圾之外有多少是本区才有的特色物品。0% 与默认混搭一致；拉高后，外地的普通物品按比例让位给本区特色物品；100% 时除了垃圾之外只出本区的东西。四个区各有一份自己的特色清单，所以同一个数值在四个区会捡到不同的东西。垃圾是单独一档，出现率不受这条影响，仍由上面的垃圾倍率决定。范围 0–100%，步长 1%。",
                            "How much of this search's haul, junk aside, is something only found in this region. 0% keeps the default mix; higher values make out-of-region goods yield proportionally to the region's own items, and at 100% only the region's own items (plus junk) remain. Each region has its own list, so one shared number still yields different goods per region. Junk is a separate tier, so its chance is untouched here and stays controlled by the junk multiplier above. Range 0-100%, step 1%.");
                    },
                    false);

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
                pityEntry.OnChanged += delegate(int value)
                {
                    int clamped = ClampPity(value);
                    if (clamped != value) pityEntry.Set(clamped);
                    Invalidated();
                };
                specialtyEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(ClampSpecialty(value));
                    if (snapped != value) specialtyEntry.Set(snapped);
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

        internal static void ResetPity()
        {
            sinceHigh = 0;
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

        internal static void SetPity(int value)
        {
            if (pityEntry == null) return;
            hasLive = false;
            pityEntry.Set(ClampPity(value));
        }

        internal static void SetSpecialty(float value)
        {
            if (specialtyEntry == null) return;
            hasLive = false;
            specialtyEntry.Set(ClampSpecialty(value));
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

        internal static void SetPityLive(int value)
        {
            EnsureLive();
            livePity = ClampPity(value);
            Invalidated();
        }

        internal static void SetSpecialtyLive(float value)
        {
            EnsureLive();
            liveSpecialty = ClampSpecialty(value);
            Invalidated();
        }

        private static void EnsureLive()
        {
            if (hasLive) return;
            hasLive = true;
            liveJunk = EntryJunk();
            liveHigh = EntryHigh();
            livePity = EntryPity();
            liveSpecialty = EntrySpecialty();
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
            int pity = livePity;
            float specialty = liveSpecialty;
            hasLive = false;
            if (junkEntry != null && junkEntry.Value != junk) junkEntry.Set(junk);
            if (highEntry != null && highEntry.Value != high) highEntry.Set(high);
            if (pityEntry != null && pityEntry.Value != pity) pityEntry.Set(pity);
            if (specialtyEntry != null && specialtyEntry.Value != specialty)
                specialtyEntry.Set(specialty);
            Invalidated();
        }

        internal static void ResetDefaults()
        {
            hasLive = false;
            SetJunkPercent(DefaultJunkPercent);
            SetHighPercent(DefaultHighPercent);
            SetPity(DefaultPity);
            SetSpecialty(DefaultSpecialty);
            sinceHigh = 0;
        }

        internal static void ApplyPreset(int preset)
        {
            float junk = DefaultJunkPercent;
            float high = DefaultHighPercent;
            int pity = DefaultPity;
            float specialty = DefaultSpecialty;
            switch (preset)
            {
                case 1: // every area drops only its own specialties
                    specialty = 100f;
                    break;
                case 2: // a small high-value chance kept honest by pity
                    high = 3f;
                    pity = 10;
                    break;
                case 3: // gamble on high-value finds
                    high = 8f;
                    break;
            }
            hasLive = false;
            SetJunkPercent(junk);
            SetHighPercent(high);
            SetPity(pity);
            SetSpecialty(specialty);
            sinceHigh = 0;
        }

        internal static EffectiveTable TableFor(Region region)
        {
            Init();
            EffectiveTable table;
            if (cache.TryGetValue(region.Id, out table)) return table;
            table = Build(region);
            cache[region.Id] = table;
            return table;
        }

        /// <summary>
        /// Rolls one item for the area, honouring the vanilla junk event and the
        /// optional high-value pity counter.
        /// </summary>
        internal static string Pick(Region region, float junkScale)
        {
            EffectiveTable table = TableFor(region);
            bool force = table.Pity > 0 && sinceHigh + 1 >= table.Pity;
            int index = table.PickIndex(junkScale, force);
            if (index < 0) return null;
            if (table.IsHigh[index]) sinceHigh = 0;
            else ++sinceHigh;
            return table.Items[index];
        }

        private static EffectiveTable Build(Region region)
        {
            RegionLootTable baseTable = region.Loot;
            string[] ids = baseTable.Items;
            int count = ids.Length;
            int junkIndex = Array.IndexOf(ids, "junk");
            int regionIndex = RegionIndex(region);
            // One shared percentage for all four areas; each area applies it to its
            // own specialty pool below, so the same f still yields different goods.
            float specialty = Mathf.Clamp(SpecialtyPercent, SpecialtyMin, SpecialtyMax) / 100f;

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
            float junkProb = Mathf.Clamp(junkBase * JunkPercent / 100f, 0f, 0.995f);

            // 3) The high-value share is carved out of the non-junk items only.
            float highChance = Mathf.Clamp(HighPercent, 0f, 30f) / 100f;
            if (highChance > 0.995f - junkProb) highChance = Mathf.Max(0f, 0.995f - junkProb);
            if (otherTotal <= 0f) junkProb = 1f - highChance;
            float normalProb = Mathf.Max(0f, 1f - junkProb - highChance);

            int pity = highChance > 0f ? Mathf.Clamp(Pity, 0, 20) : 0;
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

            // 4) Expected value, both plain and with the pity cycle folded in.
            float plain = 0f;
            float highSum = 0f;
            float normalSum = 0f;
            float normalWeighted = 0f;
            for (int i = 0; i < items.Length; ++i)
            {
                float value = ValueOf(items[i]);
                plain += probs[i] * value;
                if (isHigh[i]) highSum += probs[i] * value;
                else
                {
                    normalSum += probs[i];
                    normalWeighted += probs[i] * value;
                }
            }

            float expected = plain;
            if (pity > 0 && highChance > 0f)
            {
                float highAverage = highSum / highChance;
                float normalAverage = normalSum > 0f ? normalWeighted / normalSum : 0f;
                float cycle = (1f - Mathf.Pow(1f - highChance, pity)) / highChance;
                if (cycle < 1f) cycle = 1f;
                expected = highAverage / cycle + normalAverage * (cycle - 1f) / cycle;
            }

            float finalJunk = junkIndex >= 0 ? probs[junkIndex] : 0f;
            return new EffectiveTable(items, probs, isHigh, isJunk, highChance, finalJunk, expected, plain, pity);
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
            new[] { 5, 7 }     // Dead Terminals: modules and electronics
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
        /// Per-area high-value mix, in Region.All order (gate, scrap, refuse, terminal).
        /// </summary>
        private static readonly int[][] HighWeights =
        {
            new[] { 2, 2, 1, 2, 1, 1, 1 },
            new[] { 1, 1, 1, 1, 1, 3, 4 },
            new[] { 3, 3, 3, 3, 1, 1, 1 },
            new[] { 1, 1, 1, 1, 3, 3, 2 }
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
        /// 6 household goods, 7 electronics. SignatureCategories uses these ids.
        /// </summary>
        private static readonly Dictionary<string, int> Categories =
            new Dictionary<string, int>
            {
                { "glass_shard", 1 }, { "scrap_metal", 1 }, { "nuts_metal", 1 },
                { "flux_agent", 1 }, { "printer_plastic", 1 }, { "wire", 1 },
                { "gun_part", 1 }, { "kotton_fabric", 1 },
                { "empty_beer_bottle", 2 }, { "galaxy_blend", 2 }, { "energy_drink", 2 },
                { "soda_red", 2 }, { "cat_bar", 2 }, { "li_eat_snackbar", 2 },
                { "red_beer", 2 }, { "processed_milk", 2 }, { "processed_juice", 2 },
                { "processed_meat", 2 }, { "zerochew", 2 }, { "cup_noodle", 2 },
                { "nudka", 2 }, { "beis_icecream", 2 },
                { "bandage_item", 3 },
                { "hemostatic_bandage_item", 3 }, { "topical_bandage_item", 3 },
                { "screwdriver", 4 }, { "wire_cutter", 4 }, { "module_extractor", 4 },
                { "welder", 4 }, { "turbo_booster", 4 }, { "pipe_weapon", 4 },
                { "crowbar", 4 }, { "kitchen_knife", 4 }, { "glass_shard_shiv", 4 },
                { "box_cutter", 4 },
                { "system_module_ruined", 5 }, { "random_performance_module", 5 },
                { "random_efficiency_module", 5 }, { "random_quality_module", 5 },
                { "system_module_performance", 5 }, { "system_module_eco", 5 },
                { "system_module_fineness", 5 },
                { "caffeine_pill", 6 }, { "toothpaste", 6 }, { "water_filter", 6 },
                { "shampoo", 6 }, { "paper_towel", 6 }, { "toilet_paper", 6 },
                { "full_water_test_strip_box", 6 },
                { "full_water_purification_tablet_box", 6 },
                { "common_electronic", 7 }, { "rare_electronic", 7 }
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
        internal readonly float EvPlain;
        internal readonly int Pity;

        private readonly bool[] isJunk;
        private readonly int[] order;
        private readonly int junkIndex;

        internal EffectiveTable(string[] items, float[] probs, bool[] isHigh, bool[] isJunk,
            float highChance, float junkChance, float ev, float evPlain, int pity)
        {
            Items = items;
            Probs = probs;
            IsHigh = isHigh;
            this.isJunk = isJunk;
            HighChance = highChance;
            JunkChance = junkChance;
            Ev = ev;
            EvPlain = evPlain;
            Pity = pity;
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

        internal int PickIndex(float junkScale, bool forceHigh)
        {
            float sum = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (forceHigh && !IsHigh[i]) continue;
                if (Probs[i] <= 0f) continue;
                sum += i == junkIndex ? Probs[i] * junkScale : Probs[i];
            }
            if (sum <= 0f) return -1;

            float roll = UnityEngine.Random.value * sum;
            int last = -1;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (forceHigh && !IsHigh[i]) continue;
                if (Probs[i] <= 0f) continue;
                last = i;
                roll -= i == junkIndex ? Probs[i] * junkScale : Probs[i];
                if (roll < 0f) return i;
            }
            return last;
        }
    }
}
