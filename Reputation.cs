using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TMPro;
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
                { "med", 50 },
                { "weapons", 60 }
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
    /// 不落盘、不进 ES3。字段与方法全部按名字取，取不到就整行跳过。
    /// 装了 災變日 / ReputationInterfaceRemake 时让路，避免空行或双行。
    /// </summary>
    [HarmonyPatch]
    internal static class ReputationBoardPatch
    {
        private const string RowName = "DGM_ReputationRow";
        private const string TitleZh = "垃圾场";
        private const string TitleEn = "The Dump";
        private const string TitleColor = "8FA06A";
        private const float BarFullHeight = 65.5f;

        private static readonly int[] Thresholds = { 40, 50, 60 };
        private static readonly string[] TierZh = { "拾荒客", "熟脸", "老手", "活地图" };
        private static readonly string[] TierEn = { "Scavenger", "Regular", "Old Hand", "Living Map" };

        private static Type uiType;
        private static Type elementType;
        private static Type repType;
        private static FieldInfo layoutField;
        private static FieldInfo prefabField;
        private static FieldInfo statusField;
        private static FieldInfo numberField;
        private static FieldInfo greenField;
        private static FieldInfo redField;
        private static FieldInfo displayField;
        private static FieldInfo colorField;
        private static FieldInfo perksField;
        private static MethodInfo initMethod;
        private static MethodInfo setRepMethod;
        private static bool resolved;
        private static bool usable;
        private static bool foreignChecked;
        private static bool foreign;

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("ReputationUIManager");
            return type == null ? null : AccessTools.Method(type, "OpenUI", new Type[0]);
        }

        private static void Postfix(ReputationUIManager __instance)
        {
            if (!Core.Enabled || __instance == null) return;
            try
            {
                if (!Resolve() || ForeignRunning()) return;
                GameObject layout = Read(layoutField, __instance) as GameObject;
                GameObject prefab = Read(prefabField, __instance) as GameObject;
                if (layout == null || prefab == null) return;
                if (layout.transform.Find(RowName) != null) return;

                int points = Reputation.Points;
                object entry = BuildEntry(points);
                if (entry == null) return;

                GameObject row = UnityEngine.Object.Instantiate(prefab, layout.transform, false) as GameObject;
                if (row == null) return;
                row.name = RowName;

                Component element = row.GetComponent(elementType);
                if (element == null)
                {
                    UnityEngine.Object.Destroy(row);
                    return;
                }

                initMethod.Invoke(element, new object[] { entry });
                ApplyRow(element, points);
                PlaceRow(row, layout);
                Debug.Log("[DumpingGroundMap] reputation row shown: " + points);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation row skipped: " + ex.Message);
            }
        }

        /// <summary>合成一个只存在于内存里的派系声望，供原生 Init 读取。</summary>
        private static object BuildEntry(int points)
        {
            object entry = Activator.CreateInstance(repType);
            if (entry == null) return null;

            Write(displayField, entry, Copy.Text(TitleZh, TitleEn));
            Write(colorField, entry, TitleColor);
            if (perksField != null && perksField.FieldType.IsGenericType)
            {
                Type perkType = perksField.FieldType.GetGenericArguments()[0];
                Write(perksField, entry, Activator.CreateInstance(typeof(List<>).MakeGenericType(perkType)));
            }
            setRepMethod.Invoke(entry, new object[] { (double)points });
            return entry;
        }

        /// <summary>Init 之后覆盖状态、数字与条：等级名 + 下一门槛 + 0–60 总进度。</summary>
        private static void ApplyRow(object element, int points)
        {
            TextMeshProUGUI status = Read(statusField, element) as TextMeshProUGUI;
            if (status != null) status.SetText(StatusName(points));

            TextMeshProUGUI number = Read(numberField, element) as TextMeshProUGUI;
            if (number != null) number.SetText(NumberText(points));

            Image green = Read(greenField, element) as Image;
            if (green != null)
            {
                green.gameObject.SetActive(true);
                RectTransform rect = green.rectTransform;
                Vector2 size = rect.sizeDelta;
                size.y = BarFullHeight * BarRatio(points);
                rect.sizeDelta = size;
            }
            Image red = Read(redField, element) as Image;
            if (red != null) red.gameObject.SetActive(false);
        }

        private static string StatusName(int points)
        {
            int tier = 0;
            while (tier < Thresholds.Length && points >= Thresholds[tier]) ++tier;
            return Copy.Text(TierZh[tier], TierEn[tier]);
        }

        /// <summary>未满 60 显示「当前/下一门槛」，满 60 显示累计点数。</summary>
        private static string NumberText(int points)
        {
            int next = NextThreshold(points);
            if (next <= 0) return points.ToString(CultureInfo.InvariantCulture);
            return points.ToString(CultureInfo.InvariantCulture) + "/"
                + next.ToString(CultureInfo.InvariantCulture);
        }

        private static int NextThreshold(int points)
        {
            for (int i = 0; i < Thresholds.Length; ++i)
            {
                if (points < Thresholds[i]) return Thresholds[i];
            }
            return 0;
        }

        /// <summary>条按 0–60 总进度，与原生的 ±200 定标无关，避免 60 点只有三成高。</summary>
        private static float BarRatio(int points)
        {
            if (points <= 0) return 0f;
            float ratio = points / (float)Thresholds[Thresholds.Length - 1];
            return ratio > 1f ? 1f : ratio;
        }

        /// <summary>原生 layout 若有布局组就重建；没有就按上一行的高度手动往下排。</summary>
        private static void PlaceRow(GameObject row, GameObject layout)
        {
            RectTransform rect = row.GetComponent<RectTransform>();
            RectTransform root = layout.GetComponent<RectTransform>();
            if (rect == null || root == null) return;

            if (layout.GetComponent<LayoutGroup>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                return;
            }

            int count = layout.transform.childCount;
            if (count < 2) return;
            RectTransform previous = layout.transform.GetChild(count - 2) as RectTransform;
            if (previous == null) return;
            float step = previous.rect.height > 0f ? previous.rect.height : rect.rect.height;
            rect.anchorMin = previous.anchorMin;
            rect.anchorMax = previous.anchorMax;
            rect.pivot = previous.pivot;
            rect.sizeDelta = previous.sizeDelta;
            rect.anchoredPosition = previous.anchoredPosition - new Vector2(0f, step);
        }

        private static bool Resolve()
        {
            if (resolved) return usable;
            resolved = true;
            uiType = AccessTools.TypeByName("ReputationUIManager");
            elementType = AccessTools.TypeByName("ReputationElement");
            repType = AccessTools.TypeByName("StoreReputation");
            if (uiType == null || elementType == null || repType == null) return false;

            layoutField = AccessTools.Field(uiType, "layout");
            prefabField = AccessTools.Field(uiType, "reputationPrefab");
            statusField = AccessTools.Field(elementType, "reputationStatus");
            numberField = AccessTools.Field(elementType, "reputationNumber");
            greenField = AccessTools.Field(elementType, "greenImage");
            redField = AccessTools.Field(elementType, "redImage");
            displayField = AccessTools.Field(repType, "factionDisplay");
            colorField = AccessTools.Field(repType, "factionColor2");
            perksField = AccessTools.Field(repType, "perks");
            initMethod = AccessTools.Method(elementType, "Init", new Type[] { repType });
            setRepMethod = AccessTools.Method(repType, "SetReputation", new Type[] { typeof(double) });

            usable = layoutField != null && prefabField != null && displayField != null
                && colorField != null && perksField != null && initMethod != null
                && setRepMethod != null && statusField != null && numberField != null
                && greenField != null;
            if (!usable) Debug.LogWarning("[DumpingGroundMap] reputation row: native API not found");
            return usable;
        }

        /// <summary>災變日 与 ReputationInterfaceRemake 都会重写这块面板，存在时让路。</summary>
        private static bool ForeignRunning()
        {
            if (foreignChecked) return foreign;
            foreignChecked = true;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; ++i)
            {
                string name = all[i].GetName().Name;
                if (name == "DisasterDayMod" || name == "ReputationInterfaceRemake")
                {
                    foreign = true;
                    Debug.Log("[DumpingGroundMap] reputation row skipped: " + name + " is loaded");
                    break;
                }
            }
            return foreign;
        }

        private static object Read(FieldInfo field, object target)
        {
            if (field == null || target == null) return null;
            return field.GetValue(target);
        }

        private static void Write(FieldInfo field, object target, object value)
        {
            if (field == null || target == null) return;
            field.SetValue(target, value);
        }
    }
}
