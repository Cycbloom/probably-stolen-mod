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
    internal sealed class Region
    {
        /// <summary>
        /// 五块地块：左列入口 / 拆解堆 / 医疗，右列生活废料带 / 废武器堆。
        /// 解锁点数不写在这里，统一放在 UnlockCost 那张表里。
        /// </summary>
        internal static readonly Region[] All =
        {
            new Region("gate", "入口空地", "Main Gate",
                "通用杂项：材料、工具、日用品各有一点，适合补充常规消耗。",
                "A general mix of materials, tools, and household goods for everyday restocking.",
                0.20f, 0.22f),
            new Region("scrap", "拆解堆", "Scrap Pile",
                "材料、工具与电子废料；偶尔能拆出完整模组。",
                "Materials, tools, and electronic scrap, with the occasional intact module.",
                0.20f, 0.50f),
            new Region("refuse", "生活废料带", "Refuse Lane",
                "日用、食品、药品为主；值钱的东西不多，但胜在实用。",
                "Household goods, food, and medicine. Few valuables, but plenty of practical finds.",
                0.78f, 0.23f),
            new Region("med", "医疗废弃点", "Medical Waste",
                "绷带、药品、酒精、手术用品；能救命，也卖得上价。",
                "Bandages, pills, alcohol, and surgical supplies. Life-saving, and worth more than most.",
                0.20f, 0.78f),
            new Region("weapons", "废武器堆", "Weapon Dump",
                "断刀、土枪、散装弹药和改装件；偶尔还能翻出一把能用的枪。",
                "Broken blades, makeshift guns, loose rounds, and gun mods. Now and then a working gun.",
                0.78f, 0.76f)
        };

        internal readonly string Id;
        internal readonly string NameZh, NameEn, DetailZh, DetailEn;
        internal readonly float X, Y;
        /// <summary>解锁所需声望；0 表示无门槛。</summary>
        internal readonly int RequiredReputation;

        internal Region(string id, string zh, string en, string detailZh, string detailEn,
            float x, float y)
        {
            Id = id;
            NameZh = zh;
            NameEn = en;
            DetailZh = detailZh;
            DetailEn = detailEn;
            X = x;
            Y = y;
            RequiredReputation = UnlockCost.For(id);
        }

        internal string Name { get { return Copy.Text(NameZh, NameEn); } }
        internal string Detail { get { return Copy.Text(DetailZh, DetailEn); } }
        internal RegionLootTable Loot { get { return ScavLoot.Get(Id); } }

        internal bool Unlocked { get { return Reputation.Points >= RequiredReputation; } }
    }
}
