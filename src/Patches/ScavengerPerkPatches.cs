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
            ModLogger.Info("scavenger perk: patch target "
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
                ModLogger.Info("scavenger perk: skip, already added for day " + day);
                return;
            }

            try
            {
                StoreClient client = StoreClientList.CreateScavGeneral();
                if (client == null)
                {
                    ModLogger.Warning("scavenger perk: CreateScavGeneral returned null");
                    return;
                }
                // 用游戏自带的 mod 来源标记这位客人：ModHelper.SetClientSource 只写
                // StoreClient.sourceModId，不参与任何玩法判定，随客人对象一起生灭。
                // UI 侧据此识别「我们加的那位」——identifier 不可靠，原生随机客池、
                // 剧情与拖车摊贩也会生成 scavGeneral。
                ModHelper.SetClientSource(client, Core.Id);
                __instance.AddClient(client);
                lastStore = store;
                lastDay = day;
                ModLogger.Info("scavenger perk: +1 scavenger client day=" + day
                    + " points=" + points + " identifier=" + client.identifier
                    + " source=" + client.sourceModId);
            }
            catch (Exception ex)
            {
                ModLogger.Warning("scavenger perk: add failed: " + ex.Message);
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
    /// 覆盖文本。每次刷新都会重跑，文本不会累积。
    ///
    /// 判据是「我们加的那位」本身：ScavengerPerkPatch 创建客人时调用
    /// ModHelper.SetClientSource(client, Core.Id)，写的是 StoreClient.sourceModId。
    /// 这里只认 sourceModId == Core.Id 且声望 >= 20 的客人，其它客人（包括原生
    /// 随机客池 / 剧情 / 拖车摊贩生成的拾荒客，以及带原生吸引原因的客人）完全不动。
    /// 不再用 identifier == scavGeneral：那个值有多个原生来源，无法区分是不是我们加的。
    ///
    /// sourceModId 是普通字符串字段，跟着客人对象一起生灭；客人每天开张时重新生成、
    /// 结束日清空，不进存档，所以读档后当天重新开张会重新打标，行为一致。
    ///
    /// frame 是 private 字段，用 AccessTools.Field 取一次并缓存（与现有
    /// AccessTools.Method 取 GenerateClient 同一种做法），不用字符串拼反射。
    /// 副标题原生在「没有原因」时隐藏，客人名字整体上移 13px；补上文本后按原版
    /// 同样的 SetRect 参数（48, 70, 530, 26）恢复成「有原因」的排布。
    /// </summary>
    [HarmonyPatch]
    internal static class ClientReasonPatch
    {
        private static FieldInfo frameField;
        private static bool frameFieldMissing;

        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(NegociationUIManager), "RefreshClientHeader",
                new Type[] { typeof(StoreClient) });
            ModLogger.Info("client reason: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            if (method == null) return null;
            frameField = AccessTools.Field(typeof(NegociationUIManager), "frame");
            if (frameField == null)
            {
                frameFieldMissing = true;
                ModLogger.Warning("client reason: field NegociationUIManager.frame NOT FOUND");
            }
            return method;
        }

        private static void Postfix(NegociationUIManager __instance, StoreClient storeClient)
        {
            if (!Core.Enabled || __instance == null || storeClient == null) return;
            if (frameFieldMissing || frameField == null) return;
            // 只认我们加的那位：来源标记由 ScavengerPerkPatch 在创建时写入。
            if (storeClient.sourceModId != Core.Id) return;
            if (Reputation.Points < PerkCatalog.Perk20Threshold) return;

            try
            {
                NegotiationTabletFrame frame = frameField.GetValue(__instance) as NegotiationTabletFrame;
                if (frame == null || frame.clientSubtitle == null) return;

                string reason = Reputation.Perk20Reason;
                if (frame.clientSubtitle.text != reason)
                {
                    frame.clientSubtitle.text = reason;
                    ModLogger.Info("client reason: rewritten id=" + storeClient.identifier
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
                ModLogger.Warning("client reason: failed: " + ex.Message);
            }
        }
    }
}
