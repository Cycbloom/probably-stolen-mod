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
    /// 废武器堆的枪械成色。游戏里枪的「品质」是物品特征
    /// ItemFeatureList.EquipmentCondition（New / Excellent / Good / Fair / Poor），
    /// 不是耐久度，也不是靠不同 item id 区分；成色会经由 ItemCondition.modValue
    /// 影响成交价。拾荒生成后按权重掷一档并覆盖工厂默认值，让废武器堆以破旧枪为主。
    /// </summary>
    internal static class WeaponWear
    {
        // 顺序与 EquipmentCondition 一致：New, Excellent, Good, Fair, Poor。
        private static readonly Dictionary<string, int[]> Weights =
            new Dictionary<string, int[]>
            {
                { "printed_gun", new[] { 1, 1, 9, 24, 65 } },
                { "handmade_pistol", new[] { 1, 1, 10, 30, 58 } },
                { "heavy_handmade_pistol", new[] { 1, 2, 13, 34, 50 } },
                { "glock_receiver", new[] { 2, 4, 22, 34, 38 } },
                { "revolver", new[] { 2, 4, 21, 33, 40 } },
                { "stun_gun", new[] { 3, 4, 23, 35, 35 } },
                { "smg", new[] { 2, 4, 20, 34, 40 } },
                { "shotgun", new[] { 2, 4, 20, 32, 42 } }
            };

        /// <summary>枪械物品 id 时重掷成色；其它物品直接跳过。</summary>
        internal static float ExpectedValue(string id, float fullValue)
        {
            int[] weights;
            if (!Weights.TryGetValue(id, out weights)) return fullValue;
            double sum = 0d, total = 0d;
            double[] modifiers = { 1.25d, 1.12d, 1d, 0.88d, 0.75d };
            for (int i = 0; i < weights.Length; ++i)
            {
                sum += weights[i] * Math.Round(fullValue * modifiers[i]);
                total += weights[i];
            }
            return (float)(sum / total);
        }

        internal static void Apply(string itemId, GameItem item)
        {
            int[] weights;
            if (item == null || !Weights.TryGetValue(itemId, out weights)) return;

            int sum = 0;
            for (int i = 0; i < weights.Length; ++i) sum += weights[i];

            int roll = UnityEngine.Random.Range(0, sum);
            int pick = 0;
            for (int i = 0; i < weights.Length; ++i)
            {
                roll -= weights[i];
                if (roll < 0) { pick = i; break; }
            }
            GunHelper.SetEquipmentCondition(item, (ItemFeatureList.EquipmentCondition)pick);
        }
    }
}
