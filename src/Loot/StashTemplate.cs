using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 爆大奖的区域样板：按品类分组的奖池、该区匹配的贵重品、当场提示、报纸文案，
    /// 以及中奖次日的价格修正。八个区各一条。
    /// 价格修正按品类挂，可以挂多条（生活废料带同时挂食品与日用品）；
    /// 某件物品若确实没有对应市场类型，就把品类数组留空，只写报纸、不真降价。
    /// </summary>
    internal sealed class StashTemplate
    {
        internal readonly string RegionId;
        internal readonly RewardGroup[] Groups;
        internal readonly string[] ValuableIds;
        /// <summary>与 ValuableIds 一一对应的权重，按单价反比：越便宜权重越大。</summary>
        internal readonly int[] ValuableWeights;
        internal readonly string[] ItemTypes;
        internal readonly int[] PricePercents;
        private readonly string noticeKey;
        private readonly string newsNameKey;
        private readonly string newsDescKey;
        private readonly string displayKey;

        private StashTemplate(string regionId, RewardGroup[] groups,
            string[] valuableIds, int[] valuableWeights,
            string[] itemTypes, int[] pricePercents,
            string noticeKey,
            string newsNameKey, string newsDescKey,
            string displayKey)
        {
            RegionId = regionId;
            Groups = groups;
            ValuableIds = valuableIds;
            ValuableWeights = valuableWeights;
            ItemTypes = itemTypes;
            PricePercents = pricePercents;
            this.noticeKey = noticeKey;
            this.newsNameKey = newsNameKey;
            this.newsDescKey = newsDescKey;
            this.displayKey = displayKey;
        }

        internal string Notice { get { return Copy.Get(noticeKey); } }
        internal string NewsName { get { return Copy.Get(newsNameKey); } }
        internal string NewsDescription { get { return Copy.Get(newsDescKey); } }
        internal string DisplayName { get { return Copy.Get(displayKey); } }

        /// <summary>按池内权重抽 1 件贵重品；权重缺失时退化为均匀抽。</summary>
        internal string PickValuable()
        {
            if (ValuableIds == null || ValuableIds.Length == 0) return null;

            int total = 0;
            if (ValuableWeights != null && ValuableWeights.Length == ValuableIds.Length)
            {
                for (int i = 0; i < ValuableWeights.Length; ++i) total += ValuableWeights[i];
            }
            if (total <= 0) return ValuableIds[UnityEngine.Random.Range(0, ValuableIds.Length)];

            int roll = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < ValuableWeights.Length; ++i)
            {
                roll -= ValuableWeights[i];
                if (roll < 0) return ValuableIds[i];
            }
            return ValuableIds[ValuableIds.Length - 1];
        }

        private static readonly StashTemplate[] All =
        {
            // 贵重品权重一律按单价反比（与材料 / 近战 / 子弹的类内口径一致）：
            // 池内最便宜的一档权重最大，最贵的一两件压到个位数百分比，
            // 所以每区池子的均值都明显低于等权。池子内容不变，只动权重。
            // 入口空地：通用区，拆材料与工具两组（本区这两个大类的非垃圾表）。
            // 贵重品取芯片与高价工具 / 材料，和第 80 档贵重品池里的同类物品对得上。
            new StashTemplate("gate",
                new[]
                {
                    new RewardGroup(3, 4, "scrap_metal", "printer_plastic", "nuts_metal", "wire",
                        "gun_part", "common_electronic", "metal_ingot"),
                    new RewardGroup(3, 4, "screwdriver", "wire_cutter", "welder", "module_extractor",
                        "crowbar", "box_cutter", "hatchet")
                },
                new[] { "rare_electronic", "module_extractor_advanced", "printer_chip_gun_part",
                        "advanced_flux_agent" },
                new[] { 12, 12, 50, 50 },
                new[] { "TOOL" },
                new[] { -20 },
                "stash_gate_notice",
                "stash_gate_news_title",
                "stash_gate_news_body",
                "stash_gate_display"),

            // 拆解堆：整垛原材料为主，少量拆卸工具；贵重加工材料单独抽取。
            new StashTemplate("scrap",
                new[]
                {
                    new RewardGroup(5, 7, "scrap_metal", "printer_plastic", "nuts_metal",
                        "metal_ingot", "kotton_fabric"),
                    new RewardGroup(1, 2, "screwdriver", "wire_cutter", "welder", "crowbar")
                },
                new[] { "printer_chip_combat_machete", "printer_chip_gun_part", "advanced_flux_agent" },
                new[] { 3, 30, 30 },
                new[] { "MATERIAL" },
                new[] { -20 },
                "stash_scrap_notice",
                "stash_scrap_news_title",
                "stash_scrap_news_body",
                "stash_scrap_display"),

            // 生活废料带：食品一组、日用品一组，价格修正仍是 FOOD + HOUSEHOLD_GOOD 两条。
            // 贵重品取净水 / 泉水与正品香烟、酵母反应器，都落在食品或日用一侧。
            new StashTemplate("refuse",
                new[]
                {
                    new RewardGroup(3, 4, "zerochew", "processed_juice", "soda_red", "cat_bar",
                        "processed_milk", "li_eat_snackbar", "energy_drink", "red_beer",
                        "cup_noodle", "galaxy_blend", "processed_meat", "beis_icecream",
                        "nudka", "mini_alcohol_bottle"),
                    new RewardGroup(3, 4, "caffeine_pill", "toothpaste", "water_filter",
                        "toilet_paper", "shampoo", "full_water_test_strip_box", "paper_towel",
                        "full_water_purification_tablet_box", "wine_berry", "wine_yeast",
                        "wine_yeast_red", "wine_superyeast", "packet_red_cigarette_fake")
                },
                new[] { "bottled_water_premium", "water_filter_adv", "portable_water_purifier",
                        "wine_yeast_infinite", "packet_red_cigarette" },
                new[] { 9, 6, 9, 20, 50 },
                new[] { "FOOD", "HOUSEHOLD_GOOD" },
                new[] { -20, -20 },
                "stash_refuse_notice",
                "stash_refuse_news_title",
                "stash_refuse_news_body",
                "stash_refuse_display"),

            // 医疗废弃点：耗材 / 药品 / 器械三组，对应本区三个大类。
            // 贵重品只取医疗向的注射器、蓝血与外科器械；奖池 8 件等仍全部带 MEDICAL，价格修正不变。
            new StashTemplate("med",
                new[]
                {
                    new RewardGroup(2, 3, "bandage_item", "hemostatic_bandage_item",
                        "topical_bandage_item", "blood_bag", "rubbing_alcohol", "salve"),
                    new RewardGroup(2, 3, "fanta_pill", "birth_control_pill", "smelling_salt",
                        "dream_dust", "phagimycin_pill", "zyanide_pill", "nightmare_dust",
                        "large_purple_injector_bad"),
                    new RewardGroup(2, 2, "common_medical", "surgery_tool")
                },
                new[] { "blue_blood_bag", "black_injector", "large_purple_injector", "surgery_tool" },
                new[] { 14, 6, 50, 30 },
                new[] { "MEDICAL" },
                new[] { -20 },
                "stash_med_notice",
                "stash_med_news_title",
                "stash_med_news_body",
                "stash_med_display"),

            // 废武器堆：弹药 / 配件 / 枪支三组，对应本区三个大类。
            // 贵重品取贵重枪支、贵重近战与打印配件芯片，都落在武器这一侧。
            new StashTemplate("weapons",
                new[]
                {
                    new RewardGroup(3, 4, "small_pistol_ammo_nl", "small_pistol_ammo",
                        "heavy_pistol_ammo_nl", "small_pistol_ammo_p", "heavy_pistol_ammo",
                        "lou_etched_round", "heavy_pistol_ammo_p", "rds", "rds_makeshift"),
                    new RewardGroup(2, 3, "mag_22", "silencer_makeshift", "laser_toy",
                        "internal_springs_improved", "mag_22_high", "mag_10mm", "short_stock",
                        "mag_22_ext", "grip", "internal_springs", "bipod", "mag_10mm_high",
                        "mag_10mm_ext", "compensator", "laser", "trigger_group", "stock",
                        "mag_10mm_smg", "silencer", "barrel", "mag_10mm_smg_high", "ammo_display",
                        "grip_gel", "match_barrel", "mag_10mm_smg_box", "silencer2", "laser2"),
                    new RewardGroup(2, 2, "printed_gun", "handmade_pistol", "heavy_handmade_pistol",
                        "glock_receiver", "revolver", "stun_gun", "smg", "shotgun")
                },
                new[] { "revolver", "stun_gun", "smg", "shotgun", "glock_receiver",
                        "combat_machete", "stun_baton", "printer_chip_10_mag_ext",
                        "printer_chip_gun_part" },
                new[] { 18, 16, 10, 5, 30, 40, 50, 3, 6 },
                new[] { "AMMUNITION", "FIREARM", "GUN_MOD" },
                new[] { -20, -20, -20 },
                "stash_weapons_notice",
                "stash_weapons_news_title",
                "stash_weapons_news_body",
                "stash_weapons_display"),

            // 电子垃圾坡：电子件 / 工具 / 模组三组，对应本区三个非垃圾大类。
            // 贵重品偏电子与模组；价格修正只挂已存在的 MATERIAL / TOOL。
            new StashTemplate("electronics",
                new[]
                {
                    new RewardGroup(3, 4, "common_electronic", "wire", "flux_agent"),
                    new RewardGroup(1, 2, "screwdriver", "wire_cutter", "module_extractor"),
                    new RewardGroup(2, 3, "system_module_ruined", "system_module_corrupt",
                        "blank_module", "random_performance_module",
                        "random_efficiency_module", "random_quality_module",
                        "system_module_performance")
                },
                new[] { "rare_electronic", "module_extractor_advanced", "module_extractor" },
                new[] { 18, 18, 60 },
                new[] { "MATERIAL", "TOOL" },
                new[] { -20, -20 },
                "stash_electronics_notice",
                "stash_electronics_news_title",
                "stash_electronics_news_body",
                "stash_electronics_display"),

            // 报废车场：工具箱为主，少量机械材料与零件，贵重工具单独抽取。
            new StashTemplate("vehicles",
                new[]
                {
                    new RewardGroup(1, 2, "scrap_metal", "nuts_metal", "wire", "metal_ingot"),
                    new RewardGroup(4, 5, "screwdriver", "wire_cutter", "welder",
                        "turbo_booster", "box_cutter", "crowbar"),
                    new RewardGroup(1, 2, "gun_part", "internal_springs")
                },
                new[] { "module_extractor_advanced", "module_extractor", "printer_chip_gun_part" },
                new[] { 18, 60, 35 },
                new[] { "MATERIAL", "TOOL" },
                new[] { -20, -20 },
                "stash_vehicles_notice",
                "stash_vehicles_news_title",
                "stash_vehicles_news_body",
                "stash_vehicles_display"),

            // 废泵站：批量奖励只给净水耗材、检测用品和设备，不给水容器。
            new StashTemplate("water",
                new[]
                {
                    new RewardGroup(3, 4, WaterSupplyPack.HalfTablets, "full_water_purification_tablet_box"),
                    new RewardGroup(3, 4, "hydroreed_filter", "water_filter", "uv_filter"),
                    new RewardGroup(2, 3, WaterSupplyPack.HalfTest, "full_water_test_strip_box")
                },
                new[] { "water_filter_adv", "portable_water_purifier" },
                new[] { 15, 18 },
                new[] { "HOUSEHOLD_GOOD" },
                new[] { -20 },
                "stash_water_notice",
                "stash_water_news_title",
                "stash_water_news_body",
                "stash_water_display")
        };

        internal static StashTemplate For(string regionId)
        {
            if (string.IsNullOrEmpty(regionId)) return null;
            for (int i = 0; i < All.Length; ++i)
            {
                if (All[i].RegionId == regionId) return All[i];
            }
            return null;
        }
    }
}
