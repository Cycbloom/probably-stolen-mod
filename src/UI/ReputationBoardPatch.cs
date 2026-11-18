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
        private const string TitleKey = "reputation_title";
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
        /// 20：拆解堆/泵站，基础收益+10%，每天多一名拾荒客。
        /// 40：车场/医疗，基础收益+20%，离场付费搬运。
        /// 60：电子/武器，基础收益+30%，隐藏储藏与小奖机会提升。
        /// 80：开放贵重品。
        /// 100：开放八区补给箱及配套钥匙卡，发放信物、便签，解锁 20 信用点的次日同行夜间服务。
        /// </summary>
        private static readonly int[] PerkThresholds =
        {
            PerkCatalog.Perk20Threshold, PerkCatalog.Perk40Threshold,
            PerkCatalog.Perk60Threshold, PerkCatalog.Perk80Threshold,
            PerkCatalog.Perk100Threshold
        };

        private static readonly string[] TierKeys =
        {
            "perk_rookie_name", PerkCatalog.Perk20NameKey, PerkCatalog.Perk40NameKey,
            PerkCatalog.Perk60NameKey, PerkCatalog.Perk80NameKey, PerkCatalog.Perk100NameKey
        };

        private static bool patchLogged;
        private static int openCount;
        private static int builtAtOpen = -1;
        private static int builtFrame = -1;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(ReputationUIManager), "OpenUI", new Type[0]);
            ModLogger.Info("reputation row: patch target "
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
                    ModLogger.Info("reputation row: postfix entered (disaster_day="
                        + ModHelper.IsModRunning(DisasterDayId) + ", rir="
                        + ModHelper.IsModRunning(ReputationRemakeId) + ")");
                }
                if (builtAtOpen == thisOpen && builtFrame == Time.frameCount)
                {
                    ModLogger.Info("reputation row: skip, already built for open #"
                        + thisOpen + " children=" + (__instance.layout == null ? "?" : __instance.layout.transform.childCount.ToString(CultureInfo.InvariantCulture)));
                    return;
                }

                GameObject layout = __instance.layout;
                GameObject prefab = __instance.reputationPrefab;
                if (layout == null || prefab == null)
                {
                    ModLogger.Warning("reputation row: layout=" + (layout == null ? "null" : layout.name)
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
                    ModLogger.Warning("reputation row: prefab has no ReputationElement");
                    return;
                }

                List<PerkElement> before = CollectPerks(row);
                element.Init(entry);
                ApplyRow(element, points);
                StylePerks(row, points, __instance, before);
                PlaceRow(row, layout);
                builtAtOpen = thisOpen;
                builtFrame = Time.frameCount;
                ModLogger.Info("reputation row: added " + points
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
                ModLogger.Error("reputation row failed: " + ex);
            }
        }

        /// <summary>合成一个只存在于内存里的派系声望，供原生 Init 读取。</summary>
        private static StoreReputation BuildEntry(int points)
        {
            StoreReputation entry = new StoreReputation();
            entry.factionDisplay = Copy.Get(TitleKey);
            entry.factionColor2 = TitleColor;
            entry.perks = BuildPerks(points);
            // 写精确值而不是整数点数：原版面板的数字由 ApplyRow 覆盖成两位小数，
            // 但「势力声望面板重制」是直接读 GetReputationExact() 重画，写整数就会变成 x.00。
            entry.SetReputation(Reputation.ExactPoints);
            return entry;
        }

        /// <summary>
        /// 五个奖励图标，阈值就是 20 / 40 / 60 / 80 / 100。20、40、80 已生效，
        /// 奖励文案与门槛由 PerkCatalog 定义。
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

            ModLogger.Info("reputation row: text status="
                + (element.reputationStatus == null ? "missing" : StatusName(points))
                + " number=" + (element.reputationNumber == null ? "missing" : NumberText())
                + " bar=" + barHeight.ToString("F1") + "px"
                + " barParent=" + containerHeight.ToString("F1") + "px"
                + " expectedAt200=" + (BarFullHeight * 2f).ToString("F1") + "px");
            if (containerHeight > 0f && containerHeight < BarFullHeight * 2f - 1f)
            {
                ModLogger.Warning("reputation row: bar container is only "
                    + containerHeight.ToString("F1") + "px, 0–200 scale needs "
                    + (BarFullHeight * 2f).ToString("F1") + "px; 200 points will overflow");
            }
        }

        /// <summary>
        /// 图标：按 y 坐标（= GetBarHeight(阈值)）排序后依次对应 20 / 40 / 60 / 80 / 100。
        /// 20–80 用笑脸一族，所有奖励图标统一使用原版预制体的尺寸，
        /// 100 才用五角星作为最高档标记；未解锁 alpha 0.35。
        /// 预制体自带的 PerkElement 在 Init 前记进 before，Init 后移除，
        /// 这样五个图标的归属不依赖子物体索引（0.3.24 曾因此错位）。
        /// 图标只做展示；悬停由 DgmPerkHover 写下方说明区，不弹浮动提示框。
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

                perk.image.rectTransform.localScale = Vector3.one;

                Color color = perk.image.color;
                color.a = points >= threshold ? 1f : 0.35f;
                perk.image.color = color;
                // 悬停只喂下方说明区、不弹浮动提示框：恢复射线检测与自定义悬停组件，
                // 同时停用原生 PerkElement，避免它写入原生 Referral / Active 文案。
                perk.image.raycastTarget = true;
                perk.enabled = false;
                DgmPerkHover hover = perk.gameObject.GetComponent<DgmPerkHover>();
                if (hover == null) hover = perk.gameObject.AddComponent<DgmPerkHover>();
                hover.Setup(manager, threshold, points >= threshold);

                layout += (i == 0 ? "" : " ") + threshold + "@" + Y(perk).ToString("F1")
                    + "/s1.00";
            }
            ModLogger.Info("reputation row: perks total=" + all.Count
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
            return Copy.Get(TierKeys[tier]);
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
            ModLogger.Info("reputation row: layout group="
                + (group == null ? "none" : group.GetType().Name)
                + " children=" + layout.transform.childCount
                + " root=" + (root == null ? "no RectTransform" : root.rect.ToString()));
            if (rect == null || root == null) return;

            if (group != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                ModLogger.Info("reputation row: placed by layout group, anchored="
                    + rect.anchoredPosition + " size=" + rect.sizeDelta);
                return;
            }

            int count = layout.transform.childCount;
            if (count < 2)
            {
                ModLogger.Warning("reputation row: no sibling to align to");
                return;
            }
            RectTransform previous = layout.transform.GetChild(count - 2) as RectTransform;
            if (previous == null)
            {
                ModLogger.Warning("reputation row: previous row has no RectTransform");
                return;
            }
            float step = previous.rect.height > 0f ? previous.rect.height : rect.rect.height;
            rect.anchorMin = previous.anchorMin;
            rect.anchorMax = previous.anchorMax;
            rect.pivot = previous.pivot;
            rect.sizeDelta = previous.sizeDelta;
            rect.anchoredPosition = previous.anchoredPosition - new Vector2(0f, step);
            ModLogger.Info("reputation row: placed by sibling, previous="
                + previous.anchoredPosition + " step=" + step
                + " anchored=" + rect.anchoredPosition + " size=" + rect.sizeDelta);
        }
    }
}
