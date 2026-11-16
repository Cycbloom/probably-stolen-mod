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
    /// 大奖按自然日进行加法增长、中奖减半；从本轮起点连续五个未中日到上限。
    /// 当天概率固定，中奖的结果在次日更新时处理；固定模式暂停日窗口。
    /// 上限来自设置项 stash_cap（0.1–5.0，步进 0.1）；
    /// stash_window_enabled 关闭时固定使用实际生效上限，暂停窗口更新；
    /// 声望不到 60 时上限硬顶 0.1%，但不完全排除。
    ///
    /// 小奖设置项 minor_percent 为窗口上限，逐次搜索慢启动、加法增长、中奖减半；
    /// 声望 &lt; 60 时当前概率按 1/10 生效，0 时不出、不增长。
    /// 独立存储窗口，不登新闻、不改价格、不触发大奖的当日额外收获。
    /// 同一次搜索里大奖命中时完全跳过小奖。
    ///
    /// 一趟（进入某处垃圾场后）最多中一次大奖，标记只在内存里，换区时清空。
    /// 中奖后当天、同一区域，后续每次搜索固定再给 1 件该区样板物品。
    ///
    /// 中奖当场就用原生 QueueFuturEvent 按「明天」把事件排进未来队列，
    /// 当天打开日历就能在次日格看到；第二天开日由原版 HandleFuturEvent
    /// 激活，报纸与降价都走原生链路。次日开日的 Prefix 只做兜底：
    /// 事件已在激活列表或未来队列里就只清日期，旧存档或入队失败才补排。
    /// </summary>
    internal static class StashFind
    {
        internal const string ChanceKey = "stash_chance";
        internal const string PhaseKey = "stash_phase";
        internal const string HitDayKey = "stash_hit_day";
        internal const string HitRegionKey = "stash_hit_region";
        internal const string NewsDayKey = "stash_news_day";
        internal const string NewsFiredKey = "stash_news_fired";
        internal const string WindowDayKey = "stash_window_day";
        internal const string CycleStartKey = "stash_cycle_start";
        internal const string MissDaysKey = "stash_miss_days";
        internal const string WindowCapKey = "stash_window_cap";
        internal const string WindowHitDayKey = "stash_window_hit_day";
        internal const string MinorChanceKey = "minor_chance";
        internal const string MinorPhaseKey = "minor_phase";

        /// <summary>大奖窗口起点，单位是百分数（0.05 = 0.05%）。</summary>
        internal const float StartChance = JackpotChanceRules.Start;

        /// <summary>声望不到 60 时的硬上限，单位是百分数。</summary>
        internal const float HardCapLowRep = 0.1f;

        internal const int BonusItems = 1;

        /// <summary>小奖件数区间与目标价值浮动范围。</summary>
        internal const int MinorMinItems = 1;
        internal const int MinorMaxItems = 12;
        internal const float MinorValueFloor = 0.75f;
        internal const float MinorValueCeil = 1.25f;

        private static JackpotDailyState daily = new JackpotDailyState();
        private static float chance { get { return daily.Chance; } set { daily.Chance = value; } }
        private static float minorChance = MinorChanceRules.StartFor(Tuning.DefaultMinorPercent);
        private static int minorPhase;
        private static int adaptiveHitDay = -1;
        private static int phase; // Retained legacy save field; daily progression uses daily.MissDays.
        private static int hitDay = -1;
        private static string hitRegion = "";
        private static int newsDay = -1;
        private static int newsFired = -1;

        private static bool loaded;
        private static bool tripHit;

        internal static void ResetSession()
        {
            daily = new JackpotDailyState();
            chance = StartChance;
            minorChance = MinorChanceRules.StartFor(Tuning.DefaultMinorPercent);
            minorPhase = 0;
            adaptiveHitDay = -1;
            phase = 0;
            hitDay = newsDay = newsFired = -1;
            hitRegion = "";
            loaded = tripHit = false;
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
            if (Reputation.Points < PerkCatalog.Perk60Threshold)
                cap = Mathf.Min(cap, HardCapLowRep);
            return cap;
        }

        /// <summary>当前模式的大奖概率（%），告示板与实际掷骰使用同一个计算入口。</summary>
        internal static float CurrentChancePercent
        {
            get
            {
                EnsureLoaded();
                SyncDailyWindow();
                return JackpotChanceRules.Current(chance, EffectiveCap(), Tuning.StashWindowEnabled);
            }
        }

        internal static float CurrentMinorChancePercent
        {
            get
            {
                EnsureLoaded();
                return MinorChanceRules.Current(minorChance, Tuning.MinorPercent, Tuning.MinorWindowEnabled)
                    * PerkCatalog.MinorMultiplier(Reputation.Points);
            }
        }

        internal static float PreviewMinorPercent(Region region)
        {
            return region != null && HasMinor(region.Id) ? CurrentMinorChancePercent : 0f;
        }

        internal static void SyncDailyWindow()
        {
            if (!Core.Enabled || !PlayerStore.HasInstance || StoreStation.Instance == null || EmporiumEntry.Instance == null) return;
            if (Today() < 1) return;
            EnsureLoaded();
            if (daily.Advance(Today(), EffectiveCap(), Tuning.StashWindowEnabled, adaptiveHitDay)) SaveState();
        }

        /// <summary>该区有没有大奖样板；没有就根本不会掷大奖，告示板据此隐藏这一行。</summary>
        internal static bool HasJackpot(string regionId)
        {
            return StashTemplate.For(regionId) != null;
        }

        /// <summary>该区有没有小奖整箱；没有就根本不会掷小奖，告示板据此隐藏这一行。</summary>
        internal static bool HasMinor(string regionId)
        {
            return MinorBox.Has(regionId);
        }

        internal static float PreviewJackpotPercent(Region region)
        {
            if (region == null || !HasJackpot(region.Id)) return 0f;
            // A new destination will reset the trip flag when confirmed. Only the
            // currently visited area has already consumed its one jackpot.
            if (tripHit && Picker.ChosenRegion == region) return 0f;
            return CurrentChancePercent;
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
            SyncDailyWindow();
            int today = Today();
            if (today < 0) return;

            StashTemplate template = StashTemplate.For(region.Id);
            float cap = EffectiveCap();
            bool useWindow = Tuning.StashWindowEnabled;
            float rollChance = JackpotChanceRules.Current(chance, cap, useWindow);
            bool bonusActive = hitDay == today && hitRegion == region.Id;
            bool jackpotHit = false;

            if (template != null)
            {
                float roll = UnityEngine.Random.value * 100f;
                bool hit = !tripHit && roll < rollChance;
                float before = rollChance;

                if (hit)
                {
                    int spawned = AddJackpot(items, template);
                    // This day's probability is fixed. The next day consumes
                    // the winning-day result and halves its window once.
                    tripHit = true;
                    hitDay = today;
                    if (useWindow) adaptiveHitDay = today;
                    hitRegion = region.Id;
                    newsDay = today + 1;
                    newsFired = -1;
                    // 当场按「明天」排进原生未来队列，日历当天即可看到；
                    // 失败时保留 newsDay，由次日开日的兜底路径补排。
                    bool newsQueued = QueueNewsForTomorrow(template);
                    jackpotHit = true;
                    SaveState();
                    NotifyFind(region.Id);
                    Debug.Log("[DumpingGroundMap] stash: jackpot region=" + region.Id
                        + " day=" + today + " roll=" + roll.ToString("F3")
                        + " items=" + spawned + " chance " + before.ToString("F3")
                        + " -> " + JackpotChanceRules.Current(chance, cap, useWindow).ToString("F3")
                        + " window=" + useWindow + " phase=" + phase
                        + " news_day=" + newsDay + " news_queued=" + (newsQueued ? 1 : 0));
                }
                else
                {
                    Debug.Log("[DumpingGroundMap] stash: miss region=" + region.Id
                        + " day=" + today + " roll=" + roll.ToString("F3")
                        + " chance " + before.ToString("F3") + " -> "
                        + JackpotChanceRules.Current(chance, cap, useWindow).ToString("F3")
                        + " cap=" + cap.ToString("F2") + " phase=" + phase
                        + " trip_hit=" + (tripHit ? 1 : 0) + " window=" + useWindow);
                }
            }

            bool minorHit = !jackpotHit && TryMinor(items, region, today);
            if (HasMinor(region.Id) && Tuning.MinorPercent > 0f)
            {
                // Every eligible search is one minor-window tick. A jackpot
                // blocks the minor payout, so that search counts as no minor win.
                MinorChanceRules.AfterSearch(ref minorChance, ref minorPhase,
                    Tuning.MinorPercent, Tuning.MinorWindowEnabled, minorHit);
                if (Tuning.MinorWindowEnabled) SaveState();
            }

            if (bonusActive && template != null)
            {
                AddBonus(items, template, BonusItems);
                Debug.Log("[DumpingGroundMap] stash: bonus region=" + region.Id
                    + " day=" + today + " items=" + BonusItems);
            }
        }

        /// <summary>
        /// 四类拾获提示共用原生普通消息通知条（default 黑色背景），不另做 UI。
        /// 与原生拾荒成功、彩票兑奖保持一致；受伤的 sec 红色提示仍由原版处理。
        /// 提示用两行短句：第一行交代发现，第二行给物品名称或当天加成。
        /// 地点来自该区的措辞池，随机取一条，整条提示只出现一个地点。
        /// </summary>
        internal static void ShowNotice(string text, string tag)
        {
            try
            {
                if (StoreUIManager.Instance == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: " + tag + " notice skipped, no StoreUIManager");
                    return;
                }
                StoreUIManager.Instance.Notify(text, "default");
                Debug.Log("[DumpingGroundMap] stash: " + tag + " notice shown");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: " + tag + " notice failed: " + ex.Message);
            }
        }

        /// <summary>大奖提示：每区一句「翻到了什么」，地点仍从该区措辞池里随机取。</summary>
        private static void NotifyFind(string regionId)
        {
            StashTemplate template = StashTemplate.For(regionId);
            if (template == null) return;

            PlaceTalk talk = PlaceTalk.For(regionId);
            int pick = talk.Roll();
            string place = Copy.Text(talk.Zh[pick], talk.En[pick]);
            ShowNotice(string.Format(template.Notice, place), "jackpot");
        }

        /// <summary>
        /// 80 档补给箱与钥匙卡的当场提示。箱子走「翻出一个上锁的…」，
        /// 卡走「摸到一张…」；物品名一律走本地化词条（Picker.ItemName）。
        /// </summary>
        internal static void NoticeSpecialFind(string itemId)
        {
            if (!Core.Enabled || string.IsNullOrEmpty(itemId)) return;

            bool crate = itemId.StartsWith("loot_crate_", StringComparison.Ordinal);
            bool card = itemId.EndsWith("_keycard", StringComparison.Ordinal);
            if (!crate && !card) return;

            Region region = Picker.ChosenRegion;
            PlaceTalk talk = PlaceTalk.For(region == null ? null : region.Id);
            int pick = talk.Roll();
            string name = Picker.ItemName(itemId);

            string text = crate
                ? Copy.Text("你在" + talk.Zh[pick] + "翻出一个上锁的箱子。\n" + name,
                    "A locked crate " + talk.En[pick] + ".\n" + name)
                : Copy.Text("你在" + talk.Zh[pick] + "摸到一张钥匙卡。\n" + name,
                    "A keycard " + talk.En[pick] + ".\n" + name);

            ShowNotice(text, crate ? "crate" : "keycard");
        }

        /// <summary>
        /// 大奖：先按组抽本区各大类的非垃圾物品，再按池内权重抽 1 件该区匹配的贵重品。
        /// 贵重品池的权重同样按单价反比：越便宜权重越大，越贵越稀有。
        /// </summary>
        private static int AddJackpot(List<GameItem> items, StashTemplate template)
        {
            int spawned = AddGroups(items, template);

            string valuable = template.PickValuable();
            if (valuable == null) return spawned;
            if (Spawn(items, valuable)) ++spawned;
            return spawned;
        }

        /// <summary>按组抽奖池：每组件数在 Min–Max 之间随机，组内物品均匀抽。</summary>
        private static int AddGroups(List<GameItem> items, StashTemplate template)
        {
            RewardGroup[] groups = template.Groups;
            if (groups == null || groups.Length == 0) return 0;

            int spawned = 0;
            for (int g = 0; g < groups.Length; ++g)
            {
                RewardGroup group = groups[g];
                if (group == null || group.Ids == null || group.Ids.Length == 0) continue;
                int count = UnityEngine.Random.Range(group.Min, group.Max + 1);
                for (int i = 0; i < count; ++i)
                {
                    string id = group.Ids[UnityEngine.Random.Range(0, group.Ids.Length)];
                    if (Spawn(items, id)) ++spawned;
                }
            }
            return spawned;
        }

        /// <summary>当天同区额外收获：从各组里均匀抽，不额外给贵重品。</summary>
        private static void AddBonus(List<GameItem> items, StashTemplate template, int count)
        {
            RewardGroup[] groups = template.Groups;
            if (groups == null || groups.Length == 0) return;
            for (int i = 0; i < count; ++i)
            {
                RewardGroup group = groups[UnityEngine.Random.Range(0, groups.Length)];
                if (group == null || group.Ids == null || group.Ids.Length == 0) continue;
                Spawn(items, group.Ids[UnityEngine.Random.Range(0, group.Ids.Length)]);
            }
        }

        /// <summary>
        /// 生成一件奖励：与正常拾荒走同一条变体链路，
        /// 次品免疫宁 / 假烟 / 仿券 / 小瓶酒精换真 id 并补状态，枪械再掷成色。
        /// </summary>
        private static bool Spawn(List<GameItem> items, string id)
        {
            try
            {
                GameItem item = LootFactory.Create(id);
                if (item == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: spawn failed " + id);
                    return false;
                }
                items.Add(item);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: spawn failed " + id + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 小奖：按当前窗口概率独立掷一次，声望不足60时乘1/10。
        /// 命中后从该区的整箱表里随机抽一种，整箱只放同一样东西。
        /// 窗口由调用方在搜索末尾统一更新一次；不登新闻、不改价格。
        /// </summary>
        private static bool TryMinor(List<GameItem> items, Region region, int today)
        {
            float percent = CurrentMinorChancePercent;
            if (percent <= 0f) return false;

            MinorBox box = MinorBox.Roll(region.Id);
            if (box == null) return false;

            float roll = UnityEngine.Random.value * 100f;
            if (roll >= percent) return false;

            int crateValue;
            int count = MinorCount(box, out crateValue);
            int spawned = 0;
            for (int i = 0; i < count; ++i)
            {
                try
                {
                    GameItem item = LootFactory.Create(box.ItemId);
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

            if (spawned > 0) NotifyMinor(box, region.Id);
            Debug.Log("[DumpingGroundMap] stash: minor region=" + region.Id
                + " day=" + today + " box=" + box.ItemId + " items=" + spawned + "/" + count
                + " value=" + crateValue + " target=" + Tuning.MinorValue.ToString("F0")
                + " tier=" + (Reputation.Points < PerkCatalog.Perk60Threshold ? "1/10" : "full")
                + " roll=" + roll.ToString("F3") + " chance=" + percent.ToString("F2"));
            return spawned > 0;
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

        /// <summary>小奖提示：语气是「惊喜发现一整箱」，地点同样从该区措辞池里随机取。</summary>
        private static void NotifyMinor(MinorBox box, string regionId)
        {
            PlaceTalk talk = PlaceTalk.For(regionId);
            int pick = talk.Roll();
            string name = box.ItemName();
            ShowNotice(Copy.Text(
                "你在" + talk.Zh[pick] + "惊喜地发现：\n一整箱" + name + "！",
                "A whole crate " + talk.En[pick] + "!\n" + name), "minor");
        }

        /// <summary>
        /// 每区若干种「整箱」，全部取自该区主题，不含垃圾。
        /// 整箱只放同一样东西，件数不写死，由 minor_value（目标价值）除以单价反推。
        /// </summary>
        internal sealed class MinorBox
        {
            internal readonly string RegionId;
            internal readonly string ItemId;
            internal readonly float UnitPrice;

            private MinorBox(string regionId, string itemId, float unitPrice)
            {
                RegionId = regionId;
                ItemId = itemId;
                UnitPrice = unitPrice;
            }

            private static readonly MinorBox[] All =
            {
                // 入口空地：通用日常补给，兼有基础维修和常备药。
                new MinorBox("gate", "caffeine_pill", 10f),
                new MinorBox("gate", "toothpaste", 15f),
                new MinorBox("gate", "bandage_item", 8f),
                new MinorBox("gate", "printer_plastic", 12f),
                new MinorBox("gate", "screwdriver", 25f),

                // 生活废料带：食品 / 饮品 / 零食 / 纸品 / 医疗
                new MinorBox("refuse", "processed_meat", 25f),
                new MinorBox("refuse", "energy_drink", 18f),
                new MinorBox("refuse", "beis_icecream", 40f),
                new MinorBox("refuse", "toilet_paper", 25f),
                new MinorBox("refuse", "paper_towel", 35f),
                new MinorBox("refuse", "li_eat_snackbar", 17f),

                // 拆解堆：成批原料。
                new MinorBox("scrap", "scrap_metal", 10f),
                new MinorBox("scrap", "printer_plastic", 12f),
                new MinorBox("scrap", "nuts_metal", 14f),
                new MinorBox("scrap", "metal_ingot", 50f),
                new MinorBox("scrap", "kotton_fabric", 65f),

                // 医疗废弃点：绷带 / 药 / 消毒 / 血袋 / 通用医疗
                new MinorBox("med", "hemostatic_bandage_item", 16f),
                new MinorBox("med", "phagimycin_pill", 25f),
                new MinorBox("med", "rubbing_alcohol", 35f),
                new MinorBox("med", "blood_bag", 33f),
                new MinorBox("med", "common_medical", 40f),

                // 废武器堆：弹药 / 弹匣 / 近战 / 枪械
                new MinorBox("weapons", "small_pistol_ammo", 20f),
                new MinorBox("weapons", "heavy_pistol_ammo", 30f),
                new MinorBox("weapons", "mag_22_ext", 35f),
                new MinorBox("weapons", "box_cutter", 28f),
                new MinorBox("weapons", "kitchen_knife", 45f),
                new MinorBox("weapons", "printed_gun", 40f),

                // 电子垃圾坡：电子件 / 元件 / 模组 / 工具
                new MinorBox("electronics", "common_electronic", 33f),
                new MinorBox("electronics", "wire", 16f),
                new MinorBox("electronics", "system_module_ruined", 30f),
                new MinorBox("electronics", "system_module_corrupt", 45f),
                new MinorBox("electronics", "blank_module", 60f),

                // 报废车场：金属件 / 工具 / 可拆零件
                new MinorBox("vehicles", "wire_cutter", 40f),
                new MinorBox("vehicles", "welder", 80f),
                new MinorBox("vehicles", "screwdriver", 25f),
                new MinorBox("vehicles", "box_cutter", 28f),
                new MinorBox("vehicles", "turbo_booster", 35f),
                new MinorBox("vehicles", "crowbar", 75f),

                // 废泵站：批量奖励只给净水设备和耗材，不给瓶、桶或饮水包。
                new MinorBox("water", "hydroreed_filter", 20f),
                new MinorBox("water", "water_filter", 25f),
                new MinorBox("water", WaterSupplyPack.HalfTest, 16f),
                new MinorBox("water", WaterSupplyPack.HalfTablets, 31f),
                new MinorBox("water", "full_water_test_strip_box", 31f),
                new MinorBox("water", "full_water_purification_tablet_box", 55f)
            };

            /// <summary>该区有没有小奖整箱。</summary>
            internal static bool Has(string regionId)
            {
                for (int i = 0; i < All.Length; ++i)
                    if (All[i].RegionId == regionId) return true;
                return false;
            }

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

            internal static float ExpectedValue(string regionId, float target)
            {
                float sum = 0f;
                int matches = 0;
                foreach (MinorBox box in All)
                {
                    if (box.RegionId != regionId) continue;
                    ++matches;
                    sum += RewardExpectation.MeanMinorCount(target, box.UnitPrice)
                        * LootCatalog.ExpectedValueOf(box.ItemId, null);
                }
                return matches > 0 ? sum / matches : 0f;
            }

            internal static string[] ItemsFor(string regionId)
            {
                List<string> items = new List<string>();
                foreach (MinorBox box in All)
                    if (box.RegionId == regionId) items.Add(box.ItemId);
                return items.ToArray();
            }

            internal string ItemName()
            {
                return Picker.ItemName(ItemId);
            }
        }

        /// <summary>
        /// 四类提示共用的地点措辞池：按区准备，中英文一一对应，
        /// 每次提示随机取一条，整句只出现一个地点。
        /// 措辞与各区场景对应（医疗区不出现军械箱，武器区不出现水槽）。
        /// </summary>
        internal sealed class PlaceTalk
        {
            internal readonly string[] Zh;
            internal readonly string[] En;

            private PlaceTalk(string[] zh, string[] en)
            {
                Zh = zh;
                En = en;
            }

            private static readonly Dictionary<string, PlaceTalk> All =
                new Dictionary<string, PlaceTalk>
                {
                    { "scrap", new PlaceTalk(
                        new[] { "架子顶上", "抽屉缝里", "工作台底下", "掀开的铁皮板下",
                                "锈死的柜子里", "塌了半截的车厢里", "废料堆底层",
                                "墙角的破纸箱里", "拆开的机箱里", "堆到顶的料斗里" },
                        new[] { "on the top shelf", "in the gap between drawers",
                                "under a broken workbench", "under a peeled-back sheet of metal",
                                "inside a rusted-shut cabinet", "in a half-collapsed carriage",
                                "at the bottom of the refuse heap",
                                "in a soggy cardboard box in the corner",
                                "inside a stripped-open machine casing",
                                "in an overflowing scrap hopper" }) },
                    { "refuse", new PlaceTalk(
                        new[] { "废料堆底层", "塌了半截的车厢里", "水槽底下", "墙角的破纸箱里",
                                "烂了一半的冰箱里", "倒扣的洗衣篮下", "成堆的垃圾袋中间",
                                "塌下来的隔板下" },
                        new[] { "at the bottom of the refuse heap", "in a half-collapsed carriage",
                                "under the sink", "in a soggy cardboard box in the corner",
                                "inside a half-rotted fridge", "under an overturned laundry basket",
                                "among the piled-up garbage bags", "beneath a caved-in shelf" }) },
                    { "med", new PlaceTalk(
                        new[] { "倒塌的隔墙后面", "锈死的药柜里", "翻倒的病床下", "拆开的急救箱里",
                                "停用的处置台底下", "墙角的输液架旁", "拆下来的吊柜里" },
                        new[] { "behind a collapsed partition", "inside a rusted-shut medicine cabinet",
                                "under an overturned hospital bed", "inside a pried-open first-aid kit",
                                "under a decommissioned treatment table",
                                "beside a stack of IV stands in the corner",
                                "in a detached wall cabinet" }) },
                    { "weapons", new PlaceTalk(
                        new[] { "锈蚀的军械箱旁", "掀开的铁皮板下", "塌了半截的车厢里",
                                "拆开的枪柜里", "打空的弹箱底下", "塌下来的掩体后",
                                "堆到顶的料斗里" },
                        new[] { "beside a rusted arms crate", "under a peeled-back sheet of metal",
                                "in a half-collapsed carriage", "inside a pried-open gun locker",
                                "under an emptied ammo box", "behind a caved-in barricade",
                                "in an overflowing scrap hopper" }) },
                    { "gate", new PlaceTalk(
                        new[] { "门口的垃圾堆里", "塌了半截的车厢里", "掀开的铁皮板下",
                                "墙角的破纸箱里" },
                        new[] { "in the trash heap by the gate", "in a half-collapsed carriage",
                                "under a peeled-back sheet of metal",
                                "in a soggy cardboard box in the corner" }) },
                    { "electronics", new PlaceTalk(
                        new[] { "烧穿的配电柜里", "剥开的线缆槽里", "堆到顶的电路板垛下",
                                "拆开的机箱里", "塌了的电子货架后", "锈死的配电箱里",
                                "压扁的服务器机柜下" },
                        new[] { "inside a burnt-out switchgear cabinet",
                                "in a stripped-open cable trench",
                                "under a towering heap of circuit boards",
                                "inside a pried-open machine casing",
                                "behind a collapsed electronics rack",
                                "inside a rusted-shut distribution box",
                                "under a crushed server cabinet" }) },
                    { "vehicles", new PlaceTalk(
                        new[] { "翻倒的车厢底下", "拆空的后备箱里", "压扁的驾驶室里",
                                "卸下的车门后", "油污的底盘下", "报废车轮堆里",
                                "撬开的引擎盖下" },
                        new[] { "under an overturned carriage", "in a stripped-out trunk",
                                "inside a crushed cab", "behind a detached car door",
                                "under an oil-stained chassis", "in a heap of dead wheels",
                                "under a pried-open engine hood" }) },
                    { "water", new PlaceTalk(
                        new[] { "锈死的泵房里", "干涸的水池底下", "拆开的水泵旁",
                                "塌了半截的水塔下", "积满淤泥的阀门井里",
                                "废弃的过滤罐后面", "断裂的水管旁" },
                        new[] { "inside the rusted-shut pump house",
                                "at the bottom of a dry reservoir", "beside a dismantled pump",
                                "under a half-collapsed water tower",
                                "in a silt-filled valve pit",
                                "behind an abandoned filter tank",
                                "beside a burst water main" }) }
                };

            private static readonly PlaceTalk Fallback = new PlaceTalk(
                new[] { "墙角的破纸箱里" },
                new[] { "in a soggy cardboard box in the corner" });

            internal static PlaceTalk For(string regionId)
            {
                PlaceTalk talk;
                if (!string.IsNullOrEmpty(regionId) && All.TryGetValue(regionId, out talk)) return talk;
                return Fallback;
            }

            internal int Roll()
            {
                return En.Length == 0 ? 0 : UnityEngine.Random.Range(0, En.Length);
            }
        }


        /// <summary>
        /// 每天开日的 Prefix：只做兜底。中奖当场已经按「明天」排进未来队列，
        /// 这里先看事件是否已在激活列表或未来队列里，是就只记录发报日；
        /// 旧存档或入队失败（两者都没有）才按原逻辑用 0 偏移补排，
        /// 并交给同一次 OnDayStart 的 HandleFuturEvent 激活。
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

            string identifier = "dgm_stash_" + template.RegionId;
            if (HasQueuedOrActive(manager, identifier))
            {
                newsFired = today;
                newsDay = -1;
                SaveState();
                Debug.Log("[DumpingGroundMap] stash: news already queued day=" + today
                    + " id=" + identifier + " region=" + template.RegionId);
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
                QueueNewsForToday(manager, ev);
                newsFired = today;
                newsDay = -1;
                SaveState();
                Debug.Log("[DumpingGroundMap] stash: news queued day=" + today
                    + " id=" + ev.identifier + " region=" + template.RegionId + " fallback=1");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: news queue failed: " + ex.Message);
            }
        }

        /// <summary>中奖当场按「明天」入队；同 id 事件已存在时不重复排。</summary>
        private static bool QueueNewsForTomorrow(StashTemplate template)
        {
            try
            {
                StoreEventManager manager = StoreStation.Instance.storeEventManager;
                if (manager == null) return false;
                StoreEvent ev = BuildEvent(template);
                if (HasQueuedOrActive(manager, ev.identifier)) return true;
                manager.QueueFuturEvent(ev, 1, true);
                Debug.Log("[DumpingGroundMap] stash: news queued for tomorrow id=" + ev.identifier
                    + " today=" + Today());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: news queue at hit failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>同 identifier 的事件是否已在激活列表或未来队列里。</summary>
        private static bool HasQueuedOrActive(StoreEventManager manager, string identifier)
        {
            if (manager == null || string.IsNullOrEmpty(identifier)) return false;
            if (manager.IsEventActive(identifier)) return true;
            List<StoreEvent> future = manager.futurEvents;
            if (future == null) return false;
            for (int i = 0; i < future.Count; ++i)
            {
                StoreEvent ev = future[i];
                if (ev != null && ev.identifier == identifier) return true;
            }
            return false;
        }

        internal static void QueueNewsForToday(StoreEventManager manager, StoreEvent ev)
        {
            // day 是剩余天数，不是绝对日期。此路径只用于兜底：事件已经错过了
            // 中奖当天的入队，随即由同一次 OnDayStart 的 HandleFuturEvent 激活。
            manager.QueueFuturEvent(ev, 0, true);
        }

        /// <summary>修复旧版把绝对日期当等待天数后留在未来队列中的大奖新闻。</summary>
        internal static int RepairLegacyNews(StoreEventManager manager, int today, int queuedDay, string regionId)
        {
            if (manager == null || queuedDay <= 0 || today < queuedDay || StashTemplate.For(regionId) == null)
                return 0;
            if (manager.futurEvents.Count != manager.futurEventsDay.Count) return 0;

            int repaired = 0;
            for (int i = manager.futurEvents.Count - 1; i >= 0; --i)
            {
                StoreEvent ev = manager.futurEvents[i];
                // 旧版排队时 startDay = queuedDay + queuedDay。只处理这个明确特征，
                // 并以本存档的发报收据与区域约束，避免修改原版及其他 Mod 的事件。
                if (ev == null || ev.identifier != "dgm_stash_" + regionId
                    || ev.startDay != (long)queuedDay * 2) continue;
                if (!manager.IsEventActive(ev.identifier)) manager.ActivateEvent(ev, false, false);
                if (!manager.IsEventActive(ev.identifier)) continue;
                manager.futurEvents.RemoveAt(i);
                manager.futurEventsDay.RemoveAt(i);
                ++repaired;
            }
            return repaired;
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
            if (template.ItemTypes != null)
            {
                for (int i = 0; i < template.ItemTypes.Length; ++i)
                {
                    string type = template.ItemTypes[i];
                    int percent = template.PricePercents != null && i < template.PricePercents.Length
                        ? template.PricePercents[i] : 0;
                    if (string.IsNullOrEmpty(type) || percent == 0) continue;
                    ev.negociationDatas.Add(new NegociationData(type, percent,
                        template.NewsName, template.DisplayName));
                }
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
        internal static void Reload()
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
                daily.Day = ModHelper.GetModDataInt(Core.Id, WindowDayKey, -1);
                daily.CycleStart = ModHelper.GetModDataFloat(Core.Id, CycleStartKey, chance);
                daily.MissDays = ModHelper.GetModDataInt(Core.Id, MissDaysKey, 0);
                daily.Cap = ModHelper.GetModDataFloat(Core.Id, WindowCapKey, 0f);
                adaptiveHitDay = ModHelper.GetModDataInt(Core.Id, WindowHitDayKey, -1);
                minorChance = ModHelper.GetModDataFloat(Core.Id, MinorChanceKey, MinorChanceRules.StartFor(Tuning.MinorPercent));
                minorPhase = ModHelper.GetModDataInt(Core.Id, MinorPhaseKey, 0);
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
                int repaired = RepairLegacyNews(StoreStation.Instance.storeEventManager, Today(), newsFired, hitRegion);
                if (repaired > 0)
                    Debug.Log("[DumpingGroundMap] stash: repaired legacy queued news count=" + repaired);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DumpingGroundMap] stash: load failed: " + ex.Message);
            }
        }

        internal static void NewGame(string saveName)
        {
            daily = new JackpotDailyState();
            chance = StartChance;
            minorChance = MinorChanceRules.StartFor(Tuning.MinorPercent);
            minorPhase = 0;
            adaptiveHitDay = -1;
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
                ModHelper.SetModDataInt(Core.Id, WindowDayKey, daily.Day);
                ModHelper.SetModDataFloat(Core.Id, CycleStartKey, daily.CycleStart);
                ModHelper.SetModDataInt(Core.Id, MissDaysKey, daily.MissDays);
                ModHelper.SetModDataFloat(Core.Id, WindowCapKey, daily.Cap);
                ModHelper.SetModDataInt(Core.Id, WindowHitDayKey, adaptiveHitDay);
                ModHelper.SetModDataFloat(Core.Id, MinorChanceKey, minorChance);
                ModHelper.SetModDataInt(Core.Id, MinorPhaseKey, minorPhase);
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
}
