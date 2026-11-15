using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>拾荒次数 +2。只在当前存档用「拾荒客」开局时生效。</summary>
    [HarmonyPatch(typeof(ScavHelper), "GetMaxScavAttempts")]
    internal static class ScavengerAttemptPatch
    {
        private static void Postfix(ref int __result)
        {
            if (!Core.Enabled || __result <= 0) return;
            if (!ScavengerStart.Active) return;
            __result += ScavengerStart.ExtraScavAttempts;
        }
    }

    /// <summary>
    /// 短眠者的劣化版：昨夜去的是垃圾场时，把「夜间外出」的客人减 1 抵掉。
    ///
    /// 原生 GetNormalCustomerCount 的扣减条件是
    /// healthData.wentToAfterhoursLastNight &amp;&amp; !StartingPerk.IsShortSleeper()，
    /// 减完还会 Clamp(0, 6)。直接在返回值上加 1 会在「原生已夹到 0」时多出一名客人，
    /// 所以改成前缀里临时把 wentToAfterhoursLastNight 置 false，让原方法按
    /// 「没外出」的路径完整算一遍（含 Clamp），返回后再恢复，语义与短眠者一致。
    /// 访问日期取自存档 afterhourState，读档后地图的 LastVisitedTonightId 会丢失。
    /// </summary>
    [HarmonyPatch(typeof(StoreClientManager), "GetNormalCustomerCount")]
    internal static class ScavengerCustomerPatch
    {
        private static void Prefix(out RestoreScope __state)
        {
            __state = null;
            if (!Core.Enabled) return;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (!ScavengerTrafficRules.ProtectsMorning(store, StoreStation.GetDayCounter())) return;
            // 原版短眠者本来就不扣，不要再加回来。
            if (StartingPerk.IsShortSleeper()) return;
            __state = BeginProtectedCount(store, StoreStation.GetDayCounter());
        }

        internal static RestoreScope BeginProtectedCount(PlayerStore store, int today)
        {
            if (!ScavengerTrafficRules.ProtectsMorning(store, today)) return null;
            HealthData health = store.healthData;
            bool original = health.wentToAfterhoursLastNight;
            RestoreScope scope = new RestoreScope(() => health.wentToAfterhoursLastNight = original);
            health.wentToAfterhoursLastNight = false;
            return scope;
        }

        private static void Postfix(RestoreScope __state)
        {
            if (__state != null) __state.Restore();
        }

        private static Exception Finalizer(Exception __exception, RestoreScope __state)
        {
            if (__state != null) __state.Restore();
            return __exception;
        }
    }

    /// <summary>The exemption must also suppress the native "missed customer" night log.</summary>
    [HarmonyPatch(typeof(PlayerStore), "HandleAfterhourDebuff")]
    internal static class ScavengerNightReportPatch
    {
        private static bool Prefix(PlayerStore __instance)
        {
            return !Core.Enabled || !ScavengerTrafficRules.ProtectsNightReport(__instance, StoreStation.GetDayCounter());
        }
    }
}
