using System;
using System.Collections.Generic;

namespace DumpingGroundMap
{
    internal static class ScavLoot
    {
        private static LootDataSnapshot current = LootDataSnapshot.Bundled();
        private static readonly Dictionary<string, string> sourceTables = BuildSourceTables();
        internal static int Revision { get; private set; }
        internal static RegionLootTable Vanilla { get { return current.Vanilla; } }

        internal static RegionLootTable Get(string regionId)
        {
            RegionLootTable table;
            return current.Regions.TryGetValue(regionId, out table) ? table : current.Vanilla;
        }

        internal static string SourceTable(string itemId)
        {
            string table;
            return sourceTables.TryGetValue(itemId, out table) ? table : "junkTable";
        }

        internal static void OnTablesLoaded()
        {
            if (!Core.Enabled) return;
            LootDataSnapshot next;
            bool native = true;
            try { next = LootDataSnapshot.Capture(ModHelper.GetLootEntries, ModHelper.GetLootGroupEntries); }
            catch (Exception error)
            {
                next = LootDataSnapshot.Bundled();
                native = false;
                ModLogger.Warning("[loot] invalid or missing JSON tables; using bundled defaults: " + error.Message);
            }
            Apply(next);
            ModLogger.Info("[loot] snapshot ready: regions=" + current.Regions.Count
                + ", baseline_entries=" + current.Vanilla.Count + ", source=" + (native ? "registry" : "bundled"));
        }

        internal static void Reset() { Apply(LootDataSnapshot.Bundled()); }

        private static void Apply(LootDataSnapshot snapshot)
        {
            current = snapshot;
            ++Revision;
            Tuning.InvalidateLootData();
            ForeignLoot.InvalidateCatalog();
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
                WaterSupplyPack.HalfTest, WaterSupplyPack.HalfTablets,
                "packet_red_cigarette", "food_stamp", "mini_bottle", "wine_bottle",
                "wine_berry", "wine_yeast", "wine_yeast_red", "wine_superyeast", "wine_yeast_infinite");
            // 废泵站新增的普通净水 / 饮水物品同样归在原版 householdTable，
            // 让 ModHook 的事件上下文仍报一个真实存在的原版表名。
            AddAll(result, "householdTable",
                "bottled_water", "small_bottled_water", "large_bottled_water", "water_jug",
                "water_ration", "water_ration_small", "water_tablet", "water_test_strip",
                "water_tablet_container", "water_test_container", "hydroreed_filter", "uv_filter");
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
