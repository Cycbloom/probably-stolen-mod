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
        private static void Postfix() { Picker.ResetChoice(false); }
    }

    [HarmonyPatch(typeof(MapUIManager), "CloseLocationPanel")]
    internal static class CloseLocationPatch
    {
        private static void Prefix() { Picker.Close(); }
    }

    [HarmonyPatch(typeof(MapUIManager), "Update")]
    internal static class MapUpdatePatch
    {
        private static void Postfix(MapUIManager __instance)
        {
            Picker.CheckLocation(__instance);
            RegionBackdrop.Refresh(__instance);
        }
    }

    // Apply after the native scene becomes active, before the transition reveals it.
    [HarmonyPatch(typeof(MapUIManager), "ShowStage")]
    internal static class RegionStagePatch
    {
        private static void Postfix(MapUIManager __instance) { RegionBackdrop.Refresh(__instance); }
    }

    // Restore only after the native return transition has hidden the outgoing scene.
    [HarmonyPatch(typeof(MapUIManager), "HideStage")]
    internal static class HiddenRegionStagePatch
    {
        private static void Postfix() { RegionBackdrop.RestoreIfHidden(); }
    }
}
