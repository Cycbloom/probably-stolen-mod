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
    /// 免疫宁注射器的状态。游戏里正品 / 仿品 / 过期 / 不能用共用同一个 item id，
    /// 靠 CATEGORY_GENUINE_INJECTOR 条件区分；状态本身不改价（条件 modValue 全为 0），
    /// 所以这里在生成后补条件，并把 unitValue 同步成对应档次的价格。
    /// 正品只作贵重品出现，次品（过期 / 仿品 / 不能用）只走普通表。
    /// </summary>
    internal static class InjectorQuality
    {
        private const string Category = "CATEGORY_GENUINE_INJECTOR";
        private const long GenuineValue = 160L;

        /// <summary>普通表的次品：过期 40%（80） / 仿品 35%（40） / 不能用 25%（16）。</summary>
        internal static void ApplyBad(GameItem item)
        {
            if (item == null) return;

            int roll = UnityEngine.Random.Range(0, 100);
            ItemFeatureList.InjectorConditionType type;
            long value;
            if (roll < 40)
            {
                type = ItemFeatureList.InjectorConditionType.Expired;
                value = 80L;
            }
            else if (roll < 75)
            {
                type = ItemFeatureList.InjectorConditionType.Counterfeit;
                value = 40L;
            }
            else
            {
                type = ItemFeatureList.InjectorConditionType.Unusable;
                value = 16L;
            }

            item.RemoveFeatureByCategory(Category);
            item.AddItemFeature(ItemFeatureList.InjectorFeature(type, type));
            item.unitValue = value;

            if (type == ItemFeatureList.InjectorConditionType.Expired)
                InsInjectorHelper.SetExpired(item);
            else if (type == ItemFeatureList.InjectorConditionType.Counterfeit)
                InsInjectorHelper.SetCounterfeit(item);
            else
                InsInjectorHelper.SetUnusable(item);
        }

        /// <summary>贵重品槽位的正品：清掉可能带的状态并固定 160。</summary>
        internal static void ApplyGenuine(GameItem item)
        {
            if (item == null) return;
            item.RemoveFeatureByCategory(Category);
            item.AddItemFeature(ItemFeatureList.InjectorFeature(
                ItemFeatureList.InjectorConditionType.Genuine,
                ItemFeatureList.InjectorConditionType.Genuine));
            item.unitValue = GenuineValue;
        }
    }
}
