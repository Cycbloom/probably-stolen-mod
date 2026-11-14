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
    /// 随存档本体保存。key 是 rep_total_value，存累计价值，
    /// 不足 100 的余数不会丢。中途强退时当晚声望会丢，与当晚其它收益一致。
    ///
    /// 调试项的特殊路径（0.3.35）：
    /// - 改设置时没有存档（主菜单只有这里能打开模组设置）：不能碰
    ///   PlayerStore.Instance——getter 会新建 PlayerStore，构造函数里的
    ///   GameGridInventory/TreeNodeRender 需要 Emporium 场景，在主菜单必抛
    ///   NullReferenceException。改为把点数存进游戏自己的 SaveFiles 索引，
    ///   等下一次读档/新档应用一次。
    /// - 有存档时立即调用原生 PlayerStore.SaveGame() 落盘，不等日终。
    /// 两条路径都只用游戏自己的接口，不自己写文件。
    /// </summary>
    internal static class Reputation
    {
        internal const long ValuePerPoint = 100L;
        internal const string DataKey = "rep_total_value";
        private const string PendingFlagKey = "dgm_debug_pending";
        private const string PendingPointsKey = "dgm_debug_pending_points";

        /// <summary>五档门槛与名称。改档名只改这几行，其它地方不要再写死。</summary>
        internal const int Perk20Threshold = 20;
        internal const string Perk20NameZh = "拾荒老手";
        internal const string Perk20NameEn = "Scavenger Veteran";

        internal const int Perk40Threshold = 40;
        internal const string Perk40NameZh = "熟门熟路";
        internal const string Perk40NameEn = "Knows the Back Ways";

        /// <summary>60 档：只落门槛与档名，功能待定。</summary>
        internal const int Perk60Threshold = 60;
        internal const string Perk60NameZh = "眼力毒辣";
        internal const string Perk60NameEn = "Keen Eye";

        /// <summary>80 档：高档出现率 80 门控，高级区可翻到补给箱与对应钥匙卡。</summary>
        internal const int Perk80Threshold = 80;
        internal const string Perk80NameZh = "识货行家";
        internal const string Perk80NameEn = "Connoisseur";

        /// <summary>100 档：只落门槛与档名，功能待定。</summary>
        internal const int Perk100Threshold = 100;
        internal const string Perk100NameZh = "自己人";
        internal const string Perk100NameEn = "One of Us";

        /// <summary>客人卡上的来访原因（原生字段只能填类型/物品名，这里在 UI 侧改写）。</summary>
        internal static string Perk20Reason
        {
            get
            {
                return Copy.Text("被「" + Perk20NameZh + "」的名声吸引",
                    "Drawn by a " + Perk20NameEn + "'s reputation");
            }
        }

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
                ModHook.OnShutterOpenedLate += ScavMedal.OnShutterOpenedLate;
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
            ModHook.OnShutterOpenedLate -= ScavMedal.OnShutterOpenedLate;
            hooked = false;
        }

        /// <summary>新开一局：新的 PlayerStore.modData 是空字典，读到 0，自然归零。</summary>
        private static void NewGame(string saveName)
        {
            loadedStore = StoreKey();
            totalValue = 0L;
            ScavengerPerkPatch.ResetCache();
            ApplyPendingDebugPoints(false);
            Debug.Log("[DumpingGroundMap] reputation reset for new game");
        }

        /// <summary>
        /// 存档加载完成后无条件重读一次。
        ///
        /// 不能用「runID 没变就跳过」：同一局里读回关店前的自动存档时 runID 不变，
        /// 但 modData 里是旧值，跳过就会让内存里的静态值保留读档后的较新数值
        /// （Shutter Autosave 这类快照存档会踩到）。每次都用存档里的值覆盖。
        /// </summary>
        internal static void Reload()
        {
            string store = StoreKey();
            if (store == null)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation load skipped: no PlayerStore");
                return;
            }
            long previous = totalValue;
            loadedStore = store;
            totalValue = StoredValue();
            ScavengerPerkPatch.ResetCache();
            Debug.Log("[DumpingGroundMap] reputation load: id=" + Core.Id + " key=" + DataKey
                + " store=" + store + " value=" + totalValue + " points=" + Points
                + " previous=" + previous);
            // 主菜单改的调试值在这里补上，并立即存档，避免下次重启又丢。
            ApplyPendingDebugPoints(true);
        }

        /// <summary>累计价值，供日志核对。</summary>
        internal static long TotalValue { get { return totalValue; } }

        /// <summary>未取整的声望点数（累计价值 / 100），用于两位小数显示。</summary>
        internal static float ExactPoints { get { return totalValue / (float)ValuePerPoint; } }

        /// <summary>把一趟拾荒的总价值折成声望；不足 100 的部分留在 modData 里继续累计。</summary>
        internal static void Add(long value)
        {
            if (value <= 0L) return;
            string store = StoreKey();
            if (store == null)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenge: reputation add skipped, no PlayerStore (value="
                    + value + ")");
                return;
            }
            if (store != loadedStore)
            {
                loadedStore = store;
                totalValue = StoredValue();
            }
            totalValue = WriteStored(totalValue + value);
        }

        /// <summary>
        /// 调试用：把声望直接设成给定点数。只写同一个 rep_total_value 键，
        /// 内存态立即同步，返回实际写入的累计价值。
        /// 没有存档时什么都不做（绝不碰 PlayerStore.Instance），由调用方排队。
        /// </summary>
        internal static long SetPoints(float points)
        {
            if (points < 0f) points = 0f;
            long value = ToStored(points);
            if (!HasStore)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: set points skipped, no save loaded");
                return 0L;
            }
            loadedStore = StoreKey();
            totalValue = WriteStored(value);
            return totalValue;
        }

        /// <summary>是否有已加载的存档。PlayerStore.Instance 在无存档时会新建并抛异常。</summary>
        internal static bool HasStore { get { return PlayerStore.HasInstance; } }

        /// <summary>
        /// 设置窗口改动的统一入口。
        ///
        /// 只有「有存档 + Emporium 场景已加载」时才直接写内存并立即调原生 SaveGame；
        /// 其它情况（主菜单 EmporiumMenu、读档过程中）一律排队，等下次读档/新档应用。
        ///
        /// 0.3.43 的 bug：只判断了 PlayerStore.HasInstance。回到主菜单后 PlayerStore
        /// 对象仍在内存里（HasInstance 为 true），但 EmporiumEntry 已销毁，
        /// PlayerStore.SaveGame() 第一步就解引用 EmporiumEntry.Instance 而失败；
        /// 值只留在内存，紧接着读档又被存档里的旧值覆盖，看起来就是「没生效」。
        /// </summary>
        internal static void ApplyDebugPoints(float points)
        {
            if (points < 0f) points = 0f;
            if (!HasStore || EmporiumEntry.Instance == null)
            {
                string why = HasStore ? "Emporium not loaded" : "no save loaded";
                Debug.Log("[DumpingGroundMap] debug: " + why + ", applying on next load");
                QueueDebugPoints(points, why);
                return;
            }
            long stored = SetPoints(points);
            Debug.Log("[DumpingGroundMap] debug: reputation set to "
                + points.ToString("0.##", CultureInfo.InvariantCulture)
                + " points (value=" + stored + ")");
            // 不等日终：用原生保存入口落盘（延后一帧，避开场景初始化），否则强退就丢。
            DeferredSave.Run("debug");
        }

        /// <summary>
        /// 主菜单/无法立即存档时改调试点数：先存进游戏自己的 SaveFiles 索引，
        /// 下次读档/新档时应用一次。why 只用于日志。
        /// </summary>
        internal static bool QueueDebugPoints(float points, string why)
        {
            try
            {
                SaveFiles.IndexSave<bool>(PendingFlagKey, true);
                SaveFiles.IndexSave<float>(PendingPointsKey, points);
                Debug.Log("[DumpingGroundMap] debug: queued "
                    + points.ToString("0.##", CultureInfo.InvariantCulture)
                    + " points for next load (" + why + ")");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not queue points: " + ex.Message);
                return false;
            }
        }

        internal static void ClearPendingDebugPoints()
        {
            try
            {
                SaveFiles.IndexDeleteKey(PendingFlagKey);
                SaveFiles.IndexDeleteKey(PendingPointsKey);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not clear pending points: "
                    + ex.Message);
            }
        }

        /// <summary>
        /// 把排队的调试点数应用一次。读档时 saveNow=true（立刻落盘），
        /// 新档时 false——新档的 InitialSave 由原生 EmporiumEntry.Start 负责，
        /// 那里 PlayerStore 还没初始化完，我们自己调 SaveGame 不安全。
        /// </summary>
        private static void ApplyPendingDebugPoints(bool saveNow)
        {
            bool pending;
            try
            {
                pending = SaveFiles.IndexLoad<bool>(PendingFlagKey, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not read pending flag: " + ex.Message);
                return;
            }
            if (!pending) return;
            if (!HasStore)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: pending points kept, no PlayerStore yet");
                return;
            }
            float points;
            try
            {
                points = SaveFiles.IndexLoad<float>(PendingPointsKey, 0f);
            }
            catch (Exception)
            {
                points = 0f;
            }
            ClearPendingDebugPoints();
            loadedStore = StoreKey();
            totalValue = WriteStored(ToStored(points));
            Debug.Log("[DumpingGroundMap] debug: applied queued "
                + points.ToString("0.##", CultureInfo.InvariantCulture)
                + " points (value=" + totalValue + " saveNow=" + (saveNow ? "yes" : "no") + ")");
            if (saveNow) DeferredSave.Run("queued");
        }

        /// <summary>
        /// 用游戏自己的保存入口立即落盘，不等日终。PlayerStore.SaveGame() 内部
        /// 会处理槽文件被占用/损坏的情况，与日终存档走同一条写盘路径。
        /// </summary>
        internal static bool SaveNow(string reason)
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                if (store == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] save now (" + reason + "): no PlayerStore");
                    return false;
                }
                store.SaveGame();
                Debug.Log("[DumpingGroundMap] save now (" + reason + "): slot="
                    + store.saveSlotId + " day=" + StoreStation.GetDayCounter()
                    + " value=" + totalValue + " points=" + Points);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] save now (" + reason + ") failed: " + Describe(ex));
                // 落盘失败时不能把值留在内存里等下次读档被覆盖：排队，读档时再应用。
                // 只有调试项会走到这里（"debug" 直接改、"queued" 应用排队值时补存），
                // 两条都该保住这个值。
                float points = ExactPoints;
                QueueDebugPoints(points, "save failed: " + reason);
                Debug.LogWarning("[DumpingGroundMap] debug: save failed, "
                    + points.ToString("0.##", CultureInfo.InvariantCulture)
                    + " points queued for next load");
                return false;
            }
        }

        /// <summary>异常类型 + 消息。消息为空时至少能看出是哪一类异常。</summary>
        private static string Describe(Exception ex)
        {
            if (ex == null) return "(null)";
            string text = ex.GetType().Name;
            if (!string.IsNullOrEmpty(ex.Message)) text += ": " + ex.Message;
            return text;
        }

        private static long ToStored(float points)
        {
            if (points < 0f) points = 0f;
            double raw = points * (double)ValuePerPoint;
            long value = raw >= (double)int.MaxValue ? (long)int.MaxValue : (long)Math.Round(raw);
            return value < 0L ? 0L : value;
        }

        private static string StoreKey()
        {
            // 必须先看 HasInstance：get_Instance 在无存档时会新建 PlayerStore，
            // 构造函数依赖 Emporium 场景，在主菜单调用会抛 NullReferenceException。
            if (!PlayerStore.HasInstance) return null;
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
    /// 延后一帧再存档。读档时 FireOnGameLoadedLate 位于 PlayerStore.LoadGame
    /// 末尾、EmporiumEntry.Start 还没跑完，当场 SaveGame 会快照到未完成的界面
    /// 状态；等到 LateUpdate 时场景初始化已完成，与日终存档看到的状态一致。
    /// MonoBehaviour 必须是顶层类，Unity 才能 AddComponent。
    /// </summary>
    internal sealed class DeferredSave : MonoBehaviour
    {
        private static DeferredSave pending;
        private string reason;

        internal static void Run(string why)
        {
            if (pending != null)
            {
                if (string.IsNullOrEmpty(pending.reason)) pending.reason = why;
                return;
            }
            GameObject go = new GameObject("DGM_DeferredSave");
            UnityEngine.Object.DontDestroyOnLoad(go);
            pending = go.AddComponent<DeferredSave>();
            pending.reason = why;
        }

        private void LateUpdate()
        {
            if (string.IsNullOrEmpty(reason)) return;
            string why = reason;
            reason = null;
            pending = null;
            try
            {
                Reputation.SaveNow(why);
            }
            finally
            {
                UnityEngine.Object.Destroy(gameObject);
            }
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
            MethodBase method = AccessTools.Method(typeof(ScavHelper), "GetRandomScavengedItem",
                new Type[] { typeof(GameItem).MakeByRefType() });
            Debug.Log("[DumpingGroundMap] scavenge: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        private static void Postfix(List<GameItem> __result)
        {
            if (!Core.Enabled) return;
            int count = __result == null ? 0 : __result.Count;
            long sum = 0L;
            for (int i = 0; i < count; ++i)
            {
                GameItem item = __result[i];
                if (item == null) continue;
                try { sum += item.GetCurrentValue(false, true, true, false, 0L); }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DumpingGroundMap] scavenge: value failed: " + ex.Message);
                }
            }
            Reputation.Add(sum);
            ReputationPopBridge.Show(sum);
            Debug.Log("[DumpingGroundMap] scavenge: items=" + count + " value=" + sum
                + " total=" + Reputation.TotalValue + " points=" + Reputation.Points);
            if (count > 0 && sum <= 0L)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenge: items found but total value is 0");
            }
        }
    }

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
            Reputation.Perk20Threshold, Reputation.Perk40Threshold,
            Reputation.Perk60Threshold, Reputation.Perk80Threshold,
            Reputation.Perk100Threshold
        };
        /// <summary>
        /// 柱子上的五个奖励点，与原生 perk 同一套刻度（0–200）。
        /// 20：每天多一名拾荒客客人 + 首次达到 20 点的一枚「拾荒者信物」和一张便条（已生效）。
        /// 40：三个高级区（拆解堆 / 医疗废弃点 / 废武器堆）的统一解锁门槛（已生效）。
        /// 60：只落门槛与档名，功能待定。
        /// 80：高档出现率改为 80 门控（不到 80 按 0，到了 80 完全按滑条值，默认 3%）；四个区可翻到补给箱与对应钥匙卡（已生效）。
        /// 100：只落门槛与档名，功能待定。
        /// </summary>
        private static readonly int[] PerkThresholds =
        {
            Reputation.Perk20Threshold, Reputation.Perk40Threshold,
            Reputation.Perk60Threshold, Reputation.Perk80Threshold,
            Reputation.Perk100Threshold
        };

        /// <summary>
        /// 图标的相对缩放，与 PerkThresholds 一一对应：20–80 逐级放大，100 回到基准大小。
        /// 20 微笑 0.65 → 40 微笑 0.78 → 60 笑脸 0.90 → 80 笑脸 1.00 → 100 五角星 1.00。
        /// 放大围绕图标中心，锚点位置不变；80 以上不再大于基准，避免和相邻图标叠在一起。
        /// </summary>
        private static readonly float[] PerkScales = { 0.65f, 0.78f, 0.90f, 1f, 1f };
        private static readonly string[] TierZh =
        {
            "生手", Reputation.Perk20NameZh, Reputation.Perk40NameZh,
            Reputation.Perk60NameZh, Reputation.Perk80NameZh, Reputation.Perk100NameZh
        };
        private static readonly string[] TierEn =
        {
            "Rookie", Reputation.Perk20NameEn, Reputation.Perk40NameEn,
            Reputation.Perk60NameEn, Reputation.Perk80NameEn, Reputation.Perk100NameEn
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
            entry.SetReputation(points);
            return entry;
        }

        /// <summary>
        /// 五个奖励图标，阈值就是 20 / 40 / 60 / 80 / 100。20、40、80 已生效，
        /// 60 与 100 目前只有档名与占位说明，见 PerkThresholds 的注释。
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
                    "拾荒时可能遇到高档物资。偶尔还能翻到补给箱和对应的钥匙卡。",
                    "While scavenging you may come across high-value goods. "
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
                    "你对这片场子越来越熟。三个高级拾荒场对你开放：拆解堆、医疗废弃点、废武器堆。",
                    "You know this place better and better. "
                    + "Three high-tier scavenging grounds are open to you: "
                    + "Scrap Pile, Medical Waste, Weapon Dump.");
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
