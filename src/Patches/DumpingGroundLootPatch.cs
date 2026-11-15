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
        private static bool Prefix(ref GameItem __result)
        {
            if (!Core.Enabled) return true;
            Region region = Picker.ChosenRegion;
            if (region == null || !Picker.IsAtDump(MapUIManager.Instance)) return true;

            float junkChance = 0f;
            AfterhourLocation location = AfterhourLocation.Get("dumping_ground");
            if (location != null) junkChance = location.GetModifier("junk_chance");
            float scale = junkChance == 0f ? 1f : Mathf.Max(0f, 1f + junkChance / 100f);

            string itemId = Tuning.Pick(region, scale);
            if (string.IsNullOrEmpty(itemId)) return true;

            // Match vanilla: subscribers can replace/cancel the ID before spawning.
            string resolved = NativeLootEvents.Resolve(itemId);
            __result = LootFactory.Create(resolved);
            if (__result != null) StashFind.NoticeSpecialFind(resolved);
            return false;
        }
    }
}
