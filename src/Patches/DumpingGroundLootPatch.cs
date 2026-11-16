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

            // 只统计我们实际参与的这一段：从通过区域判定到本方法返回。
            bool perf = PerfLog.Enabled;
            long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;
            string path = "none";
            try
            {
                // 「更好的拾荒」优先：它带一次性状态机（首箱钥匙卡、首箱手套），
                // 一旦被我们的表挤掉判定，「首次」就会推迟。命中时整趟只出它的物品，
                // 不再叠加外来池或普通槽。
                string exoticId;
                if (BetterScavengingBridge.TryRoll(out exoticId))
                {
                    path = "better-scavenging";
                    __result = LootFactory.Create(exoticId, region.Id, RegionalEvents.ActiveFor(region) != null);
                    if (__result != null) StashFind.NoticeSpecialFind(exoticId);
                    return false;
                }

                EffectiveTable table = Tuning.TableFor(region);
                int index = table.PickIndex();
                if (index < 0) return true;

                path = table.Slots[index].Kind == LootKind.Foreign ? "foreign" : "table";
                // Match vanilla: subscribers can replace/cancel the ID before spawning.
                string resolved = NativeLootEvents.Resolve(table.Items[index]);
                __result = LootFactory.Create(resolved, region.Id, RegionalEvents.ActiveFor(region) != null);

                // 外来条目可能没有可用的生成数据：回退到普通槽重掷一次，
                // 保证一次搜索仍然出一件。
                if (__result == null && table.Slots[index].Kind == LootKind.Foreign)
                {
                    path = "foreign+fallback";
                    int fallback = table.FallbackOrdinaryIndex();
                    if (fallback >= 0)
                    {
                        resolved = NativeLootEvents.Resolve(table.Items[fallback]);
                        __result = LootFactory.Create(resolved, region.Id, RegionalEvents.ActiveFor(region) != null);
                    }
                }

                if (__result != null) StashFind.NoticeSpecialFind(resolved);
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
