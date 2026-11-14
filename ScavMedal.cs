using System;
using System.Collections.Generic;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 20 点纪念奖励：第二天开业前，在前台（柜台）放一枚「拾荒者信物」和一张便条。
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
    /// 两件原子发放：先放信物，再放便条；便条放不下就把信物收回来，且不写标记，
    /// 次日重来。绝不出现“只给了其中一样”。
    ///
    /// 便条用原生空白纸条 slip + PreBuiltItemHelper.CreatePaperWithText：
    /// 标题同时是物品名，正文写在 SLIP_BODY 标签里，点开走原生的 PaperUIManager，
    /// 与原版导师纸条、情报纸条是同一套机制。
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

        private const string NoteTitleZh = "拾荒老手的信物";
        private const string NoteTitleEn = "A Veteran Scavenger's Token";

        private const string NoteBodyZh =
            "恭喜——你把手伸进垃圾场最脏的地方，摸回了一枚像样的信物。"
            + "它现在就在前台，往后每天都会多一位拾荒客愿意进店看看。";

        private const string NoteBodyEn =
            "Congratulations — you reached into the filthiest corner of the dump and came back "
            + "with something worth keeping. It's on the front counter now; from here on, "
            + "one more scavenger will drop by each day.";

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

                PlayerStore store = PlayerStore.Instance;
                EmporiumEntry entry = EmporiumEntry.Instance;
                if (store == null || entry == null || entry.frontInvinvElement == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] scav medal: front counter not ready, "
                        + "retry next opening day=" + StoreStation.GetDayCounter());
                    return;
                }
                if (!entry.frontInvinvElement.CanTakeAnything())
                {
                    Debug.LogWarning("[DumpingGroundMap] scav medal: front counter full, "
                        + "retry next opening day=" + StoreStation.GetDayCounter());
                    return;
                }

                // 参数照参考 mod：own=true（玩家的、可拿取）、not stolen、ignorePerk=true、heat=0。
                GameItem token = DirectoryMaster.Item(ItemId, true);
                if (!store.TryAddDirectSellingItemToTable(token, true, false, true, 0))
                {
                    Debug.LogWarning("[DumpingGroundMap] scav medal: token add failed, "
                        + "retry next opening day=" + StoreStation.GetDayCounter());
                    return;
                }

                GameItem note = PreBuiltItemHelper.CreatePaperWithText(
                    Copy.Text(NoteBodyZh, NoteBodyEn), Copy.Text(NoteTitleZh, NoteTitleEn));
                if (!store.TryAddDirectSellingItemToTable(note, true, false, true, 0))
                {
                    RollbackToken(token);
                    Debug.LogWarning("[DumpingGroundMap] scav medal: note add failed, token rolled back, "
                        + "retry next opening day=" + StoreStation.GetDayCounter());
                    return;
                }

                ModHelper.SetModDataBool(Core.Id, GivenKey, true);
                Debug.Log("[DumpingGroundMap] scav medal: +1 " + ItemId + " +1 slip(note) on front counter"
                    + " day=" + StoreStation.GetDayCounter() + " points=" + points
                    + " note=" + note.name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scav medal: failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 便条没放上去时把已经落位的信物收回来，保证两件要么都在、要么都不在。
        /// RemoveItemFromStoreInventory 内部是 GraphUtils.TryDestroyAll，会连同格子一起清掉。
        /// </summary>
        private static void RollbackToken(GameItem token)
        {
            try
            {
                List<GameItem> items = new List<GameItem>();
                items.Add(token);
                PlayerStore.Instance.RemoveItemFromStoreInventory(items);
                Debug.Log("[DumpingGroundMap] scav medal: rollback token done");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scav medal: rollback token failed: " + ex.Message);
            }
        }
    }
}
