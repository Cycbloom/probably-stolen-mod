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
            ModLifecycle.Enable();
            Debug.Log("[DumpingGroundMap] enabled");
        }

        public void OnDisable()
        {
            Enabled = false;
            ModLifecycle.Disable();
        }
    }
}
