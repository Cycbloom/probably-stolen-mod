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
            string zh;
            string en;
            if (threshold >= Reputation.Perk100Threshold)
            {
                zh = Reputation.Perk100NameZh;
                en = Reputation.Perk100NameEn;
            }
            else if (threshold >= Reputation.Perk80Threshold)
            {
                zh = Reputation.Perk80NameZh;
                en = Reputation.Perk80NameEn;
            }
            else if (threshold >= Reputation.Perk60Threshold)
            {
                zh = Reputation.Perk60NameZh;
                en = Reputation.Perk60NameEn;
            }
            else if (threshold >= Reputation.Perk40Threshold)
            {
                zh = Reputation.Perk40NameZh;
                en = Reputation.Perk40NameEn;
            }
            else
            {
                zh = Reputation.Perk20NameZh;
                en = Reputation.Perk20NameEn;
            }
            return Copy.Text(zh, en);
        }

        /// <summary>
        /// 0.3.51 定稿：五档统一「正文一行 + 斜体风味一行」，正文里句子之间只用句号、不手动换行。
        /// 60 的隐藏储藏 / 报纸事迹与 100 的雇人服务尚未实现，文案先落，功能上线后再核一次。
        /// </summary>
        private string Description()
        {
            if (threshold >= Reputation.Perk100Threshold)
            {
                return Copy.Text(
                    "拾荒者会把你当成自己人。拾荒前可以花钱雇一名拾荒者帮手。",
                    "The scavengers treat you as one of their own. "
                    + "Before scavenging, you can pay to hire a scavenger helper.");
            }
            if (threshold >= Reputation.Perk80Threshold)
            {
                return Copy.Text(
                    "拾荒时可能遇到贵重品。偶尔还能翻到补给箱和对应的钥匙卡。",
                    "While scavenging you may come across valuable goods. "
                    + "Now and then you can even turn up a supply crate and its matching keycard.");
            }
            if (threshold >= Reputation.Perk60Threshold)
            {
                return Copy.Text(
                    "拾荒时更有机会找到隐藏的储藏。第二天你会在报纸上看到自己的事迹。",
                    "You have a better chance of finding a hidden stash while scavenging. "
                    + "The next day you'll see your deed in the newspaper.");
            }
            if (threshold >= Reputation.Perk40Threshold)
            {
                return Copy.Text(
                    "你对这片场子越来越熟。三个高级拾荒场对你开放：拆解堆、医疗废弃点、废武器堆。"
                    + "离开时还可以花钱请拾荒者帮手，把拾荒时得到的东西搬回店里。",
                    "You know this place better and better. "
                    + "Three high-tier scavenging grounds are open to you: "
                    + "Scrap Pile, Medical Waste, Weapon Dump. "
                    + "When you leave, you can also pay a scavenger helper to carry "
                    + "what you found back to the store.");
            }
            return Copy.Text(
                "你结交了许多拾荒者朋友。每天会多一名拾荒者来你店里。",
                "You've made a lot of scavenger friends. "
                + "One more scavenger visits your shop each day.");
        }

        private string Flavor()
        {
            if (threshold >= Reputation.Perk100Threshold)
            {
                return Copy.Text("在这片垃圾场里，所有人都把你当兄弟。",
                    "In this dump, everyone treats you like a brother.");
            }
            if (threshold >= Reputation.Perk80Threshold)
            {
                return Copy.Text("值钱的东西，你一眼就认得出来。",
                    "You can tell something valuable at a glance.");
            }
            if (threshold >= Reputation.Perk60Threshold)
            {
                return Copy.Text("别人翻过去的东西，你还会再看一眼。",
                    "Others walk right past it; you take a second look.");
            }
            if (threshold >= Reputation.Perk40Threshold)
            {
                return Copy.Text("你知道哪里能捡到更好的东西。",
                    "You know where the better finds are.");
            }
            return Copy.Text("拾荒者们知道哪家店会给好价钱。",
                "Scavengers know which shop pays a fair price.");
        }
    }
}
