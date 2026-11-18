using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>开日时把待发的储藏新闻排进原生未来事件队列。</summary>
    [HarmonyPatch]
    internal static class StashNewsPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(StoreEventManager), "OnDayStart", new Type[0]);
            ModLogger.Info("stash news: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        private static void Prefix(StoreEventManager __instance)
        {
            StashFind.ApplyPendingNews(__instance);
        }
    }
}
