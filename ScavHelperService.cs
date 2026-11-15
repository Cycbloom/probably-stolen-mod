using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 40 声望档的「请拾荒者帮手」：离开拾荒地面时花 50 信用点，
    /// 按价值从高到低把地面上的战利品搬回店里。
    /// 搬运部分复用原版 DrJacksonLocation.Leave 的写法
    /// （GraphUtils.EmporiumTryAdd(EmporiumEntry.backInvWindow, item)），
    /// 以及 TransferAfterhourToInv 的口袋 / 背包转移顺序。
    /// </summary>
    internal static class ScavHelperService
    {
        /// <summary>整个搬运服务一次性收费。</summary>
        internal const int Fee = 50;

        internal static bool Available
        {
            get { return Core.Enabled && Reputation.Points >= Reputation.Perk40Threshold; }
        }

        /// <summary>本次拾荒场景是否已经给过选择（含拒绝），避免重复弹窗。</summary>
        private static bool offered;
        private static Action resume;

        internal static void Reset()
        {
            offered = false;
            resume = null;
        }

        /// <summary>
        /// 在「地面还有东西，确认离开」这一步接管。返回 true 表示已经弹了自己的面板，
        /// 调用方应跳过原版面板；稍后确认或取消时再通过 resume 继续离场。
        /// </summary>
        internal static bool TryOffer(Action continueReturn)
        {
            if (!Available || offered || continueReturn == null) return false;
            EmporiumEntry entry = EmporiumEntry.Instance;
            MapUIManager map = MapUIManager.Instance;
            if (entry == null || map == null) return false;
            if (!(map.Current is DumpingGroundLocation)) return false;
            GameGridInventory ground = entry.afterhourInventory;
            if (ground == null || ground.childItems == null || ground.childItems.Count == 0) return false;
            GenericConfirmationUIManager ui = GenericConfirmationUIManager.Instance;
            if (ui == null) return false;

            offered = true;
            resume = continueReturn;
            int count = ground.childItems.Count;
            ui.OpenPanel(
                Copy.Text(
                    "地面上还有 " + count + " 件东西。花 50 信用点请拾荒者帮手，他们会按价值从高到低把能放下的搬回店里；店里装不下的仍然会被丢掉。",
                    "There are " + count + " item(s) left on the ground. Pay 50 credits to hire a scavenger helper, who carries them into the store, most valuable first; anything the store cannot hold is still lost."),
                Copy.Text("请拾荒者帮手（50 信用点）", "Hire a scavenger helper (50 credits)"),
                Copy.Text("再想想", "Not now"),
                OnHired,
                OnDeclined);
            return true;
        }

        private static void OnHired()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null || !store.HasCredit(Fee))
            {
                // 原版对「付不起」的付费服务就是静默不执行（如 Georgio 送餐），
                // 这里补一条提示，然后留在原地让玩家自己收拾，不扣款也不搬运。
                StashFind.ShowNotice(Copy.Text(
                    "信用点不够，请不了拾荒者帮手。",
                    "Not enough credits to hire a scavenger helper."), "helper-poor");
                Stay();
                return;
            }

            store.PayMoney(Fee, true);
            int carried = CarryBack();
            Debug.Log("[DumpingGroundMap] scavenger helper paid " + Fee + ", carried " + carried
                + " item(s) off the ground");
            Finish();
        }

        /// <summary>拒绝帮手 / 付不起：只关面板，留在垃圾场，原版确认仍留给下一次离场。</summary>
        private static void OnDeclined()
        {
            Stay();
        }

        private static void Stay()
        {
            resume = null;
            GenericConfirmationUIManager ui = GenericConfirmationUIManager.Instance;
            if (ui != null) ui.ClosePanel();
        }

        private static void Finish()
        {
            Action next = resume;
            resume = null;
            GenericConfirmationUIManager ui = GenericConfirmationUIManager.Instance;
            if (ui != null) ui.ClosePanel();
            if (next != null) next();
        }

        /// <summary>
        /// 先处理书包和两只手（原版 TransferAfterhourToInv 的顺序），再按价值从高到低搬地面。
        /// 口袋里的东西沿用原版 TryAddToPlayerInv 的目标顺序：后场 → 柜台 → 展示柜。
        /// 地面物品走原版博士离场时的 EmporiumTryAdd：后场窗口 → 漏斗 → 后场 → 柜台 →
        /// 展示柜 → 前台；全部满了就把东西留在原地，由原版离场逻辑销毁。
        /// </summary>
        private static int CarryBack()
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null) return 0;
            GameGridInventory ground = entry.afterhourInventory;
            int before = ground == null || ground.childItems == null ? 0 : ground.childItems.Count;

            MoveCarried(entry, entry.afterhourPocketSlotInvBackpack);
            MoveCarried(entry, entry.afterhourPocketSlotInvRight);
            MoveCarried(entry, entry.afterhourPocketSlotInvLeft);

            if (ground != null && ground.childItems != null)
            {
                List<GameItem> items = new List<GameItem>(ground.childItems);
                items.Sort(delegate(GameItem a, GameItem b)
                {
                    return ItemValue(b).CompareTo(ItemValue(a));
                });
                for (int i = 0; i < items.Count; ++i)
                {
                    GameItem item = items[i];
                    if (item == null || !ReferenceEquals(item.parentInventory, ground)) continue;
                    GraphUtils.EmporiumTryAdd(entry.backInvWindow, item);
                }
            }

            int after = ground == null || ground.childItems == null ? 0 : ground.childItems.Count;
            return before - after;
        }

        private static void MoveCarried(EmporiumEntry entry, GameSlotInventory slot)
        {
            if (slot == null) return;
            GameItem item = slot.childItem;
            if (item == null) return;
            if (GraphUtils.TryAccept(entry.backInvinvElement, item) != -1) return;
            if (GraphUtils.TryAccept(entry.invElement, item) != -1) return;
            GraphUtils.TryAccept(entry.showcaseElement, item);
        }

        /// <summary>按整件（含堆叠）估值，和声望统计用的是同一个口径。</summary>
        private static long ItemValue(GameItem item)
        {
            return item == null ? 0L : item.GetCurrentValue(false, true, true, false, 0L);
        }
    }

    [HarmonyPatch(typeof(MapUIManager), "CheckItemLeftOnGround")]
    internal static class HelperLeavePromptPatch
    {
        private static bool Prefix(Action action)
        {
            return !ScavHelperService.TryOffer(action);
        }
    }

    /// <summary>每次重建拾荒场景都重新允许一次帮手入口。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "SetupAfterhourInv")]
    internal static class HelperResetPatch
    {
        private static void Postfix()
        {
            ScavHelperService.Reset();
        }
    }
}
