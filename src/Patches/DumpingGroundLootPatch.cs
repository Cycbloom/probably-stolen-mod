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

            // 表里的虚拟 id（次品免疫宁 / 假烟 / 仿券 / 小瓶酒精）在这里换成真物品并补状态。
            string realId = LootVariants.RealId(itemId);
            GameItem item = LootVariants.Create(itemId);
            if (item == null) return true;
            LootVariants.Apply(itemId, item);
            WeaponWear.Apply(realId, item);

            LootRollContext context = new LootRollContext();
            context.groupId = "dumpingGroundTG";
            context.tableId = ScavLoot.SourceTable(realId);
            context.itemId = realId;
            if (fireLootRolled != null)
                fireLootRolled.Invoke(null, new object[] { context });

            __result = item;
            StashFind.NoticeSpecialFind(itemId);
            return false;
        }
    }
}
