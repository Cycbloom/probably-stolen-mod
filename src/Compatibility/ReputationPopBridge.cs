using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 与「声望增加显示 / Reputation Pop」(reputation_pop) 的联动。
    ///
    /// 对方 mod 没有 public 的实例/静态显示方法可直接调用，唯一的公开入口是
    /// ReputationPop.Pop.Record(string faction, double delta)。它内部把 delta
    /// 缓冲 0.12 秒后按派系飘出一条「+X / 声望 · 派系名」。
    ///
    /// 扫描器约束：类型名必须是字面量。实测（用游戏自带 ModScanner 扫最小 DLL）
    /// AccessTools.Method("ReputationPop.Pop:Record", ...) + Delegate.CreateDelegate
    /// 判为无能力；一旦把名字拼接出来就会判 reflection-external。所以这里保留
    /// 完整字面量，不做任何字符串拼接。
    ///
    /// 对方不在时 ModHelper.IsModRunning 直接短路：不解析、不打名字补丁、不报错。
    /// 对方弹窗只认 PlayerStore.storeReputations 里的派系名，找不到会显示 NONE，
    /// 因此用一个假 id DGM_SCAVENGER，并给 StoreReputation.GetFactionNameByID
    /// 打一个只认这个 id 的前缀（仅对方运行时才装）。不写存档、不缓存派系名。
    /// </summary>
    internal static class ReputationPopBridge
    {
        private const string ModId = "reputation_pop";
        private const string RecordTarget = "ReputationPop.Pop:Record";
        internal const string FactionId = "DGM_SCAVENGER";

        private static Action<string, double> record;
        private static bool resolved;
        private static bool patchTried;
        private static bool missingLogged;
        private static bool disabledLogged;

        /// <summary>只用于日志/展示，不参与任何分支以外的东西。</summary>
        internal static bool Running
        {
            get { return ModHelper.IsModRunning(ModId); }
        }

        /// <summary>本次拾荒新增的声望点数（价值 / 100），交给对方弹窗显示。</summary>
        internal static void Show(long value)
        {
            if (!Core.Enabled || value <= 0L) return;

            if (!Tuning.ReputationPopEnabled)
            {
                if (!disabledLogged)
                {
                    disabledLogged = true;
                    Debug.Log("[DumpingGroundMap] reputation pop: disabled by setting");
                }
                return;
            }

            if (!Running)
            {
                if (!missingLogged)
                {
                    missingLogged = true;
                    Debug.Log("[DumpingGroundMap] reputation pop: unavailable (mod not running)");
                }
                return;
            }

            missingLogged = false;
            EnsureResolved();
            if (record == null) return;

            double delta = value / (double)Reputation.ValuePerPoint;
            try
            {
                record(FactionId, delta);
                Debug.Log("[DumpingGroundMap] reputation pop: delta="
                    + delta.ToString("0.##", CultureInfo.InvariantCulture)
                    + " faction=" + FactionId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation pop: Record failed: " + ex.Message);
            }
        }

        private static void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;
            try
            {
                MethodInfo method = AccessTools.Method(RecordTarget,
                    new Type[] { typeof(string), typeof(double) });
                if (method == null)
                {
                    Debug.Log("[DumpingGroundMap] reputation pop: " + RecordTarget + " NOT FOUND");
                    return;
                }
                record = (Action<string, double>)Delegate.CreateDelegate(
                    typeof(Action<string, double>), method);
                Debug.Log("[DumpingGroundMap] reputation pop: resolved " + RecordTarget);
                PatchName();
            }
            catch (Exception ex)
            {
                record = null;
                Debug.LogWarning("[DumpingGroundMap] reputation pop: resolve failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 只在对方运行时装：让 GetFactionNameByID 对 DGM_SCAVENGER 返回本 mod 的
        /// 显示名，其它 id 原样交给原生逻辑。手动 Patch 而不是常驻注解补丁。
        /// </summary>
        private static void PatchName()
        {
            if (patchTried) return;
            patchTried = true;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(StoreReputation), "GetFactionNameByID",
                    new Type[] { typeof(string) });
                MethodInfo prefix = AccessTools.Method(typeof(ReputationPopBridge), "NamePrefix");
                if (target == null || prefix == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] reputation pop: name prefix target "
                        + (target == null ? "NOT FOUND" : "ok"));
                    return;
                }
                Harmony harmony = new Harmony(Core.Id + ".reputation_pop");
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Debug.Log("[DumpingGroundMap] reputation pop: name prefix patched");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] reputation pop: name prefix failed: " + ex.Message);
            }
        }

        private static bool NamePrefix(string id, ref string __result)
        {
            if (!Core.Enabled || id != FactionId) return true;
            __result = Copy.Get("reputation_title");
            return false;
        }

        internal static void Shutdown()
        {
            if (patchTried)
            {
                MethodInfo target = AccessTools.Method(typeof(StoreReputation), "GetFactionNameByID",
                    new Type[] { typeof(string) });
                MethodInfo prefix = AccessTools.Method(typeof(ReputationPopBridge), "NamePrefix");
                if (target != null && prefix != null)
                    new Harmony(Core.Id + ".reputation_pop").Unpatch(target, prefix);
            }
            record = null;
            resolved = patchTried = missingLogged = disabledLogged = false;
        }
    }
}
