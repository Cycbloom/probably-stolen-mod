using System.Globalization;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DumpingGroundMap
{
    /// <summary>
    /// 轻量性能日志：开关关闭时调用点只多一次布尔判断，
    /// 不创建秒表、不拼字符串。打开后每类动作只打一条。
    /// </summary>
    internal static class PerfLog
    {
        private const string Prefix = "[DGM][perf] ";
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        internal static bool Enabled
        {
            get { return Tuning.PerfLogEnabled; }
        }

        internal static void Line(string text)
        {
            if (!Enabled) return;
            Debug.Log(Prefix + text);
        }

        /// <summary>把 GetTimestamp 的起止差换成毫秒（一位小数）。</summary>
        internal static string Ms(long startedAt)
        {
            return ((Stopwatch.GetTimestamp() - startedAt) * MsPerTick)
                .ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
