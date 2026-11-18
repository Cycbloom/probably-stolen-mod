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
            string name = perk.Name;
            string state = active ? Copy.Get("dgmperkhover_active") : Copy.Get("dgmperkhover_inactive");
            string body = perk.Description;
            string flavor = Flavor(threshold);

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

        private static string Flavor(int perkThreshold)
        {
            int tier = perkThreshold == 20 || perkThreshold == 40 || perkThreshold == 60
                || perkThreshold == 80 ? perkThreshold : 100;
            return Copy.Get("perk_" + tier + "_flavor");
        }
    }
}
