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
            new Region("gate", "region_gate_name", "region_gate_detail", 0.15f, 0.13f),
            new Region("scrap", "region_scrap_name", "region_scrap_detail", 0.17f, 0.43f),
            new Region("refuse", "region_refuse_name", "region_refuse_detail", 0.50f, 0.12f),
            new Region("med", "region_med_name", "region_med_detail", 0.17f, 0.73f),
            new Region("weapons", "region_weapons_name", "region_weapons_detail", 0.82f, 0.72f),
            new Region("electronics", "region_electronics_name", "region_electronics_detail", 0.50f, 0.43f),
            new Region("vehicles", "region_vehicles_name", "region_vehicles_detail", 0.82f, 0.38f),
            new Region("water", "region_water_name", "region_water_detail", 0.50f, 0.74f)
        };

        internal readonly string Id;
        internal readonly string NameKey, DetailKey;
        internal readonly float X, Y;
        /// <summary>解锁所需声望；0 表示无门槛。</summary>
        internal readonly int RequiredReputation;

        internal Region(string id, string nameKey, string detailKey,
            float x, float y)
        {
            Id = id;
            NameKey = nameKey;
            DetailKey = detailKey;
            X = x;
            Y = y;
            RequiredReputation = UnlockCost.For(id);
        }

        internal string Name { get { return Copy.Get(NameKey); } }
        internal string Detail { get { return Copy.Get(DetailKey); } }
        internal RegionLootTable Loot { get { return ScavLoot.Get(Id); } }

        internal bool Unlocked { get { return Reputation.Points >= RequiredReputation; } }
    }
}
