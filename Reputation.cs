using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>
    /// 声望门槛表：区域 id → 解锁所需声望点数。没有列出的区域没有门槛。
    /// 想改门槛只改这张表，Region 构造时按 id 查一次。
    /// </summary>
    internal static class UnlockCost
    {
        private static readonly Dictionary<string, int> table =
            new Dictionary<string, int>
            {
                { "scrap", 40 },
                { "med", 40 },
                { "weapons", 40 }
            };

        internal static int For(string regionId)
        {
            int points;
            return table.TryGetValue(regionId, out points) ? points : 0;
        }
    }

    /// <summary>
    /// 声望。每趟拾荒捡到的东西按当前金币价值累计，每 100 点价值记 1 点声望。
    /// 按存档隔离：只写 PlayerStore.modData（ModHelper.Get/SetModDataInt），
    /// 随存档本体保存，一天结束时落盘。key 是 rep_total_value，存累计价值，
    /// 不足 100 的余数不会丢。中途强退时当晚声望会丢，与当晚其它收益一致。
    /// 不读写任何磁盘文件。
    /// </summary>
    internal static class Reputation
    {
        internal const long ValuePerPoint = 100L;
        internal const string DataKey = "rep_total_value";

        private static long totalValue;
        private static string loadedStore;
        private static bool hooked;

        internal static int Points { get { return (int)(totalValue / ValuePerPoint); } }

        internal static void Hook()
        {
            if (hooked) return;
            try
            {
                ModHook.OnGameLoadedLate += Reload;
                ModHook.OnNewGameStarted += NewGame;
                hooked = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] could not hook reputation: " + ex);
            }
        }

        internal static void Unhook()
        {
            if (!hooked) return;
            ModHook.OnGameLoadedLate -= Reload;
            ModHook.OnNewGameStarted -= NewGame;
            hooked = false;
        }

        /// <summary>新开一局：新的 PlayerStore.modData 是空字典，读到 0，自然归零。</summary>
        private static void NewGame(string saveName)
        {
            loadedStore = StoreKey();
            totalValue = 0L;
            Debug.Log("[DumpingGroundMap] reputation reset for new game");
        }

        /// <summary>存档加载完成后读一次；同一个存档重复触发时不再读。</summary>
        internal static void Reload()
        {
            string store = StoreKey();
            if (store == null || store == loadedStore) return;
            loadedStore = store;
            totalValue = StoredValue();
            Debug.Log("[DumpingGroundMap] reputation " + Points + " (" + store + ")");
        }

        /// <summary>把一趟拾荒的总价值折成声望；不足 100 的部分留在 modData 里继续累计。</summary>
        internal static void Add(long value)
        {
            if (value <= 0L) return;
            string store = StoreKey();
            if (store == null) return;
            if (store != loadedStore)
            {
                loadedStore = store;
                totalValue = StoredValue();
            }
            totalValue = WriteStored(totalValue + value);
        }

        private static string StoreKey()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null) return null;
            if (!string.IsNullOrEmpty(store.runID)) return store.runID;
            // 理论上不会走到这里；真拿不到 runID 时退化成按槽位区分，仅用于日志。
            return "slot_" + store.saveSlotId.ToString(CultureInfo.InvariantCulture);
        }

        private static long StoredValue()
        {
            try
            {
                int stored = ModHelper.GetModDataInt(Core.Id, DataKey, 0);
                return stored < 0 ? 0L : stored;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] could not read reputation mod data: " + ex.Message);
                return 0L;
            }
        }

        /// <summary>写入 PlayerStore.modData，返回实际写入（int 范围内）的累计价值。</summary>
        private static long WriteStored(long value)
        {
            if (value < 0L) value = 0L;
            if (value > int.MaxValue) value = int.MaxValue;
            try
            {
                ModHelper.SetModDataInt(Core.Id, DataKey, (int)value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] could not write reputation mod data: " + ex.Message);
            }
            return value;
        }
    }

    /// <summary>
    /// 每趟拾荒结束时结算声望：把这一趟捡到的所有物品按当前金币价值相加。
    /// GetRandomScavengedItem(GameItem&amp;) 是每次搜索真正产出物品的那一步；
    /// 无参重载只是转调它，所以只补这一个，避免重复计数。
    /// </summary>
    [HarmonyPatch]
    internal static class ScavengeReputationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ScavHelper), "GetRandomScavengedItem",
                new Type[] { typeof(GameItem).MakeByRefType() });
        }

        private static void Postfix(List<GameItem> __result)
        {
            if (!Core.Enabled || __result == null || __result.Count == 0) return;
            long sum = 0L;
            for (int i = 0; i < __result.Count; ++i)
            {
                GameItem item = __result[i];
                if (item == null) continue;
                try { sum += item.GetCurrentValue(false, true, true, false, 0L); }
                catch (Exception) { }
            }
            Reputation.Add(sum);
        }
    }

    /// <summary>
    /// 声望在评价箱（FeedbackBox → ReputationUIManager）里的展示。
    /// 在原生面板打开后追加一行「垃圾场」：复用原生行预制体与排版，
    /// 只在内存里合成一个 StoreReputation，不写 PlayerStore.storeReputations，
    /// 不落盘、不进 ES3。类型全部编译期绑定（typeof / 直接访问公开字段），
    /// 不使用字符串式反射或程序集扫描。
    /// 唯一的短路是 layout 下已存在同名行；不针对任何其它 mod 做兼容判断，
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

        private static readonly int[] Thresholds = { 40, 50, 60 };
        /// <summary>
        /// 柱子上的三个奖励点，与原生 perk 同一套刻度（0–200）。
        /// TODO 20：每天多一名拾荒者客人（未实现，仅图标占位）。
        /// 40：三个高级区（拆解堆 / 医疗废弃点 / 废武器堆）的统一解锁门槛，已生效。
        /// TODO 60：待定（未实现，仅图标占位）。
        /// </summary>
        private static readonly int[] PerkThresholds = { 20, 40, 60 };
        private static readonly string[] TierZh = { "生手", "熟脸", "老手", "活地图" };
        private static readonly string[] TierEn = { "Rookie", "Regular", "Old Hand", "Living Map" };

        private static bool patchLogged;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(ReputationUIManager), "OpenUI", new Type[0]);
            Debug.Log("[DumpingGroundMap] reputation row: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        private static void Postfix(ReputationUIManager __instance)
        {
            if (!Core.Enabled || __instance == null) return;
            try
            {
                if (!patchLogged)
                {
                    patchLogged = true;
                    Debug.Log("[DumpingGroundMap] reputation row: postfix entered (disaster_day="
                        + ModHelper.IsModRunning(DisasterDayId) + ", rir="
                        + ModHelper.IsModRunning(ReputationRemakeId) + ")");
                }

                GameObject layout = __instance.layout;
                GameObject prefab = __instance.reputationPrefab;
                if (layout == null || prefab == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] reputation row: layout=" + (layout == null ? "null" : layout.name)
                        + " prefab=" + (prefab == null ? "null" : prefab.name));
                    return;
                }
                if (layout.transform.Find(RowName) != null)
                {
                    Debug.Log("[DumpingGroundMap] reputation row: skip, already present");
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

                element.Init(entry);
                ApplyRow(element, points);
                StylePerks(row, points, __instance);
                PlaceRow(row, layout);
                Debug.Log("[DumpingGroundMap] reputation row: added " + points
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
            entry.SetReputation(points);
            return entry;
        }

        /// <summary>
        /// 三个奖励图标，阈值就是 20 / 40 / 60。只有 40 目前有实际效果
        /// （三个高级区的解锁门槛），20 与 60 仅占位，见 PerkThresholds 的 TODO。
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
            if (element.reputationNumber != null) element.reputationNumber.SetText(NumberText(points));

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
                + " number=" + (element.reputationNumber == null ? "missing" : NumberText(points))
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
        /// 图标：原生按阈值自动挑，40/60 会重复成同一个笑脸，所以按顺序覆盖成
        /// 微笑 / 笑脸 / 星星；未解锁 alpha 0.35。没有本地化词条，关掉射线避免空 tooltip。
        /// </summary>
        private static void StylePerks(GameObject row, int points, ReputationUIManager manager)
        {
            int index = 0;
            for (int i = 0; i < row.transform.childCount; ++i)
            {
                PerkElement perk = row.transform.GetChild(i).GetComponent<PerkElement>();
                if (perk == null || perk.image == null) continue;

                if (index == 0) perk.image.sprite = manager.spriteSmile;
                else if (index == 1) perk.image.sprite = manager.spriteHappy;
                else perk.image.sprite = manager.spriteStar;

                int threshold = index < PerkThresholds.Length
                    ? PerkThresholds[index] : PerkThresholds[PerkThresholds.Length - 1];
                Color color = perk.image.color;
                color.a = points >= threshold ? 1f : 0.35f;
                perk.image.color = color;
                perk.image.raycastTarget = false;
                ++index;
            }
            Debug.Log("[DumpingGroundMap] reputation row: perks=" + index
                + " points=" + points);
        }

        private static string StatusName(int points)
        {
            int tier = 0;
            while (tier < Thresholds.Length && points >= Thresholds[tier]) ++tier;
            return Copy.Text(TierZh[tier], TierEn[tier]);
        }

        /// <summary>原生同一套数字：整数点数 / 200。</summary>
        private static string NumberText(int points)
        {
            return points.ToString(CultureInfo.InvariantCulture) + "/200";
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
