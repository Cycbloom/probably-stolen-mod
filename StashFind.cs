using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// 60 档「眼力毒辣」的爆大奖，以及同一档位的小奖。
    ///
    /// 大奖概率用 AIMD：起点 0.05%，未中且没到上限一半时翻倍（慢启动），
    /// 过半或中过奖后改为每次 +0.05% 线性爬升；中奖后窗口砍半再继续。
    /// 上限来自设置项 stash_cap（0.1–5.0，步进 0.1）；
    /// 声望不到 60 时上限硬顶 0.1%，但不完全排除。
    ///
    /// 小奖是独立设置项 minor_percent（0–10%，默认 3%），平常概率单独掷：
    /// 声望 &lt; 60 时按设置值的 1/10 生效，≥ 60 时全值生效，0 时两档都不出；
    /// 不写 AIMD 状态、不登新闻、不改价格、不触发大奖的当日额外收获。
    /// 同一次搜索里大奖命中时完全跳过小奖。
    ///
    /// 一趟（进入某处垃圾场后）最多中一次大奖，标记只在内存里，换区时清空。
    /// 中奖后当天、同一区域，后续每次搜索固定再给 1 件该区样板物品。
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

        internal const int JackpotMinItems = 7;
        internal const int JackpotMaxItems = 8;
        internal const int BonusItems = 1;

        /// <summary>小奖件数区间与目标价值浮动范围。</summary>
        internal const int MinorMinItems = 1;
        internal const int MinorMaxItems = 12;
        internal const float MinorValueFloor = 0.75f;
        internal const float MinorValueCeil = 1.25f;

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
        ///
        /// 顺序：先掷大奖（AIMD）；大奖命中就完全跳过小奖；
        /// 大奖没中才独立掷小奖（不改 AIMD 状态）。
        /// 当天同区额外收获最后判定（每次搜索固定 1 件），与两者互不干扰。
        /// </summary>
        internal static void OnScavenge(List<GameItem> items)
        {
            if (!Core.Enabled || items == null) return;
            Region region = Picker.ChosenRegion;
            if (region == null) return;

            EnsureLoaded();
            int today = Today();
            if (today < 0) return;

            StashTemplate template = StashTemplate.For(region.Id);
            float cap = EffectiveCap();
            chance = ClampChance(chance, cap);
            bool bonusActive = hitDay == today && hitRegion == region.Id;
            bool jackpotHit = false;

            if (template != null)
            {
                float roll = UnityEngine.Random.value * 100f;
                bool hit = !tripHit && roll < chance;
                float before = chance;

                if (hit)
                {
                    int count = UnityEngine.Random.Range(JackpotMinItems, JackpotMaxItems + 1);
                    AddReward(items, template, count);
                    chance = Mathf.Max(StartChance, chance * 0.5f);
                    chance = ClampChance(chance, cap);
                    phase = 1;
                    tripHit = true;
                    hitDay = today;
                    hitRegion = region.Id;
                    newsDay = today + 1;
                    jackpotHit = true;
                    SaveState();
                    NotifyFind();
                    Debug.Log("[DumpingGroundMap] stash: jackpot region=" + region.Id
                        + " day=" + today + " roll=" + roll.ToString("F3")
                        + " items=" + count + " chance " + before.ToString("F3")
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
            }

            if (!jackpotHit) TryMinor(items, region, today);

            if (bonusActive && template != null)
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
        /// 小奖：按设置项 minor_percent 独立掷一次。声望 &lt; 60 时按设置值的 1/10 生效，
        /// 声望 ≥ 60 时按设置值全值生效；设置值为 0 时两档都是 0。
        /// 命中后从该区的整箱表里随机抽一种，整箱只放同一样东西。
        /// 不写 AIMD 状态、不登新闻、不改价格、不触发大奖的当日额外收获。
        /// </summary>
        private static void TryMinor(List<GameItem> items, Region region, int today)
        {
            float percent = Tuning.MinorPercent;
            if (percent <= 0f) return;
            if (Reputation.Points < Reputation.Perk60Threshold) percent *= 0.1f;

            MinorBox box = MinorBox.Roll(region.Id);
            if (box == null) return;

            float roll = UnityEngine.Random.value * 100f;
            if (roll >= percent) return;

            int crateValue;
            int count = MinorCount(box, out crateValue);
            int spawned = 0;
            for (int i = 0; i < count; ++i)
            {
                try
                {
                    GameItem item = ItemSpawner.Spawn(box.ItemId);
                    if (item != null)
                    {
                        items.Add(item);
                        ++spawned;
                    }
                    else
                    {
                        Debug.LogWarning("[DumpingGroundMap] stash: minor spawn failed " + box.ItemId);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: minor spawn failed "
                        + box.ItemId + ": " + ex.Message);
                }
            }

            NotifyMinor(box);
            Debug.Log("[DumpingGroundMap] stash: minor region=" + region.Id
                + " day=" + today + " box=" + box.ItemId + " items=" + spawned + "/" + count
                + " value=" + crateValue + " target=" + Tuning.MinorValue.ToString("F0")
                + " tier=" + (Reputation.Points < Reputation.Perk60Threshold ? "1/10" : "full")
                + " roll=" + roll.ToString("F3") + " chance=" + percent.ToString("F2"));
        }

        /// <summary>
        /// 件数由目标价值反推：在 目标值 × [0.75, 1.25] 里取一个金额，
        /// 除以该箱单价后四舍五入，夹在 1–12 件。crateValue 回传实际金额。
        /// </summary>
        private static int MinorCount(MinorBox box, out int crateValue)
        {
            float target = Tuning.MinorValue;
            float amount = target * UnityEngine.Random.Range(MinorValueFloor, MinorValueCeil);
            crateValue = Mathf.RoundToInt(amount);
            float raw = box.UnitPrice > 0f ? amount / box.UnitPrice : 1f;
            int count = Mathf.FloorToInt(raw + 0.5f);
            if (count < MinorMinItems) count = MinorMinItems;
            if (count > MinorMaxItems) count = MinorMaxItems;
            return count;
        }

        /// <summary>小奖提示，复用同一条通知条，一句到底、不挡面板。</summary>
        private static void NotifyMinor(MinorBox box)
        {
            try
            {
                if (StoreUIManager.Instance == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: minor notice skipped, no StoreUIManager");
                    return;
                }
                string name = box.ItemName();
                string text = Copy.Text(
                    "你惊喜地发现，" + box.SiteZh + "压着一整箱" + name + "。",
                    "To your surprise, a whole crate of " + name + " is tucked " + box.SiteEn + ".");
                StoreUIManager.Instance.Notify(text, "sec");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: minor notice failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 每区 2–3 种「整箱」。整箱只放同一样东西，件数不再写死，
        /// 由设置项 minor_value（目标价值）除以单价反推。
        /// </summary>
        internal sealed class MinorBox
        {
            internal readonly string RegionId;
            internal readonly string ItemId;
            internal readonly float UnitPrice;
            private readonly string siteZh;
            private readonly string siteEn;

            internal string SiteZh { get { return siteZh; } }
            internal string SiteEn { get { return siteEn; } }

            private MinorBox(string regionId, string itemId, float unitPrice,
                string siteZh, string siteEn)
            {
                RegionId = regionId;
                ItemId = itemId;
                UnitPrice = unitPrice;
                this.siteZh = siteZh;
                this.siteEn = siteEn;
            }

            private static readonly MinorBox[] All =
            {
                new MinorBox("refuse", "processed_meat", 25f,
                    "废料堆底下", "under the refuse heap"),
                new MinorBox("refuse", "toilet_paper", 25f,
                    "塌了一半的车厢里", "in a half-collapsed carriage"),
                new MinorBox("refuse", "paper_towel", 35f,
                    "废料堆底下", "under the refuse heap"),

                new MinorBox("scrap", "common_electronic", 33f,
                    "掀开的铁皮板下", "under a peeled-back sheet of metal"),
                new MinorBox("scrap", "gun_part", 25f,
                    "工作台底下", "under a broken workbench"),
                new MinorBox("scrap", "screwdriver", 25f,
                    "塌了一半的车厢里", "in a half-collapsed carriage"),

                new MinorBox("med", "hemostatic_bandage_item", 16f,
                    "废墟下", "under the rubble"),
                new MinorBox("med", "phagimycin_pill", 25f,
                    "倒塌的隔墙后", "behind a collapsed partition"),
                new MinorBox("med", "rubbing_alcohol", 35f,
                    "废墟下", "under the rubble"),

                new MinorBox("weapons", "small_pistol_ammo", 20f,
                    "掀开的铁皮板下", "under a peeled-back sheet of metal"),
                new MinorBox("weapons", "small_pistol_ammo_p", 26f,
                    "塌了一半的车厢里", "in a half-collapsed carriage"),
                new MinorBox("weapons", "heavy_pistol_ammo", 30f,
                    "锈蚀的军械箱旁", "beside a rusted arms crate")
            };

            internal static MinorBox Roll(string regionId)
            {
                int matches = 0;
                for (int i = 0; i < All.Length; ++i)
                    if (All[i].RegionId == regionId) ++matches;
                if (matches == 0) return null;

                int pick = UnityEngine.Random.Range(0, matches);
                for (int i = 0; i < All.Length; ++i)
                {
                    if (All[i].RegionId != regionId) continue;
                    if (pick == 0) return All[i];
                    --pick;
                }
                return null;
            }

            internal string ItemName()
            {
                try
                {
                    return LocHelper.GetLocalizedItem("item_" + ItemId + "_name", new object[0]);
                }
                catch (Exception)
                {
                    return ItemId;
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
