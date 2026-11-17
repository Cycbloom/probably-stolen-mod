using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// Let optional native-roll skills inspect the selected area's conditional
    /// pool. No third-party methods, skill ranks or probabilities are copied.
    /// </summary>
    internal static class ScavengingExpansionBridge
    {
        internal const string ModId = "eotn_scavenger_skills";
        internal const string HarmonyId = "mod.eotn_scavenger_skills";
        internal const string GroupId = "dumpingGroundTG";
        private static readonly MethodInfo rollInternal = AccessTools.Method(typeof(LootRegistry), "RollInternal",
            new Type[] { typeof(string), typeof(string) });

        internal static string Resolve(EffectiveTable table, int index)
        {
            string token = table.Items[index];
            string tableId = RegionalPoolProjection.TableId(token);
            if (table.Slots[index].Kind != LootKind.Ordinary || !RegionalPoolProjection.Supports(tableId)
                || !ModHelper.IsModRunning(ModId) || rollInternal == null)
                return NativeLootEvents.Resolve(token);

            float total = 0f;
            foreach (LootEntry entry in LootRegistry.GetTableEntries(tableId))
                if (entry != null && entry.weight > 0f && !float.IsInfinity(entry.weight)) total += entry.weight;
            List<LootEntry> entries = RegionalPoolProjection.Build(table, tableId, total);
            if (entries.Count == 0) return NativeLootEvents.Resolve(token);

            string result;
            using (RegionalRollScope.Enter(tableId, token, entries))
                result = (string)rollInternal.Invoke(null, new object[] { tableId, GroupId });
            // Native skill overlays use real IDs. Restore our water/package/etc.
            // variants whenever the selected ID belongs to this regional pool.
            return RegionalPoolProjection.RestoreVariant(table, index, result, delegate { return UnityEngine.Random.value; });
        }
    }

    internal static class RegionalPoolProjection
    {
        internal static string TableId(string token)
        {
            string id = ScavLoot.SourceTable(LootVariants.RealId(token));
            return id == "medicalTable" ? "dumpingGroundMedical" : id;
        }

        internal static bool Supports(string tableId)
        {
            return tableId == "dumpingGroundMedical" || tableId == "householdTable" || tableId == "makeshiftWeaponTable";
        }

        internal static List<LootEntry> Build(EffectiveTable table, string tableId, float nativeTotal)
        {
            List<LootEntry> result = new List<LootEntry>();
            if (nativeTotal <= 0f || float.IsNaN(nativeTotal) || float.IsInfinity(nativeTotal)) return result;
            Dictionary<string, double> weights = new Dictionary<string, double>();
            double total = 0d;
            for (int i = 0; i < table.Count; ++i)
            {
                if (table.Slots[i].Kind != LootKind.Ordinary || table.Probs[i] <= 0f || TableId(table.Items[i]) != tableId) continue;
                string realId = LootVariants.RealId(table.Items[i]);
                double weight;
                weights.TryGetValue(realId, out weight);
                weights[realId] = weight + table.Probs[i];
                total += table.Probs[i];
            }
            if (total <= 0d) return result;
            foreach (KeyValuePair<string, double> entry in weights)
                result.Add(new LootEntry(entry.Key, (float)(entry.Value / total * nativeTotal), Core.Id));
            return result;
        }

        internal static string RestoreVariant(EffectiveTable table, int original, string result, Func<float> random)
        {
            string token = table.Items[original];
            if (result == null || result == token || result == LootVariants.RealId(token)) return result == null ? null : token;
            double total = 0d;
            for (int i = 0; i < table.Count; ++i)
                if (table.Slots[i].Kind == LootKind.Ordinary && LootVariants.RealId(table.Items[i]) == result)
                    total += table.Probs[i];
            if (total <= 0d) return result; // A new candidate provided by the skill.
            double sample = Math.Max(0d, Math.Min(1d, random())) * total;
            string last = result;
            for (int i = 0; i < table.Count; ++i)
            {
                if (table.Slots[i].Kind != LootKind.Ordinary || table.Probs[i] <= 0f || LootVariants.RealId(table.Items[i]) != result) continue;
                last = table.Items[i];
                sample -= table.Probs[i];
                if (sample < 0d) return last;
            }
            return last;
        }
    }

    /// <summary>Only the synchronous delegated draw sees this snapshot.</summary>
    internal sealed class RegionalRollScope : IDisposable
    {
        private static RegionalRollScope current;
        private readonly RegionalRollScope parent;
        private readonly string tableId, token;
        private readonly List<LootEntry> entries;
        private bool consumed;
        private bool disposed;

        private RegionalRollScope(string tableId, string token, List<LootEntry> entries)
        {
            parent = current;
            this.tableId = tableId;
            this.token = token;
            this.entries = entries;
            current = this;
        }

        internal static IDisposable Enter(string tableId, string token, List<LootEntry> entries)
        {
            return new RegionalRollScope(tableId, token, entries);
        }

        internal static bool TrySnapshot(string tableId, out List<LootEntry> entries)
        {
            entries = null;
            if (current == null || current.consumed || current.tableId != tableId) return false;
            entries = new List<LootEntry>();
            foreach (LootEntry entry in current.entries) entries.Add(entry.Clone());
            return true;
        }

        internal static bool TryTake(string tableId, string groupId, out string token)
        {
            token = null;
            if (current == null || current.consumed || current.tableId != tableId || groupId != ScavengingExpansionBridge.GroupId) return false;
            current.consumed = true;
            token = current.token;
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current = parent;
        }
    }

    [HarmonyPatch(typeof(LootRegistry), "GetTableEntries")]
    internal static class RegionalSkillPoolPatch
    {
        private static void Postfix(string tableId, ref List<LootEntry> __result)
        {
            List<LootEntry> entries;
            if (RegionalRollScope.TrySnapshot(tableId, out entries)) __result = entries;
        }
    }

    [HarmonyPatch(typeof(LootRegistry), "RollInternal")]
    internal static class RegionalSkillRollPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(ScavengingExpansionBridge.HarmonyId)]
        private static bool Prefix(string tableId, string groupId, ref string __result)
        {
            string token;
            if (!RegionalRollScope.TryTake(tableId, groupId, out token)) return true;
            // Fire the normal event once, at the same point as vanilla. Skill
            // postfixes then finish their overlay without spawning extra items.
            __result = NativeLootEvents.Resolve(token);
            return false;
        }
    }
}
