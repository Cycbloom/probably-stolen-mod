using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// One area's runtime distribution: final probabilities plus the derived
    /// expected value. Rebuilt whenever settings, unlocks or junk events change.
    /// </summary>
    internal sealed class EffectiveTable
    {
        internal readonly string[] Items;
        internal readonly float[] Probs;
        internal readonly bool[] IsHigh;
        internal readonly float HighChance;
        internal readonly float JunkChance;
        internal readonly float Ev;
        internal readonly float JunkScale;
        internal readonly LootSlot[] Slots;
        internal readonly EffectiveTable WithoutRegionalEvent;
        internal readonly RegionalEvent RegionalEvent;
        private readonly float[] eventValues;
        internal float EventEv { get { return WithoutRegionalEvent == null ? 0f : Math.Max(0f, Ev - WithoutRegionalEvent.Ev); } }

        /// <summary>
        /// 贵重品在 Items / Probs 里的起始下标与件数。贵重品槽位可能和普通表里的
        /// id 重名（0.3.65 起 100–200 的几件两边都有），告示板靠这两个值区分。
        /// </summary>
        internal readonly int HighStart;
        internal readonly int HighCount;

        /// <summary>
        /// 外来条目（其它模组注入原版组的物品）在数组里的起点与件数；没有时
        /// ForeignStart 为 -1。它们排在贵重品之后、补给箱/钥匙卡之前。
        /// </summary>
        internal readonly int ForeignStart;
        internal readonly int ForeignCount;

        private readonly int[] order;

        internal EffectiveTable(string[] items, float[] probs, bool[] isHigh,
            float highChance, float junkChance, float ev, int highStart, int highCount, Region region, float junkScale = 1f,
            EffectiveTable withoutRegionalEvent = null, RegionalEvent regionalEvent = null, float[] values = null,
            int foreignStart = -1, int foreignCount = 0)
        {
            Items = items;
            Probs = probs;
            IsHigh = isHigh;
            HighChance = highChance;
            JunkChance = junkChance;
            Ev = ev;
            JunkScale = junkScale;
            HighStart = highStart;
            HighCount = highCount;
            ForeignStart = foreignCount > 0 ? foreignStart : -1;
            ForeignCount = foreignCount > 0 ? foreignCount : 0;
            WithoutRegionalEvent = withoutRegionalEvent;
            RegionalEvent = regionalEvent;
            eventValues = values;
            Slots = new LootSlot[items.Length];
            int valuableEnd = highStart + highCount;
            int foreignEnd = ForeignStart >= 0 ? ForeignStart + ForeignCount : valuableEnd;
            for (int i = 0; i < items.Length; ++i)
            {
                LootKind kind;
                if (i < highStart) kind = LootKind.Ordinary;
                else if (i < valuableEnd) kind = LootKind.Valuable;
                else if (ForeignStart >= 0 && i >= ForeignStart && i < foreignEnd) kind = LootKind.Foreign;
                else kind = items[i].StartsWith("loot_crate_", StringComparison.Ordinal) ? LootKind.SupplyCrate : LootKind.Keycard;
                Slots[i] = new LootSlot(items[i], kind,
                    kind == LootKind.Ordinary ? region.Loot.CategoryOf(items[i])
                    : kind == LootKind.Foreign ? ForeignLoot.Category : -1);
            }

            order = new int[items.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                int compare = probs[b].CompareTo(probs[a]);
                return compare != 0 ? compare : string.CompareOrdinal(items[a], items[b]);
            });
        }

        internal int Count { get { return Items.Length; } }

        /// <summary>补给箱/钥匙卡在数组里的起点：外来块之后。</summary>
        internal int SpecialStart
        {
            get { return ForeignStart >= 0 ? ForeignStart + ForeignCount : HighStart + HighCount; }
        }
        internal float ExpectedValueAt(int i, string regionId)
        {
            return eventValues == null ? LootCatalog.ExpectedValueOf(Items[i], regionId) : eventValues[i];
        }
        internal int IndexAt(int i) { return order[i]; }
        internal float ProbabilityAt(int i) { return Probs[order[i]]; }
        internal string ItemAt(int i) { return Items[order[i]]; }

        internal int PickIndex()
        {
            return PickIndex(UnityEngine.Random.value);
        }

        internal int PickIndex(float sample)
        {
            float sum = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                sum += Probs[i];
            }
            if (sum <= 0f) return -1;

            float roll = ProbabilityMath.Clamp(sample, 0f, 1f) * sum;
            int last = -1;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                last = i;
                roll -= Probs[i];
                if (roll < 0f) return i;
            }
            return last;
        }

        /// <summary>外来条目生成失败时的兜底：概率最高的普通（非垃圾）槽。</summary>
        internal int FallbackOrdinaryIndex()
        {
            int best = -1;
            float bestProb = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Slots[i].Kind != LootKind.Ordinary || Items[i] == "junk" || Probs[i] <= 0f) continue;
                if (best < 0 || Probs[i] > bestProb)
                {
                    best = i;
                    bestProb = Probs[i];
                }
            }
            return best;
        }

        internal float ProbabilityOf(LootKind kind)
        {
            float result = 0f;
            for (int i = 0; i < Count; ++i)
                if (Slots[i].Kind == kind) result += Probs[i];
            return result;
        }
    }
}
