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

        /// <summary>
        /// 一个大类：Share 是它在整张表里的固定权重（一张表按 10000 计），
        /// 类内 Items/Weights 只决定彼此之间的相对比例。以后其它区要改成
        /// 「大类 → 子项」时，直接复用这套结构即可。
        /// </summary>
        private sealed class Category
        {
            internal readonly int Share;
            internal readonly string[] Items;
            internal readonly int[] Weights;

            internal Category(int share, string[] items, int[] weights)
            {
                Share = share;
                Items = items;
                Weights = weights;
            }
        }

        private static Category Cat(int share, string[] items, int[] weights)
        {
            return new Category(share, items, weights);
        }

        /// <summary>
        /// 把若干大类摊平成一张表：每类先按 Share 定总量，再按类内权重分给子项，
        /// 取整误差补给类内权重最大的一项。namesZh / namesEn 按大类顺序给出显示名，
        /// 告示牌用它们分组。
        /// </summary>
        private static RegionLootTable BuildCategories(string[] namesZh, string[] namesEn,
            params Category[] categories)
        {
            Builder b = new Builder();
            List<int> itemCategory = new List<int>();
            for (int c = 0; c < categories.Length; ++c)
            {
                Category cat = categories[c];
                int sum = 0;
                int largest = 0;
                for (int i = 0; i < cat.Weights.Length; ++i)
                {
                    sum += cat.Weights[i];
                    if (cat.Weights[i] > cat.Weights[largest]) largest = i;
                }

                int[] weights = new int[cat.Items.Length];
                int assigned = 0;
                for (int i = 0; i < cat.Items.Length; ++i)
                {
                    weights[i] = (int)Math.Round(cat.Share * (double)cat.Weights[i] / sum,
                        MidpointRounding.AwayFromZero);
                    if (weights[i] < 1) weights[i] = 1;
                    assigned += weights[i];
                }
                weights[largest] += cat.Share - assigned;

                for (int i = 0; i < cat.Items.Length; ++i)
                {
                    b.Add(cat.Items[i], weights[i]);
                    itemCategory.Add(c);
                }
            }
            RegionLootTable table = b.Build();
            return new RegionLootTable(table.Items, table.Weights, namesZh, namesEn,
                itemCategory.ToArray());
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
        /// 每区的主题表，全部按「大类 → 子项」两层定义：Share 是大类在整表
        /// （10000 份）里的份额，类内权重只决定彼此的相对比例。
        ///
        /// 两条固定规则：
        /// 1. 垃圾份额沿用各区原有基准，不搞一刀切（入口 48.75%、拆解 59.89%、
        ///    生活 47.67%、医疗 46.47%、废武器 50%），这样专精与垃圾倍率的
        ///    插值结果和以前一致。
        /// 2. 类内权重与单价成反比：同一大类里单价越低权重越大，单价越高越靠后，
        ///    避免单一高价物品概率过高、也让 EV 不再随高价件膨胀。
        ///    材料是唯一有额外系数的：按枪匠 3D 打印机 / 机床芯片的实际消耗量
        ///    调整后，废金属、3D 打印耗材最高，线缆、金属螺丝压低
        ///    （明细见 docs/掉落权重与期望收益.md）。
        /// </summary>
        private static Dictionary<string, RegionLootTable> BuildThemes()
        {
            Dictionary<string, RegionLootTable> result = new Dictionary<string, RegionLootTable>();

            // 入口空地：通用杂项，保持原版的构成比例（垃圾 48.75%）。
            result["gate"] = BuildCategories(
                new[] { "垃圾", "材料", "食品", "日用", "医疗", "近战", "工具", "杂项" },
                new[] { "Junk", "Materials", "Food", "Household", "Medical", "Melee", "Tools", "Misc" },
                Cat(4875, new[] { "junk" }, new[] { 1 }),
                Cat(1750,
                    new[] { "scrap_metal", "printer_plastic", "nuts_metal", "wire", "gun_part",
                        "common_electronic", "metal_ingot" },
                    new[] { 140, 120, 20, 18, 12, 8, 4 }),
                Cat(1000,
                    new[] { "zerochew", "processed_juice", "soda_red", "cat_bar", "processed_milk",
                        "li_eat_snackbar", "energy_drink", "red_beer", "cup_noodle", "galaxy_blend",
                        "processed_meat", "beis_icecream", "nudka" },
                    new[] { 75, 43, 40, 40, 38, 35, 33, 33, 30, 24, 24, 15, 7 }),
                Cat(850,
                    new[] { "caffeine_pill", "toothpaste", "water_filter", "toilet_paper", "shampoo",
                        "full_water_test_strip_box", "paper_towel", "full_water_purification_tablet_box" },
                    new[] { 60, 40, 24, 24, 20, 19, 17, 11 }),
                Cat(450,
                    new[] { "bandage_item", "hemostatic_bandage_item", "topical_bandage_item" },
                    new[] { 75, 38, 38 }),
                Cat(500,
                    new[] { "glass_shard_shiv", "pipe_weapon", "box_cutter", "kitchen_knife" },
                    new[] { 75, 40, 21, 13 }),
                Cat(350,
                    new[] { "screwdriver", "wire_cutter", "welder", "module_extractor" },
                    new[] { 24, 15, 8, 6 }),
                Cat(225,
                    new[] { "glass_shard", "empty_beer_bottle", "scratcher", "system_module_ruined", "turbo_booster" },
                    new[] { 300, 120, 30, 20, 17 }));

            // 拆解堆：材料 / 工具 / 模组，垃圾比例最高（59.89%）。
            result["scrap"] = BuildCategories(
                new[] { "垃圾", "材料", "工具", "模组", "杂项" },
                new[] { "Junk", "Materials", "Tools", "Modules", "Misc" },
                Cat(5989, new[] { "junk" }, new[] { 1 }),
                Cat(2900,
                    new[] { "scrap_metal", "printer_plastic", "nuts_metal", "wire", "gun_part",
                        "common_electronic", "metal_ingot" },
                    new[] { 140, 120, 20, 18, 12, 8, 4 }),
                Cat(520,
                    new[] { "screwdriver", "turbo_booster", "wire_cutter", "welder", "module_extractor" },
                    new[] { 24, 17, 15, 8, 6 }),
                Cat(400,
                    new[] { "system_module_ruined", "system_module_corrupt", "blank_module",
                        "random_performance_module", "random_efficiency_module", "random_quality_module",
                        "system_module_performance", "system_module_eco", "system_module_fineness" },
                    new[] { 20, 13, 10, 9, 9, 9, 9, 6, 6 }),
                Cat(191,
                    new[] { "glass_shard", "crowbar", "food_stamp_fake" },
                    new[] { 300, 8, 3 }));

            // 生活废料带：食品 / 日用品 / 便宜药品，垃圾 47.67%。
            result["refuse"] = BuildCategories(
                new[] { "垃圾", "食品", "日用", "医疗", "空瓶" },
                new[] { "Junk", "Food", "Household", "Medical", "Bottles" },
                Cat(4767, new[] { "junk" }, new[] { 1 }),
                Cat(2600,
                    new[] { "zerochew", "processed_juice", "soda_red", "cat_bar", "processed_milk",
                        "li_eat_snackbar", "energy_drink", "red_beer", "cup_noodle", "galaxy_blend",
                        "processed_meat", "beis_icecream", "nudka", "mini_alcohol_bottle" },
                    new[] { 75, 43, 40, 40, 38, 35, 33, 33, 30, 24, 24, 15, 7, 3 }),
                Cat(1800,
                    new[] { "caffeine_pill", "toothpaste", "water_filter", "toilet_paper", "shampoo",
                        "full_water_test_strip_box", "paper_towel", "full_water_purification_tablet_box",
                        "wine_berry", "wine_yeast", "wine_yeast_red", "wine_superyeast",
                        "packet_red_cigarette_fake" },
                    new[] { 60, 40, 24, 24, 20, 19, 17, 11, 10, 12, 15, 5, 8 }),
                Cat(500,
                    new[] { "bandage_item", "fanta_pill", "birth_control_pill", "smelling_salt",
                        "hemostatic_bandage_item", "topical_bandage_item", "dream_dust",
                        "large_purple_injector_bad" },
                    new[] { 75, 50, 40, 40, 38, 38, 33, 8 }),
                Cat(333, new[] { "empty_beer_bottle", "wine_bottle" }, new[] { 3, 1 }));

            // 医疗废弃点：医疗耗材 / 药品 / 器械，垃圾 46.47%。
            result["med"] = BuildCategories(
                new[] { "垃圾", "医疗耗材", "药品", "器械" },
                new[] { "Junk", "Medical supplies", "Medicine", "Instruments" },
                Cat(4647, new[] { "junk" }, new[] { 1 }),
                Cat(3200,
                    new[] { "bandage_item", "hemostatic_bandage_item", "topical_bandage_item",
                        "blood_bag", "rubbing_alcohol", "salve" },
                    new[] { 75, 38, 38, 18, 17, 8 }),
                Cat(2000,
                    new[] { "fanta_pill", "birth_control_pill", "smelling_salt", "dream_dust",
                        "phagimycin_pill", "zyanide_pill", "nightmare_dust",
                        "large_purple_injector_bad" },
                    new[] { 50, 40, 40, 33, 24, 18, 17, 6 }),
                Cat(153,
                    new[] { "common_medical", "surgery_tool" },
                    new[] { 15, 3 }));

            // 废武器堆：垃圾 5000、材料 2000、近战 1500、子弹 480、配件 520、枪支 500。
            // 子弹与配件名义上各 5%，把 20 份从子弹挪给配件，让配件总量略高于子弹。
            result["weapons"] = BuildCategories(
                new[] { "垃圾", "材料", "近战", "子弹", "配件", "枪支" },
                new[] { "Junk", "Materials", "Melee", "Ammo", "Gun mods", "Guns" },
                Cat(5000, new[] { "junk" }, new[] { 1 }),
                // 材料：按枪匠配方，废金属与 3D 打印耗材最高，线缆、金属螺丝压低。
                Cat(2000,
                    new[] { "scrap_metal", "printer_plastic", "nuts_metal", "wire", "gun_part",
                        "common_electronic", "metal_ingot" },
                    new[] { 140, 120, 20, 18, 12, 8, 4 }),
                // 近战：玻璃刀、铅管、美工刀等便宜常见品最多，贵重近战在尾部。
                Cat(1500,
                    new[] { "glass_shard_shiv", "pipe_weapon", "box_cutter", "kitchen_knife", "hatchet",
                        "kitchen_cleaver", "crowbar", "combat_knife", "skinning_knife", "stun_baton",
                        "combat_machete" },
                    new[] { 30, 26, 20, 14, 10, 8, 6, 5, 3, 2, 1 }),
                // 子弹：先归一个大类，再在类内按单价反比抽具体弹种。
                Cat(480,
                    new[] { "small_pistol_ammo_nl", "small_pistol_ammo", "heavy_pistol_ammo_nl",
                        "small_pistol_ammo_p", "heavy_pistol_ammo", "lou_etched_round",
                        "heavy_pistol_ammo_p" },
                    new[] { 30, 24, 20, 18, 14, 13, 10 }),
                // 配件：便宜的弹匣、消音器、激光笔最常见，高价件只作点缀。
                Cat(520,
                    new[] { "mag_22", "silencer_makeshift", "laser_toy", "internal_springs_improved",
                        "mag_22_high", "rds_makeshift", "mag_10mm", "short_stock", "mag_22_ext", "grip",
                        "internal_springs", "bipod", "mag_10mm_high", "mag_10mm_ext", "compensator",
                        "laser", "trigger_group", "stock", "mag_10mm_smg", "silencer", "rds", "barrel",
                        "mag_10mm_smg_high", "ammo_display", "grip_gel", "match_barrel",
                        "mag_10mm_smg_box", "silencer2", "laser2" },
                    new[] { 40, 40, 33, 30, 24, 24, 20, 17, 17, 15, 15, 15, 15, 12, 12, 12, 12, 15,
                        11, 11, 10, 10, 9, 9, 9, 8, 7, 7, 6 }),
                // 枪支：打印枪最常见，土枪次之，冲锋枪与霰弹枪最稀有。
                Cat(500,
                    new[] { "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "glock_receiver",
                        "revolver", "stun_gun", "smg", "shotgun" },
                    new[] { 40, 22, 14, 9, 6, 4, 3, 2 }));

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
                "full_water_test_strip_box", "full_water_purification_tablet_box",
                "packet_red_cigarette", "food_stamp", "mini_bottle", "wine_bottle",
                "wine_berry", "wine_yeast", "wine_yeast_red", "wine_superyeast", "wine_yeast_infinite");
            AddAll(result, "makeshiftWeaponTable",
                "glass_shard_shiv", "box_cutter", "kitchen_knife",
                "combat_knife", "kitchen_cleaver", "hatchet", "skinning_knife", "stun_baton", "combat_machete");
            AddAll(result, "gunTable",
                "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "glock_receiver",
                "revolver", "stun_gun", "smg", "shotgun");
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
                "nightmare_dust", "common_medical", "large_purple_injector");
            // 贵重品池里的物品在开启前不会出现，这里只是给 ModHook 报一个合理的原版表名。
            AddAll(result, "householdTable",
                "water_filter_adv", "portable_water_purifier", "bottled_water_premium");
            AddAll(result, "dumpingGroundMedical", "blue_blood_bag", "black_injector");
            AddAll(result, "materialTable", "rare_electronic", "advanced_flux_agent");
            AddAll(result, "toolTable", "module_extractor_advanced");
            AddAll(result, "t2moduleTable",
                "printer_chip_pistol_double", "printer_chip_heavy_handmade_pistol_frame",
                "printer_chip_combat_machete", "printer_chip_gun_part", "printer_chip_10_mag_ext");
            return result;
        }

        private static void AddAll(Dictionary<string, string> target, string tableId, params string[] items)
        {
            for (int i = 0; i < items.Length; ++i) target[items[i]] = tableId;
        }
    }
}
