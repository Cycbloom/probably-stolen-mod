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
    /// 柱上奖励图标的悬停说明。原生 PerkElement.OnPointerEnter 只会用
    /// StoreReputationPerk 的本地化键填 tooltip，我们没有词条，所以把原生
    /// 组件关掉（perk.enabled = false，Unity 不会再向它派发指针事件），
    /// 改由本组件填 ReputationUIManager 上那五个 public 文本字段。
    /// 原生 tooltip 的显示只看 GameObject 是否 active，位置由原生 Update
    /// 每帧调 UpdateTooltipPosition 跟随鼠标，这里只负责开关与文案。
    /// 全部公开字段访问：不写文件、不反射、不加资源。
    /// </summary>
    internal sealed class DgmPerkTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private ReputationUIManager manager;
        private int threshold;
        private bool unlocked;

        internal void Setup(ReputationUIManager owner, int perkThreshold, bool isUnlocked)
        {
            manager = owner;
            threshold = perkThreshold;
            unlocked = isUnlocked;
        }

        public void OnPointerEnter(PointerEventData eventData) { Show(); }

        public void OnPointerExit(PointerEventData eventData) { Hide(); }

        private void OnDisable() { Hide(); }

        private void Show()
        {
            ReputationUIManager m = manager != null ? manager : ReputationUIManager.Instance;
            if (m == null || m.reputationTooltip == null) return;

            string state = unlocked ? Copy.Text("已激活", "Active") : Copy.Text("未激活", "Inactive");
            if (m.perkTitle != null)
            {
                m.perkTitle.SetText(Title() + " [" + state + "] "
                    + threshold.ToString(CultureInfo.InvariantCulture) + "/200");
            }
            if (m.perkStatus != null) m.perkStatus.SetText(string.Empty);
            if (m.perkProgress != null) m.perkProgress.SetText(string.Empty);
            if (m.perkdescription != null) m.perkdescription.SetText(Description());
            // 0.3.51 起五档统一「正文一行 + 斜体风味一行」，风味句沿用原来的位置与样式。
            if (m.perkFlavorText != null) m.perkFlavorText.SetText(Flavor());
            m.reputationTooltip.SetActive(true);
        }

        private void Hide()
        {
            ReputationUIManager m = manager != null ? manager : ReputationUIManager.Instance;
            if (m != null && m.reputationTooltip != null) m.reputationTooltip.SetActive(false);
        }

        private string Title()
        {
            PerkDefinition perk = PerkCatalog.For(threshold);
            return Copy.Text(perk.NameZh, perk.NameEn);
        }

        private string Description()
        {
            PerkDefinition perk = PerkCatalog.For(threshold);
            return Copy.Text(perk.DescriptionZh, perk.DescriptionEn);
        }

        private string Flavor()
        {
            if (threshold >= PerkCatalog.Perk100Threshold)
            {
                return Copy.Text("在这片垃圾场里，所有人都把你当兄弟。",
                    "In this dump, everyone treats you like a brother.");
            }
            if (threshold >= PerkCatalog.Perk80Threshold)
            {
                return Copy.Text("值钱的东西，你一眼就认得出来。",
                    "You can tell something valuable at a glance.");
            }
            if (threshold >= PerkCatalog.Perk60Threshold)
            {
                return Copy.Text("别人翻过去的东西，你还会再看一眼。",
                    "Others walk right past it; you take a second look.");
            }
            if (threshold >= PerkCatalog.Perk40Threshold)
            {
                return Copy.Text("你知道哪里能捡到更好的东西。",
                    "You know where the better finds are.");
            }
            return Copy.Text("拾荒者们知道哪家店会给好价钱。",
                "Scavengers know which shop pays a fair price.");
        }
    }
}
