using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 大奖奖池里的一组：本区某一个大类的非垃圾物品，件数在 Min–Max 之间。
    /// 五区都按本区主题拆成 2–3 组，组内只从该大类抽，不再混成一个池。
    /// </summary>
    internal sealed class RewardGroup
    {
        internal readonly string[] Ids;
        internal readonly int Min;
        internal readonly int Max;

        internal RewardGroup(int min, int max, params string[] ids)
        {
            Min = min;
            Max = max;
            Ids = ids;
        }
    }
}
