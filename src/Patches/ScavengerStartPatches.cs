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
    /// </summary>
    [HarmonyPatch(typeof(StoreClientManager), "GetNormalCustomerCount")]
    internal static class ScavengerCustomerPatch
    {
        private static bool suppressed;

        private static void Prefix()
        {
            suppressed = false;
            if (!Core.Enabled || !ScavengerStart.Active) return;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (store == null || store.healthData == null) return;
            if (!store.healthData.wentToAfterhoursLastNight) return;
            // 原版短眠者本来就不扣，不要再加回来。
            if (StartingPerk.IsShortSleeper()) return;
            MapUIManager map = MapUIManager.Instance;
            if (map == null || map.LastVisitedTonightId != ScavengerStart.DumpingGroundId) return;

            store.healthData.wentToAfterhoursLastNight = false;
            suppressed = true;
        }

        private static void Postfix()
        {
            Restore();
        }

        private static Exception Finalizer(Exception __exception)
        {
            Restore();
            return __exception;
        }

        private static void Restore()
        {
            if (!suppressed) return;
            suppressed = false;
            PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
            if (store != null && store.healthData != null)
            {
                store.healthData.wentToAfterhoursLastNight = true;
            }
        }
    }
}
