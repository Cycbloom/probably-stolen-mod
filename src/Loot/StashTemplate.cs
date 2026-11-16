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
        private readonly string noticeZh;
        private readonly string noticeEn;
        private readonly string newsNameZh;
        private readonly string newsNameEn;
        private readonly string newsDescZh;
        private readonly string newsDescEn;
        private readonly string displayZh;
        private readonly string displayEn;

        private StashTemplate(string regionId, RewardGroup[] groups,
            string[] valuableIds, int[] valuableWeights,
            string[] itemTypes, int[] pricePercents,
            string noticeZh, string noticeEn,
            string newsNameZh, string newsNameEn, string newsDescZh, string newsDescEn,
            string displayZh, string displayEn)
        {
            RegionId = regionId;
            Groups = groups;
            ValuableIds = valuableIds;
            ValuableWeights = valuableWeights;
            ItemTypes = itemTypes;
            PricePercents = pricePercents;
            this.noticeZh = noticeZh;
            this.noticeEn = noticeEn;
            this.newsNameZh = newsNameZh;
            this.newsNameEn = newsNameEn;
            this.newsDescZh = newsDescZh;
            this.newsDescEn = newsDescEn;
            this.displayZh = displayZh;
            this.displayEn = displayEn;
        }

        internal string Notice { get { return Copy.Text(noticeZh, noticeEn); } }
        internal string NewsName { get { return Copy.Text(newsNameZh, newsNameEn); } }
        internal string NewsDescription { get { return Copy.Text(newsDescZh, newsDescEn); } }
        internal string DisplayName { get { return Copy.Text(displayZh, displayEn); } }

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
                new[] { "TOOL" }, new[] { -20 },
                "你在{0}翻出一柜工具和材料！\n今天继续在本区拾荒，每次多得1件物品。",
                "Tools and materials {0}!\nSearch here again today: +1 item per search.",
                "门口空地挖出的工具箱和料垛",
                "A Tool Chest and a Stack of Stock at the Main Gate",
                "有人昨夜在入口空地的垃圾堆里拖出一只整箱工具，底下还压着成捆的材料和几块值钱的芯片。五金铺老板今早脸色不太好——今天工具和材料多半都要便宜。",
                "Someone dragged a chest of tools out of the trash by the main gate last night, with bundles of stock and a few pricey chips still underneath. The hardware dealer was looking sour this morning; tools and materials are likely to go cheap today.",
                "工具倾销", "Tool Surplus"),

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
                new[] { "MATERIAL" }, new[] { -20 },
                "你在{0}翻出整垛原材料和拆卸工具！\n今天继续在本区拾荒，每次多得1件物品。",
                "Raw stock and dismantling tools {0}!\nSearch here again today: +1 item per search.",
                "拆解堆出了整垛料和几箱工具",
                "Raw Stock and Tools From the Scrap Pile",
                "拆解堆昨夜有人从塌掉的夹层里拖出整垛还没回炉的金属、塑料和成卷布料，外加几把拆卸工具和加工用的值钱货。消息传开，收料的开价一个比一个低，今天材料怕是要贱卖。",
                "Someone hauled stacks of unmelted metal, plastic and fabric out of a collapsed cavity in the scrap pile, with dismantling tools and valuable manufacturing supplies. Buyers are undercutting each other; materials will likely sell cheap today.",
                "材料过剩", "Material Glut"),

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
                new[] { "FOOD", "HOUSEHOLD_GOOD" }, new[] { -20, -20 },
                "你在{0}翻出整车食品和日用品！\n今天继续在本区拾荒，每次多得1件物品。",
                "A cart of food and supplies {0}!\nSearch here again today: +1 item per search.",
                "废料带里翻出的年货",
                "A Holiday Haul in the Refuse Lane",
                "有拾荒客说，昨晚在生活废料带里翻出了一整车没拆封的食品和日用品，还夹着几瓶好水、一条正品烟和别的值钱货。货一到早市，摊主们就抢着压价，今天这两类东西恐怕都不值钱了。",
                "A scavenger claims to have hauled a whole cart of sealed food and household goods out of the refuse lane last night, along with a few bottles of good water, a genuine pack of cigarettes and other valuables. By the time it reached the morning market, stallholders were already undercutting each other — both are likely to go cheap today.",
                "食品日用品涌入", "Food and Household Surplus"),

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
                new[] { "MEDICAL" }, new[] { -20 },
                "你在{0}找到一批药品和医疗器械！\n今天继续在本区拾荒，每次多得1件物品。",
                "A stash of medical supplies {0}!\nSearch here again today: +1 item per search.",
                "医疗废弃点的整箱药品上了早市",
                "Crates of Medicine Reach the Morning Market",
                "医疗废弃点昨夜有人拖出成箱还没过期的绷带、药片和手术器械，连几支值钱的针剂也一并流了出来。天没亮就摆上了早市，开价压得比药铺进货还低。今天医疗用品怕是要跟着一起跌。",
                "Someone dragged crates of still-unexpired bandages, pills and surgical instruments out of the medical dump last night, and a few pricey injectors went with them. They were on the morning market before dawn, priced below what the pharmacy pays wholesale. Medical supplies are likely to fall with them today.",
                "医疗物资倾销", "Medical Supplies Dumped"),

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
                new[] { "AMMUNITION", "FIREARM", "GUN_MOD" }, new[] { -20, -20, -20 },
                "你在{0}翻出枪支、弹药和配件！\n今天继续在本区拾荒，每次多得1件物品。",
                "Guns, ammo and gun mods {0}!\nSearch here again today: +1 item per search.",
                "废武器堆的军械箱",
                "An Arms Crate in the Weapon Dump",
                "有人昨夜在废武器堆撬开一只军械箱，抱走成把的子弹和两三支还能打响的枪，连枪上的配件和几件值钱货都拆下来带走了。枪铺今天大概要被迫让价。",
                "Someone cracked open an arms crate in the weapon dump last night and walked off with fistfuls of rounds, two or three working guns, and the mods and valuables stripped off them. The gun shop will likely be forced to cut prices today.",
                "军械流入市场", "Arms Flood the Market"),

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
                new[] { "MATERIAL", "TOOL" }, new[] { -20, -20 },
                "你在{0}翻出整箱电子件和模组！\n今天继续在本区拾荒，每次多得1件物品。",
                "A crate of electronics and modules {0}!\nSearch here again today: +1 item per search.",
                "电子垃圾坡的整箱元件",
                "A Crate of Components at the E-Waste Slope",
                "有人昨夜在电子垃圾坡的烧穿配电柜后面拖出整箱还能用的电子元件和几块完好模组，连工具也一并带走。货一早上了摊，今天电子件和工具多半要一起跌。",
                "Someone dragged a crate of still-working components and a few intact modules out from behind a burnt-out switchgear cabinet on the e-waste slope last night, tools included. The lot hit the stalls at dawn, so electronics and tools will likely both go cheap today.",
                "电子件倾销", "Electronics Surplus"),

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
                new[] { "MATERIAL", "TOOL" }, new[] { -20, -20 },
                "你在{0}拆出一车工具和金属件！\n今天继续在本区拾荒，每次多得1件物品。",
                "A cart of tools and metal parts {0}!\nSearch here again today: +1 item per search.",
                "报废车场拆出的整箱工具",
                "A Tool Chest From the Vehicle Graveyard",
                "报废车场昨夜有人把一辆压扁的车壳整个拆开，拖出成箱工具、整捆金属件和几块还能用的零件。废料商和五金铺同时收到货，今天材料与工具怕是要一起掉价。",
                "Someone cut open a crushed car shell at the vehicle graveyard last night and hauled out a chest of tools, bundles of metal parts, and a few salvageable components. The scrap dealers and the hardware shop both got stock at once; materials and tools will likely fall together today.",
                "拆车件涌入", "Salvage Flood"),

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
                new[] { "HOUSEHOLD_GOOD" }, new[] { -20 },
                "你在{0}翻出整箱净水用品！\n今天继续在本区拾荒，每次多得1件物品。",
                "A crate of water supplies {0}!\nSearch here again today: +1 item per search.",
                "废泵站的整箱净水物资",
                "A Crate of Water Supplies at the Pump Station",
                "废泵站昨夜有人从锈死的泵房里拖出整箱净水药片、滤芯和水质检测用品，还找到了能用的净水设备。消息传开后，日用品摊主今早都在压价。",
                "Someone dragged crates of purification tablets, filter cartridges, and water-testing supplies out of a rusted-shut pump house last night, along with working purification equipment. Word spread fast, and the household stalls were undercutting each other this morning.",
                "净水物资涌入", "Water Supplies Flood In")
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
