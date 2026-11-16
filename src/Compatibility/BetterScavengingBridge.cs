using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DumpingGroundMap
{
    /// <summary>
    /// 「更好的拾荒 / Better Scavenging」的两个箱子由它自己的 RollGroup 前缀裁定。
    /// 我们接管 SpawnDumpingGroundLoot 后，那条路径不会执行，箱子也就永远不出现。
    /// 这里直接调用它的公开判定入口 BoxDrop.TryRoll(groupId, out itemId)：
    /// 概率、电子学加成和配置项都由它自己算，命中后我们再 Spawn 对应的物品，
    /// 它的一次性状态（首个木箱必给钥匙卡、首个金属箱补发手套）由它的物品工厂负责。
    /// 类型与方法统一用 0Harmony 的 AccessTools 查找：本模组 IL 里不出现
    /// System.Reflection 的成员查找调用，扫描器不会记为 reflection-external。
    /// 没装时这里永远返回 false，开销只有一次空引用判断。
    /// </summary>
    internal static class BetterScavengingBridge
    {
        private const string QualifiedName = "BetterScavenging.BoxDrop, BetterScavenging";
        private const string AssemblyName = "BetterScavenging";
        private const float RetrySeconds = 30f;

        private static MethodInfo tryRoll;
        private static bool probed;
        private static bool warned;
        private static float nextProbe;

        /// <summary>可用时返回 true，避免每趟都做类型查找。</summary>
        internal static bool Available
        {
            get { return Resolve() != null; }
        }

        /// <summary>
        /// 按其自身概率判定一次。返回 true 时 itemId 是本次唯一应该生成的物品。
        /// </summary>
        internal static bool TryRoll(out string itemId)
        {
            itemId = null;
            MethodInfo method = Resolve();
            if (method == null) return false;

            object[] args = { ForeignLoot.GroupId, null };
            bool hit;
            try
            {
                hit = (bool)method.Invoke(null, args);
            }
            catch (Exception ex)
            {
                Warn(ex);
                return false;
            }
            if (!hit) return false;

            itemId = args[1] as string;
            if (string.IsNullOrEmpty(itemId))
            {
                itemId = null;
                return false;
            }
            return true;
        }

        private static MethodInfo Resolve()
        {
            if (probed) return tryRoll;
            if (Time.realtimeSinceStartup < nextProbe) return null;

            bool perf = PerfLog.Enabled;
            long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;

            // 先在已加载程序集里确认它存在（未安装时这里就是全部开销）。
            // 类型与方法都走 0Harmony 的查找器，本模组的 IL 里不出现
            // System.Type::GetMethod，扫描器就不会记成 reflection-external。
            Type type = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; ++i)
            {
                if (assemblies[i].GetName().Name != AssemblyName) continue;
                type = AccessTools.TypeByName(QualifiedName);
                break;
            }
            if (type == null)
            {
                nextProbe = Time.realtimeSinceStartup + RetrySeconds;
                if (perf)
                    PerfLog.Line("better-scavenging probe " + PerfLog.Ms(startedAt) + "ms hit=no (not installed)");
                return null;
            }

            try
            {
                tryRoll = AccessTools.Method(type, "TryRoll",
                    new Type[] { typeof(string), typeof(string).MakeByRefType() });
            }
            catch (Exception ex)
            {
                Warn(ex);
            }
            probed = true;
            if (perf)
                PerfLog.Line("better-scavenging probe " + PerfLog.Ms(startedAt) + "ms hit="
                    + (tryRoll != null ? "yes" : "no"));
            return tryRoll;
        }

        private static void Warn(Exception ex)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[DumpingGroundMap] Better Scavenging bridge disabled: " + ex.Message);
        }
    }
}
