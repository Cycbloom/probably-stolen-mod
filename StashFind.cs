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

        /// <summary>大奖里每区合计的贵重品件数（0.3.70 起固定 1 件）。</summary>
        internal const int ValuableItems = 1;

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

        /// <summary>当前 AIMD 窗口的爆大奖概率（%），告示板用；与实际掷骰取同一个值。</summary>
        internal static float CurrentChancePercent
        {
            get
            {
                EnsureLoaded();
                return ClampChance(chance, EffectiveCap());
            }
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
                    int spawned = AddJackpot(items, template);
                    chance = Mathf.Max(StartChance, chance * 0.5f);
                    chance = ClampChance(chance, cap);
                    phase = 1;
                    tripHit = true;
                    hitDay = today;
                    hitRegion = region.Id;
                    newsDay = today + 1;
                    jackpotHit = true;
                    SaveState();
                    NotifyFind(region.Id);
                    Debug.Log("[DumpingGroundMap] stash: jackpot region=" + region.Id
                        + " day=" + today + " roll=" + roll.ToString("F3")
                        + " items=" + spawned + " chance " + before.ToString("F3")
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
                AddBonus(items, template, BonusItems);
                Debug.Log("[DumpingGroundMap] stash: bonus region=" + region.Id
                    + " day=" + today + " items=" + BonusItems);
            }
        }

        /// <summary>
        /// 四类当场提示共用同一条通知条：StoreUIManager.Instance.Notify(text, "sec")，
        /// 与原生「受伤」提示同一个位置和样式，不另做 UI。
        /// 每句都按「谁在哪里翻到什么」拼：地点来自该区的措辞池，随机取一条，
        /// 整句只出现一个地点。
        /// </summary>
        private static void ShowNotice(string text, string tag)
        {
            try
            {
                if (StoreUIManager.Instance == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: " + tag + " notice skipped, no StoreUIManager");
                    return;
                }
                StoreUIManager.Instance.Notify(text, "sec");
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
                ? Copy.Text("你在" + talk.Zh[pick] + "翻出一个上锁的" + name + "。",
                    "You found a locked " + name.ToLowerInvariant() + " " + talk.En[pick] + ".")
                : Copy.Text("你在" + talk.Zh[pick] + "摸到一张" + name + "。",
                    "You picked up " + WithArticle(name) + " " + talk.En[pick] + ".");

            ShowNotice(text, crate ? "crate" : "keycard");
        }

        /// <summary>英文冠词：engineering 这类元音开头的名词前用 an。</summary>
        private static string WithArticle(string name)
        {
            string lower = name == null ? string.Empty : name.ToLowerInvariant();
            char first = lower.Length > 0 ? lower[0] : 'x';
            bool vowel = first == 'a' || first == 'e' || first == 'i' || first == 'o' || first == 'u';
            return (vowel ? "an " : "a ") + lower;
        }

        /// <summary>
        /// 大奖：先按组抽本区各大类的非垃圾物品，再抽 1 件该区匹配的贵重品。
        /// </summary>
        private static int AddJackpot(List<GameItem> items, StashTemplate template)
        {
            int spawned = AddGroups(items, template);

            string[] valuables = template.ValuableIds;
            if (valuables == null || valuables.Length == 0) return spawned;

            for (int i = 0; i < ValuableItems; ++i)
            {
                int index = UnityEngine.Random.Range(0, valuables.Length);
                if (Spawn(items, valuables[index])) ++spawned;
            }
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
                string realId = LootVariants.RealId(id);
                GameItem item = LootVariants.Create(id);
                if (item == null)
                {
                    Debug.LogWarning("[DumpingGroundMap] stash: spawn failed " + id);
                    return false;
                }
                LootVariants.Apply(id, item);
                WeaponWear.Apply(realId, item);
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
        /// 小奖：按设置项 minor_percent 独立掷一次。声望 &lt; 60 时按设置值的 1/10 生效，
        /// 声望 ≥ 60 时按设置值全值生效；设置值为 0 时两档都是 0。
        /// 命中后从该区的整箱表里随机抽一种，整箱只放同一样东西。
        /// 不写 AIMD 状态、不登新闻、不改价格、不触发大奖的当日额外收获。
        /// </summary>
        private static void TryMinor(List<GameItem> items, Region region, int today)
        {
            float percent = Tuning.EffectiveMinorPercent;
            if (percent <= 0f) return;

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

            NotifyMinor(box, region.Id);
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

        /// <summary>小奖提示：语气是「惊喜发现一整箱」，地点同样从该区措辞池里随机取。</summary>
        private static void NotifyMinor(MinorBox box, string regionId)
        {
            PlaceTalk talk = PlaceTalk.For(regionId);
            int pick = talk.Roll();
            string name = box.ItemName();
            ShowNotice(Copy.Text(
                "你惊喜地发现，" + talk.Zh[pick] + "压着一整箱" + name + "。",
                "To your surprise, a whole crate of " + name + " was tucked " + talk.En[pick] + "."), "minor");
        }

        /// <summary>
        /// 每区 4–6 种「整箱」，全部取自该区主题，单一单价 15–60、不含垃圾。
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
                // 生活废料带：食品 / 饮品 / 零食 / 纸品 / 医疗
                new MinorBox("refuse", "processed_meat", 25f),
                new MinorBox("refuse", "energy_drink", 18f),
                new MinorBox("refuse", "beis_icecream", 40f),
                new MinorBox("refuse", "toilet_paper", 25f),
                new MinorBox("refuse", "paper_towel", 35f),
                new MinorBox("refuse", "hemostatic_bandage_item", 16f),

                // 拆解堆：电子件 / 材料 / 锭 / 工具 / 模组
                new MinorBox("scrap", "common_electronic", 33f),
                new MinorBox("scrap", "gun_part", 25f),
                new MinorBox("scrap", "metal_ingot", 50f),
                new MinorBox("scrap", "screwdriver", 25f),
                new MinorBox("scrap", "system_module_corrupt", 45f),

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
                new MinorBox("weapons", "printed_gun", 40f)
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
                                "in a soggy cardboard box in the corner" }) }
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
    /// 大奖奖池里的一组：本区某一个大类的非垃圾物品，件数在 Min–Max 之间。
    /// 五区都按本区主题拆成 2–3 组，组内只从该大类抽，不再混成一个池。
    /// </summary>
    internal sealed class RewardGroup
    {
        internal readonly string[] Ids;
        internal readonly int Min;
        internal readonly int Max;

        internal RewardGroup(int min, int max, params string[] ids)
        {
            Min = min;
            Max = max;
            Ids = ids;
        }
    }

    /// <summary>
    /// 爆大奖的区域样板：按品类分组的奖池、该区匹配的贵重品、当场提示、报纸文案，
    /// 以及中奖次日的价格修正。五个区各一条。
    /// 价格修正按品类挂，可以挂多条（生活废料带同时挂食品与日用品）；
    /// 某件物品若确实没有对应市场类型，就把品类数组留空，只写报纸、不真降价。
    /// </summary>
    internal sealed class StashTemplate
    {
        internal readonly string RegionId;
        internal readonly RewardGroup[] Groups;
        internal readonly string[] ValuableIds;
        internal readonly string[] ItemTypes;
        internal readonly int[] PricePercents;
        private readonly string noticeZh;
        private readonly string noticeEn;
        private readonly string newsNameZh;
        private readonly string newsNameEn;
        private readonly string newsDescZh;
        private readonly string newsDescEn;
        private readonly string displayZh;
        private readonly string displayEn;

        private StashTemplate(string regionId, RewardGroup[] groups, string[] valuableIds,
            string[] itemTypes, int[] pricePercents,
            string noticeZh, string noticeEn,
            string newsNameZh, string newsNameEn, string newsDescZh, string newsDescEn,
            string displayZh, string displayEn)
        {
            RegionId = regionId;
            Groups = groups;
            ValuableIds = valuableIds;
            ItemTypes = itemTypes;
            PricePercents = pricePercents;
            this.noticeZh = noticeZh;
            this.noticeEn = noticeEn;
            this.newsNameZh = newsNameZh;
            this.newsNameEn = newsNameEn;
            this.newsDescZh = newsDescZh;
            this.newsDescEn = newsDescEn;
            this.displayZh = displayZh;
            this.displayEn = displayEn;
        }

        internal string Notice { get { return Copy.Text(noticeZh, noticeEn); } }
        internal string NewsName { get { return Copy.Text(newsNameZh, newsNameEn); } }
        internal string NewsDescription { get { return Copy.Text(newsDescZh, newsDescEn); } }
        internal string DisplayName { get { return Copy.Text(displayZh, displayEn); } }

        private static readonly StashTemplate[] All =
        {
            // 入口空地：通用区，拆材料与工具两组（本区这两个大类的非垃圾表）。
            // 贵重品取芯片与高价工具 / 材料，和第 80 档贵重品池里的同类物品对得上。
            new StashTemplate("gate",
                new[]
                {
                    new RewardGroup(3, 4, "scrap_metal", "printer_plastic", "nuts_metal", "wire",
                        "gun_part", "common_electronic", "metal_ingot"),
                    new RewardGroup(3, 4, "screwdriver", "wire_cutter", "welder", "module_extractor",
                        "crowbar", "box_cutter", "hatchet")
                },
                new[] { "rare_electronic", "module_extractor_advanced", "printer_chip_gun_part",
                        "advanced_flux_agent", "xray_scanner" },
                new[] { "TOOL" }, new[] { -20 },
                "你在{0}撬开一只铁皮柜：上层工具码得整整齐齐，底层压着成捆的材料和几块值钱的芯片。\n今天在这个地方，每次搜索都可能多一件东西。",
                "You pried open a tin chest {0}: tools stacked neatly on top, bundles of stock and a few pricey chips wedged underneath.\nEvery search here today may turn up something extra.",
                "门口空地挖出的工具箱和料垛",
                "A Tool Chest and a Stack of Stock at the Main Gate",
                "有人昨夜在入口空地的垃圾堆里拖出一只整箱工具，底下还压着成捆的材料和几块值钱的芯片。五金铺老板今早脸色不太好——今天工具和材料多半都要便宜。",
                "Someone dragged a chest of tools out of the trash by the main gate last night, with bundles of stock and a few pricey chips still underneath. The hardware dealer was looking sour this morning; tools and materials are likely to go cheap today.",
                "工具倾销", "Tool Surplus"),

            // 拆解堆：材料 / 工具 / 模组三组，按本区三个大类各取一组。
            // 贵重品取打印芯片与高价材料、工具，与拆解堆的产出对路。
            new StashTemplate("scrap",
                new[]
                {
                    new RewardGroup(2, 3, "scrap_metal", "printer_plastic", "nuts_metal", "wire",
                        "gun_part", "common_electronic", "metal_ingot"),
                    new RewardGroup(2, 3, "screwdriver", "turbo_booster", "wire_cutter",
                        "welder", "module_extractor", "crowbar"),
                    new RewardGroup(2, 3, "system_module_ruined", "system_module_corrupt",
                        "blank_module", "random_performance_module", "random_efficiency_module",
                        "random_quality_module", "system_module_performance",
                        "system_module_eco", "system_module_fineness")
                },
                new[] { "printer_chip_pistol_double", "printer_chip_heavy_handmade_pistol_frame",
                        "printer_chip_combat_machete", "printer_chip_gun_part",
                        "printer_chip_10_mag_ext", "advanced_flux_agent",
                        "module_extractor_advanced", "rare_electronic" },
                new[] { "MATERIAL" }, new[] { -20 },
                "你在{0}刨出一垛料、一箱工具和几块还没拆封的模组，底下还压着值钱的芯片。\n今天在这个地方，每次搜索都可能多一件东西。",
                "You dug out a stack of stock, a box of tools and a few sealed modules {0}, with pricey chips tucked underneath.\nEvery search here today may turn up something extra.",
                "拆解堆出了整垛料和几箱工具",
                "Stock, Tools and Modules From the Scrap Pile",
                "拆解堆昨夜出了怪事：有人从塌掉的夹层里拖出整垛还没回炉的金属和塑料，外加一箱工具和几块完好的模组，里头还夹着值钱的芯片。消息传开，收料的开价一个比一个低，今天材料怕是要贱卖。",
                "Something odd happened at the scrap pile last night: someone hauled a whole stack of unmelted metal and plastic out of a collapsed cavity, plus a box of tools, a few intact modules and some pricey chips. Word got around, and the buyers are already undercutting each other — materials will probably sell cheap today.",
                "材料过剩", "Material Glut"),

            // 生活废料带：食品一组、日用品一组，价格修正仍是 FOOD + HOUSEHOLD_GOOD 两条。
            // 贵重品取净水 / 泉水与正品香烟、酵母反应器，都落在食品或日用一侧。
            new StashTemplate("refuse",
                new[]
                {
                    new RewardGroup(3, 4, "zerochew", "processed_juice", "soda_red", "cat_bar",
                        "processed_milk", "li_eat_snackbar", "energy_drink", "red_beer",
                        "cup_noodle", "galaxy_blend", "processed_meat", "beis_icecream",
                        "nudka", "mini_alcohol_bottle"),
                    new RewardGroup(3, 4, "caffeine_pill", "toothpaste", "water_filter",
                        "toilet_paper", "shampoo", "full_water_test_strip_box", "paper_towel",
                        "full_water_purification_tablet_box", "wine_berry", "wine_yeast",
                        "wine_yeast_red", "wine_superyeast", "packet_red_cigarette_fake")
                },
                new[] { "bottled_water_premium", "water_filter_adv", "portable_water_purifier",
                        "wine_yeast_infinite", "packet_red_cigarette" },
                new[] { "FOOD", "HOUSEHOLD_GOOD" }, new[] { -20, -20 },
                "你在{0}翻出一整车没拆封的食品和日用品，中间还夹着几件值钱货。\n今天在这个地方，每次搜索都可能多一件东西。",
                "You uncovered a whole cart of sealed food and household goods {0}, with a few valuables tucked in among them.\nEvery search here today may turn up something extra.",
                "废料带里翻出的年货",
                "A Holiday Haul in the Refuse Lane",
                "有拾荒客说，昨晚在生活废料带里翻出了一整车没拆封的食品和日用品，还夹着几瓶好水、一条正品烟和别的值钱货。货一到早市，摊主们就抢着压价，今天这两类东西恐怕都不值钱了。",
                "A scavenger claims to have hauled a whole cart of sealed food and household goods out of the refuse lane last night, along with a few bottles of good water, a genuine pack of cigarettes and other valuables. By the time it reached the morning market, stallholders were already undercutting each other — both are likely to go cheap today.",
                "食品日用品涌入", "Food and Household Surplus"),

            // 医疗废弃点：耗材 / 药品 / 器械三组，对应本区三个大类。
            // 贵重品只取医疗向的注射器、蓝血与外科器械；奖池 8 件等仍全部带 MEDICAL，价格修正不变。
            new StashTemplate("med",
                new[]
                {
                    new RewardGroup(2, 3, "bandage_item", "hemostatic_bandage_item",
                        "topical_bandage_item", "blood_bag", "rubbing_alcohol", "salve"),
                    new RewardGroup(2, 3, "fanta_pill", "birth_control_pill", "smelling_salt",
                        "dream_dust", "phagimycin_pill", "zyanide_pill", "nightmare_dust",
                        "large_purple_injector_bad"),
                    new RewardGroup(2, 2, "common_medical", "surgery_tool")
                },
                new[] { "blue_blood_bag", "black_injector", "large_purple_injector", "surgery_tool" },
                new[] { "MEDICAL" }, new[] { -20 },
                "你在{0}找到一批还没过期的医疗物资：绷带、药片、器械都在，还有几件值钱的药。\n今天在这个地方，每次搜索都可能多一件东西。",
                "You found a batch of still-unexpired medical supplies {0}: bandages, pills and instruments, plus a few pricey items.\nEvery search here today may turn up something extra.",
                "医疗废弃点的整箱药品上了早市",
                "Crates of Medicine Reach the Morning Market",
                "医疗废弃点昨夜有人拖出成箱还没过期的绷带、药片和手术器械，连几支值钱的针剂也一并流了出来。天没亮就摆上了早市，开价压得比药铺进货还低。今天医疗用品怕是要跟着一起跌。",
                "Someone dragged crates of still-unexpired bandages, pills and surgical instruments out of the medical dump last night, and a few pricey injectors went with them. They were on the morning market before dawn, priced below what the pharmacy pays wholesale. Medical supplies are likely to fall with them today.",
                "医疗物资倾销", "Medical Supplies Dumped"),

            // 废武器堆：弹药 / 配件 / 枪支三组，对应本区三个大类。
            // 贵重品取贵重枪支、贵重近战与打印配件芯片，都落在武器这一侧。
            new StashTemplate("weapons",
                new[]
                {
                    new RewardGroup(3, 4, "small_pistol_ammo_nl", "small_pistol_ammo",
                        "heavy_pistol_ammo_nl", "small_pistol_ammo_p", "heavy_pistol_ammo",
                        "lou_etched_round", "heavy_pistol_ammo_p", "rds", "rds_makeshift"),
                    new RewardGroup(2, 3, "mag_22", "silencer_makeshift", "laser_toy",
                        "internal_springs_improved", "mag_22_high", "mag_10mm", "short_stock",
                        "mag_22_ext", "grip", "internal_springs", "bipod", "mag_10mm_high",
                        "mag_10mm_ext", "compensator", "laser", "trigger_group", "stock",
                        "mag_10mm_smg", "silencer", "barrel", "mag_10mm_smg_high", "ammo_display",
                        "grip_gel", "match_barrel", "mag_10mm_smg_box", "silencer2", "laser2"),
                    new RewardGroup(2, 2, "printed_gun", "handmade_pistol", "heavy_handmade_pistol",
                        "glock_receiver", "revolver", "stun_gun", "smg", "shotgun")
                },
                new[] { "revolver", "stun_gun", "smg", "shotgun", "glock_receiver",
                        "combat_machete", "stun_baton", "printer_chip_10_mag_ext",
                        "printer_chip_gun_part" },
                new[] { "AMMUNITION", "FIREARM", "GUN_MOD" }, new[] { -20, -20, -20 },
                "你在{0}翻出弹药、配件和还能打响的枪，箱底还压着值钱货。\n今天在这个地方，每次搜索都可能多一件东西。",
                "You turned up ammo, gun mods and a few guns that still fire {0}, with valuables stashed at the bottom of the crate.\nEvery search here today may turn up something extra.",
                "废武器堆的军械箱",
                "An Arms Crate in the Weapon Dump",
                "有人昨夜在废武器堆撬开一只军械箱，抱走成把的子弹和两三支还能打响的枪，连枪上的配件和几件值钱货都拆下来带走了。枪铺今天大概要被迫让价。",
                "Someone cracked open an arms crate in the weapon dump last night and walked off with fistfuls of rounds, two or three working guns, and the mods and valuables stripped off them. The gun shop will likely be forced to cut prices today.",
                "军械流入市场", "Arms Flood the Market")
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
