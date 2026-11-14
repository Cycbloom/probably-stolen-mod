using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    public sealed class Core : IMod
    {
        internal const string Id = "local.dumping_ground_map";
        internal static bool Enabled;

        public void Init(ModManifest manifest)
        {
            Tuning.Init();
        }

        public void OnEnable()
        {
            Enabled = true;
            Tuning.Init();
            Debug.Log("[DumpingGroundMap] enabled");
        }

        public void OnDisable()
        {
            Enabled = false;
            NativeSettings.Reset();
            Picker.Close();
            Picker.ResetChoice();
        }
    }

    internal sealed class Region
    {
        internal static readonly Region[] All =
        {
            new Region("gate", "入口空地", "Main Gate",
                "通用杂项：材料、工具、日用品各有一点，适合补充常规消耗。",
                "A general mix of materials, tools, and household goods for everyday restocking.",
                0.20f, 0.22f),
            new Region("scrap", "拆解堆", "Scrap Pile",
                "材料与工具占大头，偶尔能拆出完整模组。",
                "Mostly materials and tools, with the occasional intact module.",
                0.20f, 0.50f),
            new Region("refuse", "生活废料带", "Refuse Lane",
                "日用、食品、药品为主；值钱的东西不多，但胜在实用。",
                "Household goods, food, and medicine. Few valuables, but plenty of practical finds.",
                0.78f, 0.22f),
            new Region("terminal", "报废终端区", "Dead Terminals",
                "电线与电子废料最多，也能翻出模组；垃圾比例全场地最高。",
                "The most wire and electronic scrap, plus the odd module. Junk share is the highest here.",
                0.78f, 0.78f),
            new Region("med", "医疗废弃点", "Medical Waste",
                "绷带、药品、酒精、手术用品；能救命，但大多卖不上价。",
                "Bandages, pills, alcohol, and surgical supplies. Life-saving, but rarely worth much.",
                0.20f, 0.78f),
            new Region("weapons", "废武器堆", "Weapon Dump",
                "断刀、铁管、土枪和散装弹药；偶尔还能翻出一把能用的枪。",
                "Broken blades, pipes, makeshift guns, and loose rounds. Now and then a working gun.",
                0.50f, 0.78f),
            new Region("parts", "配件箱", "Parts Crates",
                "成箱的弹匣、消音器、瞄具和改装件，多半是便宜货。",
                "Crates of magazines, suppressors, sights, and mods, mostly the cheap kind.",
                0.50f, 0.50f)
        };

        internal readonly string Id;
        internal readonly string NameZh, NameEn, DetailZh, DetailEn;
        internal readonly float X, Y;

        internal Region(string id, string zh, string en, string detailZh, string detailEn,
            float x, float y)
        {
            Id = id;
            NameZh = zh;
            NameEn = en;
            DetailZh = detailZh;
            DetailEn = detailEn;
            X = x;
            Y = y;
        }

        internal string Name { get { return Copy.Text(NameZh, NameEn); } }
        internal string Detail { get { return Copy.Text(DetailZh, DetailEn); } }
        internal RegionLootTable Loot { get { return ScavLoot.Get(Id); } }
    }

    internal static class Copy
    {
        internal static string Text(string zh, string en)
        {
            string locale = LocHelper.GetCurrentLocaleCode();
            return locale != null && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;
        }
    }

    /// <summary>
    /// One weighted item table for one area. Weights are relative integers; the
    /// chance of an item is weight / total weight.
    /// </summary>
    internal sealed class RegionLootTable
    {
        internal readonly string[] Items;
        internal readonly int[] Weights;
        internal readonly int Total;
        private readonly int junkIndex;
        private readonly int[] displayOrder;

        internal RegionLootTable(string[] items, int[] weights)
        {
            Items = items;
            Weights = weights;
            int sum = 0;
            for (int i = 0; i < weights.Length; ++i) sum += weights[i];
            Total = sum;
            junkIndex = Array.IndexOf(items, "junk");

            displayOrder = new int[items.Length];
            for (int i = 0; i < displayOrder.Length; ++i) displayOrder[i] = i;
            Array.Sort(displayOrder, delegate(int a, int b) { return weights[b].CompareTo(weights[a]); });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return displayOrder[i]; }
        internal float ProbabilityAt(int i) { return Weights[displayOrder[i]] / (float)Total; }

        /// <summary>
        /// Rolls one item. junkScale mirrors the vanilla "junk_chance" event, which
        /// only raises the junk table's weight.
        /// </summary>
        internal string Pick(float junkScale)
        {
            float sum = Total;
            if (junkIndex >= 0 && junkScale != 1f)
                sum += Weights[junkIndex] * (junkScale - 1f);
            if (sum <= 0f) return null;

            float roll = UnityEngine.Random.value * sum;
            for (int i = 0; i < Items.Length; ++i)
            {
                float weight = i == junkIndex ? Weights[i] * junkScale : Weights[i];
                roll -= weight;
                if (roll < 0f) return Items[i];
            }
            return Items[Items.Length - 1];
        }
    }

    internal static class ScavLoot
    {
        private static readonly Dictionary<string, RegionLootTable> tables = BuildTables();
        private static readonly Dictionary<string, string> sourceTables = BuildSourceTables();

        internal static RegionLootTable Get(string regionId)
        {
            RegionLootTable table;
            return tables.TryGetValue(regionId, out table) ? table : tables["gate"];
        }

        internal static string SourceTable(string itemId)
        {
            string table;
            return sourceTables.TryGetValue(itemId, out table) ? table : "junkTable";
        }

        private sealed class Builder
        {
            private readonly List<string> items = new List<string>();
            private readonly List<int> weights = new List<int>();

            internal Builder Add(string id, int weight)
            {
                items.Add(id);
                weights.Add(weight);
                return this;
            }

            internal RegionLootTable Build()
            {
                return new RegionLootTable(items.ToArray(), weights.ToArray());
            }
        }

        private static Dictionary<string, RegionLootTable> BuildTables()
        {
            Dictionary<string, RegionLootTable> result = new Dictionary<string, RegionLootTable>();

            Builder b = new Builder();
            b.Add("junk", 274);
            b.Add("glass_shard", 10);
            b.Add("system_module_ruined", 8);
            b.Add("pipe_weapon", 10);
            b.Add("empty_beer_bottle", 10);
            b.Add("scratcher", 8);
            b.Add("scrap_metal", 16);
            b.Add("nuts_metal", 14);
            b.Add("flux_agent", 12);
            b.Add("printer_plastic", 14);
            b.Add("wire", 14);
            b.Add("gun_part", 8);
            b.Add("common_electronic", 6);
            b.Add("kotton_fabric", 4);
            b.Add("random_performance_module", 1);
            b.Add("random_efficiency_module", 1);
            b.Add("random_quality_module", 1);
            b.Add("system_module_performance", 1);
            b.Add("system_module_eco", 1);
            b.Add("system_module_fineness", 1);
            b.Add("screwdriver", 6);
            b.Add("wire_cutter", 4);
            b.Add("module_extractor", 1);
            b.Add("welder", 1);
            b.Add("turbo_booster", 5);
            b.Add("crowbar", 3);
            b.Add("caffeine_pill", 6);
            b.Add("toothpaste", 6);
            b.Add("water_filter", 5);
            b.Add("shampoo", 4);
            b.Add("paper_towel", 3);
            b.Add("toilet_paper", 5);
            b.Add("full_water_test_strip_box", 4);
            b.Add("full_water_purification_tablet_box", 2);
            b.Add("glass_shard_shiv", 4);
            b.Add("box_cutter", 3);
            b.Add("kitchen_knife", 2);
            b.Add("galaxy_blend", 5);
            b.Add("energy_drink", 6);
            b.Add("soda_red", 6);
            b.Add("cat_bar", 6);
            b.Add("li_eat_snackbar", 6);
            b.Add("red_beer", 6);
            b.Add("processed_milk", 5);
            b.Add("processed_juice", 5);
            b.Add("processed_meat", 4);
            b.Add("zerochew", 5);
            b.Add("cup_noodle", 5);
            b.Add("nudka", 2);
            b.Add("beis_icecream", 3);
            b.Add("bandage_item", 8);
            b.Add("hemostatic_bandage_item", 6);
            b.Add("topical_bandage_item", 6);
            result["gate"] = b.Build();

            b = new Builder();
            b.Add("junk", 334);
            b.Add("glass_shard", 38);
            b.Add("system_module_ruined", 12);
            b.Add("pipe_weapon", 12);
            b.Add("empty_beer_bottle", 8);
            b.Add("scratcher", 8);
            b.Add("scrap_metal", 45);
            b.Add("nuts_metal", 35);
            b.Add("flux_agent", 18);
            b.Add("printer_plastic", 22);
            b.Add("wire", 28);
            b.Add("gun_part", 18);
            b.Add("common_electronic", 14);
            b.Add("kotton_fabric", 7);
            b.Add("random_performance_module", 1);
            b.Add("random_efficiency_module", 1);
            b.Add("random_quality_module", 1);
            b.Add("system_module_performance", 1);
            b.Add("system_module_eco", 1);
            b.Add("system_module_fineness", 1);
            b.Add("screwdriver", 14);
            b.Add("wire_cutter", 11);
            b.Add("module_extractor", 3);
            b.Add("welder", 4);
            b.Add("turbo_booster", 10);
            b.Add("crowbar", 7);
            b.Add("caffeine_pill", 3);
            b.Add("water_filter", 3);
            b.Add("toilet_paper", 3);
            b.Add("glass_shard_shiv", 10);
            b.Add("box_cutter", 10);
            b.Add("kitchen_knife", 6);
            b.Add("zerochew", 3);
            b.Add("bandage_item", 5);
            result["scrap"] = b.Build();

            b = new Builder();
            b.Add("junk", 492);
            b.Add("glass_shard", 6);
            b.Add("system_module_ruined", 5);
            b.Add("pipe_weapon", 7);
            b.Add("empty_beer_bottle", 16);
            b.Add("scratcher", 10);
            b.Add("scrap_metal", 10);
            b.Add("nuts_metal", 6);
            b.Add("flux_agent", 8);
            b.Add("printer_plastic", 6);
            b.Add("wire", 8);
            b.Add("gun_part", 4);
            b.Add("common_electronic", 4);
            b.Add("kotton_fabric", 4);
            b.Add("screwdriver", 4);
            b.Add("turbo_booster", 4);
            b.Add("crowbar", 2);
            b.Add("caffeine_pill", 26);
            b.Add("toothpaste", 26);
            b.Add("water_filter", 18);
            b.Add("shampoo", 14);
            b.Add("paper_towel", 10);
            b.Add("toilet_paper", 18);
            b.Add("full_water_test_strip_box", 12);
            b.Add("full_water_purification_tablet_box", 6);
            b.Add("glass_shard_shiv", 6);
            b.Add("box_cutter", 5);
            b.Add("kitchen_knife", 3);
            b.Add("galaxy_blend", 18);
            b.Add("energy_drink", 22);
            b.Add("soda_red", 24);
            b.Add("cat_bar", 24);
            b.Add("li_eat_snackbar", 22);
            b.Add("red_beer", 22);
            b.Add("processed_milk", 18);
            b.Add("processed_juice", 18);
            b.Add("processed_meat", 12);
            b.Add("zerochew", 18);
            b.Add("cup_noodle", 18);
            b.Add("nudka", 14);
            b.Add("beis_icecream", 12);
            b.Add("bandage_item", 30);
            b.Add("hemostatic_bandage_item", 20);
            b.Add("topical_bandage_item", 20);
            result["refuse"] = b.Build();

            b = new Builder();
            b.Add("junk", 1357);
            b.Add("wire", 420);
            b.Add("printer_plastic", 500);
            b.Add("common_electronic", 200);
            b.Add("system_module_ruined", 60);
            b.Add("system_module_corrupt", 25);
            b.Add("blank_module", 20);
            b.Add("random_performance_module", 6);
            b.Add("random_efficiency_module", 6);
            b.Add("random_quality_module", 6);
            b.Add("system_module_performance", 4);
            b.Add("system_module_eco", 3);
            b.Add("system_module_fineness", 3);
            b.Add("rare_electronic", 1);
            b.Add("welder", 10);
            b.Add("wire_cutter", 8);
            b.Add("kotton_fabric", 10);
            b.Add("metal_ingot", 10);
            b.Add("gun_part", 10);
            b.Add("screwdriver", 6);
            result["terminal"] = b.Build();

            b = new Builder();
            b.Add("junk", 275);
            b.Add("bandage_item", 60);
            b.Add("hemostatic_bandage_item", 40);
            b.Add("topical_bandage_item", 40);
            b.Add("blood_bag", 15);
            b.Add("rubbing_alcohol", 10);
            b.Add("salve", 5);
            b.Add("surgery_tool", 1);
            b.Add("birth_control_pill", 15);
            b.Add("fanta_pill", 15);
            b.Add("phagimycin_pill", 15);
            b.Add("zyanide_pill", 15);
            b.Add("smelling_salt", 6);
            b.Add("caffeine_pill", 12);
            b.Add("dream_dust", 5);
            b.Add("nightmare_dust", 5);
            b.Add("common_medical", 10);
            b.Add("water_filter", 6);
            b.Add("toothpaste", 4);
            b.Add("toilet_paper", 3);
            b.Add("processed_meat", 4);
            b.Add("cup_noodle", 4);
            b.Add("kotton_fabric", 8);
            b.Add("glass_shard", 10);
            b.Add("empty_beer_bottle", 6);
            result["med"] = b.Build();

            b = new Builder();
            b.Add("junk", 859);
            b.Add("pipe_weapon", 200);
            b.Add("glass_shard_shiv", 250);
            b.Add("box_cutter", 40);
            b.Add("kitchen_knife", 24);
            b.Add("crowbar", 8);
            b.Add("combat_knife", 10);
            b.Add("kitchen_cleaver", 8);
            b.Add("hatchet", 8);
            b.Add("skinning_knife", 3);
            b.Add("stun_baton", 1);
            b.Add("combat_machete", 1);
            b.Add("printed_gun", 30);
            b.Add("handmade_pistol", 3);
            b.Add("heavy_handmade_pistol", 1);
            b.Add("revolver", 1);
            b.Add("small_pistol_ammo_nl", 80);
            b.Add("small_pistol_ammo", 60);
            b.Add("small_pistol_ammo_p", 18);
            b.Add("heavy_pistol_ammo_nl", 24);
            b.Add("heavy_pistol_ammo", 30);
            b.Add("heavy_pistol_ammo_p", 10);
            b.Add("lou_etched_round", 10);
            b.Add("scrap_metal", 30);
            b.Add("nuts_metal", 20);
            b.Add("gun_part", 20);
            b.Add("glass_shard", 30);
            b.Add("empty_beer_bottle", 20);
            b.Add("kotton_fabric", 6);
            b.Add("welder", 4);
            b.Add("metal_ingot", 6);
            b.Add("common_electronic", 8);
            b.Add("wire_cutter", 4);
            b.Add("processed_meat", 6);
            b.Add("cup_noodle", 6);
            b.Add("bandage_item", 8);
            b.Add("hemostatic_bandage_item", 4);
            result["weapons"] = b.Build();

            b = new Builder();
            b.Add("junk", 1478);
            b.Add("mag_22", 275);
            b.Add("mag_22_high", 40);
            b.Add("mag_22_ext", 14);
            b.Add("mag_10mm", 40);
            b.Add("mag_10mm_high", 10);
            b.Add("mag_10mm_ext", 4);
            b.Add("mag_10mm_smg", 3);
            b.Add("mag_10mm_smg_high", 1);
            b.Add("mag_10mm_smg_box", 1);
            b.Add("rds_makeshift", 45);
            b.Add("rds", 3);
            b.Add("laser_toy", 90);
            b.Add("laser", 3);
            b.Add("laser2", 1);
            b.Add("silencer_makeshift", 90);
            b.Add("silencer", 3);
            b.Add("silencer2", 1);
            b.Add("barrel", 3);
            b.Add("compensator", 3);
            b.Add("bipod", 5);
            b.Add("gl", 1);
            b.Add("grip", 5);
            b.Add("stock", 5);
            b.Add("short_stock", 5);
            b.Add("internal_springs", 5);
            b.Add("internal_springs_improved", 70);
            b.Add("trigger_group", 3);
            b.Add("match_barrel", 2);
            b.Add("pistol_switch", 3);
            b.Add("ammo_display", 2);
            b.Add("grip_gel", 2);
            b.Add("small_pistol_ammo_nl", 130);
            b.Add("small_pistol_ammo", 50);
            b.Add("heavy_pistol_ammo_nl", 40);
            b.Add("lou_etched_round", 10);
            b.Add("gun_part", 100);
            b.Add("common_electronic", 60);
            b.Add("wire", 50);
            b.Add("screwdriver", 50);
            b.Add("scrap_metal", 120);
            b.Add("nuts_metal", 60);
            b.Add("welder", 8);
            b.Add("wire_cutter", 20);
            b.Add("metal_ingot", 20);
            b.Add("kotton_fabric", 10);
            b.Add("empty_beer_bottle", 30);
            b.Add("bandage_item", 20);
            b.Add("processed_meat", 6);
            b.Add("cup_noodle", 6);
            b.Add("hemostatic_bandage_item", 8);
            result["parts"] = b.Build();

            return result;
        }

        private static Dictionary<string, string> BuildSourceTables()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            AddAll(result, "junkTable",
                "junk", "glass_shard", "system_module_ruined", "pipe_weapon", "empty_beer_bottle", "scratcher");
            AddAll(result, "materialTable",
                "scrap_metal", "nuts_metal", "flux_agent", "printer_plastic", "wire", "gun_part",
                "common_electronic", "kotton_fabric", "metal_ingot");
            AddAll(result, "t1moduleTable",
                "random_performance_module", "random_efficiency_module", "random_quality_module",
                "system_module_corrupt", "blank_module");
            AddAll(result, "t2moduleTable",
                "system_module_performance", "system_module_eco", "system_module_fineness");
            AddAll(result, "toolTable",
                "screwdriver", "wire_cutter", "module_extractor", "welder", "turbo_booster", "crowbar");
            AddAll(result, "householdTable",
                "caffeine_pill", "toothpaste", "water_filter", "shampoo", "paper_towel", "toilet_paper",
                "full_water_test_strip_box", "full_water_purification_tablet_box");
            AddAll(result, "makeshiftWeaponTable",
                "glass_shard_shiv", "box_cutter", "kitchen_knife",
                "combat_knife", "kitchen_cleaver", "hatchet", "skinning_knife", "stun_baton", "combat_machete");
            AddAll(result, "gunTable", "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "revolver");
            AddAll(result, "ammoTable",
                "small_pistol_ammo", "small_pistol_ammo_p", "small_pistol_ammo_nl",
                "heavy_pistol_ammo", "heavy_pistol_ammo_p", "heavy_pistol_ammo_nl", "lou_etched_round");
            AddAll(result, "gunModTable",
                "rds", "rds_makeshift", "silencer", "silencer2", "silencer_makeshift", "barrel",
                "compensator", "mag_10mm", "mag_10mm_high", "mag_10mm_ext", "mag_22", "mag_22_high",
                "mag_22_ext", "mag_10mm_smg", "mag_10mm_smg_high", "mag_10mm_smg_box", "laser", "laser2",
                "laser_toy", "bipod", "gl", "grip", "stock", "short_stock", "internal_springs",
                "internal_springs_improved", "trigger_group", "match_barrel", "pistol_switch",
                "ammo_display", "grip_gel");
            AddAll(result, "packedFoodTable",
                "galaxy_blend", "energy_drink", "soda_red", "cat_bar", "li_eat_snackbar", "red_beer",
                "processed_milk", "processed_juice", "processed_meat", "zerochew", "cup_noodle", "nudka",
                "beis_icecream");
            AddAll(result, "dumpingGroundMedical",
                "bandage_item", "hemostatic_bandage_item", "topical_bandage_item");
            AddAll(result, "medicalTable",
                "blood_bag", "rubbing_alcohol", "salve", "surgery_tool", "birth_control_pill",
                "fanta_pill", "phagimycin_pill", "zyanide_pill", "smelling_salt", "dream_dust",
                "nightmare_dust", "common_medical");
            // 高档池里的物品在开启前不会出现，这里只是给 ModHook 报一个合理的原版表名。
            AddAll(result, "householdTable",
                "water_filter_adv", "portable_water_purifier", "bottled_water_premium");
            AddAll(result, "dumpingGroundMedical", "blue_blood_bag", "black_injector");
            AddAll(result, "materialTable", "rare_electronic");
            AddAll(result, "toolTable", "module_extractor_advanced");
            return result;
        }

        private static void AddAll(Dictionary<string, string> target, string tableId, params string[] items)
        {
            for (int i = 0; i < items.Length; ++i) target[items[i]] = tableId;
        }
    }

    internal sealed class Picker
    {
        private const string WindowId = Core.Id + ":picker";
        private const string BoardWindowId = Core.Id + ":board";
        internal static readonly Color Accent = new Color32(242, 201, 115, 255);
        internal static readonly Color Ink = new Color32(225, 235, 227, 255);
        private static Picker current;
        private static CustomUIWindow boardWindow;

        /// <summary>
        /// 本次进入垃圾场时选定的区域。选定后一直沿用，直到离开该地点，
        /// 所以同一次停留里连续拾荒不再弹地图。
        /// </summary>
        internal static Region ChosenRegion { get; private set; }
        private static DumpingGroundLocation chosenAt;
        private readonly MapUIManager map;
        private readonly Action resume;
        /// <summary>
        /// 非空表示这张地图是「前往垃圾场」时弹出的：确认后继续原版 Travel 进入。
        /// 为空则是地点面板里点「拾荒」的兜底弹窗。
        /// </summary>
        private DumpingGroundLocation target;
        private readonly CustomUIElement[] pins = new CustomUIElement[Region.All.Length];
        private readonly Image[] pinRings = new Image[Region.All.Length];
        private Region selected;
        private CustomUIWindow window;
        private GameObject shade;
        private CustomUIElement description, confirm;

        private Picker(MapUIManager map, Action resume)
        {
            this.map = map;
            this.resume = resume;
        }

        internal static bool IsAtDump(MapUIManager map)
        {
            return map != null && map.Current is DumpingGroundLocation && !map.IsInTransit;
        }

        internal static bool Intercept(MapUIManager map, Action resume)
        {
            if (!Core.Enabled || !IsAtDump(map) || !ScavHelper.CanScavenge())
                return false;
            // 本次停留已经选过区域：不再弹地图，直接执行原有的拾荒。
            if (ChosenRegion != null && chosenAt == map.Current)
                return false;
            if (ChosenRegion != null)
                ResetChoice();
            if (current != null && current.window != null && current.window.IsVisible)
                return true;
            if (CustomUIManager.Instance == null)
                return false;

            Close();
            Picker picker = new Picker(map, resume);
            current = picker;
            try
            {
                picker.Build();
                return true;
            }
            catch (Exception ex)
            {
                Close();
                Debug.LogError("[DumpingGroundMap] Could not open map: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 前往垃圾场时先弹区域地图：确认后由 resume 重新调用原版 Travel 完成"进入"。
        /// 返回 true 表示这次 Travel 暂缓执行。
        /// </summary>
        internal static bool InterceptTravel(MapUIManager map, DumpingGroundLocation destination)
        {
            if (!Core.Enabled || map == null || destination == null) return false;
            if (map.IsInTransit || !destination.IsAvailable() || !ScavHelper.CanScavenge())
                return false;
            // 已经在本次行程里选过这块区域（确认后重新调 Travel 的那一次）。
            if (ChosenRegion != null && chosenAt == destination)
                return false;
            if (ChosenRegion != null)
                ResetChoice();
            if (current != null && current.window != null && current.window.IsVisible)
                return true;
            if (CustomUIManager.Instance == null)
                return false;

            Close();
            Picker picker = new Picker(map, delegate { map.Travel(destination); });
            picker.target = destination;
            current = picker;
            try
            {
                picker.Build();
                return true;
            }
            catch (Exception ex)
            {
                Close();
                Debug.LogError("[DumpingGroundMap] Could not open map: " + ex);
                return false;
            }
        }

        internal static void Close()
        {
            CloseBoard();
            Picker picker = current;
            if (picker == null) return;
            if (picker.window != null && picker.window.IsAlive)
                picker.window.Close();
            else
                picker.Cleanup();
        }

        private void Cleanup()
        {
            CloseBoard();
            if (current == this) current = null;
            if (shade != null)
            {
                shade.SetActive(false);
                UnityEngine.Object.Destroy(shade);
                shade = null;
            }
        }

        internal static void CheckLocation(MapUIManager map)
        {
            // 前往途中弹出的地图（target 非空）不要求人已经在垃圾场。
            if (current != null && (current.map != map
                || (current.target == null && !IsAtDump(map))
                || current.window == null || !current.window.IsVisible))
                Close();

            // 离开垃圾场（回家后 Current 变空、换去别的地点、读档后场景重建）时丢掉本次选择。
            if (ChosenRegion != null && !map.IsInTransit && map.Current != chosenAt)
                ResetChoice();
        }

        internal static void ResetChoice()
        {
            ChosenRegion = null;
            chosenAt = null;
        }

        private void Build()
        {
            CustomUIManager ui = CustomUIManager.Instance;
            CustomUIBuilder b = ui.CreateWindow(WindowId, Copy.Text("垃圾场 · 拾荒地图", "Dumping Ground · Search Map"),
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            window = b.Handle;
            b.SetSize(740, 592).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(Cleanup);

            shade = new GameObject("DumpingGroundMapShade", typeof(RectTransform), typeof(Image));
            RectTransform cover = (RectTransform)shade.transform;
            cover.SetParent(window.Rect.parent, false);
            cover.anchorMin = Vector2.zero;
            cover.anchorMax = Vector2.one;
            cover.offsetMin = Vector2.zero;
            cover.offsetMax = Vector2.zero;
            cover.SetSiblingIndex(window.Rect.GetSiblingIndex());
            shade.GetComponent<Image>().color = new Color32(9, 16, 19, 183);

            b.AddLabel(Copy.Text("选一个地标即进入该区域；进入后点“拾荒”直接按该区的物品表拾取，本次停留内不再询问。告示板可查看该区物品概率。",
                "Pick a landmark to enter that area; after that, Search uses its table for the rest of the visit. Open a notice board to see its item chances."));
            Size(b.Last, -1, 44);
            b.AddSpace(6);
            Sprite background = ui.GetModSprite(Core.Id, "dumping_ground_map", true);
            b.AddImage(background, 680, 328, "map");
            CustomUIElement mapPanel = b.Last;
            Size(mapPanel, -1, 328);
            mapPanel.SetImageColor(background == null ? (Color)new Color32(39, 60, 56, 255) : Color.white);
            // Keep click targets tied to the same rectangle as the map image.
            RectTransform graphic = mapPanel.image.rectTransform;

            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                Vector2 anchor = new Vector2(region.X, region.Y);
                RectTransform pinRect = PinRect(graphic, "Landmark " + (i + 1), anchor);
                Image ring = pinRect.gameObject.AddComponent<Image>();
                Sprite ringSprite = ui.GetSprite("border_white");
                if (ringSprite != null)
                {
                    ring.sprite = ringSprite;
                    ring.type = Image.Type.Sliced;
                }
                ring.raycastTarget = false;
                pinRings[i] = ring;
                CustomUIBuilder pinBuilder = new CustomUIBuilder(ui, window, pinRect);
                pinBuilder.AddButton((i + 1) + "  " + region.Name, () => Select(region), "area_" + i);
                CustomUIElement pin = pinBuilder.Last;
                pin.Rect.anchorMin = Vector2.zero;
                pin.Rect.anchorMax = Vector2.one;
                pin.Rect.offsetMin = new Vector2(2, 2);
                pin.Rect.offsetMax = new Vector2(-2, -2);
                pins[i] = pin;

                RectTransform boardRect = PinRect(graphic, "Notice " + (i + 1), anchor);
                boardRect.anchoredPosition = new Vector2(0f, -40f);
                boardRect.sizeDelta = new Vector2(110f, 26f);
                CustomUIBuilder boardBuilder = new CustomUIBuilder(ui, window, boardRect);
                boardBuilder.AddButton(Copy.Text("告示板", "Notice"), () =>
                {
                    Select(region);
                    OpenBoard(region);
                }, "area_board_" + i);
                CustomUIElement boardButton = boardBuilder.Last;
                boardButton.Rect.anchorMin = Vector2.zero;
                boardButton.Rect.anchorMax = Vector2.one;
                boardButton.Rect.offsetMin = Vector2.zero;
                boardButton.Rect.offsetMax = Vector2.zero;
            }

            b.AddSpace(8).AddLabel("", "description");
            description = b.Last;
            Size(description, -1, 64);
            if (description.text != null) description.text.alignment = TextAlignmentOptions.MidlineLeft;
            b.AddSpace(8).BeginRow(12, TextAnchor.MiddleRight);
            b.AddButton(Copy.Text("取消", "Cancel"), Close);
            Size(b.Last, 130, 36);
            b.AddButton(target != null
                ? Copy.Text("进入该区域", "Enter this area")
                : Copy.Text("开始拾荒", "Start search"), StartSearch);
            confirm = b.Last;
            Size(confirm, 178, 36);
            b.End();
            Select(null);
            b.Show();
        }

        private static RectTransform PinRect(RectTransform parent, string name, Vector2 anchor)
        {
            RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(172, 42);
            return rect;
        }

        internal static void Size(CustomUIElement element, float width, float height)
        {
            element.SetPreferredSize(width, height);
            LayoutElement layout = element.root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                if (width > 0f) layout.minWidth = width;
                if (height > 0f) layout.minHeight = height;
                layout.flexibleHeight = 0;
            }
        }

        private static void OpenBoard(Region region)
        {
            if (current == null) return;
            CloseBoard();
            CustomUIManager ui = CustomUIManager.Instance;
            if (ui == null) return;

            CustomUIBuilder b = ui.CreateWindow(BoardWindowId,
                Copy.Text("拾荒告示板 · ", "Loot Notice · ") + region.Name,
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            boardWindow = b.Handle;
            b.SetSize(620, 552).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(BoardClosed);

            b.AddLabel(Copy.Text("—— 本区掉落公示 ——", "— Area Loot Notice —"), "board_header");
            b.Last.SetTextColor(Accent);
            Size(b.Last, -1, 28);
            b.AddLabel(region.Detail, "board_detail");
            Size(b.Last, -1, 42);
            b.AddLabel(Copy.Text("当前概率（四舍五入，包含设置里的调节；夜间事件会临时改变比例）：",
                "Current chances (rounded, settings included; night events can still shift them):"), "board_caption");
            Size(b.Last, -1, 26);
            b.AddSpace(4);

            EffectiveTable table = Tuning.TableFor(region);
            b.BeginScroll(300);
            b.BeginGrid(2, 286, 22, 4);
            for (int i = 0; i < table.Count; ++i)
            {
                int index = table.IndexAt(i);
                b.AddLabel(ItemName(table.Items[index]) + "  ·  " + Percent(table.ProbabilityAt(i)), "board_item_" + i);
            }
            b.End();
            b.End();

            b.AddSpace(6);
            b.AddLabel(Copy.Text("本区期望价值：", "Expected value: ") + table.Ev.ToString("0.00", CultureInfo.InvariantCulture),
                "board_ev");
            Size(b.Last, -1, 24);
            b.AddLabel(Copy.Text("垃圾事件只抬高“垃圾”，其余物品按比例让位；各区域期望价值刻意保持接近。",
                "Junk events only raise \"junk\"; other items yield proportionally. Expected value is kept close across areas."),
                "board_footer");
            Size(b.Last, -1, 42);
            b.Show();
        }

        private static void CloseBoard()
        {
            CustomUIWindow window = boardWindow;
            boardWindow = null;
            if (window != null && window.IsAlive) window.Close();
        }

        private static void BoardClosed()
        {
            boardWindow = null;
        }

        /// <summary>
        /// 部分物品的显示名不等于 item_&lt;id&gt;_name：绷带类固定用 item_bandage_name 等，
        /// 满盒物品复用了容器物品的名字词条。这里按游戏工厂里的实际取值做映射。
        /// </summary>
        private static readonly Dictionary<string, string> NameKeys =
            new Dictionary<string, string>
            {
                { "bandage_item", "item_bandage_name" },
                { "hemostatic_bandage_item", "item_hemostatic_bandage_name" },
                { "topical_bandage_item", "item_topical_bandage_name" },
                { "full_water_test_strip_box", "item_water_test_container_name" },
                { "full_water_purification_tablet_box", "item_water_tablet_container_name" }
            };

        internal static string ItemName(string identifier)
        {
            string key;
            if (!NameKeys.TryGetValue(identifier, out key))
                key = "item_" + identifier + "_name";

            string name = LocHelper.GetLocalizedItem(key);
            string table, missingKey;
            if (string.IsNullOrEmpty(name) || LocHelper.TryParseMissingTranslation(name, out table, out missingKey))
                return FallbackName(identifier);
            return name;
        }

        /// <summary>
        /// 随机模组的物品 id 不是目录里的实体物品，游戏运行时才生成具体模组，
        /// 因此没有名称词条，GetLocalizedItem 会返回"缺词条"提示文本。
        /// </summary>
        private static string FallbackName(string identifier)
        {
            if (identifier == "random_performance_module") return Copy.Text("随机性能模组", "Random Performance Module");
            if (identifier == "random_efficiency_module") return Copy.Text("随机效率模组", "Random Efficiency Module");
            if (identifier == "random_quality_module") return Copy.Text("随机品质模组", "Random Quality Module");
            return identifier;
        }

        internal static string Percent(float probability)
        {
            float pct = probability * 100f;
            if (pct < 0.1f) return "<0.1%";
            if (pct < 10f) return pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return Mathf.RoundToInt(pct).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void Select(Region region)
        {
            if (current != this) return;
            selected = region;
            for (int i = 0; i < pins.Length; ++i)
            {
                bool active = Region.All[i] == selected;
                pins[i].SetTextColor(active ? Accent : Ink);
                pinRings[i].color = active ? Accent : (Color)new Color32(95, 131, 117, 255);
            }

            int left = Math.Max(0, ScavHelper.GetScavTimeLeft());
            string remaining = Copy.Text("剩余拾荒次数：", "Searches left: ") + left;
            if (selected == null)
            {
                string hint = target != null
                    ? Copy.Text("点击地图上的编号地标，再点“进入该区域”前往此地。", "Select a numbered landmark, then choose \"Enter this area\".")
                    : Copy.Text("点击地图上的编号地标查看掉落倾向。", "Select a numbered landmark to see its loot bias.");
                description.SetText(hint + "\n" + remaining);
            }
            else
            {
                description.SetText(selected.Detail + "\n" + remaining);
            }
            confirm.SetInteractable(selected != null && (target != null || ScavHelper.CanScavenge()));
        }

        private void StartSearch()
        {
            if (current != this || selected == null) return;

            Region choice = selected;
            DumpingGroundLocation destination = target;
            if (destination != null)
            {
                // 前往途中：确认后按原版流程继续 Travel，选定区域随行程带入。
                if (!Core.Enabled || !destination.IsAvailable() || map.IsInTransit)
                {
                    Close();
                    return;
                }
                Close();
                ChosenRegion = choice;
                chosenAt = destination;
                resume();
                return;
            }

            // 兜底：已经在垃圾场里（读档等边缘情况）从「拾荒」按钮弹的地图。
            if (!Core.Enabled || !IsAtDump(map) || !ScavHelper.CanScavenge())
            {
                Close();
                return;
            }
            DumpingGroundLocation location = map.Current as DumpingGroundLocation;
            Close();
            // 记住这次选择，本次停留里后续拾荒直接沿用（Intercept 不会再弹地图）。
            ChosenRegion = choice;
            chosenAt = location;
            resume();
        }
    }

    /// <summary>
    /// All loot tuning lives in the game's own mod-config window instead of the map.
    /// A postfix on ModConfigWindow.BuildEntry hooks the native sliders, and a patch
    /// on CustomUIElement.SetFixedWidth appends a value label to the same row, right
    /// of the slider, so the number sits where the thumb is instead of on its own
    /// line. Below the last entry the area value block and the preset buttons
    /// are appended. Dragging only changes Tuning's in-memory values and the labels;
    /// the config file is written on PointerUp or when Apply is pressed.
    /// </summary>
    internal static class NativeSettings
    {
        private const string KeyJunk = "junk_percent";
        private const string KeyHigh = "high_percent";
        private const string LogTag = "[DGM-SLIDER] ";

        private static CustomUIWindow window;
        private static CustomUIWindow builtFor;
        private static CustomUIElement junkValue, highValue;
        private static CustomUIElement specialtyDropdown;
        private static CustomUIElement[] evLabels;
        private static CustomUIElement junkSlider, highSlider;
        private static bool dragging;
        private static int dragKind;
        private static float lastSlideAt;
        private static int slideCount;
        private static float lastLogAt;

        /// <summary>Builder and entry key of the row currently being built.</summary>
        private static CustomUIBuilder building;
        private static string buildingKey;

        internal static void BeginEntry(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            building = null;
            buildingKey = null;
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;

                CustomUIWindow target = b.Handle;
                if (target == null) return;
                if (!ReferenceEquals(target, builtFor))
                {
                    Reset();
                    builtFor = target;
                    window = target;
                }
                building = b;
                buildingKey = key;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "begin entry failed: " + ex);
            }
        }

        private static bool IsOurKey(string key)
        {
            return key == KeyJunk || key == KeyHigh
                || key == Tuning.SpecialtyKey;
        }

        /// <summary>
        /// The native row calls SetFixedWidth on the slider right after adding it.
        /// Our postfix appends a value label to that same open row, so the number
        /// sits to the right of the slider instead of taking a line of its own.
        /// </summary>
        internal static void AttachValueLabel(CustomUIElement element)
        {
            try
            {
                if (element == null || building == null || buildingKey == null) return;
                if (!ReferenceEquals(element, building.Last)) return;
                if (element.slider == null) return;

                string key = buildingKey;
                // Cleared while we build the label so our own SetFixedWidth call below
                // cannot re-enter this postfix.
                buildingKey = null;
                building.AddLabel("", "dgm_value_" + key);
                CustomUIElement label = building.Last;
                // Fixed width with flexibleWidth 0 so the row keeps the slider where
                // the native layout put it and the number just sits to its right.
                label.SetFixedWidth(72f);
                if (label.text != null)
                {
                    label.text.alignment = TextAlignmentOptions.MidlineLeft;
                    label.text.color = Picker.Ink;
                }
                if (key == KeyJunk) junkValue = label;
                else highValue = label;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "value label failed: " + ex);
            }
        }

        /// <summary>Called from the BuildEntry postfix, after the native row is closed.</summary>
        internal static void Inject(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            try
            {
                if (b == null || config == null || entry == null) return;
                if (config.ModId != Core.Id) return;
                string key = entry.Key;
                if (!IsOurKey(key)) return;
                if (window == null) window = b.Handle;
                if (window == null) return;

                if (key == KeyJunk)
                {
                    junkSlider = HookSlider(window, key, 1);
                    junkValue = EnsureValueLine(b, key, junkValue);
                }
                else if (key == KeyHigh)
                {
                    highSlider = HookSlider(window, key, 2);
                    highValue = EnsureValueLine(b, key, highValue);
                }
                else
                {
                    specialtyDropdown = b.Last;
                    AddEvBlock(b);
                }
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError(LogTag + "inject failed: " + ex);
            }
            finally
            {
                building = null;
                buildingKey = null;
            }
        }

        /// <summary>
        /// Fallback only: if the row patch did not add a value label, keep the old
        /// separate line so the number is never missing.
        /// </summary>
        private static CustomUIElement EnsureValueLine(CustomUIBuilder b, string key, CustomUIElement current)
        {
            if (current != null) return current;
            b.AddLabel("", "dgm_value_" + key);
            CustomUIElement label = b.Last;
            Picker.Size(label, -1, 22);
            return label;
        }

        private static CustomUIElement HookSlider(CustomUIWindow target, string key, int kind)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing, live value disabled");
                return null;
            }
            element.slider.onValueChanged.AddListener(delegate(float value) { OnSlide(kind, value); });
            HookRelease(element, key);
            Debug.Log(LogTag + key + ": live listener bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>
        /// PointerUp trigger on the GameObject that owns the Slider component. Putting
        /// it on child graphics would make EventTrigger the press/drag target and the
        /// Slider would never move (regression fixed in 0.3.3).
        /// </summary>
        private static void HookRelease(CustomUIElement element, string key)
        {
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data) { EndDrag(); });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": release hook on " + go.name);
        }

        private static void AddEvBlock(CustomUIBuilder b)
        {
            b.AddSpace(6);
            b.AddLabel(Copy.Text("—— 每次拾荒大约能捡到的物品价值（拖动时实时刷新） ——",
                "— Rough value of what one search turns up (live while dragging) —"), "dgm_ev_header");
            CustomUIElement header = b.Last;
            if (header.text != null) header.text.color = Picker.Accent;
            Picker.Size(header, -1, 26);

            // Fixed yardstick above the area rows: the untouched vanilla tables.
            b.AddLabel("", "dgm_ev_reference");
            CustomUIElement reference = b.Last;
            Picker.Size(reference, -1, 24);
            reference.SetText(ReferenceLine());
            if (reference.text != null) reference.text.color = Picker.Ink;

            evLabels = new CustomUIElement[Region.All.Length + 1];
            for (int i = 0; i < evLabels.Length; ++i)
            {
                b.AddLabel("", "dgm_ev_" + i);
                evLabels[i] = b.Last;
                Picker.Size(evLabels[i], -1, 24);
            }

            b.AddSpace(4);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            PresetButton(b, 0, Copy.Text("默认", "Default"));
            PresetButton(b, 1, Copy.Text("专精拉满", "Specialty max"));
            PresetButton(b, 2, Copy.Text("赌高档", "Gamble"));
            b.End();
            b.AddSpace(4);
        }

        private static void PresetButton(CustomUIBuilder b, int preset, string text)
        {
            b.AddButton(text, delegate { ApplyPreset(preset); });
            Picker.Size(b.Last, 140, 34);
        }

        internal static void RefreshIfOpen()
        {
            if (window == null || !window.IsAlive) return;
            Refresh();
        }

        /// <summary>
        /// Writes any value still held in memory. Called before the native Apply runs
        /// and when the window is rebuilt or the mod is disabled, so a lost PointerUp
        /// can never leave Tuning stuck on an unsaved preview value.
        /// </summary>
        internal static void CommitDrag()
        {
            if (dragging)
            {
                EndDrag();
                return;
            }
            Tuning.CommitLive();
        }

        private static void Refresh()
        {
            if (window == null || !window.IsAlive) return;

            if (junkValue != null)
                junkValue.SetText(Mathf.RoundToInt(Tuning.JunkPercent) + "%");
            if (highValue != null)
                highValue.SetText(Tuning.HighPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (evLabels == null) return;

            float baseSum = 0f;
            float newSum = 0f;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                float before = Tuning.BaseEv(region);
                EffectiveTable table = Tuning.TableFor(region);
                baseSum += before;
                newSum += table.Ev;
                evLabels[i].SetText(Line(region.Name, before, table.Ev));
            }
            float count = Region.All.Length;
            evLabels[Region.All.Length].SetText(Line(
                Copy.Text("各区平均", "all-area average"), baseSum / count, newSum / count));

            SyncWidgets();
        }

        private static string ReferenceLine()
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                float value = Tuning.BaseEv(Region.All[i]);
                if (value < min) min = value;
                if (value > max) max = value;
            }
            // Every area is reverse-solved to the same target, so one compact figure
            // is enough; fall back to a range if rounding ever splits them.
            string valueText = (max - min) <= 0.011f
                ? Copy.Text("各区 ", "every area ") + min.ToString("0.00", CultureInfo.InvariantCulture)
                : min.ToString("0.00", CultureInfo.InvariantCulture) + " – "
                    + max.ToString("0.00", CultureInfo.InvariantCulture);
            return Copy.Text("原版参考（垃圾 100%、无专精）：",
                "Vanilla reference (100% junk, no specialty): ") + valueText;
        }

        private static string Line(string name, float before, float after)
        {
            float change = before > 0f ? (after - before) / before * 100f : 0f;
            return name + "　" + Copy.Text("原版 ", "vanilla ")
                + before.ToString("0.00", CultureInfo.InvariantCulture) + "  →  "
                + Copy.Text("现在 ", "now ")
                + after.ToString("0.00", CultureInfo.InvariantCulture)
                + "（" + (change >= 0f ? "+" : "") + change.ToString("0.0", CultureInfo.InvariantCulture) + "%）";
        }

        private static void OnSlide(int kind, float value)
        {
            if (kind == 1)
            {
                BeginDrag(1, Tuning.JunkPercent);
                float snapped = Mathf.Round(value);
                SyncSlider(junkSlider, snapped);
                Tuning.SetJunkPercentLive(snapped);
                LogSlide(1, value, Tuning.JunkPercent);
            }
            else if (kind == 2)
            {
                BeginDrag(2, Tuning.HighPercent);
                float snapped = Mathf.Round(value * 2f) / 2f;
                SyncSlider(highSlider, snapped);
                Tuning.SetHighPercentLive(snapped);
                LogSlide(2, value, Tuning.HighPercent);
            }
        }

        private static void ApplyPreset(int preset)
        {
            EndDrag();
            Tuning.ApplyPreset(preset);
            // The native dropdown owns its own selection; writing the config entry
            // does not move it, so drive the widget explicitly.
            if (specialtyDropdown != null)
                specialtyDropdown.SetIndex(Tuning.SpecialtyTier, false);
            Refresh();
        }

        private static void SyncSlider(CustomUIElement element, float value)
        {
            if (element == null || element.slider == null) return;
            if (Mathf.Abs(element.slider.value - value) > 0.001f) element.SetFloat(value, false);
        }

        private static void SyncWidgets()
        {
            if (dragging) return;
            SyncSlider(junkSlider, Tuning.JunkPercent);
            SyncSlider(highSlider, Tuning.HighPercent);
        }

        private static void BeginDrag(int kind, float from)
        {
            float now = Time.realtimeSinceStartup;
            if (!dragging || kind != dragKind || now - lastSlideAt > 0.4f)
            {
                if (dragging) Tuning.CommitLive();
                dragging = true;
                dragKind = kind;
                slideCount = 0;
                lastLogAt = 0f;
                Debug.Log(LogTag + "drag begin kind=" + kind + " from="
                    + from.ToString("0.###", CultureInfo.InvariantCulture));
            }
            lastSlideAt = now;
        }

        private static void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            lastSlideAt = 0f;
            Tuning.CommitLive();
            Debug.Log(LogTag + "drag end kind=" + dragKind + " callbacks=" + slideCount
                + " saved=" + Num(Tuning.JunkPercent) + "/" + Num(Tuning.HighPercent));
            Refresh();
        }

        /// <summary>Throttled trail: one line every 0.25 s plus a callback counter.</summary>
        private static void LogSlide(int kind, float raw, float applied)
        {
            ++slideCount;
            float now = Time.realtimeSinceStartup;
            if (now - lastLogAt < 0.25f) return;
            lastLogAt = now;
            Debug.Log(LogTag + "kind=" + kind
                + " raw=" + raw.ToString("0.###", CultureInfo.InvariantCulture)
                + " applied=" + applied.ToString("0.###", CultureInfo.InvariantCulture)
                + " live=" + (Tuning.HasLive ? 1 : 0)
                + " callbacks=" + slideCount
                + " ev=" + Tuning.TableFor(Region.All[0]).Ev.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string Num(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static void Reset()
        {
            Tuning.CommitLive();
            if (dragging)
            {
                dragging = false;
                lastSlideAt = 0f;
            }
            window = null;
            builtFor = null;
            junkValue = null;
            highValue = null;
            specialtyDropdown = null;
            evLabels = null;
            junkSlider = null;
            highSlider = null;
            building = null;
            buildingKey = null;
            dragging = false;
            dragKind = 0;
            lastSlideAt = 0f;
            slideCount = 0;
            lastLogAt = 0f;
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class ModConfigEntryPatch
    {
        private static void Prefix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            NativeSettings.BeginEntry(b, config, entry);
        }

        private static void Postfix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            NativeSettings.Inject(b, config, entry);
        }
    }

    /// <summary>
    /// BuildEntry fixes the slider width right after creating it, while the entry's
    /// row is still open. Appending the value label at that moment puts it on the
    /// same row as the slider instead of on a line below.
    /// </summary>
    [HarmonyPatch(typeof(CustomUIElement), "SetFixedWidth")]
    internal static class SliderValueRowPatch
    {
        private static void Postfix(CustomUIElement __instance)
        {
            NativeSettings.AttachValueLabel(__instance);
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "Apply")]
    internal static class ModConfigApplyPatch
    {
        private static void Prefix()
        {
            NativeSettings.CommitDrag();
        }

        private static void Postfix()
        {
            NativeSettings.RefreshIfOpen();
        }
    }

    [HarmonyPatch(typeof(MapUIManager), "ScavengeButtonClick")]
    internal static class LegacyPanelPatch
    {
        private static bool Prefix(MapUIManager __instance)
        {
            return !Picker.Intercept(__instance, __instance.ScavengeButtonClick);
        }
    }

    [HarmonyPatch(typeof(DumpingGroundLocation), "OnScavenge")]
    internal static class LocationPanelPatch
    {
        private static bool Prefix(DumpingGroundLocation __instance)
        {
            MapUIManager map = MapUIManager.Instance;
            if (map == null || !ReferenceEquals(map.Current, __instance)) return true;
            return !Picker.Intercept(map, () => Resume(__instance));
        }

        private static void Resume(DumpingGroundLocation location)
        {
            AccessTools.Method(typeof(DumpingGroundLocation), "OnScavenge").Invoke(location, null);
        }
    }

    /// <summary>
    /// 「前往」垃圾场时先选区域：选中并确认后才真正 Travel 进入，形成
    /// 「去拾荒 → 地图 → 选地点 → 进入 → 开始拾荒」的顺序。
    /// </summary>
    [HarmonyPatch(typeof(MapUIManager), "Travel")]
    internal static class TravelPatch
    {
        private static bool Prefix(MapUIManager __instance, AfterhourLocation location)
        {
            DumpingGroundLocation destination = location as DumpingGroundLocation;
            if (destination == null) return true;
            return !Picker.InterceptTravel(__instance, destination);
        }
    }

    /// <summary>
    /// 离开垃圾场时清空本次区域选择：下次再前往需重新选一次。
    /// </summary>
    [HarmonyPatch(typeof(DumpingGroundLocation), "Leave")]
    internal static class LeaveDumpPatch
    {
        private static void Postfix() { Picker.ResetChoice(); }
    }

    [HarmonyPatch(typeof(MapUIManager), "CloseLocationPanel")]
    internal static class CloseLocationPatch
    {
        private static void Prefix() { Picker.Close(); }
    }

    [HarmonyPatch(typeof(MapUIManager), "Update")]
    internal static class MapUpdatePatch
    {
        private static void Postfix(MapUIManager __instance) { Picker.CheckLocation(__instance); }
    }

    /// <summary>
    /// Replaces the vanilla dumping-ground roll with the selected area's table.
    /// When no area is active the vanilla distributed roll still runs.
    /// </summary>
    [HarmonyPatch(typeof(ScavHelper), "SpawnDumpingGroundLoot")]
    internal static class DumpingGroundLootPatch
    {
        private static readonly MethodInfo fireLootRolled =
            AccessTools.Method(typeof(ModHook), "FireOnLootRolled");

        private static bool Prefix(ref GameItem __result)
        {
            if (!Core.Enabled) return true;
            Region region = Picker.ChosenRegion;
            if (region == null) return true;

            float junkChance = 0f;
            AfterhourLocation location = AfterhourLocation.Get("dumping_ground");
            if (location != null) junkChance = location.GetModifier("junk_chance");
            float scale = junkChance == 0f ? 1f : Mathf.Max(0f, 1f + junkChance / 100f);

            string itemId = Tuning.Pick(region, scale);
            if (string.IsNullOrEmpty(itemId)) return true;

            GameItem item = ItemSpawner.Spawn(itemId);
            if (item == null) return true;

            LootRollContext context = new LootRollContext();
            context.groupId = "dumpingGroundTG";
            context.tableId = ScavLoot.SourceTable(itemId);
            context.itemId = itemId;
            if (fireLootRolled != null)
                fireLootRolled.Invoke(null, new object[] { context });

            __result = item;
            return false;
        }
    }
}
