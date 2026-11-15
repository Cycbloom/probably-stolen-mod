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
    public sealed class Core : IMod
    {
        internal const string Id = "local.dumping_ground_map";
        internal static bool Enabled;

        public void Init(ModManifest manifest)
        {
            Tuning.Init();
        }

        public void OnEnable()
        {
            Enabled = true;
            Tuning.Init();
            Reputation.Hook();
            // 必须排在 Reputation.Hook() 之后：新档事件的订阅顺序决定执行顺序，
            // 我们要在 Reputation.NewGame 把声望归零之后写入初始 20 点。
            ScavengerStart.Hook();
            Debug.Log("[DumpingGroundMap] enabled");
        }

        public void OnDisable()
        {
            Enabled = false;
            ScavengerStart.Unhook();
            Reputation.Unhook();
            NativeSettings.Reset();
            Picker.Close();
            Picker.ResetChoice();
        }
    }
}
