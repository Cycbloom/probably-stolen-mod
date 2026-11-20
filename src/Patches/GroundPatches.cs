using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    [HarmonyPatch(typeof(EmporiumEntry), "SetupAfterhourInv")]
    internal static class AfterhourGroundPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            if (!Core.Enabled) return;
            try
            {
                GroundExpand.ResetForSetup();
                GroundExpand.Apply(__instance);
            }
            catch (System.Exception error)
            {
                // 这里在 EmporiumEntry.Start 中间执行，抛出去会把整个商店界面建一半就断掉，
                // 所以只记日志、放弃扩展，宁可地面保持原版尺寸。
                ModLogger.Warning("[DGM-GROUND] 跳过：初始化地面时出错 " + error);
            }
        }
    }

    /// <summary>画布尺寸变化时原生 Validate 会把地面放回右下角；这里按放大后的尺寸重新摆。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "Validate")]
    internal static class AfterhourGroundLayoutPatch
    {
        [HarmonyAfter(ContainerWindowScaleBridge.HarmonyId)]
        private static void Postfix(EmporiumEntry __instance)
        {
            // 只在拾荒场景、地面窗口真正显示时才重排；商店里的 Validate 一律不碰。
            if (!Core.Enabled || !GroundExpand.Visible) return;
            GroundExpand.Relayout(__instance);
        }
    }

    /// <summary>进入拾荒场景、地面窗口显示时才补一次接管与摆位。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "ShowAfterhourInv")]
    internal static class AfterhourGroundShowPatch
    {
        [HarmonyAfter(ContainerWindowScaleBridge.HarmonyId)]
        private static void Postfix(EmporiumEntry __instance)
        {
            if (!Core.Enabled) return;
            GroundExpand.SetVisible(true);
            GroundExpand.Apply(__instance);
            GroundExpand.Relayout(__instance);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "HideAfterhourInv")]
    internal static class AfterhourGroundHidePatch
    {
        private static void Postfix()
        {
            GroundExpand.SetVisible(false);
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "ScavengeDumpingGrounds")]
    internal static class ScavengeGroundScopePatch
    {
        private static void Prefix(out bool __state)
        {
            __state = false;
            GroundExpand.BeginScavenge();
            __state = true;
        }
        internal static System.Exception Finalizer(System.Exception __exception, bool __state)
        {
            if (__state) GroundExpand.EndScavenge();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GraphUtils), "TryAcceptAll")]
    internal static class GroundOverflowPatch
    {
        private static void Postfix(GraphNodeStorage currentNode, int __result)
        {
            if (__result <= 0) return;
            GroundExpand.OnRejected(currentNode);
        }
    }
}
