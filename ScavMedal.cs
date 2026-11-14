using System;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 20 点纪念奖励：第二天开业前，在前台（柜台）放一枚「拾荒者信物」。
    ///
    /// 做法照搬「神秘仰慕者 / Secret Admirer」：它也是在
    /// StoreShutterButton.OnShutterDoorOpenMorning() 的末尾（Postfix）用
    /// PlayerStore.AddDirectSellingItemToTable(...) 把礼物放到前台。
    /// 我们不需要 Harmony：原生方法内最后一行就是
    /// ModHook.FireOnShutterOpenedLate()，订阅它即可，位置等价。
    ///
    /// 时序（StoreShutterButton.OpenShutter 的 IL 顺序）：
    ///   PlayerStore.OnShutterOpened()      -> 内含 GenerateClient()，20 点客人奖励在这里
    ///   OpenShutter() / ToggleShutterDoor()
    ///   StoreShutterButton.OnShutterDoorOpenMorning()
    ///     ... 原生报纸 / 导师纸条 / 仰慕者礼物都在这里放
    ///     ModHook.FireOnShutterOpenedLate()  <- 我们
    ///   EmporiumEntry.ShowFrontArea()      -> 前台此时才显示，所以物品“开业时就在”
    ///   PlayerStore.OnLateShutterOpen()
    ///
    /// 风险点（实测用）：事件发生时前台容器理论上已经可用——同一方法里前面的
    /// 原生纸条就是这么放的。如果实测发现信物没有出现在前台，先看日志里
    /// TryAddDirectSellingItemToTable 是否返回 false；若不是，则说明
    /// EmporiumEntry.Instance.frontInvinvElement 此时还没准备好，可改用
    /// PlayerStore.OnLateShutterOpen() 的 Postfix（ShowFrontArea 之后）。
    ///
    /// 一次性：标记写在 PlayerStore.modData（键 scav_medal_given），随存档保存，
    /// 即“每个存档时间线一次”。读回发过之后的存档不再发；回档到发之前的存档
    /// 会再发一次，因为那条时间线从未收到过。前台满时不写标记，次日重试。
    /// </summary>
    internal static class ScavMedal
    {
        /// <summary>与柱子上的 20 点刻度、ScavengerPerkPatch 保持一致。</summary>
        private const int Threshold = 20;

        private const string ItemId = "scav_token";
        private const string GivenKey = "scav_medal_given";

        internal static void OnShutterOpenedLate()
        {
            if (!Core.Enabled) return;
            try
            {
                int points = Reputation.Points;
                if (points < Threshold) return;

                if (ModHelper.GetModDataBool(Core.Id, GivenKey, false))
                {
                    Debug.Log("[DumpingGroundMap] scav medal: skip (already given) points=" + points);
                    return;
                }

                if (PlayerStore.Instance == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] scav medal: no PlayerStore, skipped");
                    return;
                }

                // 参数照参考 mod：own=true（玩家的、可拿取）、not stolen、ignorePerk=true、heat=0。
                if (!PlayerStore.Instance.TryAddDirectSellingItemToTable(ItemId, true, false, true, 0))
                {
                    Debug.LogWarning("[DumpingGroundMap] scav medal: front counter full, retry next opening day="
                        + StoreStation.GetDayCounter());
                    return;
                }

                ModHelper.SetModDataBool(Core.Id, GivenKey, true);
                Debug.Log("[DumpingGroundMap] scav medal: +1 " + ItemId + " on front counter day="
                    + StoreStation.GetDayCounter() + " points=" + points);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scav medal: failed: " + ex.Message);
            }
        }
    }
}
