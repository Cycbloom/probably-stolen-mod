using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>
    /// 40 声望档的「请拾荒者帮手」：离场确认框里除了原版「离开」「取消」，
    /// 再多一个「花钱请人帮忙」——花钱 + 按价值从高到低把地面上的东西搬回店里 + 离场。
    /// 原版两个按钮完全不动：点「离开」就是不付费、不搬运地走。
    /// 搬运部分复用原版 DrJacksonLocation.Leave 的写法
    /// （GraphUtils.EmporiumTryAdd(EmporiumEntry.backInvWindow, item)），
    /// 以及 TransferAfterhourToInv 的口袋 / 背包转移顺序。
    /// </summary>
    internal static class ScavHelperService
    {
        /// <summary>整个搬运服务一次性收费。</summary>
        internal const int Fee = 50;

        /// <summary>「店铺便捷升级：背包挂钩」的 mod id。</summary>
        private const string BackpackHookModId = "backpack_wall_slot_mod";

        /// <summary>面板向下让出的高度：按钮高 30 + 间隙 8 + 底部留白 10。</summary>
        private const float ExtraHeight = 48f;

        /// <summary>新按钮与原按钮之间的间隙。</summary>
        private const float Gap = 8f;

        private static bool armed;
        private static bool showing;

        private static GameObject helperButton;
        private static RectTransform helperSlot;
        private static RectTransform panelRect;
        private static RectTransform bodyRect;
        private static RectTransform groupRect;
        private static GameObject panelOwner;
        private static Vector2 originalPanelSize;
        private static Vector2 originalBodyPosition;
        private static Vector2 originalGroupPosition;
        private static bool layoutApplied;

        internal static bool Available
        {
            get { return Core.Enabled && Reputation.Points >= Reputation.Perk40Threshold; }
        }

        /// <summary>
        /// 装了「店铺便捷升级：背包挂钩」时，背包交给原版离场流程处理：
        /// 对方在 EmporiumEntry.TryAddToPlayerInv 上的前缀会把包挂到墙上，店里就省下一个格子。
        /// 我们这边只做「不搬背包」——挂钩被占或被关掉时，原版仍会照老路子把包放进店里，不会丢。
        /// </summary>
        private static bool BackpackHookInstalled
        {
            get
            {
                try { return ModHelper.IsModRunning(BackpackHookModId); }
                catch { return false; }
            }
        }

        /// <summary>每次重建拾荒场景时清掉未消费的标记。</summary>
        internal static void Reset()
        {
            armed = false;
        }

        /// <summary>原版「地面还有东西，确认离开」即将弹框时调用。</summary>
        internal static void Arm()
        {
            armed = false;
            if (!Available) return;
            EmporiumEntry entry = EmporiumEntry.Instance;
            MapUIManager map = MapUIManager.Instance;
            if (entry == null || map == null) return;
            if (!(map.Current is DumpingGroundLocation)) return;
            GameGridInventory ground = entry.afterhourInventory;
            if (ground == null || ground.childItems == null || ground.childItems.Count == 0) return;
            armed = true;
        }

        internal static void ArmResult(bool opened)
        {
            if (!opened) armed = false;
        }

        /// <summary>确认框打开 / 关闭时同步第三个按钮与原版面板的尺寸。</summary>
        internal static void SyncPanel(GenericConfirmationUIManager ui, bool open)
        {
            if (ui == null) return;
            if (!open || !armed)
            {
                Cancel();
                return;
            }
            armed = false;
            if (!Build(ui)) return;
            showing = true;
            ApplyLayout(ui);
            helperButton.SetActive(true);
        }

        private static void Cancel()
        {
            armed = false;
            if (!showing) return;
            showing = false;
            if (helperButton != null) helperButton.SetActive(false);
            RestoreLayout();
        }

        private static bool Build(GenericConfirmationUIManager ui)
        {
            if (helperButton != null && helperSlot != null && panelOwner == ui.panel) return true;
            if (ui.confirmButton == null || ui.panel == null) return false;

            // 按钮组带横向排版，塞进去会被重新排到左右两边；这里单开一格放在按钮行下方。
            helperSlot = CreateSlot(ui);
            if (helperSlot == null) return false;

            GameObject clone = UnityEngine.Object.Instantiate(ui.confirmButton, helperSlot, false);
            clone.name = "DGM_PaidHelperButton";

            TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = Copy.Text("花钱请人帮忙", "Hire Help");
            }

            Button button = clone.GetComponent<Button>();
            if (button == null)
            {
                // 原按钮不是 UGUI Button 时克隆出来点不动，宁可不显示。
                UnityEngine.Object.Destroy(clone);
                UnityEngine.Object.Destroy(helperSlot.gameObject);
                helperSlot = null;
                Debug.LogWarning("[DumpingGroundMap] helper button skipped: confirm button has no Button");
                return false;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnHelperPressed);

            RectTransform rect = clone.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            helperButton = clone;
            panelOwner = ui.panel;
            layoutApplied = false;
            return true;
        }

        private static RectTransform CreateSlot(GenericConfirmationUIManager ui)
        {
            RectTransform source = ui.confirmButton.GetComponent<RectTransform>();
            if (source == null) return null;

            GameObject slot = new GameObject("DGM_HelperButtonSlot", typeof(RectTransform));
            RectTransform rect = slot.GetComponent<RectTransform>();
            rect.SetParent(ui.panel.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = source.rect.size;
            return rect;
        }

        private static void ApplyLayout(GenericConfirmationUIManager ui)
        {
            panelRect = ui.panel == null ? null : ui.panel.GetComponent<RectTransform>();
            bodyRect = ui.bodyTMP == null ? null : ui.bodyTMP.rectTransform;
            groupRect = ui.confirmButton == null
                ? null
                : ui.confirmButton.transform.parent as RectTransform;
            if (panelRect == null || groupRect == null) return;

            if (!layoutApplied)
            {
                originalPanelSize = panelRect.sizeDelta;
                originalBodyPosition = bodyRect == null ? Vector2.zero : bodyRect.anchoredPosition;
                originalGroupPosition = groupRect.anchoredPosition;
                layoutApplied = true;
            }

            // 面板整体长高，正文与按钮行上移一半，底部刚好空出一行按钮的位置。
            panelRect.sizeDelta = new Vector2(originalPanelSize.x, originalPanelSize.y + ExtraHeight);
            if (bodyRect != null)
            {
                bodyRect.anchoredPosition = originalBodyPosition + new Vector2(0f, ExtraHeight / 2f);
            }
            groupRect.anchoredPosition = originalGroupPosition + new Vector2(0f, ExtraHeight / 2f);

            Vector2 confirm = panelRect.InverseTransformPoint(ui.confirmButton.transform.position);
            float height = ui.confirmButton.GetComponent<RectTransform>().rect.height;
            helperSlot.anchoredPosition = new Vector2(confirm.x, confirm.y - height / 2f - Gap - helperSlot.sizeDelta.y / 2f);
        }

        private static void RestoreLayout()
        {
            if (!layoutApplied) return;
            layoutApplied = false;
            if (panelRect == null || groupRect == null) return;
            panelRect.sizeDelta = originalPanelSize;
            if (bodyRect != null) bodyRect.anchoredPosition = originalBodyPosition;
            groupRect.anchoredPosition = originalGroupPosition;
        }

        private static void OnHelperPressed()
        {
            try
            {
                if (!Available) return;
                GenericConfirmationUIManager ui = GenericConfirmationUIManager.Instance;
                PlayerStore store = PlayerStore.Instance;
                if (store == null || !store.HasCredit(Fee))
                {
                    // 钱不够：留在原地不扣款，把提示写进确认框正文，玩家仍可点「离开」或「取消」。
                    if (ui != null && ui.bodyTMP != null)
                    {
                        ui.bodyTMP.text = Copy.Text(
                            "信用点不够，请不了拾荒者帮手。你可以自己把东西捡起来，或者直接离开。",
                            "Not enough credits to hire a scavenger helper. Pick the items up yourself or just leave.");
                    }
                    return;
                }

                store.PayMoney(Fee, true);
                int carried = CarryBack();
                Debug.Log("[DumpingGroundMap] scavenger helper paid " + Fee + ", carried " + carried
                    + " item(s) off the ground");

                if (ui != null) ui.ClosePanel();
                MapUIManager map = MapUIManager.Instance;
                if (map != null) map.ReturnNow();
            }
            catch (Exception error)
            {
                Debug.LogWarning("[DumpingGroundMap] helper button failed: " + error);
            }
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

            // 装了背包挂钩：背包留在口袋里，让原版离场流程把它挂到墙上。
            // 这样它不占店内格子，帮手搬地面时也不用替它留位置。
            if (BackpackHookInstalled)
            {
                Debug.Log("[DumpingGroundMap] helper: backpack left for wall hook");
            }
            else
            {
                MoveCarried(entry, entry.afterhourPocketSlotInvBackpack);
            }
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

    /// <summary>原版离场确认是不是「拾荒地面还有东西」这一次。</summary>
    [HarmonyPatch(typeof(MapUIManager), "CheckItemLeftOnGround")]
    internal static class HelperLeavePromptPatch
    {
        private static void Prefix()
        {
            if (!Core.Enabled) return;
            ScavHelperService.Arm();
        }

        private static void Postfix(bool __result)
        {
            ScavHelperService.ArmResult(__result);
        }
    }

    /// <summary>确认框弹出来时在原按钮下方补一个按钮，关掉时恢复原版尺寸。</summary>
    [HarmonyPatch(typeof(GenericConfirmationUIManager), "OpenPanel")]
    internal static class HelperPanelPatch
    {
        private static void Postfix(GenericConfirmationUIManager __instance)
        {
            if (!Core.Enabled) return;
            ScavHelperService.SyncPanel(__instance, true);
        }
    }

    [HarmonyPatch(typeof(GenericConfirmationUIManager), "ClosePanel")]
    internal static class HelperPanelClosePatch
    {
        private static void Postfix(GenericConfirmationUIManager __instance)
        {
            ScavHelperService.SyncPanel(__instance, false);
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
