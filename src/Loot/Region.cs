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
        /// 八块地块：入口 / 拆解堆 / 生活废料带 / 医疗 / 废武器堆 / 电子垃圾坡 /
        /// 报废车场 / 废泵站。解锁点数不写在这里，统一放在 UnlockCost 那张表里。
        /// 前五块保持原有顺序和 id，后三块追加在末尾，避免旧存档与旧下标错位。
        /// 坐标是地标图片底部中心的 normalized UV（Y 向上）。
        /// </summary>
        internal static readonly Region[] All =
        {
            new Region("gate", "入口空地", "Main Gate",
                "通用杂项：材料、工具、日用品各有一点，适合补充常规消耗。",
                "A general mix of materials, tools, and household goods for everyday restocking.",
                0.15f, 0.13f),
            new Region("scrap", "拆解堆", "Scrap Pile",
                "成批金属、打印耗材、螺丝和布料；少量拆卸工具与电子残件。",
                "Metal, printing stock, fasteners and fabric, with a few dismantling tools and electronic remnants.",
                0.17f, 0.43f),
            new Region("refuse", "生活废料带", "Refuse Lane",
                "日用、食品、药品为主；值钱的东西不多，但胜在实用。",
                "Household goods, food, and medicine. Few valuables, but plenty of practical finds.",
                0.50f, 0.12f),
            new Region("med", "医疗废弃点", "Medical Waste",
                "绷带、药品、酒精、手术用品；能救命，也卖得上价。",
                "Bandages, pills, alcohol, and surgical supplies. Life-saving, and worth more than most.",
                0.17f, 0.73f),
            new Region("weapons", "废武器堆", "Weapon Dump",
                "断刀、土枪、散装弹药和改装件；偶尔还能翻出一把能用的枪。",
                "Broken blades, makeshift guns, loose rounds, and gun mods. Now and then a working gun.",
                0.82f, 0.72f),
            new Region("electronics", "电子垃圾坡", "E-Waste Slope",
                "电子元件、线缆与各类模组；维修和电子升级的主要来源。",
                "Components, cables and modules: the main source for electronics repairs and upgrades.",
                0.50f, 0.43f),
            new Region("vehicles", "报废车场", "Vehicle Graveyard",
                "压扁的车壳、拆空的车厢和散落的金属件；工具与可拆零件最多。",
                "Crushed shells, stripped cabins, and scattered metal. The best place for tools and salvageable parts.",
                0.82f, 0.38f),
            new Region("water", "废泵站", "Abandoned Pump Station",
                "净水用品与带水容器；半满附近最常见，水质以幽灵水为主，两端少见。",
                "Treatment supplies and water containers. Mid-fill and ghostwater are most common; extremes are rare.",
                0.50f, 0.74f)
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
