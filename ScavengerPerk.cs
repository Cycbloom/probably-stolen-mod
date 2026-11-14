using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 20 点奖励：每天多一名拾荒客客人。
    ///
    /// 客人链路：PlayerStore.OnShutterOpened() 每天开张调用一次
    /// StoreClientManager.GenerateClient()（全程序集仅此一处调用），
    /// 内部依次生成正常客人、原生 perk 客人、服务客人等。
    /// 因此把补丁挂在 GenerateClient 的 Postfix：每天开张一次，
    /// 天然排在正常客人之后，不需要碰 GetNormalCustomerCount。
    ///
    /// 拾荒客客人的工厂是 StoreClientList.CreateScavGeneral()
    /// （identifier = scavGeneral，名字词条 name_scavenger）。
    /// 不用 TryAddClient：它会被 IsClientOnCooldown 用 clientTracker 里
    /// 该 identifier 的计数永久拦住，不适合“每天一名”的奖励；改用原生
    /// HandlePerkClient 同款的 AddClient。
    ///
    /// 只读 Reputation.Points，不改存档结构；客人本身由游戏正常保存。
    /// 用 runID + 天数防同一天重复（例如反复开关店门）。
    /// </summary>
    [HarmonyPatch]
    internal static class ScavengerPerkPatch
    {
        /// <summary>奖励门槛，与柱子上的 20 点刻度一致；要调整只改这里。</summary>
        private const int Threshold = 20;

        private static string lastStore;
        private static int lastDay = int.MinValue;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(StoreClientManager), "GenerateClient", new Type[0]);
            Debug.Log("[DumpingGroundMap] scavenger perk: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        private static void Postfix(StoreClientManager __instance)
        {
            if (!Core.Enabled || __instance == null) return;
            int points = Reputation.Points;
            if (points < Threshold) return;

            string store = PlayerStore.Instance == null ? null : PlayerStore.Instance.runID;
            int day = StoreStation.GetDayCounter();
            if (store != null && store == lastStore && day == lastDay)
            {
                Debug.Log("[DumpingGroundMap] scavenger perk: skip, already added for day " + day);
                return;
            }

            try
            {
                StoreClient client = StoreClientList.CreateScavGeneral();
                if (client == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] scavenger perk: CreateScavGeneral returned null");
                    return;
                }
                __instance.AddClient(client);
                lastStore = store;
                lastDay = day;
                Debug.Log("[DumpingGroundMap] scavenger perk: +1 scavenger client day=" + day
                    + " points=" + points + " identifier=" + client.identifier);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] scavenger perk: add failed: " + ex.Message);
            }
        }
    }
}
