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
    /// 声望在评价箱（FeedbackBox → ReputationUIManager）里的展示。
    /// 在原生面板打开后追加一行「垃圾场」：复用原生行预制体与排版，
    /// 只在内存里合成一个 StoreReputation，不写 PlayerStore.storeReputations，
    /// 不落盘、不进 ES3。类型全部编译期绑定（typeof / 直接访问公开字段），
    /// 不使用字符串式反射或程序集扫描。
    /// 唯一的短路是“本次 OpenUI 已经建过行”（Prefix 记打开序号、Postfix 比对），
    /// 不查找子物体：原生 OpenUI 用 Destroy 清空 layout，延迟到帧末才移除，
    /// 查找会命中上一轮的旧行。不针对任何其它 mod 做兼容判断，
    /// 其它 mod 的运行状态只写进日志，不参与分支。
    /// </summary>
    [HarmonyPatch]
    internal static class ReputationBoardPatch
    {
        private const string RowName = "DGM_ReputationRow";
        private const string TitleZh = "拾荒客";
        private const string TitleEn = "Scavenger";
        private const string TitleColor = "8FA06A";
        private const float BarFullHeight = 65.5f;
        private const string DisasterDayId = "disaster_day";
        private const string ReputationRemakeId = "cirno9.reputation_interface_remake";

        /// <summary>0–200 刻度上的档位分界：20 / 40 / 60 / 80 / 100。</summary>
        private static readonly int[] Thresholds =
        {
            PerkCatalog.Perk20Threshold, PerkCatalog.Perk40Threshold,
            PerkCatalog.Perk60Threshold, PerkCatalog.Perk80Threshold,
            PerkCatalog.Perk100Threshold
        };
        /// <summary>
        /// 柱子上的五个奖励点，与原生 perk 同一套刻度（0–200）。
        /// 20：每天多一名拾荒客客人 + 首次达到 20 点的一枚「拾荒者信物」和一张便条（已生效）。
        /// 40：三个高级区（拆解堆 / 医疗废弃点 / 废武器堆）的统一解锁门槛（已生效）。
        /// 60：隐藏储藏与小奖机会提升。
        /// 80：贵重品出现率改为 80 门控（不到 80 按 0，到了 80 完全按滑条值，默认 3%）；四个区可翻到补给箱与对应钥匙卡（已生效）。
        /// 100：只落门槛与档名，功能待定。
        /// </summary>
        private static readonly int[] PerkThresholds =
        {
            PerkCatalog.Perk20Threshold, PerkCatalog.Perk40Threshold,
            PerkCatalog.Perk60Threshold, PerkCatalog.Perk80Threshold,
            PerkCatalog.Perk100Threshold
        };

        /// <summary>
        /// 图标的相对缩放，与 PerkThresholds 一一对应：20–80 逐级放大，100 回到基准大小。
        /// 20 微笑 0.65 → 40 微笑 0.78 → 60 笑脸 0.90 → 80 笑脸 1.00 → 100 五角星 1.00。
        /// 放大围绕图标中心，锚点位置不变；80 以上不再大于基准，避免和相邻图标叠在一起。
        /// </summary>
        private static readonly float[] PerkScales = { 0.65f, 0.78f, 0.90f, 1f, 1f };
        private static readonly string[] TierZh =
        {
            "生手", PerkCatalog.Perk20NameZh, PerkCatalog.Perk40NameZh,
            PerkCatalog.Perk60NameZh, PerkCatalog.Perk80NameZh, PerkCatalog.Perk100NameZh
        };
        private static readonly string[] TierEn =
        {
            "Rookie", PerkCatalog.Perk20NameEn, PerkCatalog.Perk40NameEn,
            PerkCatalog.Perk60NameEn, PerkCatalog.Perk80NameEn, PerkCatalog.Perk100NameEn
        };

        private static bool patchLogged;
        private static int openCount;
        private static int builtAtOpen = -1;
        private static int builtFrame = -1;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(ReputationUIManager), "OpenUI", new Type[0]);
            Debug.Log("[DumpingGroundMap] reputation row: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        /// <summary>
        /// 每次 OpenUI 开始先记一次“本次打开还没建行”。
        /// 不能用 layout.Find(RowName) 判断重复：OpenUI 里用的是 Destroy，
        /// 延迟到帧末才真正移除，Postfix 执行时上一轮的旧行还挂在 layout 下，
        /// 查找必定命中，于是第二次打开就永远跳过。
        /// </summary>
        private static void Prefix()
        {
            ++openCount;
        }

        private static void Postfix(ReputationUIManager __instance)
        {
            if (!Core.Enabled || __instance == null) return;
            int thisOpen = openCount;
            try
            {
                if (!patchLogged)
                {
                    patchLogged = true;
                    Debug.Log("[DumpingGroundMap] reputation row: postfix entered (disaster_day="
                        + ModHelper.IsModRunning(DisasterDayId) + ", rir="
                        + ModHelper.IsModRunning(ReputationRemakeId) + ")");
                }
                if (builtAtOpen == thisOpen && builtFrame == Time.frameCount)
                {
                    Debug.Log("[DumpingGroundMap] reputation row: skip, already built for open #"
                        + thisOpen + " children=" + (__instance.layout == null ? "?" : __instance.layout.transform.childCount.ToString(CultureInfo.InvariantCulture)));
                    return;
                }

                GameObject layout = __instance.layout;
                GameObject prefab = __instance.reputationPrefab;
                if (layout == null || prefab == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] reputation row: layout=" + (layout == null ? "null" : layout.name)
                        + " prefab=" + (prefab == null ? "null" : prefab.name));
                    return;
                }

                int points = Reputation.Points;
                StoreReputation entry = BuildEntry(points);

                GameObject row = UnityEngine.Object.Instantiate(prefab, layout.transform, false) as GameObject;
                if (row == null) return;
                row.name = RowName;

                ReputationElement element = row.GetComponent<ReputationElement>();
                if (element == null)
                {
                    UnityEngine.Object.Destroy(row);
                    Debug.LogWarning("[DumpingGroundMap] reputation row: prefab has no ReputationElement");
                    return;
                }

                List<PerkElement> before = CollectPerks(row);
                element.Init(entry);
                ApplyRow(element, points);
                StylePerks(row, points, __instance, before);
                PlaceRow(row, layout);
                builtAtOpen = thisOpen;
                builtFrame = Time.frameCount;
                Debug.Log("[DumpingGroundMap] reputation row: added " + points
                    + " open=#" + thisOpen
                    + " frame=" + builtFrame
                    + " active=" + row.activeSelf
                    + " parent=" + (row.transform.parent == null ? "null" : row.transform.parent.name)
                    + " children=" + layout.transform.childCount
                    + " local=" + row.transform.localPosition
                    + " size=" + (row.transform as RectTransform).sizeDelta);
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] reputation row failed: " + ex);
            }
        }

        /// <summary>合成一个只存在于内存里的派系声望，供原生 Init 读取。</summary>
        private static StoreReputation BuildEntry(int points)
        {
            StoreReputation entry = new StoreReputation();
            entry.factionDisplay = Copy.Text(TitleZh, TitleEn);
            entry.factionColor2 = TitleColor;
            entry.perks = BuildPerks(points);
            // 写精确值而不是整数点数：原版面板的数字由 ApplyRow 覆盖成两位小数，
            // 但「势力声望面板重制」是直接读 GetReputationExact() 重画，写整数就会变成 x.00。
            entry.SetReputation(Reputation.ExactPoints);
            return entry;
        }

        /// <summary>
        /// 五个奖励图标，阈值就是 20 / 40 / 60 / 80 / 100。20、40、80 已生效，
        /// 奖励文案与门槛由 PerkCatalog 定义；100 档额外待遇尚未开放。
        /// </summary>
        private static List<StoreReputationPerk> BuildPerks(int points)
        {
            List<StoreReputationPerk> perks = new List<StoreReputationPerk>(PerkThresholds.Length);
            for (int i = 0; i < PerkThresholds.Length; ++i)
            {
                StoreReputationPerk perk = new StoreReputationPerk();
                perk.perkId = string.Empty;
                perk.perkThreshold = PerkThresholds[i];
                perk.perkType = 1;
                perk.isUnlocked = points >= PerkThresholds[i];
                perks.Add(perk);
            }
            return perks;
        }

        /// <summary>
        /// Init 之后覆盖状态与数字：等级名 + 0–200 刻度上的整数点数。
        /// 柱子不再自己算高度，直接用原生 Init → UpdateBar → GetBarHeight 的结果。
        /// </summary>
        private static void ApplyRow(ReputationElement element, int points)
        {
            if (element.reputationStatus != null) element.reputationStatus.SetText(StatusName(points));
            if (element.reputationNumber != null) element.reputationNumber.SetText(NumberText());

            float barHeight = 0f;
            float containerHeight = 0f;
            if (element.greenImage != null)
            {
                barHeight = element.greenImage.rectTransform.rect.height;
                RectTransform parent = element.greenImage.rectTransform.parent as RectTransform;
                if (parent != null) containerHeight = parent.rect.height;
            }

            Debug.Log("[DumpingGroundMap] reputation row: text status="
                + (element.reputationStatus == null ? "missing" : StatusName(points))
                + " number=" + (element.reputationNumber == null ? "missing" : NumberText())
                + " bar=" + barHeight.ToString("F1") + "px"
                + " barParent=" + containerHeight.ToString("F1") + "px"
                + " expectedAt200=" + (BarFullHeight * 2f).ToString("F1") + "px");
            if (containerHeight > 0f && containerHeight < BarFullHeight * 2f - 1f)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation row: bar container is only "
                    + containerHeight.ToString("F1") + "px, 0–200 scale needs "
                    + (BarFullHeight * 2f).ToString("F1") + "px; 200 points will overflow");
            }
        }

        /// <summary>
        /// 图标：按 y 坐标（= GetBarHeight(阈值)）排序后依次对应 20 / 40 / 60 / 80 / 100。
        /// 20–80 用笑脸一族并逐级放大（20 微笑最小 → 40 微笑 → 60 笑脸 → 80 笑脸最大），
        /// 100 才用五角星作为最高档标记；未解锁 alpha 0.35。
        /// 预制体自带的 PerkElement 在 Init 前记进 before，Init 后移除，
        /// 这样五个图标的归属不依赖子物体索引（0.3.24 曾因此错位）。
        /// 每个图标挂上自己的 tooltip 组件，未解锁也能悬停预览。
        /// </summary>
        private static void StylePerks(GameObject row, int points,
            ReputationUIManager manager, List<PerkElement> before)
        {
            List<PerkElement> all = CollectPerks(row);
            List<PerkElement> fresh = new List<PerkElement>();
            for (int i = 0; i < all.Count; ++i)
            {
                PerkElement perk = all[i];
                if (perk == null || perk.image == null) continue;
                if (before.Contains(perk))
                {
                    perk.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(perk.gameObject);
                    continue;
                }
                fresh.Add(perk);
            }
            fresh.Sort(delegate(PerkElement a, PerkElement b)
            {
                return Y(a).CompareTo(Y(b));
            });

            string layout = string.Empty;
            for (int i = 0; i < fresh.Count; ++i)
            {
                PerkElement perk = fresh[i];
                int index = i < PerkThresholds.Length ? i : PerkThresholds.Length - 1;
                int threshold = PerkThresholds[index];

                if (index == 0) perk.image.sprite = manager.spriteSmile;
                else if (index == 1) perk.image.sprite = manager.spriteSmile;
                else if (index == 2) perk.image.sprite = manager.spriteHappy;
                else if (index == 3) perk.image.sprite = manager.spriteHappy;
                else perk.image.sprite = manager.spriteStar;

                float scale = PerkScales[index];
                perk.image.rectTransform.localScale = new Vector3(scale, scale, 1f);

                Color color = perk.image.color;
                color.a = points >= threshold ? 1f : 0.35f;
                perk.image.color = color;
                perk.image.raycastTarget = true;
                perk.enabled = false;

                DgmPerkTooltip tip = perk.gameObject.GetComponent<DgmPerkTooltip>();
                if (tip == null) tip = perk.gameObject.AddComponent<DgmPerkTooltip>();
                tip.Setup(manager, threshold, points >= threshold);

                layout += (i == 0 ? "" : " ") + threshold + "@" + Y(perk).ToString("F1")
                    + "/s" + scale.ToString("F2");
            }
            Debug.Log("[DumpingGroundMap] reputation row: perks total=" + all.Count
                + " used=" + fresh.Count + " stale=" + (all.Count - fresh.Count)
                + " points=" + points + " y=[" + layout + "]");
        }

        private static float Y(PerkElement perk)
        {
            RectTransform rect = perk.transform as RectTransform;
            return rect == null ? 0f : rect.anchoredPosition.y;
        }

        private static List<PerkElement> CollectPerks(GameObject row)
        {
            List<PerkElement> list = new List<PerkElement>();
            for (int i = 0; i < row.transform.childCount; ++i)
            {
                PerkElement perk = row.transform.GetChild(i).GetComponent<PerkElement>();
                if (perk != null) list.Add(perk);
            }
            return list;
        }

        private static string StatusName(int points)
        {
            int tier = 0;
            while (tier < Thresholds.Length && points >= Thresholds[tier]) ++tier;
            return Copy.Text(TierZh[tier], TierEn[tier]);
        }

        /// <summary>
        /// 与原生一致显示两位小数：累计价值 / 100，如 12.34/200。
        /// 柱高与图标解锁仍用整数点数（floor），见 Reputation.Points。
        /// </summary>
        private static string NumberText()
        {
            return Reputation.ExactPoints.ToString("F2", CultureInfo.InvariantCulture) + "/200";
        }

        /// <summary>原生 layout 若有布局组就重建；没有就按上一行的高度手动往下排。</summary>
        private static void PlaceRow(GameObject row, GameObject layout)
        {
            RectTransform rect = row.GetComponent<RectTransform>();
            RectTransform root = layout.GetComponent<RectTransform>();
            LayoutGroup group = layout.GetComponent<LayoutGroup>();
            Debug.Log("[DumpingGroundMap] reputation row: layout group="
                + (group == null ? "none" : group.GetType().Name)
                + " children=" + layout.transform.childCount
                + " root=" + (root == null ? "no RectTransform" : root.rect.ToString()));
            if (rect == null || root == null) return;

            if (group != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                Debug.Log("[DumpingGroundMap] reputation row: placed by layout group, anchored="
                    + rect.anchoredPosition + " size=" + rect.sizeDelta);
                return;
            }

            int count = layout.transform.childCount;
            if (count < 2)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation row: no sibling to align to");
                return;
            }
            RectTransform previous = layout.transform.GetChild(count - 2) as RectTransform;
            if (previous == null)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation row: previous row has no RectTransform");
                return;
            }
            float step = previous.rect.height > 0f ? previous.rect.height : rect.rect.height;
            rect.anchorMin = previous.anchorMin;
            rect.anchorMax = previous.anchorMax;
            rect.pivot = previous.pivot;
            rect.sizeDelta = previous.sizeDelta;
            rect.anchoredPosition = previous.anchoredPosition - new Vector2(0f, step);
            Debug.Log("[DumpingGroundMap] reputation row: placed by sibling, previous="
                + previous.anchoredPosition + " step=" + step
                + " anchored=" + rect.anchoredPosition + " size=" + rect.sizeDelta);
        }
    }
}
