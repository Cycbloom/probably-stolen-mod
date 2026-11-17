using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DumpingGroundMap
{
    /// <summary>
    /// 区分「基础抽取」与技能额外抽取。一次 GetRandomScavengedItem 调用内的第一发
    /// SpawnDumpingGroundLoot 算基础抽取；其后由金属探测器、满载而归、废中寻宝、
    /// 布里丹驴等追加的抽取都算额外抽取，只从普通/外来槽里出东西。
    /// 作用域只由本模组读取，原版与第三方的前后缀、事件回调不受影响。
    /// </summary>
    internal static class ExtraDrawScope
    {
        private static readonly List<int> draws = new List<int>();

        internal static void BeginSearch()
        {
            draws.Add(0);
        }

        internal static void EndSearch()
        {
            if (draws.Count > 0) draws.RemoveAt(draws.Count - 1);
        }

        internal static bool TakeExtra()
        {
            if (draws.Count == 0) return false;
            int last = draws.Count - 1;
            bool extra = draws[last] > 0;
            draws[last] = draws[last] + 1;
            return extra;
        }
    }

    /// <summary>
    /// Replaces the vanilla dumping-ground roll with the selected area's table.
    /// When no area is active the vanilla distributed roll still runs.
    /// </summary>
    [HarmonyPatch(typeof(ScavHelper), "SpawnDumpingGroundLoot")]
    internal static class DumpingGroundLootPatch
    {
        // DiscoveryLoot needs its ref __state prefix before we skip the native
        // roll. This must be independent of the two mods' load order.
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(ScavengingExpansionBridge.HarmonyId)]
        private static bool Prefix(ref GameItem __result)
        {
            if (!Core.Enabled) return true;
            Region region = Picker.ChosenRegion;
            if (region == null || !Picker.IsAtDump(MapUIManager.Instance)) return true;

            // 只统计我们实际参与的这一段：从通过区域判定到本方法返回。
            bool perf = PerfLog.Enabled;
            long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;
            string path = "none";
            // 每次 SpawnDumpingGroundLoot 只消费一次：首次是基础抽取，
            // 其后都算技能/道具追加的额外抽取。
            bool extra = ExtraDrawScope.TakeExtra();
            try
            {
                // 「更好的拾荒」优先：它带一次性状态机（首箱钥匙卡、首箱手套），
                // 一旦被我们的表挤掉判定，「首次」就会推迟。命中时整趟只出它的物品，
                // 不再叠加外来池或普通槽。额外抽取只出普通物品，也不能消费它的
                // 一次性状态。
                string exoticId;
                if (!extra && BetterScavengingBridge.TryRoll(out exoticId))
                {
                    path = "better-scavenging";
                    __result = LootFactory.Create(exoticId, region.Id, RegionalEvents.ActiveFor(region) != null);
                    return false;
                }

                EffectiveTable table = Tuning.TableFor(region);
                int index = extra ? table.PickOrdinaryIndex() : table.PickIndex();
                if (index < 0) return true;

                path = extra
                    ? (table.Slots[index].Kind == LootKind.Foreign ? "extra+foreign" : "extra")
                    : (table.Slots[index].Kind == LootKind.Foreign ? "foreign" : "table");
                // Match vanilla: subscribers can replace/cancel the ID before spawning.
                string resolved = ScavengingExpansionBridge.Resolve(table, index);
                __result = LootFactory.Create(resolved, region.Id, RegionalEvents.ActiveFor(region) != null);

                // 外来条目可能没有可用的生成数据：回退到普通槽重掷一次，
                // 保证一次搜索仍然出一件。
                if (__result == null && table.Slots[index].Kind == LootKind.Foreign)
                {
                    path = "foreign+fallback";
                    int fallback = table.FallbackOrdinaryIndex();
                    if (fallback >= 0)
                    {
                        resolved = ScavengingExpansionBridge.Resolve(table, fallback);
                        __result = LootFactory.Create(resolved, region.Id, RegionalEvents.ActiveFor(region) != null);
                    }
                }

                // 特殊物品提示不在这里弹：布里丹驴等技能可能在同一次搜索里
                // 再抽一件并丢弃较低者，提前提示会出现「有提示没物品」。
                // 统一等最终战利品列表结算后再提示（ScavengeReputationPatch）。
                return false;
            }
            finally
            {
                if (perf)
                {
                    PerfLog.Line("scavenge region=" + region.Id + " " + PerfLog.Ms(startedAt)
                        + "ms path=" + path + (__result == null ? " result=null" : ""));
                }
            }
        }
    }
}
