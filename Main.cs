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
            Reputation.Hook();
            Debug.Log("[DumpingGroundMap] enabled");
        }

        public void OnDisable()
        {
            Enabled = false;
            Reputation.Unhook();
            NativeSettings.Reset();
            Picker.Close();
            Picker.ResetChoice();
        }
    }

    internal sealed class Region
    {
        /// <summary>
        /// 五块地块：左列入口 / 拆解堆 / 医疗，右列生活废料带 / 废武器堆。
        /// 解锁点数不写在这里，统一放在 UnlockCost 那张表里。
        /// </summary>
        internal static readonly Region[] All =
        {
            new Region("gate", "入口空地", "Main Gate",
                "通用杂项：材料、工具、日用品各有一点，适合补充常规消耗。",
                "A general mix of materials, tools, and household goods for everyday restocking.",
                0.20f, 0.22f),
            new Region("scrap", "拆解堆", "Scrap Pile",
                "材料、工具与电子废料；偶尔能拆出完整模组。",
                "Materials, tools, and electronic scrap, with the occasional intact module.",
                0.20f, 0.50f),
            new Region("refuse", "生活废料带", "Refuse Lane",
                "日用、食品、药品为主；值钱的东西不多，但胜在实用。",
                "Household goods, food, and medicine. Few valuables, but plenty of practical finds.",
                0.78f, 0.23f),
            new Region("med", "医疗废弃点", "Medical Waste",
                "绷带、药品、酒精、手术用品；能救命，也卖得上价。",
                "Bandages, pills, alcohol, and surgical supplies. Life-saving, and worth more than most.",
                0.20f, 0.78f),
            new Region("weapons", "废武器堆", "Weapon Dump",
                "断刀、土枪、散装弹药和改装件；偶尔还能翻出一把能用的枪。",
                "Broken blades, makeshift guns, loose rounds, and gun mods. Now and then a working gun.",
                0.78f, 0.76f)
        };

        internal readonly string Id;
        internal readonly string NameZh, NameEn, DetailZh, DetailEn;
        internal readonly float X, Y;
        /// <summary>解锁所需声望；0 表示无门槛。</summary>
        internal readonly int RequiredReputation;

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
            RequiredReputation = UnlockCost.For(id);
        }

        internal string Name { get { return Copy.Text(NameZh, NameEn); } }
        internal string Detail { get { return Copy.Text(DetailZh, DetailEn); } }
        internal RegionLootTable Loot { get { return ScavLoot.Get(Id); } }

        internal bool Unlocked { get { return Reputation.Points >= RequiredReputation; } }
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
        private readonly Dictionary<string, int> indexOf = new Dictionary<string, int>();

        internal RegionLootTable(string[] items, int[] weights)
        {
            Items = items;
            Weights = weights;
            int sum = 0;
            for (int i = 0; i < weights.Length; ++i) sum += weights[i];
            Total = sum;
            junkIndex = Array.IndexOf(items, "junk");
            for (int i = 0; i < items.Length; ++i) indexOf[items[i]] = i;

            displayOrder = new int[items.Length];
            for (int i = 0; i < displayOrder.Length; ++i) displayOrder[i] = i;
            Array.Sort(displayOrder, delegate(int a, int b) { return weights[b].CompareTo(weights[a]); });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return displayOrder[i]; }
        internal float ProbabilityAt(int i) { return Weights[displayOrder[i]] / (float)Total; }

        /// <summary>垃圾自己的权重；表里没有垃圾时为 0。</summary>
        internal int JunkWeight { get { return junkIndex >= 0 ? Weights[junkIndex] : 0; } }

        /// <summary>权重下标；表里没这件物品时返回 -1。</summary>
        internal int IndexOf(string itemId)
        {
            int index;
            return indexOf.TryGetValue(itemId, out index) ? index : -1;
        }


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
        /// <summary>原版分布（入口空地那张表），专精插值的起点。</summary>
        private static readonly RegionLootTable vanilla = BuildVanilla();
        private static readonly Dictionary<string, RegionLootTable> themes = BuildThemes();
        private static readonly Dictionary<string, string> sourceTables = BuildSourceTables();

        internal static RegionLootTable Vanilla { get { return vanilla; } }

        internal static RegionLootTable Get(string regionId)
        {
            RegionLootTable table;
            return themes.TryGetValue(regionId, out table) ? table : vanilla;
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

        private static RegionLootTable BuildVanilla()
        {
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
            return b.Build();
        }

        /// <summary>
        /// 每区的主题表。非垃圾权重统一归一化到 1000，垃圾权重单独给，
        /// 这样专精插值可以在两张表之间按比例混合。
        /// </summary>
        private static Dictionary<string, RegionLootTable> BuildThemes()
        {
            Dictionary<string, RegionLootTable> result = new Dictionary<string, RegionLootTable>();

            // 拆解堆：材料 / 工具 / 电子废料，垃圾比例最高。
            Builder b = new Builder();
            b.Add("junk", 1493);
            b.Add("flux_agent", 167);
            b.Add("gun_part", 167);
            b.Add("kotton_fabric", 167);
            b.Add("nuts_metal", 167);
            b.Add("scrap_metal", 167);
            b.Add("metal_ingot", 47);
            b.Add("common_electronic", 20);
            b.Add("printer_plastic", 20);
            b.Add("wire", 20);
            b.Add("glass_shard", 18);
            b.Add("module_extractor", 4);
            b.Add("screwdriver", 4);
            b.Add("turbo_booster", 4);
            b.Add("welder", 4);
            b.Add("wire_cutter", 4);
            b.Add("system_module_performance", 3);
            b.Add("blank_module", 2);
            b.Add("random_efficiency_module", 2);
            b.Add("random_performance_module", 2);
            b.Add("random_quality_module", 2);
            b.Add("system_module_corrupt", 2);
            b.Add("system_module_eco", 2);
            b.Add("system_module_fineness", 2);
            b.Add("system_module_ruined", 2);
            b.Add("rare_electronic", 1);
            result["scrap"] = b.Build();

            // 生活废料带：食品 / 日用为主，掺一点便宜药品。
            b = new Builder();
            b.Add("junk", 911);
            b.Add("empty_beer_bottle", 159);
            b.Add("cat_bar", 44);
            b.Add("cup_noodle", 44);
            b.Add("energy_drink", 44);
            b.Add("galaxy_blend", 44);
            b.Add("li_eat_snackbar", 44);
            b.Add("processed_juice", 44);
            b.Add("processed_meat", 44);
            b.Add("processed_milk", 44);
            b.Add("red_beer", 44);
            b.Add("soda_red", 44);
            b.Add("zerochew", 44);
            b.Add("caffeine_pill", 34);
            b.Add("full_water_purification_tablet_box", 34);
            b.Add("full_water_test_strip_box", 34);
            b.Add("paper_towel", 34);
            b.Add("shampoo", 34);
            b.Add("toilet_paper", 34);
            b.Add("toothpaste", 34);
            b.Add("water_filter", 34);
            b.Add("beis_icecream", 27);
            b.Add("nudka", 27);
            b.Add("fanta_pill", 6);
            b.Add("birth_control_pill", 5);
            b.Add("bandage_item", 4);
            b.Add("dream_dust", 4);
            b.Add("hemostatic_bandage_item", 4);
            b.Add("smelling_salt", 4);
            b.Add("topical_bandage_item", 4);
            result["refuse"] = b.Build();

            // 医疗废弃点：医疗品为主，垃圾比例低于低级区。
            b = new Builder();
            b.Add("junk", 868);
            b.Add("fanta_pill", 117);
            b.Add("birth_control_pill", 93);
            b.Add("bandage_item", 88);
            b.Add("hemostatic_bandage_item", 88);
            b.Add("rubbing_alcohol", 88);
            b.Add("smelling_salt", 88);
            b.Add("topical_bandage_item", 88);
            b.Add("dream_dust", 78);
            b.Add("salve", 58);
            b.Add("phagimycin_pill", 56);
            b.Add("blood_bag", 42);
            b.Add("zyanide_pill", 42);
            b.Add("nightmare_dust", 39);
            b.Add("common_medical", 35);
            result["med"] = b.Build();

            // 废武器堆：弹药与近战杂件为主，掺少量枪械和改装件。
            b = new Builder();
            b.Add("junk", 1059);
            b.Add("small_pistol_ammo_nl", 217);
            b.Add("small_pistol_ammo", 152);
            b.Add("heavy_pistol_ammo_nl", 127);
            b.Add("small_pistol_ammo_p", 117);
            b.Add("heavy_pistol_ammo", 101);
            b.Add("lou_etched_round", 101);
            b.Add("heavy_pistol_ammo_p", 84);
            b.Add("box_cutter", 7);
            b.Add("crowbar", 7);
            b.Add("glass_shard_shiv", 7);
            b.Add("kitchen_knife", 6);
            b.Add("pipe_weapon", 6);
            b.Add("mag_22", 4);
            b.Add("silencer_makeshift", 4);
            b.Add("hatchet", 3);
            b.Add("internal_springs_improved", 3);
            b.Add("kitchen_cleaver", 3);
            b.Add("laser_toy", 3);
            b.Add("printed_gun", 3);
            b.Add("bipod", 2);
            b.Add("combat_knife", 2);
            b.Add("grip", 2);
            b.Add("internal_springs", 2);
            b.Add("mag_10mm", 2);
            b.Add("mag_10mm_high", 2);
            b.Add("mag_22_ext", 2);
            b.Add("mag_22_high", 2);
            b.Add("pistol_switch", 2);
            b.Add("rds_makeshift", 2);
            b.Add("short_stock", 2);
            b.Add("skinning_knife", 2);
            b.Add("stock", 2);
            b.Add("ammo_display", 1);
            b.Add("barrel", 1);
            b.Add("combat_machete", 1);
            b.Add("compensator", 1);
            b.Add("grip_gel", 1);
            b.Add("handmade_pistol", 1);
            b.Add("heavy_handmade_pistol", 1);
            b.Add("laser", 1);
            b.Add("laser2", 1);
            b.Add("mag_10mm_ext", 1);
            b.Add("mag_10mm_smg", 1);
            b.Add("mag_10mm_smg_box", 1);
            b.Add("mag_10mm_smg_high", 1);
            b.Add("match_barrel", 1);
            b.Add("rds", 1);
            b.Add("silencer", 1);
            b.Add("silencer2", 1);
            b.Add("stun_baton", 1);
            b.Add("trigger_group", 1);
            result["weapons"] = b.Build();

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
        /// <summary>未解锁地标的字色与边框色，比正常态暗一档。</summary>
        private static readonly Color LockedInk = new Color32(126, 138, 131, 255);
        private static readonly Color LockedRing = new Color32(58, 72, 66, 255);
        private static readonly Color RingInk = new Color32(95, 131, 117, 255);
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
        private CustomUIElement description, confirm, reputationLabel;

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
            b.SetSize(740, 624).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(Cleanup);

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
                // 未解锁的地标在编号后带上门槛，按钮同时压暗，点开仍会解释原因。
                string pinLabel = (i + 1) + "  " + region.Name;
                if (region.RequiredReputation > 0) pinLabel += " · " + region.RequiredReputation;
                pinBuilder.AddButton(pinLabel, () => Select(region), "area_" + i);
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

            b.AddSpace(6).AddLabel("", "reputation");
            reputationLabel = b.Last;
            Size(reputationLabel, -1, 24);
            if (reputationLabel.text != null) reputationLabel.text.alignment = TextAlignmentOptions.MidlineLeft;
            RefreshReputation();

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
            b.AddLabel(Copy.Text("垃圾事件只抬高“垃圾”，其余物品按比例让位；带声望门槛的区域整体更值钱。",
                "Junk events only raise \"junk\"; other items yield proportionally. Areas behind a reputation gate are worth more overall."),
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
            // 80 档的补给箱是预构建容器，没有独立词条，沿用同类箱子的叫法。
            if (identifier == "loot_crate_engineering") return Copy.Text("工程箱", "Engineering Crate");
            if (identifier == "loot_crate_service") return Copy.Text("服务箱", "Service Crate");
            if (identifier == "loot_crate_medical") return Copy.Text("医疗箱", "Medical Crate");
            if (identifier == "loot_crate_security") return Copy.Text("安保箱", "Security Crate");
            return identifier;
        }

        internal static string Percent(float probability)
        {
            float pct = probability * 100f;
            if (pct < 0.1f) return "<0.1%";
            if (pct < 10f) return pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return Mathf.RoundToInt(pct).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>地图下方那行：当前声望 + 各高级区门槛。</summary>
        private void RefreshReputation()
        {
            if (reputationLabel == null) return;
            string text = Copy.Text("声望 ", "Reputation ") + Reputation.Points;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                if (region.RequiredReputation <= 0) continue;
                text += "  ·  " + region.Name + " " + region.RequiredReputation;
            }
            reputationLabel.SetText(text);
            reputationLabel.SetTextColor(Accent);
        }

        private void PaintPins()
        {
            for (int i = 0; i < pins.Length; ++i)
            {
                Region pinRegion = Region.All[i];
                bool active = pinRegion == selected;
                bool locked = !pinRegion.Unlocked;
                pins[i].SetTextColor(active ? Accent : (locked ? LockedInk : Ink));
                pinRings[i].color = active ? Accent : (locked ? LockedRing : RingInk);
            }
        }

        private void Select(Region region)
        {
            if (current != this) return;
            if (region != null && !region.Unlocked)
            {
                // 锁定区：不选中，只解释门槛，避免“点了没反应”。
                selected = null;
                PaintPins();
                int left = Math.Max(0, ScavHelper.GetScavTimeLeft());
                string need = Copy.Text("需要声望 ", "Needs ") + region.RequiredReputation
                    + Copy.Text(" 才能进入「", " reputation to enter ") + region.Name
                    + Copy.Text("」；当前声望 ", "\"; you have ") + Reputation.Points + "。";
                description.SetText(need + "\n" + Copy.Text("剩余拾荒次数：", "Searches left: ") + left);
                confirm.SetInteractable(false);
                return;
            }

            selected = region;
            PaintPins();

            int remainingSearches = Math.Max(0, ScavHelper.GetScavTimeLeft());
            string remaining = Copy.Text("剩余拾荒次数：", "Searches left: ") + remainingSearches;
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
            if (!selected.Unlocked)
            {
                Select(selected);
                return;
            }

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
                StashFind.ResetTrip();
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
            StashFind.ResetTrip();
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
        private const string KeyCrate = "crate_percent";
        private const string KeyCard = "keycard_percent";
        private const string KeyMinor = "minor_percent";
        private const string KeyMinorValue = "minor_value";
        private const string LogTag = "[DGM-SLIDER] ";

        private static CustomUIWindow window;
        private static CustomUIWindow builtFor;
        private static CustomUIElement junkValue, highValue;
        private static CustomUIElement crateValue, cardValue;
        private static CustomUIElement minorValue;
        private static CustomUIElement minorCrateValue;
        private static CustomUIElement debugValue;
        private static CustomUIElement specialtyDropdown;
        private static CustomUIElement[] evLabels;
        private static CustomUIElement junkSlider, highSlider;
        private static CustomUIElement crateSlider, cardSlider;
        private static CustomUIElement minorSlider;
        private static CustomUIElement minorCrateSlider;
        private static CustomUIElement debugSlider;
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
                || key == KeyCrate || key == KeyCard || key == KeyMinor
                || key == KeyMinorValue
                || key == Tuning.SpecialtyKey
                || key == Tuning.DebugEnabledKey || key == Tuning.DebugPointsKey;
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
                if (buildingKey != KeyJunk && buildingKey != KeyHigh
                    && buildingKey != KeyCrate && buildingKey != KeyCard
                    && buildingKey != KeyMinor && buildingKey != KeyMinorValue
                    && buildingKey != Tuning.DebugPointsKey) return;
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
                // The debug row only prints "19.5", so a narrower label is enough
                // and leaves room next to the 224px slider.
                label.SetFixedWidth(key == Tuning.DebugPointsKey ? 56f : 72f);
                if (label.text != null)
                {
                    label.text.alignment = TextAlignmentOptions.MidlineLeft;
                    label.text.color = Picker.Ink;
                }
                if (key == KeyJunk) junkValue = label;
                else if (key == KeyHigh) highValue = label;
                else if (key == KeyCrate) crateValue = label;
                else if (key == KeyCard) cardValue = label;
                else if (key == KeyMinor) minorValue = label;
                else if (key == KeyMinorValue) minorCrateValue = label;
                else debugValue = label;
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
                else if (key == KeyCrate)
                {
                    crateSlider = HookSlider(window, key, 3);
                    crateValue = EnsureValueLine(b, key, crateValue);
                }
                else if (key == KeyCard)
                {
                    cardSlider = HookSlider(window, key, 4);
                    cardValue = EnsureValueLine(b, key, cardValue);
                }
                else if (key == KeyMinor)
                {
                    minorSlider = HookSlider(window, key, 5);
                    minorValue = EnsureValueLine(b, key, minorValue);
                }
                else if (key == KeyMinorValue)
                {
                    minorCrateSlider = HookSlider(window, key, 6);
                    minorCrateValue = EnsureValueLine(b, key, minorCrateValue);
                }
                else if (key == Tuning.SpecialtyKey)
                {
                    specialtyDropdown = b.Last;
                    HookSpecialtyDropdown(specialtyDropdown, key);
                    AddEvBlock(b);
                }
                else if (key == Tuning.DebugEnabledKey)
                {
                    HookDebugToggle(window, key);
                }
                else if (key == Tuning.DebugPointsKey)
                {
                    debugSlider = HookDebugSlider(window, key);
                    debugValue = EnsureValueLine(b, key, debugValue);
                    if (debugValue != null)
                        debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
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
            b.AddSpace(2);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            PresetButton(b, 3, Copy.Text("默认 200", "Default 200"));
            PresetButton(b, 4, Copy.Text("贴近原版 100", "Vanilla-ish 100"));
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
        /// 调试开关与滑条都常显，不按条件隐藏。原生 BuildEntry 给开关/滑条传的
        /// onChanged 是 null，勾选只改控件、要按「应用」才写回配置；这里自己绑
        /// 事件，让开关即时生效、滑条松手即写入。
        /// </summary>
        private static void HookDebugToggle(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.toggle == null)
            {
                Debug.LogWarning(LogTag + key + ": toggle widget missing");
                return;
            }
            element.OnToggle(delegate(bool value) { Tuning.SetDebugEnabledLive(value); });
            Debug.Log(LogTag + key + ": live listener bound");
        }

        /// <summary>
        /// 原生下拉框的 onChanged 也是 null，换档只改控件、按「应用」才写回配置。
        /// 这里自己绑 OnDropdown，换档立即写配置项并走同一条刷新链路
        /// （Tuning.Invalidated → NativeSettings.RefreshIfOpen → Refresh），
        /// 与滑条拖动、预设按钮完全一致。
        /// </summary>
        private static void HookSpecialtyDropdown(CustomUIElement element, string key)
        {
            if (element == null || element.dropdown == null)
            {
                Debug.LogWarning(LogTag + key + ": dropdown widget missing, live EV refresh disabled");
                return;
            }
            element.OnDropdown(delegate(int index)
            {
                Tuning.SetSpecialtyTierLive(index);
                Debug.Log(LogTag + "ev refresh tier=" + Tuning.SpecialtyTier
                    + " percent=" + Tuning.SpecialtyPercent.ToString("0.#", CultureInfo.InvariantCulture)
                    + " source=dropdown");
            });
            Debug.Log(LogTag + key + ": dropdown bound (options="
                + element.dropdown.options.Count + ")");
        }

        private static CustomUIElement HookDebugSlider(CustomUIWindow target, string key)
        {
            CustomUIElement element = target.Get(key);
            if (element == null || element.slider == null)
            {
                Debug.LogWarning(LogTag + key + ": slider widget missing");
                return null;
            }
            element.OnSlider(delegate(float value)
            {
                float snapped = Mathf.Round(value * 2f) * 0.5f;
                if (Mathf.Abs(snapped - value) > 0.001f) element.SetFloat(snapped, false);
                if (debugValue != null)
                    debugValue.SetText(snapped.ToString("0.0", CultureInfo.InvariantCulture));
            });
            GameObject go = element.slider.gameObject;
            UnityEngine.EventSystems.EventTrigger trigger =
                go.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger.triggers == null)
                trigger.triggers = new List<UnityEngine.EventSystems.EventTrigger.Entry>();
            UnityEngine.EventSystems.EventTrigger.Entry entry =
                new UnityEngine.EventSystems.EventTrigger.Entry();
            entry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            entry.callback.AddListener(delegate(UnityEngine.EventSystems.BaseEventData data)
            {
                CommitDebugSlider();
            });
            trigger.triggers.Add(entry);
            Debug.Log(LogTag + key + ": slider bound (min="
                + element.slider.minValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " max=" + element.slider.maxValue.ToString("0.###", CultureInfo.InvariantCulture)
                + " whole=" + element.slider.wholeNumbers + ")");
            return element;
        }

        /// <summary>松手写入：按 0.5 点吸附，由 Tuning 决定是否因开关关闭而忽略。</summary>
        private static void CommitDebugSlider()
        {
            if (debugSlider == null || debugSlider.slider == null) return;
            float value = debugSlider.slider.value;
            Tuning.SetDebugPointsFromSlider(value);
            // 松手后以真正写入的值为准：开关关闭时会被忽略，标签要退回原值。
            if (debugValue != null)
                debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
            Debug.Log(LogTag + "debug slider release raw="
                + value.ToString("0.###", CultureInfo.InvariantCulture)
                + " entry=" + Tuning.DebugPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " stored=" + Reputation.ExactPoints.ToString("0.##", CultureInfo.InvariantCulture)
                + " hasStore=" + Reputation.HasStore);
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
            if (crateValue != null)
                crateValue.SetText(Tuning.CratePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (cardValue != null)
                cardValue.SetText(Tuning.KeycardPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorValue != null)
                minorValue.SetText(Tuning.MinorPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (minorCrateValue != null)
                minorCrateValue.SetText(Tuning.MinorValue.ToString("0", CultureInfo.InvariantCulture));
            if (debugValue != null)
                debugValue.SetText(Tuning.DebugPoints.ToString("0.0", CultureInfo.InvariantCulture));
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
            else if (kind == 3)
            {
                BeginDrag(3, Tuning.CratePercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(crateSlider, snapped);
                Tuning.SetCratePercentLive(snapped);
                LogSlide(3, value, Tuning.CratePercent);
            }
            else if (kind == 4)
            {
                BeginDrag(4, Tuning.KeycardPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(cardSlider, snapped);
                Tuning.SetKeycardPercentLive(snapped);
                LogSlide(4, value, Tuning.KeycardPercent);
            }
            else if (kind == 5)
            {
                BeginDrag(5, Tuning.MinorPercent);
                float snapped = Mathf.Round(value * 10f) / 10f;
                SyncSlider(minorSlider, snapped);
                Tuning.SetMinorPercentLive(snapped);
                LogSlide(5, value, Tuning.MinorPercent);
            }
            else if (kind == 6)
            {
                BeginDrag(6, Tuning.MinorValue);
                float snapped = Mathf.Round(value / 10f) * 10f;
                SyncSlider(minorCrateSlider, snapped);
                Tuning.SetMinorValueLive(snapped);
                LogSlide(6, value, Tuning.MinorValue);
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
            SyncSlider(crateSlider, Tuning.CratePercent);
            SyncSlider(cardSlider, Tuning.KeycardPercent);
            SyncSlider(minorSlider, Tuning.MinorPercent);
            SyncSlider(minorCrateSlider, Tuning.MinorValue);
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
            crateValue = null;
            cardValue = null;
            minorValue = null;
            minorCrateValue = null;
            debugValue = null;
            specialtyDropdown = null;
            evLabels = null;
            junkSlider = null;
            highSlider = null;
            crateSlider = null;
            cardSlider = null;
            minorSlider = null;
            minorCrateSlider = null;
            debugSlider = null;
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
