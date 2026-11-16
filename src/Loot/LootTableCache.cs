using System.Collections.Generic;
using System.Diagnostics;

namespace DumpingGroundMap
{
    internal sealed class LootTableCache
    {
        private readonly Dictionary<string, EffectiveTable> tables = new Dictionary<string, EffectiveTable>();
        private LootSettings settings;
        private bool unlocked;
        private bool initialized;
        private float junkScale;
        private int stage;
        private ForeignLootTable foreign;

        /// <summary>是否已经构建过表；用于性能日志区分首次构建与重建。</summary>
        private bool builtAny;
        private string resetReason = "首次构建";

        internal void Clear()
        {
            tables.Clear();
            initialized = false;
        }

        internal EffectiveTable Get(Region region, LootSettings snapshot, bool perk80, float eventJunkScale = 1f, int reputation = 0,
            RegionalEvent regionalEvent = null, ForeignLootTable foreignLoot = null)
        {
            eventJunkScale = ProbabilityMath.NormalizeJunkScale(eventJunkScale);
            int nextStage = LootBalance.Stage(reputation);
            bool reset = !initialized || !snapshot.Equals(settings) || unlocked != perk80 || junkScale != eventJunkScale || stage != nextStage
                || !ReferenceEquals(foreignLoot, foreign);
            if (reset)
            {
                // 只在开关打开时判定原因；关闭时这里只有一次布尔读取。
                if (PerfLog.Enabled)
                {
                    if (!initialized) resetReason = builtAny ? "设置变更" : "首次构建";
                    else if (!snapshot.Equals(settings)) resetReason = "设置变更";
                    else if (!ReferenceEquals(foreignLoot, foreign)) resetReason = "外来池版本变化";
                    else if (junkScale != eventJunkScale) resetReason = "权重变更";
                    else resetReason = "进度/存档变化";
                }
                tables.Clear();
                settings = snapshot;
                unlocked = perk80;
                junkScale = eventJunkScale;
                stage = nextStage;
                foreign = foreignLoot;
                initialized = true;
            }
            EffectiveTable table;
            if (!tables.TryGetValue(region.Id, out table) || table.RegionalEvent != regionalEvent)
            {
                bool perf = PerfLog.Enabled;
                long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;
                table = LootCalculator.Build(region, snapshot, perk80, junkScale, reputation, foreignLoot);
                table = RegionalEventRules.Apply(region, table, regionalEvent);
                tables[region.Id] = table;
                if (perf)
                {
                    bool first = !builtAny;
                    builtAny = true;
                    PerfLog.Line("table " + (first ? "first build" : "rebuild")
                        + " region=" + region.Id + " " + PerfLog.Ms(startedAt) + "ms"
                        + " reason=" + (reset ? resetReason : "区域或事件变化"));
                }
            }
            return table;
        }
    }
}
