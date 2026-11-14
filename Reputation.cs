using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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
}
