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
    /// 表里那些「虚拟 id」到真实物品的映射与状态补写：
    /// 免疫宁次品、假海明威香烟、仿品食品券、小瓶酒精。
    /// 虚拟 id 只活在掉落表与告示板里，真正生成时换成游戏自己的 id。
    /// </summary>
    internal static class LootVariants
    {
        internal static string RealId(string itemId)
        {
            if (itemId == LootCatalog.InjectorBadId) return LootCatalog.InjectorId;
            if (itemId == LootCatalog.CigaretteFakeId) return LootCatalog.CigaretteId;
            if (itemId == LootCatalog.StampFakeId) return "food_stamp";
            if (itemId == LootCatalog.MiniAlcoholId) return "mini_bottle";
            return itemId;
        }

        /// <summary>小瓶酒精要现灌液体，不能走目录 id；其余直接生成。</summary>
        internal static GameItem Create(string itemId)
        {
            if (itemId == LootCatalog.MiniAlcoholId)
                return PreBuildChemHelper.CreateMiniAlcoholBottle();
            return ItemSpawner.Spawn(RealId(itemId));
        }

        internal static void Apply(string itemId, GameItem item)
        {
            if (item == null) return;
            if (itemId == LootCatalog.InjectorBadId) { InjectorQuality.ApplyBad(item); return; }
            if (itemId == LootCatalog.InjectorId) { InjectorQuality.ApplyGenuine(item); return; }
            if (itemId == LootCatalog.CigaretteId) { Cigarette(item, true); return; }
            if (itemId == LootCatalog.CigaretteFakeId) { Cigarette(item, false); return; }
            if (itemId == LootCatalog.StampFakeId) { FakeStamp(item); return; }
        }

        /// <summary>
        /// 香烟的单价由游戏的 ItemCondition onActivateAction 写入：正品 100、赝品 50。
        /// 这里只补条件，不再写 unitValue，避免覆盖游戏定价。
        /// </summary>
        private static void Cigarette(GameItem item, bool genuine)
        {
            item.RemoveFeatureByCategory("CATEGORY_GENUINE_CIGARETTE");
            if (genuine)
            {
                InsCigaretteHelper.SetGenuine(item);
                item.AddItemFeature(ItemFeatureList.CigaretteAuthenticity());
            }
            else
            {
                InsCigaretteHelper.SetConterfeit(item);
                item.AddItemFeature(ItemFeatureList.CigaretteCounterfeit());
            }
        }

        /// <summary>仿品食品券：外观像真的（fake authenticity），实际兑不了，只值废纸价。</summary>
        private static void FakeStamp(GameItem item)
        {
            item.RemoveFeatureByCategory("CATEGORY_GENUINE_STAMP");
            InsStampHelper.Init(item);
            InsStampHelper.SetCounterfeit(item, 40);
            item.AddItemFeature(ItemFeatureList.StampFakeAuthenticity());
            item.unitValue = 3L;
        }
    }
}
