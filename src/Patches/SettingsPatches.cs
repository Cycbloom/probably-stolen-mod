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
    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class ModConfigEntryPatch
    {
        private static void Prefix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            NativeSettings.BeginEntry(b, config, entry);
        }

        private static void Postfix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            NativeSettings.Inject(b, config, entry);
        }
    }

    /// <summary>
    /// BuildEntry fixes the slider width right after creating it, while the entry's
    /// row is still open. Appending the value label at that moment puts it on the
    /// same row as the slider instead of on a line below.
    /// </summary>
    [HarmonyPatch(typeof(CustomUIElement), "SetFixedWidth")]
    internal static class SliderValueRowPatch
    {
        private static void Postfix(CustomUIElement __instance)
        {
            NativeSettings.AttachValueLabel(__instance);
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "Apply")]
    internal static class ModConfigApplyPatch
    {
        private static void Prefix()
        {
            NativeSettings.CommitDrag();
        }

        private static void Postfix()
        {
            NativeSettings.RefreshIfOpen();
        }
    }
}
