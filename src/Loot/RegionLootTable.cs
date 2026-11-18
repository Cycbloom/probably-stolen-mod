using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    /// <summary>
    /// One weighted item table for one area. Weights are relative integers; the
    /// chance of an item is weight / total weight.
    /// </summary>
    internal sealed class RegionLootTable
    {
        internal readonly string[] Items;
        internal readonly int[] Weights;
        internal readonly int Total;
        private readonly int junkIndex;
        private readonly int[] displayOrder;
        private readonly Dictionary<string, int> indexOf = new Dictionary<string, int>();

        /// <summary>大类名本地化键与每件物品所属的大类下标；原版表没有大类，两者为 null。</summary>
        private readonly string[] categoryKeys;
        private readonly int[] itemCategory;

        internal RegionLootTable(string[] items, int[] weights)
            : this(items, weights, null, null)
        {
        }

        internal RegionLootTable(string[] items, int[] weights,
            string[] categoryKeys, int[] itemCategory)
        {
            if (items == null || weights == null || items.Length == 0 || items.Length != weights.Length)
                throw new ArgumentException("Loot items and weights must have matching non-empty lengths.");
            if (categoryKeys != null && (itemCategory == null || itemCategory.Length != items.Length))
                throw new ArgumentException("Loot category metadata must cover every item.");
            HashSet<string> unique = new HashSet<string>();
            for (int i = 0; i < items.Length; ++i)
            {
                if (string.IsNullOrEmpty(items[i]) || !unique.Add(items[i]) || weights[i] <= 0)
                    throw new ArgumentException("Invalid or duplicate loot item: " + items[i]);
                if (categoryKeys != null && (itemCategory[i] < 0 || itemCategory[i] >= categoryKeys.Length))
                    throw new ArgumentException("Invalid category for " + items[i]);
            }
            Items = items;
            Weights = weights;
            this.categoryKeys = categoryKeys;
            this.itemCategory = itemCategory;
            int sum = 0;
            for (int i = 0; i < weights.Length; ++i) sum += weights[i];
            Total = sum;
            junkIndex = Array.IndexOf(items, "junk");
            for (int i = 0; i < items.Length; ++i) indexOf[items[i]] = i;

            displayOrder = new int[items.Length];
            for (int i = 0; i < displayOrder.Length; ++i) displayOrder[i] = i;
            Array.Sort(displayOrder, delegate(int a, int b) { return weights[b].CompareTo(weights[a]); });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return displayOrder[i]; }
        internal float ProbabilityAt(int i) { return Weights[displayOrder[i]] / (float)Total; }

        /// <summary>大类数量；原版表为 0。</summary>
        internal int CategoryCount { get { return categoryKeys == null ? 0 : categoryKeys.Length; } }

        /// <summary>物品所属大类下标；不在主题表里时返回 -1。</summary>
        internal int CategoryOf(string itemId)
        {
            if (itemCategory == null) return -1;
            int index = IndexOf(itemId);
            return index >= 0 ? itemCategory[index] : -1;
        }

        /// <summary>按当前语言取大类名。</summary>
        internal string CategoryName(int index)
        {
            if (categoryKeys == null || index < 0 || index >= categoryKeys.Length) return null;
            return Copy.Get(categoryKeys[index]);
        }

        /// <summary>垃圾自己的权重；表里没有垃圾时为 0。</summary>
        internal int JunkWeight { get { return junkIndex >= 0 ? Weights[junkIndex] : 0; } }

        /// <summary>权重下标；表里没这件物品时返回 -1。</summary>
        internal int IndexOf(string itemId)
        {
            int index;
            return indexOf.TryGetValue(itemId, out index) ? index : -1;
        }


        /// <summary>
        /// Rolls one item. junkScale mirrors the vanilla "junk_chance" event, which
        /// only raises the junk table's weight.
        /// </summary>
        internal string Pick(float junkScale)
        {
            float sum = Total;
            if (junkIndex >= 0 && junkScale != 1f)
                sum += Weights[junkIndex] * (junkScale - 1f);
            if (sum <= 0f) return null;

            float roll = UnityEngine.Random.value * sum;
            for (int i = 0; i < Items.Length; ++i)
            {
                float weight = i == junkIndex ? Weights[i] * junkScale : Weights[i];
                roll -= weight;
                if (roll < 0f) return Items[i];
            }
            return Items[Items.Length - 1];
        }
    }
}
