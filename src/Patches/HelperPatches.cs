using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>原版离场确认是不是「拾荒地面还有东西」这一次。</summary>
    [HarmonyPatch(typeof(MapUIManager), "CheckItemLeftOnGround")]
    internal static class HelperLeavePromptPatch
    {
        private static void Prefix()
        {
            if (!Core.Enabled) return;
            ScavHelperService.Arm();
        }

        private static void Postfix(bool __result)
        {
            ScavHelperService.ArmResult(__result);
        }
    }

    /// <summary>确认框弹出来时在原按钮下方补一个按钮，关掉时恢复原版尺寸。</summary>
    [HarmonyPatch(typeof(GenericConfirmationUIManager), "OpenPanel")]
    internal static class HelperPanelPatch
    {
        private static void Postfix(GenericConfirmationUIManager __instance)
        {
            if (!Core.Enabled) return;
            ScavHelperService.SyncPanel(__instance, true);
        }
    }

    [HarmonyPatch(typeof(GenericConfirmationUIManager), "ClosePanel")]
    internal static class HelperPanelClosePatch
    {
        private static void Postfix(GenericConfirmationUIManager __instance)
        {
            ScavHelperService.SyncPanel(__instance, false);
        }
    }

    /// <summary>每次重建拾荒场景都重新允许一次帮手入口。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "SetupAfterhourInv")]
    internal static class HelperResetPatch
    {
        private static void Postfix()
        {
            ScavHelperService.Reset();
        }
    }
}
