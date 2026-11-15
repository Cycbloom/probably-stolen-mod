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
    /// 声望。每趟拾荒捡到的东西按当前金币价值累计，每 100 点价值记 1 点声望。
    /// 难度会乘在「本次新增的累计价值」上：倍率直接读游戏的
    /// Difficulty.Rules.reputationGainMultiplier（原生 Difficulty.Vanilla 里
    /// cozy 1.5 / normal 1 / hard 0.75 / challenge 0.5），因此最高难度
    /// 「赤字连连」是 200 价值 1 点、最低难度「价格欺诈」是约 66.7 价值 1 点。
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
        private const string PendingRunKey = "dgm_debug_pending_run";
        private const string PendingSlotKey = "dgm_debug_pending_slot";

        /// <summary>客人卡上的来访原因（原生字段只能填类型/物品名，这里在 UI 侧改写）。</summary>
        internal static string Perk20Reason
        {
            get
            {
                return Copy.Text("被「" + PerkCatalog.Perk20NameZh + "」的名声吸引",
                    "Drawn by a " + PerkCatalog.Perk20NameEn + "'s reputation");
            }
        }

        private static long totalValue;
        private static string loadedStore;
        private static bool multiplierWarned;

        internal static int Points { get { return (int)(totalValue / ValuePerPoint); } }

        internal static void ResetSession()
        {
            loadedStore = null;
            totalValue = 0L;
            ScavengerPerkPatch.ResetCache();
        }

        /// <summary>新开一局：新的 PlayerStore.modData 是空字典，读到 0，自然归零。</summary>
        internal static void NewGame(string saveName)
        {
            loadedStore = StoreKey();
            totalValue = 0L;
            ScavengerPerkPatch.ResetCache();
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

        /// <summary>
        /// 难度倍率：直接读游戏自己的 Difficulty.Rules.reputationGainMultiplier。
        /// 原生 Difficulty.Vanilla() 定义 cozy 1.5 / normal 1 / hard 0.75 / challenge 0.5，
        /// 原生 StoreReputation.ModReputation 也是用它乘正声望增量；这里复用同一条来源，
        /// 游戏以后调数值会自动跟着走。取不到（无存档 / mod 难度没配 rules / 非正数）时
        /// 退化成 1，行为与不装本 mod 一致。
        /// </summary>
        internal static float ReadGainMultiplier(out string difficultyId)
        {
            difficultyId = "?";
            try
            {
                // Difficulty.Rules → Difficulty.Current → PlayerStore.Instance；
                // 无存档时 get_Instance 会新建 PlayerStore 并在主菜单抛异常，先挡住。
                if (!PlayerStore.HasInstance) return 1f;
                Difficulty current = Difficulty.Current;
                if (current != null) difficultyId = current.id;
                DifficultyRules rules = Difficulty.Rules;
                if (rules == null) return 1f;
                float multiplier = rules.reputationGainMultiplier;
                return multiplier > 0f ? multiplier : 1f;
            }
            catch (Exception ex)
            {
                if (!multiplierWarned)
                {
                    multiplierWarned = true;
                    Debug.LogWarning("[DumpingGroundMap] difficulty multiplier unavailable: "
                        + Describe(ex));
                }
                return 1f;
            }
        }

        /// <summary>按难度倍率折算本次新增的累计价值；四舍五入到整数价值（远离零）。</summary>
        private static long ScaleValue(long value, float multiplier)
        {
            if (multiplier == 1f) return value;
            double scaled = value * (double)multiplier;
            return scaled <= 0d ? 0L : (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 把一趟拾荒的总价值折成声望；难度倍率乘在「本次新增的累计价值」上，
        /// 已累计的旧值不追溯（不会因为换难度/读旧档而重算）。
        /// 返回实际计入的累计价值（已乘倍率），供飘字与日志使用。
        /// 不足 100 的部分留在 modData 里继续累计。
        /// </summary>
        internal static long Add(long value)
        {
            if (value <= 0L) return 0L;
            string store = StoreKey();
            if (store == null)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenge: reputation add skipped, no PlayerStore (value="
                    + value + ")");
                return 0L;
            }
            if (store != loadedStore)
            {
                loadedStore = store;
                totalValue = StoredValue();
            }
            string difficultyId;
            float multiplier = ReadGainMultiplier(out difficultyId);
            long scaled = ScaleValue(value, multiplier);
            totalValue = WriteStored(totalValue + scaled);
            if (multiplier != 1f)
            {
                Debug.Log("[DumpingGroundMap] reputation: difficulty=" + difficultyId
                    + " multiplier=" + multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                    + " value=" + value + " -> " + scaled);
            }
            return scaled;
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
        internal static bool QueueDebugPoints(float points, string why, bool bindToCurrentRun = false)
        {
            try
            {
                PlayerStore owner = bindToCurrentRun && PlayerStore.HasInstance ? PlayerStore.Instance : null;
                SaveFiles.IndexSave<string>(PendingRunKey, owner == null ? "" : owner.runID ?? "");
                SaveFiles.IndexSave<int>(PendingSlotKey, owner == null ? -1 : owner.saveSlotId);
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
                SaveFiles.IndexDeleteKey(PendingRunKey);
                SaveFiles.IndexDeleteKey(PendingSlotKey);
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
        internal static void ApplyPendingDebugPoints(bool saveNow)
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
            string pendingRun;
            int pendingSlot;
            try
            {
                pendingRun = SaveFiles.IndexLoad<string>(PendingRunKey, "");
                pendingSlot = SaveFiles.IndexLoad<int>(PendingSlotKey, -1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] debug: could not read pending owner: " + ex.Message);
                return;
            }
            PlayerStore current = PlayerStore.Instance;
            if (pendingSlot >= 0 && ((current.runID ?? "") != pendingRun || current.saveSlotId != pendingSlot))
                return;
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
                PlayerStore store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
                if (!Core.Enabled || EmporiumEntry.Instance == null || store == null)
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
                QueueDebugPoints(points, "save failed: " + reason, true);
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
}
