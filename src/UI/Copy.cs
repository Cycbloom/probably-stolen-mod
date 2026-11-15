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
    internal static class Copy
    {
        internal static string Text(string zh, string en)
        {
            string locale = LocHelper.GetCurrentLocaleCode();
            return locale != null && locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? zh : en;
        }
    }
}
