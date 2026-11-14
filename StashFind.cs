using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 60 档「眼力毒辣」的爆大奖。
    ///
    /// 概率用 AIMD：起点 0.05%，未中且没到上限一半时翻倍（慢启动），
    /// 过半或中过奖后改为每次 +0.05% 线性爬升；中奖后窗口砍半再继续。
    /// 上限来自设置项 stash_cap（0.1–5.0，步进 0.1）；
    /// 声望不到 60 时上限硬顶 0.1%，但不完全排除。
    ///
    /// 一趟（进入某处垃圾场后）最多中一次，标记只在内存里，换区时清空。
    /// 中奖后当天、同一区域，每次搜索另有 25% 概率再给 1 件该区样板物品。
    ///
    /// 中奖当晚把「第二天出新闻」写进存档（同声望的 modData 机制），
    /// 第二天开日时由 StoreEventManager.OnDayStart 的 Prefix 调原生
    /// QueueFuturEvent 预定并激活事件，报纸与降价都走原生链路。
    /// </summary>
    internal static class StashFind
    {
        internal const string ChanceKey = "stash_chance";
        internal const string PhaseKey = "stash_phase";
        internal const string HitDayKey = "stash_hit_day";
        internal const string HitRegionKey = "stash_hit_region";
        internal const string NewsDayKey = "stash_news_day";
        internal const string NewsFiredKey = "stash_news_fired";

        /// <summary>窗口起点与线性步长，单位是百分数（0.05 = 0.05%）。</summary>
        internal const float StartChance = 0.05f;
        internal const float LinearStep = 0.05f;

        /// <summary>声望不到 60 时的硬上限，单位是百分数。</summary>
        internal const float HardCapLowRep = 0.1f;

        internal const float BonusChance = 25f;
        internal const int JackpotItems = 5;
        internal const int BonusItems = 1;

        private static float chance = StartChance;
        private static int phase;
        private static int hitDay = -1;
        private static string hitRegion = "";
        private static int newsDay = -1;
        private static int newsFired = -1;

        private static bool loaded;
        private static bool tripHit;
        private static bool hooked;

        internal static void Hook()
        {
            if (hooked) return;
            try
            {
                ModHook.OnGameLoadedLate += Reload;
                ModHook.OnNewGameStarted += NewGame;
                hooked = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] could not hook stash find: " + ex);
            }
        }

        internal static void Unhook()
        {
            if (!hooked) return;
            ModHook.OnGameLoadedLate -= Reload;
            ModHook.OnNewGameStarted -= NewGame;
            hooked = false;
        }

        /// <summary>进入某个区域时调用：一趟最多中一次，换区/重新进图都重置。</summary>
        internal static void ResetTrip()
        {
            tripHit = false;
        }

        private static int Today()
        {
            try { return StoreStation.GetDayCounter(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: day counter failed: " + ex.Message);
                return -1;
            }
        }

        /// <summary>实际生效的上限；声望不到 60 时压到 0.1%。</summary>
        private static float EffectiveCap()
        {
            float cap = Tuning.StashCap;
            if (Reputation.Points < Reputation.Perk60Threshold)
                cap = Mathf.Min(cap, HardCapLowRep);
            return cap;
        }

        private static float ClampChance(float value, float cap)
        {
            if (float.IsNaN(value)) return StartChance;
            return Mathf.Clamp(value, StartChance, cap);
        }

        /// <summary>
        /// 每次拾荒（GetRandomScavengedItem 的产物已经成型）调用一次。
        /// 追加的物品直接进同一次返回列表，因此后面按列表结算声望时会被计入。
        /// </summary>
        internal static void OnScavenge(List<GameItem> items)
        {
            if (!Core.Enabled || items == null) return;
            Region region = Picker.ChosenRegion;
            if (region == null) return;

            StashTemplate template = StashTemplate.For(region.Id);
            if (template == null) return;   // 目前只有生活废料带落地

            EnsureLoaded();
            int today = Today();
            if (today < 0) return;

            float cap = EffectiveCap();
            chance = ClampChance(chance, cap);
            bool bonusActive = hitDay == today && hitRegion == region.Id;

            float roll = UnityEngine.Random.value * 100f;
            bool hit = !tripHit && roll < chance;
            float before = chance;

            if (hit)
            {
                AddReward(items, template, JackpotItems);
                chance = Mathf.Max(StartChance, chance * 0.5f);
                chance = ClampChance(chance, cap);
                phase = 1;
                tripHit = true;
                hitDay = today;
                hitRegion = region.Id;
                newsDay = today + 1;
                SaveState();
                NotifyFind();
                Debug.Log("[DumpingGroundMap] stash: jackpot region=" + region.Id
                    + " day=" + today + " roll=" + roll.ToString("F3")
                    + " items=" + JackpotItems + " chance " + before.ToString("F3")
                    + " -> " + chance.ToString("F3") + " phase=" + phase
                    + " news_day=" + newsDay);
            }
            else
            {
                float next;
                if (phase == 0 && chance < cap * 0.5f)
                {
                    next = chance * 2f;
                    if (next >= cap * 0.5f) phase = 1;
                }
                else
                {
                    next = chance + LinearStep;
                    phase = 1;
                }
                if (next > cap) next = cap;
                chance = next;
                SaveState();
                Debug.Log("[DumpingGroundMap] stash: miss region=" + region.Id
                    + " day=" + today + " roll=" + roll.ToString("F3")
                    + " chance " + before.ToString("F3") + " -> " + chance.ToString("F3")
                    + " cap=" + cap.ToString("F2") + " phase=" + phase
                    + " trip_hit=" + (tripHit ? 1 : 0));
            }

            if (bonusActive && UnityEngine.Random.value * 100f < BonusChance)
            {
                AddReward(items, template, BonusItems);
                Debug.Log("[DumpingGroundMap] stash: bonus region=" + region.Id
                    + " day=" + today + " items=" + BonusItems);
            }
        }

        /// <summary>
        /// 中奖当场提示：复用原生拾荒界面「受伤」那套通知
        /// （StoreUIManager.Instance.Notify(text, "sec")，最终落到
        /// ReputationUIManager 的同一个 notifier 位置与样式），不另做 UI。
        /// 两行用 \n 分隔，TMP 文本直接支持；文案短，不挡拾荒面板。
        /// </summary>
        private static void NotifyFind()
        {
            try
            {
                if (StoreUIManager.Instance == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: notice skipped, no StoreUIManager");
                    return;
                }
                string text = Copy.Text(
                    "你翻出了一整间储藏室。\n今天在这个地方，每次搜索都可能多一件东西。",
                    "You've dug out a whole storeroom.\nEvery search here today may turn up something extra.");
                StoreUIManager.Instance.Notify(text, "sec");
                Debug.Log("[DumpingGroundMap] stash: notice shown");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: notice failed: " + ex.Message);
            }
        }

        private static void AddReward(List<GameItem> items, StashTemplate template, int count)
        {
            string[] pool = template.RewardIds;
            if (pool == null || pool.Length == 0) return;
            for (int i = 0; i < count; ++i)
            {
                string id = pool[UnityEngine.Random.Range(0, pool.Length)];
                try
                {
                    GameItem item = ItemSpawner.Spawn(id);
                    if (item != null) items.Add(item);
                    else Debug.LogWarning("[DumpingGroundMap] stash: spawn failed " + id);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: spawn failed " + id + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 每天开日的 Prefix：如果这次开日该出我们的事件，就先走原生
        /// QueueFuturEvent 排进当天；同一次 OnDayStart 里的 HandleFuturEvent
        /// 会立刻把它激活，报纸与价格效果都由原生逻辑接管。
        /// </summary>
        internal static void ApplyPendingNews(StoreEventManager manager)
        {
            if (!Core.Enabled || manager == null) return;
            EnsureLoaded();
            if (newsDay <= 0) return;

            int today = Today();
            if (today < 0 || today < newsDay) return;

            StashTemplate template = StashTemplate.For(hitRegion);
            if (template == null)
            {
                newsDay = -1;
                SaveState();
                return;
            }
            if (newsFired == today)
            {
                newsDay = -1;
                SaveState();
                return;
            }

            try
            {
                StoreEvent ev = BuildEvent(template);
                manager.QueueFuturEvent(ev, today, true);
                newsFired = today;
                newsDay = -1;
                SaveState();
                Debug.Log("[DumpingGroundMap] stash: news queued day=" + today
                    + " id=" + ev.identifier + " region=" + template.RegionId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: news queue failed: " + ex.Message);
            }
        }

        private static StoreEvent BuildEvent(StashTemplate template)
        {
            StoreEvent ev = new StoreEvent();
            ev.identifier = "dgm_stash_" + template.RegionId;
            ev.newsName = template.NewsName;
            ev.displayName = template.DisplayName;
            ev.newsDescription = template.NewsDescription;
            ev.eventType = StoreEvent.EventType.NORMALE;
            ev.eventArea = StoreEvent.EventArea.ALL;
            ev.cooldown = 60;
            ev.duration = 1;
            ev.totalDuration = 1;
            ev.isHidden = false;
            ev.isHiddenFromNewsPaper = false;
            ev.IsKnown = true;
            // 与原版「储藏室被挖出」同一档；报纸布局有限时按重要性降序取前几条。
            ev.importance = 3;
            if (template.ItemType != null && template.PricePercent != 0)
            {
                ev.negociationDatas.Add(new NegociationData(template.ItemType,
                    template.PricePercent, template.NewsName, template.DisplayName));
            }
            return ev;
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            Reload(CurrentStoreName());
        }

        private static string CurrentStoreName()
        {
            try
            {
                return PlayerStore.HasInstance ? PlayerStore.Instance.runID : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>OnGameLoadedLate 是无参委托（与声望那套一致）。</summary>
        private static void Reload()
        {
            Reload(CurrentStoreName());
        }

        private static void Reload(string saveName)
        {
            try
            {
                chance = ClampChance(
                    ModHelper.GetModDataFloat(Core.Id, ChanceKey, StartChance), Tuning.StashCap);
                phase = ModHelper.GetModDataInt(Core.Id, PhaseKey, 0);
                hitDay = ModHelper.GetModDataInt(Core.Id, HitDayKey, -1);
                hitRegion = ModHelper.GetModData(Core.Id, HitRegionKey, "") ?? "";
                newsDay = ModHelper.GetModDataInt(Core.Id, NewsDayKey, -1);
                newsFired = ModHelper.GetModDataInt(Core.Id, NewsFiredKey, -1);
                tripHit = false;
                loaded = true;
                Debug.Log("[DumpingGroundMap] stash: loaded chance=" + chance.ToString("F3")
                    + " phase=" + phase + " hit_day=" + hitDay
                    + " hit_region=" + hitRegion + " news_day=" + newsDay
                    + " news_fired=" + newsFired + " store=" + (saveName ?? "?"));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: load failed: " + ex.Message);
            }
        }

        private static void NewGame(string saveName)
        {
            chance = StartChance;
            phase = 0;
            hitDay = -1;
            hitRegion = "";
            newsDay = -1;
            newsFired = -1;
            tripHit = false;
            loaded = true;
            Debug.Log("[DumpingGroundMap] stash: reset for new game");
        }

        private static void SaveState()
        {
            try
            {
                ModHelper.SetModDataFloat(Core.Id, ChanceKey, chance);
                ModHelper.SetModDataInt(Core.Id, PhaseKey, phase);
                ModHelper.SetModDataInt(Core.Id, HitDayKey, hitDay);
                ModHelper.SetModData(Core.Id, HitRegionKey, hitRegion ?? "");
                ModHelper.SetModDataInt(Core.Id, NewsDayKey, newsDay);
                ModHelper.SetModDataInt(Core.Id, NewsFiredKey, newsFired);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: save failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 爆大奖的区域样板：奖励物品池、报纸文案、以及「要不要真挂价格效果」。
    /// 目前只落地「生活废料带 → 日用品」；以后加医疗品等只需再补一条，
    /// 没有对应市场品类的区域把 ItemType 留 null、只在正文里提一句。
    /// </summary>
    internal sealed class StashTemplate
    {
        internal readonly string RegionId;
        internal readonly string[] RewardIds;
        internal readonly string ItemType;
        internal readonly int PricePercent;
        private readonly string newsNameZh;
        private readonly string newsNameEn;
        private readonly string newsDescZh;
        private readonly string newsDescEn;
        private readonly string displayZh;
        private readonly string displayEn;

        private StashTemplate(string regionId, string[] rewardIds, string itemType, int pricePercent,
            string newsNameZh, string newsNameEn, string newsDescZh, string newsDescEn,
            string displayZh, string displayEn)
        {
            RegionId = regionId;
            RewardIds = rewardIds;
            ItemType = itemType;
            PricePercent = pricePercent;
            this.newsNameZh = newsNameZh;
            this.newsNameEn = newsNameEn;
            this.newsDescZh = newsDescZh;
            this.newsDescEn = newsDescEn;
            this.displayZh = displayZh;
            this.displayEn = displayEn;
        }

        internal string NewsName { get { return Copy.Text(newsNameZh, newsNameEn); } }
        internal string NewsDescription { get { return Copy.Text(newsDescZh, newsDescEn); } }
        internal string DisplayName { get { return Copy.Text(displayZh, displayEn); } }

        // 日用品清单与 Tuning 的生活废料带主题表同源，随机/盒装不做区分。
        private static readonly string[] HouseholdIds =
        {
            "caffeine_pill", "toothpaste", "water_filter", "shampoo",
            "paper_towel", "toilet_paper",
            "full_water_test_strip_box", "full_water_purification_tablet_box"
        };

        private static readonly StashTemplate[] All =
        {
            new StashTemplate("refuse", HouseholdIds, "HOUSEHOLD_GOOD", -20,
                "垃圾场里翻出的储藏",
                "A Storeroom Found in the Dump",
                "某位店主昨晚在垃圾场里翻出了一整间储藏室，日用品装了好几箱。大量商品涌入市场，很可能导致今日价格下跌。",
                "A shopkeeper dug a whole storeroom out of the dump last night, household goods by the crate. A high volume of goods making its way to market will likely lower prices today.",
                "储藏室出货", "Storeroom Surplus")
        };

        internal static StashTemplate For(string regionId)
        {
            if (string.IsNullOrEmpty(regionId)) return null;
            for (int i = 0; i < All.Length; ++i)
            {
                if (All[i].RegionId == regionId) return All[i];
            }
            return null;
        }
    }

    /// <summary>开日时把待发的储藏新闻排进原生未来事件队列。</summary>
    [HarmonyPatch]
    internal static class StashNewsPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodBase method = AccessTools.Method(typeof(StoreEventManager), "OnDayStart", new Type[0]);
            Debug.Log("[DumpingGroundMap] stash news: patch target "
                + (method == null ? "NOT FOUND" : method.DeclaringType.Name + "." + method.Name));
            return method;
        }

        private static void Prefix(StoreEventManager __instance)
        {
            StashFind.ApplyPendingNews(__instance);
        }
    }
}
