using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// One area's runtime distribution: final probabilities plus the derived
    /// expected value. Rebuilt whenever a setting changes.
    /// </summary>
    internal sealed class EffectiveTable
    {
        internal readonly string[] Items;
        internal readonly float[] Probs;
        internal readonly bool[] IsHigh;
        internal readonly float HighChance;
        internal readonly float JunkChance;
        internal readonly float Ev;

        /// <summary>
        /// 贵重品在 Items / Probs 里的起始下标与件数。贵重品槽位可能和普通表里的
        /// id 重名（0.3.65 起 100–200 的几件两边都有），告示板靠这两个值区分。
        /// </summary>
        internal readonly int HighStart;
        internal readonly int HighCount;

        private readonly bool[] isJunk;
        private readonly int[] order;
        private readonly int junkIndex;

        internal EffectiveTable(string[] items, float[] probs, bool[] isHigh, bool[] isJunk,
            float highChance, float junkChance, float ev, int highStart, int highCount)
        {
            Items = items;
            Probs = probs;
            IsHigh = isHigh;
            this.isJunk = isJunk;
            HighChance = highChance;
            JunkChance = junkChance;
            Ev = ev;
            HighStart = highStart;
            HighCount = highCount;
            junkIndex = Array.IndexOf(items, "junk");

            order = new int[items.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                int compare = probs[b].CompareTo(probs[a]);
                return compare != 0 ? compare : string.CompareOrdinal(items[a], items[b]);
            });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return order[i]; }
        internal float ProbabilityAt(int i) { return Probs[order[i]]; }
        internal string ItemAt(int i) { return Items[order[i]]; }

        internal int PickIndex(float junkScale)
        {
            float sum = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                sum += i == junkIndex ? Probs[i] * junkScale : Probs[i];
            }
            if (sum <= 0f) return -1;

            float roll = UnityEngine.Random.value * sum;
            int last = -1;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                last = i;
                roll -= i == junkIndex ? Probs[i] * junkScale : Probs[i];
                if (roll < 0f) return i;
            }
            return last;
        }
    }
}
