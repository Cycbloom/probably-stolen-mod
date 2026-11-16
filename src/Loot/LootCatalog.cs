using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal static class LootCatalog
    {
        /// <summary>
        /// 80档区域补给池的主箱型；实际分布使用SupplyPool的全部条目。
        /// 用 PreBuiltItemHelper 的 loot_crate_* id，ItemSpawner 会自动走预构建满箱。
        /// </summary>
        internal static string CrateFor(string regionId)
        {
            SupplyEntry[] entries = SupplyPool.For(regionId);
            return entries.Length > 0 ? entries[0].CrateId : null;
        }

        /// <summary>80 档：与箱子同区出现的对应钥匙卡。</summary>
        internal static string CardFor(string regionId)
        {
            SupplyEntry[] entries = SupplyPool.For(regionId);
            return entries.Length > 0 ? entries[0].CardId : null;
        }

        internal static float ValueOf(string itemId)
        {
            float value;
            return UnitValues.TryGetValue(itemId, out value) ? value : 0f;
        }

        internal static float ExpectedValueOf(string itemId, string regionId)
        {
            float value = ValueOf(itemId);
            if (regionId == "water" && PumpWater.IsContainer(itemId)) return PumpWater.ExpectedValue(itemId, value);
            if (itemId == HydraUsage.ItemId) return (124f + 176f + 325f) / 3f;
            return WeaponWear.ExpectedValue(itemId, value);
        }

        /// <summary>
        /// 贵重品池：原版没有单独的索引，这里是本模组自己拼的一池稀有物品，
        /// 分四类——芯片类（打印枪、弹匣、近战的打印芯片）、贵重枪支、
        /// 贵重刀具 / 近战、枪匠需要的高价材料与工具，另有原来那 7 件。
        /// 与游戏自带的「奢侈品 / LUXURY_ITEM」类别不是一回事：那一类是
        /// 买卖与展览用的奢侈品标签，本池只是拾荒时按概率抽取的物品清单。
        /// 它们不进基础表，只有贵重品出现率大于 0 且声望达到 80 时才进最终表。
        /// </summary>
        internal static readonly ValuableDefinition[] Valuables =
        {
            // 顺序固定：gate, scrap, refuse, med, weapons, electronics, vehicles, water。
            // 前五列是既有值，逐字保留；后三列是新区域的映射，全部大于 0。
            new ValuableDefinition("blue_blood_bag", 2, 1, 3, 4, 1, 1, 1, 1),
            new ValuableDefinition("water_filter_adv", 2, 1, 3, 1, 2, 1, 1, 5),
            new ValuableDefinition("portable_water_purifier", 1, 1, 3, 1, 2, 1, 1, 5),
            new ValuableDefinition("bottled_water_premium", 2, 1, 3, 1, 2, 1, 1, 5),
            new ValuableDefinition("black_injector", 1, 1, 1, 5, 3, 1, 1, 1),
            new ValuableDefinition("rare_electronic", 1, 3, 1, 1, 1, 5, 3, 1),
            new ValuableDefinition("module_extractor_advanced", 1, 4, 1, 1, 3, 5, 5, 2),
            new ValuableDefinition("printer_chip_pistol_double", 1, 2, 1, 1, 2, 2, 2, 1),
            new ValuableDefinition("printer_chip_heavy_handmade_pistol_frame", 1, 2, 1, 1, 2, 2, 2, 1),
            new ValuableDefinition("printer_chip_combat_machete", 1, 2, 1, 1, 3, 2, 2, 1),
            new ValuableDefinition("printer_chip_gun_part", 1, 3, 1, 2, 2, 4, 4, 1),
            new ValuableDefinition("printer_chip_10_mag_ext", 1, 2, 1, 1, 3, 3, 3, 1),
            new ValuableDefinition("revolver", 2, 1, 1, 1, 3, 1, 1, 1),
            new ValuableDefinition("stun_gun", 2, 1, 1, 1, 3, 1, 1, 1),
            new ValuableDefinition("smg", 1, 1, 1, 1, 2, 1, 1, 1),
            new ValuableDefinition("shotgun", 1, 1, 1, 1, 2, 1, 1, 1),
            new ValuableDefinition("combat_machete", 2, 3, 2, 3, 4, 1, 2, 1),
            new ValuableDefinition("stun_baton", 2, 3, 1, 2, 3, 1, 2, 1),
            new ValuableDefinition("advanced_flux_agent", 1, 2, 1, 1, 2, 4, 3, 1),
            new ValuableDefinition("large_purple_injector", 1, 1, 2, 4, 1, 1, 1, 1),
            new ValuableDefinition("glock_receiver", 1, 1, 1, 1, 3, 1, 1, 1),
            new ValuableDefinition("heavy_handmade_pistol", 1, 1, 1, 1, 3, 1, 1, 1),
            new ValuableDefinition("surgery_tool", 1, 1, 1, 4, 1, 1, 1, 1),
            new ValuableDefinition("module_extractor", 1, 3, 1, 1, 1, 4, 5, 2),
            new ValuableDefinition("packet_red_cigarette", 1, 1, 3, 1, 1, 1, 1, 1),
            new ValuableDefinition("wine_yeast_infinite", 1, 2, 1, 1, 1, 1, 1, 1),
        };

        /// <summary>正品免疫宁：贵重品槽位固定给这一件，次品走 InjectorBadId。</summary>
        internal const string InjectorId = "large_purple_injector";

        /// <summary>普通表里的免疫宁次品（过期 / 仿品 / 不能用），生成时再掷具体状态。</summary>
        internal const string InjectorBadId = "large_purple_injector_bad";

        /// <summary>正品海明威香烟（贵重品槽位）。</summary>
        internal const string CigaretteId = "packet_red_cigarette";

        /// <summary>普通表里的假海明威香烟。</summary>
        internal const string CigaretteFakeId = "packet_red_cigarette_fake";

        /// <summary>普通表里的仿品食品券。</summary>
        internal const string StampFakeId = "food_stamp_fake";

        /// <summary>小瓶酒精：不是目录 id，要调 PreBuildChemHelper 现做。</summary>
        internal const string MiniAlcoholId = "mini_alcohol_bottle";

        /// <summary>贵重品池（按展示顺序）；告示牌用它拼「贵重品」分组。</summary>
        internal static string[] HighValueIds
        {
            get { return Array.ConvertAll(Valuables, entry => entry.Id); }
        }

        internal static bool IsHighValue(string itemId)
        {
            return Array.Exists(Valuables, entry => entry.Id == itemId);
        }

        /// <summary>
        /// Per-area valuable mix, in Region.All order (gate, scrap, refuse,
        /// medical, weapons, electronics, vehicles, water).
        /// 每个区都能掉到池里的全部四类，只是权重偏向本区主题。
        /// </summary>


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
                { WaterSupplyPack.HalfTest, 16f },
                { WaterSupplyPack.HalfTablets, 31f },
                // 废泵站普通表：饮水、净水用品与容器；数值取游戏 unitValue。
                { "bottled_water", 5f },
                { "small_bottled_water", 4f },
                { "large_bottled_water", 9f },
                { "water_jug", 15f },
                { "water_ration", 30f },
                { "water_ration_small", 15f },
                { "water_tablet", 6f },
                { "water_test_strip", 3f },
                { "water_tablet_container", 1f },
                { "water_test_container", 1f },
                { "hydroreed_filter", 20f },
                { "uv_filter", 150f },
                { "mini_bottle", 3f },
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
                { "glock_receiver", 200f },
                { "revolver", 240f },
                { "stun_gun", 250f },
                { "smg", 280f },
                { "shotgun", 310f },
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
                { "module_extractor_advanced", 350f },
                // 贵重品池新增：芯片、贵重枪械、贵重近战、枪匠高价材料与工具。
                { "printer_chip_pistol_double", 400f },
                { "printer_chip_heavy_handmade_pistol_frame", 450f },
                { "printer_chip_combat_machete", 480f },
                { "printer_chip_gun_part", 300f },
                { "printer_chip_10_mag_ext", 400f },
                { "advanced_flux_agent", 300f },
                // 0.3.65：正品免疫宁 160；次品掷过期 80 / 仿品 40 / 不能用 16，按 40/35/25 取均值。
                { "large_purple_injector", 160f },
                { "large_purple_injector_bad", 50f },
                // 0.3.66：烟、酒瓶与酿酒原料；小瓶酒精按瓶 + 酒精液体估 25。
                // 0.3.89：香烟改由游戏真伪条件定价，正品 100、赝品 50。
                { "packet_red_cigarette", 100f },
                { "packet_red_cigarette_fake", 50f },
                { "food_stamp_fake", 3f },
                { "mini_alcohol_bottle", 25f },
                { "wine_bottle", 12f },
                { "wine_berry", 14f },
                { "wine_yeast", 10f },
                { "wine_yeast_red", 8f },
                { "wine_superyeast", 20f },
                { "wine_yeast_infinite", 250f },
                // 80 档补给箱按满箱内容估的均价，只用于界面的期望价值。
                { "loot_crate_engineering", 450f },
                { "loot_crate_service", 350f },
                { "loot_crate_medical", 500f },
                { "loot_crate_security", 600f },
                { "eng_keycard", 75f },
                { "ser_keycard", 40f },
                { "med_keycard", 75f },
                { "sec_keycard", 100f }
            };

    }
}
