using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>
    /// 每趟拾荒结束时结算声望：把这一趟捡到的所有物品按当前金币价值相加。
    /// GetRandomScavengedItem(GameItem&amp;) 是每次搜索真正产出物品的那一步；
    /// 无参重载只是转调它，所以只补这一个，避免重复计数。
    /// </summary>
    [HarmonyPatch]
    internal static class ScavengeReputationPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(ScavHelper), "GetRandomScavengedItem",
                new Type[] { typeof(GameItem).MakeByRefType() });
            Debug.Log("[DumpingGroundMap] scavenge: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        // 一次搜索的作用域：第一发 SpawnDumpingGroundLoot 算基础抽取，
        // 其后由技能/道具追加的抽取只出普通物品。
        private static void Prefix(out bool __state)
        {
            ExtraDrawScope.BeginSearch();
            __state = true;
        }

        private static void Finalizer(bool __state)
        {
            if (__state) ExtraDrawScope.EndSearch();
        }

        // Count skill-added loot after Scavenging Expansion finishes the list.
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(ScavengingExpansionBridge.HarmonyId)]
        private static void Postfix(List<GameItem> __result)
        {
            if (!Core.Enabled) return;
            // 60 档爆大奖先追加物品，再统一结算价值，确保那几件也计入声望。
            StashFind.OnScavenge(__result);
            int count = __result == null ? 0 : __result.Count;
            long sum = 0L;
            for (int i = 0; i < count; ++i)
            {
                GameItem item = __result[i];
                if (item == null) continue;
                try { sum += item.GetCurrentValue(false, true, true, false, 0L); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DumpingGroundMap] scavenge: value failed: " + ex.Message);
                }
            }
            long repValue = Reputation.Add(sum);
            // Record unlocks on the day they happen so the new area's first
            // event can join tomorrow, even if this was today's last search.
            RegionalEvents.Sync();
            ReputationPopBridge.Show(repValue);
            // 特殊物品提示放到最终列表之后：布里丹驴等技能比较并丢弃较低者时，
            // 被丢掉的那件不会再留下「有提示没物品」。
            StashFind.NoticeSpecialFinds(__result);
            Debug.Log("[DumpingGroundMap] scavenge: items=" + count + " value=" + sum
                + " repValue=" + repValue + " total=" + Reputation.TotalValue
                + " points=" + Reputation.Points);
            if (count > 0 && sum <= 0L)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenge: items found but total value is 0");
            }
        }
    }
}
