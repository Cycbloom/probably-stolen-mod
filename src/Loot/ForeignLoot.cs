using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DumpingGroundMap
{
    /// <summary>
    /// 其它模组加进原版拾荒组的条目，连同它们在原版组里占的权重。
    /// 只读：不向 LootRegistry 写任何东西。
    /// </summary>
    internal sealed class ForeignLootTable
    {
        internal static readonly ForeignLootTable Empty =
            new ForeignLootTable(new string[0], new float[0], 0f);

        internal readonly string[] Ids;
        internal readonly float[] Weights;

        /// <summary>池子在原版整组里自然占的比例（0–1），用来封顶实际份额。</summary>
        internal readonly float NaturalShare;

        internal ForeignLootTable(string[] ids, float[] weights, float naturalShare)
        {
            Ids = ids;
            Weights = weights;
            NaturalShare = naturalShare;
        }

        internal bool IsEmpty { get { return Ids.Length == 0; } }
        internal int Count { get { return Ids.Length; } }

        internal bool SameContent(ForeignLootTable other)
        {
            if (other == null || Ids.Length != other.Ids.Length) return false;
            for (int i = 0; i < Ids.Length; ++i)
            {
                if (Ids[i] != other.Ids[i]) return false;
                float scale = Math.Max(Math.Abs(Weights[i]), Math.Abs(other.Weights[i]));
                if (Math.Abs(Weights[i] - other.Weights[i]) > scale * 0.000001f) return false;
            }
            return Math.Abs(NaturalShare - other.NaturalShare) <= 0.000001f;
        }

        /// <summary>池内相对权重总和，用于按原比例分配份额。</summary>
        internal float Total
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Weights.Length; ++i) total += Weights[i];
                return total;
            }
        }
    }

    /// <summary>
    /// 把原版 dumpingGroundTG 展开成「物品 × 权重」，再减去本模组各区的表。
    /// 剩下的就是其它模组注入、以及（可选）我们没收录的原版物品。
    /// </summary>
    internal static class ForeignLoot
    {
        /// <summary>ScavHelper.SpawnDumpingGroundLoot 唯一使用的表组。</summary>
        internal const string GroupId = "dumpingGroundTG";

        /// <summary>告示板「外来条目」分组用的哨兵类别。</summary>
        internal const int Category = -2;

        private const float ReloadSeconds = 2f;

        private sealed class Accumulator
        {
            internal float Weight;
            internal bool ModAdded;
        }

        private static HashSet<string> ownIds;
        private static readonly HashSet<string> missingLogged = new HashSet<string>(StringComparer.Ordinal);
        private static ForeignLootTable strictCache;
        private static ForeignLootTable allCache;
        private static float nextRead;
        private static bool warnedRead;

        /// <summary>
        /// 读一次原版组，返回去重后的外来池。2 秒内重复调用复用上次结果，
        /// 所以滑条逐帧刷新不会反复克隆注册表。
        /// </summary>
        internal static ForeignLootTable Snapshot(bool includeVanilla)
        {
            ForeignLootTable previous = includeVanilla ? allCache : strictCache;
            if (previous != null && Time.realtimeSinceStartup < nextRead) return previous;

            bool perf = PerfLog.Enabled;
            long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;
            ForeignLootTable table = Read(includeVanilla);
            nextRead = Time.realtimeSinceStartup + ReloadSeconds;
            if (perf)
            {
                PerfLog.Line("foreign rescan " + PerfLog.Ms(startedAt) + "ms entries="
                    + table.Count + " vanilla=" + (includeVanilla ? "yes" : "no"));
            }
            if (previous != null && previous.SameContent(table)) return previous;

            if (includeVanilla) allCache = table;
            else strictCache = table;
            return table;
        }

        /// <summary>丢弃缓存：读档、换场景时让下次查询重新展开原版组。</summary>
        internal static void Invalidate()
        {
            strictCache = null;
            allCache = null;
            nextRead = 0f;
        }

        private static ForeignLootTable Read(bool includeVanilla)
        {
            List<LootEntry> group;
            try
            {
                group = LootRegistry.GetGroupEntries(GroupId);
            }
            catch (Exception ex)
            {
                WarnRead(ex);
                return ForeignLootTable.Empty;
            }
            if (group == null || group.Count == 0) return ForeignLootTable.Empty;

            Dictionary<string, Accumulator> acc = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            double all = 0d;
            foreach (LootEntry entry in group)
                if (Usable(entry)) all += entry.weight;
            if (all <= 0d) return ForeignLootTable.Empty;
            for (int g = 0; g < group.Count; ++g)
            {
                LootEntry entry = group[g];
                if (!Usable(entry)) continue;

                List<LootEntry> table;
                try
                {
                    table = LootRegistry.GetTableEntries(entry.id);
                }
                catch (Exception ex)
                {
                    WarnRead(ex);
                    continue;
                }
                if (table == null) continue;

                // Native rolls first choose a table, then choose within it.
                // Scaling every weight in one table must not change its share.
                double tableTotal = 0d;
                foreach (LootEntry item in table)
                    if (Usable(item)) tableTotal += item.weight;
                if (tableTotal <= 0d) continue;

                for (int t = 0; t < table.Count; ++t)
                {
                    LootEntry item = table[t];
                    if (!Usable(item)) continue;
                    float weight = (float)(entry.weight / all * (item.weight / tableTotal));

                    Accumulator record;
                    if (!acc.TryGetValue(item.id, out record))
                    {
                        record = new Accumulator();
                        acc[item.id] = record;
                    }
                    record.Weight += weight;
                    if (!string.IsNullOrEmpty(item.sourceModId)) record.ModAdded = true;
                }
            }
            HashSet<string> own = OwnIds();
            List<string> ids = new List<string>();
            List<float> weights = new List<float>();
            float pool = 0f;
            foreach (KeyValuePair<string, Accumulator> pair in acc)
            {
                if (own.Contains(pair.Key)) continue;
                if (!includeVanilla && !pair.Value.ModAdded) continue;
                if (!CanSpawn(pair.Key))
                {
                    LogMissing(pair.Key);
                    continue;
                }
                ids.Add(pair.Key);
                weights.Add(pair.Value.Weight);
                pool += pair.Value.Weight;
            }
            if (ids.Count == 0) return ForeignLootTable.Empty;

            // 排序让结果稳定：注册表字典的顺序不保证，否则缓存会一直翻。
            int[] order = new int[ids.Count];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b) { return string.CompareOrdinal(ids[a], ids[b]); });

            string[] sortedIds = new string[ids.Count];
            float[] sortedWeights = new float[ids.Count];
            for (int i = 0; i < order.Length; ++i)
            {
                sortedIds[i] = ids[order[i]];
                sortedWeights[i] = weights[order[i]];
            }
            return new ForeignLootTable(sortedIds, sortedWeights, pool);
        }

        private static bool Usable(LootEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.id) && entry.weight > 0f
                && !float.IsNaN(entry.weight) && !float.IsInfinity(entry.weight);
        }

        /// <summary>本模组所有区表、原版副本、贵重品、箱卡与垃圾的 id 并集。</summary>
        private static HashSet<string> OwnIds()
        {
            if (ownIds != null) return ownIds;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            ids.Add("junk");

            RegionLootTable vanilla = ScavLoot.Vanilla;
            for (int i = 0; i < vanilla.Count; ++i) ids.Add(vanilla.Items[i]);

            for (int i = 0; i < Region.All.Length; ++i)
            {
                RegionLootTable table = Region.All[i].Loot;
                for (int j = 0; j < table.Count; ++j) ids.Add(table.Items[j]);

                SupplyEntry[] supplies = SupplyPool.For(Region.All[i].Id);
                for (int j = 0; j < supplies.Length; ++j)
                {
                    ids.Add(supplies[j].CrateId);
                    ids.Add(supplies[j].CardId);
                }
            }
            for (int i = 0; i < LootCatalog.Valuables.Length; ++i) ids.Add(LootCatalog.Valuables[i].Id);
            ownIds = ids;
            return ownIds;
        }

        /// <summary>
        /// 只收游戏真的能生成的 id。DirectoryMaster 查不到时再查预构建工厂，
        /// 因为只注册了工厂的物品不在目录里。
        /// </summary>
        private static bool CanSpawn(string id)
        {
            try
            {
                if (DirectoryMaster.Has<GameItem>(id)) return true;
            }
            catch (Exception)
            {
            }
            Dictionary<string, Func<GameItem>> prebuilt = PreBuiltItemHelper.preBuiltItems;
            return prebuilt != null && prebuilt.ContainsKey(id);
        }

        private static void LogMissing(string id)
        {
            if (!missingLogged.Add(id)) return;
            ModLogger.Warning("foreign loot: skipping unknown item id '" + id + "'");
        }

        private static void WarnRead(Exception ex)
        {
            if (warnedRead) return;
            warnedRead = true;
            ModLogger.Warning("foreign loot: cannot read native tables: " + ex.Message);
        }
    }
}
