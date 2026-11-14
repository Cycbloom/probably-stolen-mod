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

        /// <summary>
        /// 读档/新档时清掉「今天已加过」的内存标记。
        /// 这个标记只防同一局内同一天重复加；读回旧存档后它属于过期状态，
        /// 必须与存档一起归零，否则读档后当天会误跳过、或与存档里的客人重复。
        /// </summary>
        internal static void ResetCache()
        {
            lastStore = null;
            lastDay = int.MinValue;
        }

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

    /// <summary>
    /// 客人卡副标题里的「来访原因」改写。
    ///
    /// 原生 StoreClient.attractedType / attractedItemId 只能填「大类名」或「物品名」，
    /// 表达不了「被这位店主的名声吸引」，所以不在数据上做文章，改在 UI 侧覆盖：
    /// 副标题由 NegociationUIManager.RefreshClientHeader(StoreClient) 最后拼装并
    /// 赋给 frame.clientSubtitle（TextMeshProUGUI），我们在这个方法的 Postfix 里
    /// 覆盖文本。每次刷新都会重跑，文本不会累积；只对
    /// identifier = scavGeneral 且声望 >= 20 档的客人生效，其它客人完全不动。
    ///
    /// 两个判断口径的取舍：
    /// - 只认「我们加出来的那位」需要持久标记；StoreClient 没有可用的自定义字段，
    ///   用内存集合会在读档后失效（客人是反序列化出来的新对象）。
    /// - 因此采用「所有没有原生吸引原因的拾荒客」：既覆盖我们每天加的那位，也覆盖
    ///   原生随机生成的、本来就没写吸引原因的拾荒客；带原生原因的（例如展示柜吸引）
    ///   一律跳过，不覆盖真实信息。无需额外标记，读档后依然成立。
    ///
    /// frame 是 private 字段，用 AccessTools.Field 取一次并缓存（与现有
    /// AccessTools.Method 取 GenerateClient 同一种做法），不用字符串拼反射。
    /// 副标题原生在「没有原因」时隐藏，客人名字整体上移 13px；补上文本后按原版
    /// 同样的 SetRect 参数（48, 70, 530, 26）恢复成「有原因」的排布。
    /// </summary>
    [HarmonyPatch]
    internal static class ClientReasonPatch
    {
        private const string ScavengerClientId = "scavGeneral";

        private static FieldInfo frameField;
        private static bool frameFieldMissing;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(NegociationUIManager), "RefreshClientHeader",
                new Type[] { typeof(StoreClient) });
            Debug.Log("[DumpingGroundMap] client reason: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            if (method == null) return null;
            frameField = AccessTools.Field(typeof(NegociationUIManager), "frame");
            if (frameField == null)
            {
                frameFieldMissing = true;
                Debug.LogWarning("[DumpingGroundMap] client reason: field NegociationUIManager.frame NOT FOUND");
            }
            return method;
        }

        private static void Postfix(NegociationUIManager __instance, StoreClient storeClient)
        {
            if (!Core.Enabled || __instance == null || storeClient == null) return;
            if (frameFieldMissing || frameField == null) return;
            if (storeClient.identifier != ScavengerClientId) return;
            if (Reputation.Points < Reputation.Perk20Threshold) return;

            // 已经有原生吸引原因的客人保持原样，只补「本来没有原因」的那些。
            if (!string.IsNullOrEmpty(storeClient.attractedItemId)
                || !string.IsNullOrEmpty(storeClient.attractedType)) return;

            try
            {
                NegotiationTabletFrame frame = frameField.GetValue(__instance) as NegotiationTabletFrame;
                if (frame == null || frame.clientSubtitle == null) return;

                string reason = Reputation.Perk20Reason;
                if (frame.clientSubtitle.text != reason)
                {
                    frame.clientSubtitle.text = reason;
                    Debug.Log("[DumpingGroundMap] client reason: rewritten id=" + storeClient.identifier
                        + " day=" + StoreStation.GetDayCounter() + " points=" + Reputation.Points
                        + " text=" + reason);
                }
                frame.clientSubtitle.gameObject.SetActive(true);
                if (frame.clientName != null)
                {
                    // 与原生「有吸引原因」时相同的排布参数。
                    WildNetworkUI.SetRect(frame.clientName.rectTransform, 48f, 70f, 530f, 26f);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] client reason: failed: " + ex.Message);
            }
        }
    }
}
