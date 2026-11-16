using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DumpingGroundMap
{
    /// <summary>
    /// 悬停奖励图标时把该档的名字、正文与风味写进下方说明区。
    /// 只更新原生面板的文本字段，不激活浮动提示框，
    /// 所以不会弹出原生 Referral / Active 那一套。
    /// </summary>
    internal sealed class DgmPerkHover : MonoBehaviour, IPointerEnterHandler
    {
        private ReputationUIManager manager;
        private int threshold;
        private bool active;

        internal void Setup(ReputationUIManager owner, int perkThreshold, bool unlocked)
        {
            manager = owner;
            threshold = perkThreshold;
            active = unlocked;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ReputationUIManager m = manager != null ? manager : ReputationUIManager.Instance;
            if (m == null) return;

            PerkDefinition perk = PerkCatalog.For(threshold);
            string name = Copy.Text(perk.NameZh, perk.NameEn);
            string state = active ? Copy.Text("已激活", "Active") : Copy.Text("未激活", "Inactive");
            string body = Copy.Text(perk.DescriptionZh, perk.DescriptionEn);
            string flavor = Copy.Text(FlavorZh(threshold), FlavorEn(threshold));

            Set(m.perkTitle, name + " [" + state + "] " + threshold + "/200");
            Set(m.perkStatus, string.Empty);
            Set(m.perkProgress, string.Empty);
            Set(m.perkdescription, body);
            // 与原生/旧版一致：风味标签自带斜体样式，文本里不再包 <i>。
            Set(m.perkFlavorText, flavor);

            // 原生面板没有显示浮动提示框的代码；这里再兜底关掉一次，
            // 但绝不关闭包含说明文本的那个对象。
            GameObject tooltip = m.reputationTooltip;
            if (tooltip != null && tooltip.activeSelf
                && (m.perkTitle == null || !m.perkTitle.transform.IsChildOf(tooltip.transform)))
            {
                tooltip.SetActive(false);
            }
        }

        private static void Set(TMP_Text label, string text)
        {
            if (label != null) label.SetText(text);
        }

        private static string FlavorZh(int perkThreshold)
        {
            switch (perkThreshold)
            {
                case 20: return "拾荒者们知道哪家店会给好价钱。";
                case 40: return "你知道哪里能捡到更好的东西。";
                case 60: return "别人翻过去的东西，你还会再看一眼。";
                case 80: return "值钱的东西，你一眼就认得出来。";
                default: return "在这片垃圾场里，所有人都把你当兄弟。";
            }
        }

        private static string FlavorEn(int perkThreshold)
        {
            switch (perkThreshold)
            {
                case 20: return "Scavengers know which shop pays well.";
                case 40: return "You know where the better finds are.";
                case 60: return "What others pass over, you look at twice.";
                case 80: return "You can spot something valuable at a glance.";
                default: return "In this dump, everyone treats you like family.";
            }
        }
    }
}
