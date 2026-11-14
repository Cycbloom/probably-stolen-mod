using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// 必须按存档隔离：写到 persistentDataPath/DumpingGroundMap/rep_&lt;runID&gt;.txt，
    /// runID 是 PlayerStore 里每个存档独有的 GUID，所以不进 ModConfig，
    /// 换存档、开新档都从 0 开始。
    /// </summary>
    internal static class Reputation
    {
        internal const long ValuePerPoint = 100L;

        private static long totalValue;
        private static string loadedRun;
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

        /// <summary>
        /// 新开一局：runID 已经换掉，读到的通常是不存在的文件，自然归零。
        /// </summary>
        private static void NewGame(string saveName)
        {
            Reload();
        }

        /// <summary>按当前存档读一次；同一个 runID 重复触发时不再读盘。</summary>
        internal static void Reload()
        {
            string run = RunId();
            if (run == null || run == loadedRun) return;
            loadedRun = run;
            totalValue = ReadFile(run);
            Debug.Log("[DumpingGroundMap] reputation " + Points + " (" + run + ")");
        }

        /// <summary>把一趟拾荒的总价值折成声望；不足 100 的部分留在文件里继续累计。</summary>
        internal static void Add(long value)
        {
            if (value <= 0L) return;
            string run = RunId();
            if (run == null) return;
            if (run != loadedRun)
            {
                loadedRun = run;
                totalValue = ReadFile(run);
            }
            totalValue += value;
            WriteFile(loadedRun, totalValue);
        }

        private static string RunId()
        {
            PlayerStore store = PlayerStore.Instance;
            if (store == null) return null;
            if (!string.IsNullOrEmpty(store.runID)) return store.runID;
            // 理论上不会走到这里；真拿不到 runID 时退化成按槽位隔离。
            return "slot_" + store.saveSlotId.ToString(CultureInfo.InvariantCulture);
        }

        private static string Folder
        {
            get { return Path.Combine(Application.persistentDataPath, "DumpingGroundMap"); }
        }

        private static string PathFor(string run)
        {
            char[] chars = run.ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                char c = chars[i];
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!safe) chars[i] = '_';
            }
            return Path.Combine(Folder, "rep_" + new string(chars) + ".txt");
        }

        private static long ReadFile(string run)
        {
            try
            {
                string path = PathFor(run);
                if (!File.Exists(path)) return 0L;
                string text = File.ReadAllText(path).Trim();
                long value;
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return 0L;
                return value < 0L ? 0L : value;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] could not read reputation: " + ex.Message);
                return 0L;
            }
        }

        private static void WriteFile(string run, long value)
        {
            try
            {
                string folder = Folder;
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                File.WriteAllText(PathFor(run), value.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] could not save reputation: " + ex.Message);
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
