using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>
    /// Turns the fixed per-area weight tables plus the player's settings into a
    /// runtime probability table, and computes expected value from the same
    /// unitValue figures used for the balance design.
    /// </summary>
    internal static class Tuning
    {
        internal const float DefaultJunkPercent = 50f;
        internal const float DefaultHighPercent = 3f;
        internal const int DefaultSpecialtyTier = 2;

        /// <summary>80 档：补给箱与钥匙卡的绝对概率，各自一个设置项（默认 0.5%、范围 0–5%）。</summary>
        internal const string CratePercentKey = "crate_percent";
        internal const string KeycardPercentKey = "keycard_percent";
        internal const float DefaultSpecialPercent = 0.5f;
        internal const float SpecialPercentMin = 0f;
        internal const float SpecialPercentMax = 5f;

        /// <summary>60 档小奖：每次拾荒独立掷一次，默认 3%、范围 0–10%、0.1 步进。</summary>
        internal const string MinorPercentKey = "minor_percent";
        internal const float DefaultMinorPercent = 3f;
        internal const float MinorPercentMin = 0f;
        internal const float MinorPercentMax = 10f;

        /// <summary>小奖整箱的目标价值：件数按「目标值 ×[0.75,1.25] ÷ 单价」反推。</summary>
        internal const string MinorValueKey = "minor_value";
        internal const float DefaultMinorValue = 200f;
        internal const float MinorValueMin = 50f;
        internal const float MinorValueMax = 400f;
        internal const float MinorValueStep = 10f;

        internal const float JunkMin = 0f;
        internal const float JunkMax = 100f;
        internal const float HighMin = 0f;
        internal const float HighMax = 30f;

        /// <summary>60 档爆大奖的概率上限；声望不到 60 时实际生效值会被压到 0.1%。</summary>
        internal const string StashCapKey = "stash_cap";
        internal const float DefaultStashCap = 1.0f;
        internal const float StashCapMin = 0.1f;
        internal const float StashCapMax = 5.0f;

        private const string SectionName = "拾荒掉落 / Loot";
        private const string IntegrationSection = "声望飘字 / Reputation Pop";
        private const string DebugSection = "调试 / Debug";

        /// <summary>声望飘字开关：只在装了 Reputation Pop 时才有实际效果。</summary>
        internal const string ReputationPopKey = "reputation_pop";
        internal const bool DefaultReputationPop = true;

        /// <summary>调试项。默认关闭；关闭时「设置声望点数」不生效、界面里也会被隐藏。</summary>
        internal const string DebugEnabledKey = "debug_enabled";
        internal const string DebugPointsKey = "debug_points";
        internal const float DebugPointsMin = 0f;
        internal const float DebugPointsMax = 200f;

        /// <summary>
        /// 拾荒地面：只放大拾荒时整理战利品的那块「地面」。
        /// 12×9 是原版；默认 19×21，宽 12–19、高 9–31。
        /// 尺寸不进存档，改完下次重建地面时生效。
        /// </summary>
        internal const string GroundSection = "拾荒地面 / Ground";
        internal const string GroundWidthKey = "ground_width";
        internal const string GroundHeightKey = "ground_height";
        internal const int DefaultGroundWidth = 19;
        internal const int DefaultGroundHeight = 21;
        internal const int GroundWidthMin = 12;
        internal const int GroundWidthMax = 19;
        internal const int GroundHeightMin = 9;
        internal const int GroundHeightMax = 31;

        /// <summary>
        /// One shared tier for every area. Each area still applies it to its
        /// own signature pool, so the same tier yields different goods.
        /// </summary>
        internal const string SpecialtyKey = "specialty";

        /// <summary>Dropdown labels and the percentage each tier stands for.</summary>
        private static readonly string[] SpecialtyLabels = { "较少", "中等", "较多", "全部" };
        private static readonly float[] SpecialtyPercents = { 25f, 50f, 75f, 100f };

        private static ConfigEntry<float> junkEntry;
        private static ConfigEntry<float> highEntry;
        private static ConfigEntry<float> crateEntry;
        private static ConfigEntry<float> cardEntry;
        private static ConfigEntry<float> minorEntry;
        private static ConfigEntry<float> minorValueEntry;
        private static ConfigEntry<float> stashCapEntry;
        private static ConfigEntry<string> specialtyEntry;
        private static ConfigEntry<bool> reputationPopEntry;
        private static ConfigEntry<bool> debugEnabledEntry;
        private static ConfigEntry<float> debugPointsEntry;
        private static ConfigEntry<int> groundWidthEntry;
        private static ConfigEntry<int> groundHeightEntry;
        private static int liveGroundWidth;
        private static int liveGroundHeight;
        private static bool debugIgnoredLogged;
        private static readonly Dictionary<string, EffectiveTable> cache =
            new Dictionary<string, EffectiveTable>();
        private static bool cachePerk80;
        private static readonly Dictionary<string, float> baseEvCache =
            new Dictionary<string, float>();
        private static bool hasLive;
        private static float liveJunk;
        private static float liveHigh;
        private static float liveCrate;
        private static float liveCard;
        private static float liveMinor;
        private static float liveMinorValue;
        private static bool initialized;

        private static float EntryJunk()
        {
            if (junkEntry == null) return DefaultJunkPercent;
            float value = junkEntry.Value;
            if (float.IsNaN(value)) return DefaultJunkPercent;
            return Mathf.Round(Mathf.Clamp(value, JunkMin, JunkMax));
        }

        private static float EntryHigh()
        {
            if (highEntry == null) return DefaultHighPercent;
            float value = highEntry.Value;
            if (float.IsNaN(value)) return DefaultHighPercent;
            return Mathf.Round(Mathf.Clamp(value, HighMin, HighMax) * 2f) / 2f;
        }

        private static float EntryCratePercent()
        {
            if (crateEntry == null) return DefaultSpecialPercent;
            return ClampSpecialPercent(crateEntry.Value);
        }

        private static float EntryKeycardPercent()
        {
            if (cardEntry == null) return DefaultSpecialPercent;
            return ClampSpecialPercent(cardEntry.Value);
        }

        private static float EntryMinorPercent()
        {
            if (minorEntry == null) return DefaultMinorPercent;
            return ClampMinorPercent(minorEntry.Value);
        }

        private static float EntryMinorValue()
        {
            if (minorValueEntry == null) return DefaultMinorValue;
            return ClampMinorValue(minorValueEntry.Value);
        }

        private static int EntrySpecialtyTier()
        {
            if (specialtyEntry == null) return DefaultSpecialtyTier;
            int tier = Array.IndexOf(SpecialtyLabels, specialtyEntry.Value);
            return tier < 0 ? DefaultSpecialtyTier : tier;
        }

        /// <summary>
        /// 25/50/75/100 percent, picked from four fixed tiers. Each area applies it
        /// to its own signature list, junk excluded.
        /// </summary>
        internal static float SpecialtyPercent
        {
            get { return SpecialtyPercents[EntrySpecialtyTier()]; }
        }

        /// <summary>0-based tier index: 0 = 较少, 1 = 中等, 2 = 较多, 3 = 全部.</summary>
        internal static int SpecialtyTier
        {
            get { return EntrySpecialtyTier(); }
        }

        private static int EntryGroundWidth()
        {
            if (groundWidthEntry == null) return DefaultGroundWidth;
            return Mathf.Clamp(groundWidthEntry.Value, GroundWidthMin, GroundWidthMax);
        }

        private static int EntryGroundHeight()
        {
            if (groundHeightEntry == null) return DefaultGroundHeight;
            return Mathf.Clamp(groundHeightEntry.Value, GroundHeightMin, GroundHeightMax);
        }

        /// <summary>声望飘字开关；配置项没注册成功时按默认（开）处理。</summary>
        internal static bool ReputationPopEnabled
        {
            get { return reputationPopEntry == null ? DefaultReputationPop : reputationPopEntry.Value; }
        }

        /// <summary>调试项总开关；配置项没注册成功时按关闭处理。</summary>
        internal static bool DebugEnabled
        {
            get { return debugEnabledEntry != null && debugEnabledEntry.Value; }
        }

        /// <summary>界面输入的声望点数（未取整的原始值）。</summary>
        internal static float DebugPoints
        {
            get { return debugPointsEntry == null ? 0f : debugPointsEntry.Value; }
        }

        /// <summary>
        /// 界面上的开关是即时生效的：原生配置窗口不会把勾选变化实时写回配置，
        /// 只有按下「应用」才写。这里由我们的滑条/开关事件直接写入同一个配置项。
        /// </summary>
        internal static void SetDebugEnabledLive(bool value)
        {
            if (debugEnabledEntry == null || debugEnabledEntry.Value == value) return;
            debugEnabledEntry.Set(value);
        }

        /// <summary>
        /// 滑条松手时调用：按 0.5 点吸附后写回配置项，由 OnChanged 统一负责
        /// 「开关是否打开」的判断与坐实写入。数值没变则不重复触发。
        /// </summary>
        internal static void SetDebugPointsFromSlider(float value)
        {
            if (debugPointsEntry == null) return;
            float snapped = Mathf.Round(value * 2f) * 0.5f;
            if (snapped < DebugPointsMin) snapped = DebugPointsMin;
            if (snapped > DebugPointsMax) snapped = DebugPointsMax;
            if (Mathf.Abs(debugPointsEntry.Value - snapped) < 0.001f) return;
            debugPointsEntry.Set(snapped);
        }

        /// <summary>拾荒地面宽度（格）。</summary>
        internal static int GroundWidth
        {
            get { return hasLive ? liveGroundWidth : EntryGroundWidth(); }
        }

        /// <summary>拾荒地面高度（格）。</summary>
        internal static int GroundHeight
        {
            get { return hasLive ? liveGroundHeight : EntryGroundHeight(); }
        }

        /// <summary>地面宽度滑条拖动中的内存值；松手时才写配置。</summary>
        internal static void SetGroundWidthLive(int value)
        {
            EnsureLive();
            liveGroundWidth = Mathf.Clamp(value, GroundWidthMin, GroundWidthMax);
            Invalidated();
        }

        /// <summary>地面高度滑条拖动中的内存值；松手时才写配置。</summary>
        internal static void SetGroundHeightLive(int value)
        {
            EnsureLive();
            liveGroundHeight = Mathf.Clamp(value, GroundHeightMin, GroundHeightMax);
            Invalidated();
        }

        /// <summary>
        /// 三个预设按钮共用：只改宽高两项，不动垃圾、贵重品、专精等其它设置。
        /// </summary>
        internal static void ApplyGroundPreset(int width, int height)
        {
            hasLive = false;
            if (groundWidthEntry != null)
            {
                int w = Mathf.Clamp(width, GroundWidthMin, GroundWidthMax);
                if (groundWidthEntry.Value != w) groundWidthEntry.Set(w);
            }
            if (groundHeightEntry != null)
            {
                int h = Mathf.Clamp(height, GroundHeightMin, GroundHeightMax);
                if (groundHeightEntry.Value != h) groundHeightEntry.Set(h);
            }
            // 两项都写完后统一刷新一次，避免界面只更新一半。
            Invalidated();
        }

        /// <summary>
        /// While a slider is being dragged the values live here instead of in the
        /// config entries, so the file is written once when the drag ends.
        /// </summary>
        internal static float JunkPercent { get { return hasLive ? liveJunk : EntryJunk(); } }

        internal static float HighPercent { get { return hasLive ? liveHigh : EntryHigh(); } }

        /// <summary>80 档补给箱概率（%，0–5，0.1 步进）。</summary>
        internal static float CratePercent
        {
            get { return hasLive ? liveCrate : EntryCratePercent(); }
        }

        /// <summary>80 档钥匙卡概率（%，0–5，0.1 步进）。</summary>
        internal static float KeycardPercent
        {
            get { return hasLive ? liveCard : EntryKeycardPercent(); }
        }

        /// <summary>60 档小奖概率（%，0–10，0.1 步进）。</summary>
        internal static float MinorPercent
        {
            get { return hasLive ? liveMinor : EntryMinorPercent(); }
        }

        /// <summary>小奖整箱目标价值（50–400，按 10 吸附）。</summary>
        internal static float MinorValue
        {
            get { return hasLive ? liveMinorValue : EntryMinorValue(); }
        }

        /// <summary>小奖的实际生效概率（%）：声望不到 60 时按 1/10 计，告示板与实际掷骰共用。</summary>
        internal static float EffectiveMinorPercent
        {
            get
            {
                float percent = MinorPercent;
                if (Reputation.Points < Reputation.Perk60Threshold) percent *= 0.1f;
                return percent;
            }
        }

        /// <summary>True while a slider drag is holding unsaved values in memory.</summary>
        internal static bool HasLive { get { return hasLive; } }

        internal static float ClampJunk(float value)
        {
            return float.IsNaN(value) ? DefaultJunkPercent : Mathf.Clamp(value, JunkMin, JunkMax);
        }

        internal static float ClampHigh(float value)
        {
            return float.IsNaN(value) ? DefaultHighPercent : Mathf.Clamp(value, HighMin, HighMax);
        }

        /// <summary>按 0.1 步进吸附并夹在 0–5。</summary>
        internal static float ClampSpecialPercent(float value)
        {
            if (float.IsNaN(value)) return DefaultSpecialPercent;
            value = Mathf.Round(Mathf.Clamp(value, SpecialPercentMin, SpecialPercentMax) * 10f) / 10f;
            return Mathf.Clamp(value, SpecialPercentMin, SpecialPercentMax);
        }

        /// <summary>按 0.1 步进吸附并夹在 0–10。</summary>
        internal static float ClampMinorPercent(float value)
        {
            if (float.IsNaN(value)) return DefaultMinorPercent;
            value = Mathf.Round(Mathf.Clamp(value, MinorPercentMin, MinorPercentMax) * 10f) / 10f;
            return Mathf.Clamp(value, MinorPercentMin, MinorPercentMax);
        }

        /// <summary>按 10 吸附并夹在 50–400。</summary>
        internal static float ClampMinorValue(float value)
        {
            if (float.IsNaN(value)) return DefaultMinorValue;
            value = Mathf.Round(Mathf.Clamp(value, MinorValueMin, MinorValueMax) / MinorValueStep)
                * MinorValueStep;
            return Mathf.Clamp(value, MinorValueMin, MinorValueMax);
        }

        /// <summary>按 0.1 步进吸附并夹在 0.1–5.0。</summary>
        internal static float ClampStashCap(float value)
        {
            if (float.IsNaN(value)) return DefaultStashCap;
            value = Mathf.Round(Mathf.Clamp(value, StashCapMin, StashCapMax) * 10f) / 10f;
            return Mathf.Clamp(value, StashCapMin, StashCapMax);
        }

        internal static float StashCap
        {
            get
            {
                if (stashCapEntry == null) return DefaultStashCap;
                return ClampStashCap(stashCapEntry.Value);
            }
        }

        /// <summary>
        /// Vanilla reference: the untouched weight tables (junk at 100%, no
        /// specialty, no valuable share), i.e. 12.26 in every area. This is a
        /// fixed yardstick and does not follow the player's current settings.
        /// </summary>
        internal static float BaseEv(Region region)
        {
            float ev;
            if (baseEvCache.TryGetValue(region.Id, out ev)) return ev;
            ev = Build(region, 100f, 0f, 0f, false).Ev;
            baseEvCache[region.Id] = ev;
            return ev;
        }

        internal static void Init()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                ModConfig config = ModHelper.GetConfig(Core.Id);

                junkEntry = config.Define("junk_percent", DefaultJunkPercent, SectionName,
                    delegate { return Copy.Text("垃圾出现率倍率（%，0–100）", "Junk chance multiplier (%, 0-100)"); },
                    0f, 100f,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒出多少垃圾，只由这条决定。\n50%：入口空地的垃圾约 49% → 约 24%。\n100%：原版水平（约 49%）；0%：不出垃圾。\n注意：50% 不是「一半掉落都是垃圾」，\n而是把原本的垃圾量减半。",
                            "Sets how much junk shows up each search; it only changes junk.\n50%: Main Gate junk goes from about 49% to about 24%.\n100%: vanilla level (about 49%). 0%: no junk at all.\nNote: 50% does not mean half of all drops are junk -\nit halves the original junk amount.");
                    },
                    false);

                highEntry = config.Define("high_percent", DefaultHighPercent, SectionName,
                    delegate { return Copy.Text("贵重品出现率（%，0–30）", "Valuable find chance (%, 0-30)"); },
                    0f, 30f,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒单独判断：这一趟会不会出贵重品。\n没出不累积，下次概率不变，没有保底。\n8% 约每 12 次出 1 次；0% 不出；最高 30%。\n贵重品换自普通物品，垃圾不受影响。\n本项在声望达到 80（识货行家）后生效：\n80 之前按 0 算（设置值会保留），80 之后完全按滑条数值生效。",
                            "Rolled once per search:\nit decides only whether this haul includes a valuable item.\nNo pity: a miss does not raise the chance next time.\n8%: about 1 in 12 searches. 0%: never. Up to 30%.\nValuable items replace normal items; junk is not affected.\nThis setting activates at 80 reputation (Connoisseur):\nbefore 80 it counts as 0 (your value is kept); from 80 on the slider applies as-is.");
                    },
                    false);

                crateEntry = config.Define(CratePercentKey, DefaultSpecialPercent, SectionName,
                    delegate { return Copy.Text("补给箱概率（%，0–5）", "Supply crate chance (%, 0-5)"); },
                    SpecialPercentMin, SpecialPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "声望达到 80（识货行家）后，每次拾荒出补给箱的绝对概率。\n默认 0.5%，范围 0–5%，按 0.1 吸附；0% 不出。\n补给箱从普通物品份额里扣，垃圾概率不变。\n只在本区有对应箱子时生效（拆解堆、生活废料带、医疗废弃点、废武器堆）。",
                            "Absolute chance per search for a supply crate, active at 80 reputation (Connoisseur).\nDefault 0.5%, range 0-5%, 0.1 steps; 0% disables it.\nThe crate share comes out of normal items, so junk chance is unchanged.\nOnly applies where the area has a matching crate (Scrap Pile, Refuse Lane, Medical Waste, Weapon Dump).");
                    },
                    false);

                cardEntry = config.Define(KeycardPercentKey, DefaultSpecialPercent, SectionName,
                    delegate { return Copy.Text("钥匙卡概率（%，0–5）", "Keycard chance (%, 0-5)"); },
                    SpecialPercentMin, SpecialPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "声望达到 80（识货行家）后，每次拾荒出对应钥匙卡的绝对概率。\n默认 0.5%，范围 0–5%，按 0.1 吸附；0% 不出。\n钥匙卡从普通物品份额里扣，垃圾概率不变。\n只在本区有对应箱子时生效（拆解堆、生活废料带、医疗废弃点、废武器堆）。",
                            "Absolute chance per search for the matching keycard, active at 80 reputation (Connoisseur).\nDefault 0.5%, range 0-5%, 0.1 steps; 0% disables it.\nThe keycard share comes out of normal items, so junk chance is unchanged.\nOnly applies where the area has a matching crate (Scrap Pile, Refuse Lane, Medical Waste, Weapon Dump).");
                    },
                    false);

                minorEntry = config.Define(MinorPercentKey, DefaultMinorPercent, SectionName,
                    delegate { return Copy.Text("小奖概率（%，0–10）", "Minor prize chance (%, 0-10)"); },
                    MinorPercentMin, MinorPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒独立掷一次小奖，两档门槛：\n声望达到 60（眼力毒辣）时按本设置值生效；不到 60 时按本值的 1/10 生效（默认 3% 即 0.3%）。\n默认 3%，范围 0–10%，按 0.1 吸附；0% 两档都不出。\n小奖按区翻出一整箱同一种物品（每区 2–3 种），\n整箱值多少钱看下一项「小奖整箱价值」。\n不发新闻、不改价格、不触发大奖的当日额外收获，也不影响爆大奖的累积窗口。\n同一次搜索里大奖、小奖都命中时只算大奖。\n小奖物品与正常拾荒一样计入声望。",
                            "Every search rolls once for a minor prize, in two tiers:\nfrom 60 reputation (Keen Eye) it uses this value; below 60 it uses one tenth (3% becomes 0.3%).\nDefault 3%, range 0-10%, 0.1 steps; 0% disables both tiers.\nA minor prize is a whole crate of one item type (2-3 crates per area);\nits value is the \"Minor prize crate value\" setting below.\nNo news, no price change, no jackpot bonus item, and no effect on the jackpot window.\nIf both jackpot and minor prize hit in the same search, only the jackpot counts.\nMinor prize items count toward reputation like any other find.");
                    },
                    false);

                minorValueEntry = config.Define(MinorValueKey, DefaultMinorValue, SectionName,
                    delegate { return Copy.Text("小奖整箱价值", "Minor prize crate value"); },
                    MinorValueMin, MinorValueMax,
                    delegate
                    {
                        return Copy.Text(
                            "只决定小奖那一整箱值多少钱，不影响爆大奖，也不影响上面的基础 EV。\n每次小奖在 目标值 × 0.75–1.25 之间随机取一个金额，\n除以该箱物品单价后四舍五入得到件数（最少 1 件、最多 12 件）。\n默认 200，范围 50–400，按 10 吸附。\n例：目标 200、单价 25 的物品约 6–10 件；目标 100 约 3–5 件。",
                            "Sets how much a minor-prize crate is worth; it does not touch the jackpot or the base EV above.\nEach minor prize picks a random amount in target x 0.75-1.25,\ndivides it by the item's unit price and rounds to get the count (1 to 12 items).\nDefault 200, range 50-400, 10 steps.\nExample: target 200 with a 25-value item is about 6-10 items; target 100 is about 3-5.");
                    },
                    false);

                stashCapEntry = config.Define(StashCapKey, DefaultStashCap, SectionName,
                    delegate { return Copy.Text("爆大奖概率上限（%，0.1–5.0）", "Jackpot chance cap (%, 0.1-5.0)"); },
                    StashCapMin, StashCapMax,
                    delegate
                    {
                        return Copy.Text(
                            "60 档「眼力毒辣」的爆大奖概率上限。\n每次拾荒未中：没到上限一半就翻倍，过半后每次 +0.05%。\n中奖后窗口砍半，再线性往上爬。\n声望不到 60 时，上限固定压到 0.1%（不是完全不中）。\n只影响已配置储藏样板的区域（目前是生活废料带）。",
                            "Cap for the 60-point Keen Eye jackpot.\nEach miss doubles the chance until half the cap, then adds 0.05% per miss.\nA win halves the window; it then climbs linearly again.\nBelow 60 reputation the cap is hard-limited to 0.1% (not excluded).\nOnly applies to regions with a stash template (currently Refuse Lane).");
                    },
                    false);

                specialtyEntry = config.Define(SpecialtyKey, SpecialtyLabels[DefaultSpecialtyTier],
                    SectionName,
                    delegate { return Copy.Text("区域专精度", "Region specialty"); },
                    SpecialtyLabels,
                    delegate
                    {
                        return Copy.Text(
                            "决定除垃圾外，捡到的东西多偏向本区特产。\n档位越高，本区特产越多，通用物品越少。\n「全部」：除垃圾外只出本区特产。\n如拆解堆：「较少」仍有食品、日用，\n「全部」只出材料、工具与电子件。\n每个区都有自己的一批特产；垃圾不受影响。",
                            "Controls how strongly, junk aside, a haul leans toward this region's own goods.\nHigher tiers mean more local goods and fewer generic ones.\nAll: nothing but local goods and junk.\nExample, Scrap Pile: Rare still brings food and household goods;\nAll brings only materials, tools, and electronics.\nEach region has its own goods; junk is not affected.");
                    },
                    false);

                MigrateSpecialtyTier(config);

                groundWidthEntry = config.Define(GroundWidthKey, DefaultGroundWidth, GroundSection,
                    delegate { return Copy.Text("地面宽度（格，12–19）", "Ground width (cells, 12-19)"); },
                    GroundWidthMin, GroundWidthMax,
                    delegate
                    {
                        return Copy.Text(
                            "只影响拾荒时整理战利品的那块「地面」。\n12 是原版宽度；调小不会把已经放大的地面缩回去。\n尺寸不进存档，改完下次进拾荒场景、重建地面时生效。",
                            "Only affects the ground where scavenging loot is sorted.\n12 is the vanilla width. Lowering it never shrinks a ground that is already larger.\nThe size is not stored in the save; it applies the next time the scavenging scene rebuilds the ground.");
                    },
                    false);

                groundHeightEntry = config.Define(GroundHeightKey, DefaultGroundHeight, GroundSection,
                    delegate { return Copy.Text("地面高度（格，9–31）", "Ground height (cells, 9-31)"); },
                    GroundHeightMin, GroundHeightMax,
                    delegate
                    {
                        return Copy.Text(
                            "只影响拾荒时整理战利品的那块「地面」。\n9 是原版高度；调小不会把已经放大的地面缩回去。\n尺寸不进存档，改完下次进拾荒场景、重建地面时生效。",
                            "Only affects the ground where scavenging loot is sorted.\n9 is the vanilla height. Lowering it never shrinks a ground that is already larger.\nThe size is not stored in the save; it applies the next time the scavenging scene rebuilds the ground.");
                    },
                    false);

                reputationPopEntry = config.Define(ReputationPopKey, DefaultReputationPop,
                    IntegrationSection,
                    delegate
                    {
                        return ReputationPopBridge.Running
                            ? Copy.Text("声望飘字（需安装 Reputation Pop）",
                                "Reputation pop-ups (requires Reputation Pop)")
                            : Copy.Text("声望飘字（需安装 Reputation Pop，当前未安装）",
                                "Reputation pop-ups (requires Reputation Pop - not installed)");
                    },
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒结算后，把本次获得的声望用 Reputation Pop 的气泡飘出来。\n只在装了「声望增加显示 / Reputation Pop」时生效；\n未安装时本项不起作用，也不会报错。\n关掉后不再弹出，声望照常累计。",
                            "After each scavenge, pops this haul's reputation gain as a Reputation Pop bubble.\nOnly works when the Reputation Pop mod is installed;\nwithout it this option does nothing and logs no error.\nTurning it off stops the pop-ups only; reputation still accumulates.");
                    },
                    false);

                reputationPopEntry.OnChanged += delegate(bool value)
                {
                    Debug.Log("[DumpingGroundMap] reputation pop: setting=" + (value ? "on" : "off"));
                };

                debugEnabledEntry = config.Define(DebugEnabledKey, false, DebugSection,
                    delegate { return Copy.Text("启用调试项", "Enable debug options"); },
                    delegate
                    {
                        return Copy.Text(
                            "打开后下面的「设置声望点数」才会生效。\n仅供测试，默认关闭。",
                            "Turns on the reputation setter below.\nTesting only; off by default.");
                    },
                    false);

                debugEnabledEntry.OnChanged += delegate(bool value)
                {
                    if (!value) Reputation.ClearPendingDebugPoints();
                    Debug.Log("[DumpingGroundMap] debug: options " + (value ? "on" : "off"));
                };

                debugPointsEntry = config.Define(DebugPointsKey, 0f, DebugSection,
                    delegate { return Copy.Text("设置声望点数", "Set reputation points"); },
                    DebugPointsMin, DebugPointsMax,
                    delegate
                    {
                        return Copy.Text(
                            "仅供测试，会直接改写当前存档的声望（写同一个键 rep_total_value）。\n数值按 0.5 点取整，例如 19.5。\n需先打开上面的「启用调试项」才会生效。\n已有存档时改完立即存档；在主菜单改则排到下次读档时生效。",
                            "Testing only - overwrites the reputation stored in the current save (same rep_total_value key).\nValues snap to 0.5 points, for example 19.5.\nRequires \"Enable debug options\" above to be on.\nWith a save loaded the change is saved immediately; from the main menu it applies on the next load.");
                    },
                    false);

                debugPointsEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(value * 2f) * 0.5f;
                    if (snapped < DebugPointsMin) snapped = DebugPointsMin;
                    if (snapped > DebugPointsMax) snapped = DebugPointsMax;
                    if (snapped != value) debugPointsEntry.Set(snapped);
                    if (!DebugEnabled)
                    {
                        if (!debugIgnoredLogged)
                        {
                            debugIgnoredLogged = true;
                            Debug.Log("[DumpingGroundMap] debug: reputation setter ignored (debug off)");
                        }
                        return;
                    }
                    debugIgnoredLogged = false;
                    // 统一入口：能安全存档（Emporium 场景里的 PlayerStore）就直接写并落盘，
                    // 否则排队等下次读档。主菜单里 PlayerStore 可能仍在内存中，
                    // 但那时调 SaveGame 会失败，不能直接写。
                    Reputation.ApplyDebugPoints(snapped);
                };

                junkEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(ClampJunk(value));
                    if (snapped != value) junkEntry.Set(snapped);
                    Invalidated();
                };
                highEntry.OnChanged += delegate(float value)
                {
                    float snapped = Mathf.Round(ClampHigh(value) * 2f) / 2f;
                    if (snapped != value) highEntry.Set(snapped);
                    Invalidated();
                };
                stashCapEntry.OnChanged += delegate(float value)
                {
                    float snapped = ClampStashCap(value);
                    if (snapped != value) stashCapEntry.Set(snapped);
                    Invalidated();
                };
                crateEntry.OnChanged += delegate(float value)
                {
                    float snapped = ClampSpecialPercent(value);
                    if (snapped != value) crateEntry.Set(snapped);
                    Invalidated();
                };
                cardEntry.OnChanged += delegate(float value)
                {
                    float snapped = ClampSpecialPercent(value);
                    if (snapped != value) cardEntry.Set(snapped);
                    Invalidated();
                };
                minorEntry.OnChanged += delegate(float value)
                {
                    float snapped = ClampMinorPercent(value);
                    if (snapped != value) minorEntry.Set(snapped);
                    Invalidated();
                };
                minorValueEntry.OnChanged += delegate(float value)
                {
                    float snapped = ClampMinorValue(value);
                    if (snapped != value) minorValueEntry.Set(snapped);
                    Invalidated();
                };
                specialtyEntry.OnChanged += delegate(string value)
                {
                    Invalidated();
                };
                groundWidthEntry.OnChanged += delegate(int value)
                {
                    int snapped = Mathf.Clamp(value, GroundWidthMin, GroundWidthMax);
                    if (snapped != value) groundWidthEntry.Set(snapped);
                    Invalidated();
                };
                groundHeightEntry.OnChanged += delegate(int value)
                {
                    int snapped = Mathf.Clamp(value, GroundHeightMin, GroundHeightMax);
                    if (snapped != value) groundHeightEntry.Set(snapped);
                    Invalidated();
                };
            }
            catch (Exception ex)
            {
                Debug.LogError("[DumpingGroundMap] Could not register settings: " + ex);
            }
        }

        private static void Invalidated()
        {
            cache.Clear();
            NativeSettings.RefreshIfOpen();
            Picker.RefreshBoardIfOpen();
        }

        /// <summary>
        /// 0.3.12 turned the 0-100 specialty number into four named tiers. A stored
        /// number is not one of the choice labels, so the config reader would fall
        /// back to the default; read the raw value once and map it to the nearest
        /// tier (0-37 = 较少, 38-62 = 中等, 63-87 = 较多, 88-100 = 全部).
        /// </summary>
        private static void MigrateSpecialtyTier(ModConfig config)
        {
            if (specialtyEntry == null || config == null) return;
            string raw;
            if (!TryReadRaw(config, SpecialtyKey, out raw)) return;
            float percent;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                return;
            int tier = DefaultSpecialtyTier;
            float best = float.MaxValue;
            for (int i = 0; i < SpecialtyPercents.Length; ++i)
            {
                float distance = Mathf.Abs(percent - SpecialtyPercents[i]);
                if (distance < best)
                {
                    best = distance;
                    tier = i;
                }
            }
            specialtyEntry.Set(SpecialtyLabels[tier]);
        }

        private static bool TryReadRaw(ModConfig config, string key, out string value)
        {
            value = null;
            try
            {
                FieldInfo field = typeof(ModConfig).GetField("rawValues",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) return false;
                Dictionary<string, string> raw =
                    field.GetValue(config) as Dictionary<string, string>;
                return raw != null && raw.TryGetValue(key, out value) && value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void SetJunkPercent(float value)
        {
            if (junkEntry == null) return;
            hasLive = false;
            junkEntry.Set(ClampJunk(value));
        }

        internal static void SetHighPercent(float value)
        {
            if (highEntry == null) return;
            hasLive = false;
            highEntry.Set(ClampHigh(value));
        }

        internal static void SetSpecialtyTier(int tier)
        {
            if (specialtyEntry == null) return;
            hasLive = false;
            specialtyEntry.Set(SpecialtyLabels[Mathf.Clamp(tier, 0, SpecialtyLabels.Length - 1)]);
        }

        /// <summary>
        /// 下拉框换档：立即写回配置项，由 OnChanged → Invalidated 统一清缓存并刷新
        /// EV 文本，不依赖「应用」。不清 hasLive，避免打断正在预览的滑条数值。
        /// </summary>
        internal static void SetSpecialtyTierLive(int tier)
        {
            if (specialtyEntry == null) return;
            string label = SpecialtyLabels[Mathf.Clamp(tier, 0, SpecialtyLabels.Length - 1)];
            if (specialtyEntry.Value == label) return;
            specialtyEntry.Set(label);
        }

        /// <summary>
        /// Slider preview: keeps the value in memory only, so a drag can refresh
        /// numbers and EV every frame without touching the config file.
        /// </summary>
        internal static void SetJunkPercentLive(float value)
        {
            EnsureLive();
            liveJunk = ClampJunk(value);
            Invalidated();
        }

        internal static void SetHighPercentLive(float value)
        {
            EnsureLive();
            liveHigh = ClampHigh(value);
            Invalidated();
        }

        /// <summary>补给箱概率滑条预览（0–5%，内存态）。</summary>
        internal static void SetCratePercentLive(float value)
        {
            EnsureLive();
            liveCrate = ClampSpecialPercent(value);
            Invalidated();
        }

        /// <summary>钥匙卡概率滑条预览（0–5%，内存态）。</summary>
        internal static void SetKeycardPercentLive(float value)
        {
            EnsureLive();
            liveCard = ClampSpecialPercent(value);
            Invalidated();
        }

        /// <summary>小奖概率滑条预览（0–10%，内存态）。</summary>
        internal static void SetMinorPercentLive(float value)
        {
            EnsureLive();
            liveMinor = ClampMinorPercent(value);
            Invalidated();
        }

        /// <summary>小奖整箱价值滑条预览（50–400，内存态）。</summary>
        internal static void SetMinorValueLive(float value)
        {
            EnsureLive();
            liveMinorValue = ClampMinorValue(value);
            Invalidated();
        }

        private static void EnsureLive()
        {
            if (hasLive) return;
            hasLive = true;
            liveJunk = EntryJunk();
            liveHigh = EntryHigh();
            liveCrate = EntryCratePercent();
            liveCard = EntryKeycardPercent();
            liveMinor = EntryMinorPercent();
            liveMinorValue = EntryMinorValue();
            liveGroundWidth = EntryGroundWidth();
            liveGroundHeight = EntryGroundHeight();
        }

        /// <summary>
        /// Writes the dragged value through ConfigEntry.Set once, which saves the
        /// config file; called on pointer-release, panel close and preset clicks.
        /// </summary>
        internal static void CommitLive()
        {
            if (!hasLive) return;
            float junk = liveJunk;
            float high = liveHigh;
            float crate = liveCrate;
            float card = liveCard;
            float minor = liveMinor;
            float minorValue = liveMinorValue;
            int groundWidth = liveGroundWidth;
            int groundHeight = liveGroundHeight;
            hasLive = false;
            if (junkEntry != null && junkEntry.Value != junk) junkEntry.Set(junk);
            if (highEntry != null && highEntry.Value != high) highEntry.Set(high);
            if (crateEntry != null && crateEntry.Value != crate) crateEntry.Set(crate);
            if (cardEntry != null && cardEntry.Value != card) cardEntry.Set(card);
            if (minorEntry != null && minorEntry.Value != minor) minorEntry.Set(minor);
            if (minorValueEntry != null && minorValueEntry.Value != minorValue)
                minorValueEntry.Set(minorValue);
            if (groundWidthEntry != null && groundWidthEntry.Value != groundWidth)
                groundWidthEntry.Set(groundWidth);
            if (groundHeightEntry != null && groundHeightEntry.Value != groundHeight)
                groundHeightEntry.Set(groundHeight);
            Invalidated();
        }

        internal static void ResetDefaults()
        {
            hasLive = false;
            SetJunkPercent(DefaultJunkPercent);
            SetHighPercent(DefaultHighPercent);
            SetSpecialtyTier(DefaultSpecialtyTier);
        }

        internal static void ApplyPreset(int preset)
        {
            // 3 / 4 只改「小奖整箱价值」，不动垃圾、贵重品与专精。
            if (preset == 3 || preset == 4)
            {
                hasLive = false;
                if (minorValueEntry != null)
                    minorValueEntry.Set(preset == 3 ? DefaultMinorValue : 100f);
                return;
            }
            float junk = DefaultJunkPercent;
            float high = DefaultHighPercent;
            int tier = DefaultSpecialtyTier;
            switch (preset)
            {
                case 1: // every area drops only its own specialties
                    tier = SpecialtyLabels.Length - 1;
                    break;
                case 2: // gamble on valuable finds
                    high = 8f;
                    break;
            }
            hasLive = false;
            SetJunkPercent(junk);
            SetHighPercent(high);
            SetSpecialtyTier(tier);
        }

        internal static EffectiveTable TableFor(Region region)
        {
            Init();
            bool perk80 = Reputation.Points >= Reputation.Perk80Threshold;
            if (perk80 != cachePerk80)
            {
                cachePerk80 = perk80;
                cache.Clear();
            }
            EffectiveTable table;
            if (cache.TryGetValue(region.Id, out table)) return table;
            // 80 档门控：声望不到 80 时贵重品一律按 0 算，滑条上的值只保留、不生效。
            float highPercent = perk80 ? HighPercent : 0f;
            table = Build(region, JunkPercent, highPercent, SpecialtyPercent, perk80);
            cache[region.Id] = table;
            return table;
        }

        /// <summary>
        /// Rolls one item for the area, honouring the vanilla junk event.
        /// </summary>
        internal static string Pick(Region region, float junkScale)
        {
            EffectiveTable table = TableFor(region);
            int index = table.PickIndex(junkScale);
            if (index < 0) return null;
            return table.Items[index];
        }

        private static EffectiveTable Build(Region region, float junkPercent,
            float highPercent, float specialtyPercent, bool perk80)
        {
            RegionLootTable theme = region.Loot;
            RegionLootTable vanilla = ScavLoot.Vanilla;
            float specialty = Mathf.Clamp(specialtyPercent, 0f, 100f) / 100f;

            // 1) 两张表各自归一化：非垃圾权重和记成 1，垃圾单独记成
            //    「每 1 份非垃圾权重对应多少垃圾权重」。专精就是这两张表
            //    之间的线性插值，垃圾也一起插。
            float vanillaNonJunk = 0f;
            for (int i = 0; i < vanilla.Count; ++i)
                if (vanilla.Items[i] != "junk") vanillaNonJunk += vanilla.Weights[i];
            float themeNonJunk = 0f;
            for (int i = 0; i < theme.Count; ++i)
                if (theme.Items[i] != "junk") themeNonJunk += theme.Weights[i];
            if (vanillaNonJunk <= 0f) vanillaNonJunk = 1f;
            if (themeNonJunk <= 0f) themeNonJunk = 1f;

            // 两张表的并集：主题表在前，原版表独有、主题表没有的物品补在后面。
            List<string> idList = new List<string>(theme.Items);
            for (int i = 0; i < vanilla.Count; ++i)
                if (theme.IndexOf(vanilla.Items[i]) < 0) idList.Add(vanilla.Items[i]);

            int count = idList.Count;
            string[] ids = idList.ToArray();
            int junkIndex = Array.IndexOf(ids, "junk");
            float[] weights = new float[count];
            float otherTotal = 0f;
            for (int i = 0; i < count; ++i)
            {
                if (i == junkIndex) continue;
                int themeIndex = theme.IndexOf(ids[i]);
                int vanillaIndex = vanilla.IndexOf(ids[i]);
                float themeWeight = themeIndex >= 0 ? theme.Weights[themeIndex] / themeNonJunk : 0f;
                float vanillaWeight = vanillaIndex >= 0 ? vanilla.Weights[vanillaIndex] / vanillaNonJunk : 0f;
                float weight = (1f - specialty) * vanillaWeight + specialty * themeWeight;
                weights[i] = weight;
                otherTotal += weight;
            }

            // 2) 垃圾概率 = 插值后的垃圾占比 × 垃圾倍率。垃圾倍率是唯一能
            //    单独调垃圾多少的旋钮，专精插值不会把它清零。
            float vanillaJunkShare = vanilla.JunkWeight / vanillaNonJunk;
            float themeJunkShare = theme.JunkWeight / themeNonJunk;
            float junkPerNonJunk = (1f - specialty) * vanillaJunkShare + specialty * themeJunkShare;
            float junkBase = junkPerNonJunk / (junkPerNonJunk + 1f);
            float junkProb = Mathf.Clamp(junkBase * junkPercent / 100f, 0f, 0.995f);

            // 3) The valuable share and the 80-tier crate/keycard shares are
            //    carved out of the non-junk items only.
            float highChance = Mathf.Clamp(highPercent, 0f, 30f) / 100f;
            string crateId = perk80 ? CrateFor(region.Id) : null;
            string cardId = perk80 ? CardFor(region.Id) : null;
            bool withCrate = crateId != null && CratePercent > 0f;
            bool withCard = cardId != null && KeycardPercent > 0f;
            float crateChance = withCrate ? ClampSpecialPercent(CratePercent) / 100f : 0f;
            float cardChance = withCard ? ClampSpecialPercent(KeycardPercent) / 100f : 0f;
            float reserved = highChance + crateChance + cardChance;
            if (reserved > 0.995f - junkProb)
            {
                float scale = reserved > 0f ? Mathf.Max(0f, 0.995f - junkProb) / reserved : 0f;
                highChance *= scale;
                crateChance *= scale;
                cardChance *= scale;
                reserved = highChance + crateChance + cardChance;
            }
            if (otherTotal <= 0f) junkProb = 1f - reserved;
            float normalProb = Mathf.Max(0f, 1f - junkProb - reserved);

            bool withHigh = highChance > 0f;
            int highCount = withHigh ? HighValue.Length : 0;
            int specialCount = (withCrate ? 1 : 0) + (withCard ? 1 : 0);

            string[] items = new string[count + highCount + specialCount];
            float[] probs = new float[count + highCount + specialCount];
            bool[] isHigh = new bool[count + highCount + specialCount];
            bool[] isJunk = new bool[count + highCount + specialCount];

            for (int i = 0; i < count; ++i)
            {
                items[i] = ids[i];
                if (i == junkIndex) probs[i] = junkProb;
                else probs[i] = otherTotal > 0f ? normalProb * (weights[i] / otherTotal) : 0f;
                isJunk[i] = i == junkIndex;
            }

            if (withHigh)
            {
                int[] highWeights = HighWeights[RegionIndex(region)];
                float highTotal = 0f;
                for (int j = 0; j < HighValue.Length; ++j) highTotal += highWeights[j];
                if (highTotal <= 0f) highTotal = 1f;
                for (int j = 0; j < HighValue.Length; ++j)
                {
                    int slot = count + j;
                    items[slot] = HighValue[j];
                    probs[slot] = highChance * highWeights[j] / highTotal;
                    isHigh[slot] = true;
                }
            }

            int slotNext = count + highCount;
            if (withCrate)
            {
                items[slotNext] = crateId;
                probs[slotNext] = crateChance;
                ++slotNext;
            }
            if (withCard)
            {
                items[slotNext] = cardId;
                probs[slotNext] = cardChance;
            }

            // 4) Expected value of one roll.
            float expected = 0f;
            for (int i = 0; i < items.Length; ++i)
                expected += probs[i] * ValueOf(items[i]);

            float finalJunk = junkIndex >= 0 ? probs[junkIndex] : 0f;
            return new EffectiveTable(items, probs, isHigh, isJunk, highChance, finalJunk, expected,
                count, highCount);
        }

        private static int RegionIndex(Region region)
        {
            for (int i = 0; i < Region.All.Length; ++i)
                if (Region.All[i].Id == region.Id) return i;
            return 0;
        }

        /// <summary>
        /// 80 档：该区对应的满箱上锁补给箱。入口空地不给箱子。
        /// 用 PreBuiltItemHelper 的 loot_crate_* id，ItemSpawner 会自动走预构建满箱。
        /// </summary>
        internal static string CrateFor(string regionId)
        {
            switch (regionId)
            {
                case "scrap": return "loot_crate_engineering";
                case "refuse": return "loot_crate_service";
                case "med": return "loot_crate_medical";
                case "weapons": return "loot_crate_security";
                default: return null;
            }
        }

        /// <summary>80 档：与箱子同区出现的对应钥匙卡。</summary>
        internal static string CardFor(string regionId)
        {
            switch (regionId)
            {
                case "scrap": return "eng_keycard";
                case "refuse": return "ser_keycard";
                case "med": return "med_keycard";
                case "weapons": return "sec_keycard";
                default: return null;
            }
        }

        internal static float ValueOf(string itemId)
        {
            float value;
            return UnitValues.TryGetValue(itemId, out value) ? value : 0f;
        }

        /// <summary>
        /// 贵重品池：原版没有单独的索引，这里是本模组自己拼的一池稀有物品，
        /// 分四类——芯片类（打印枪、弹匣、近战的打印芯片）、贵重枪支、
        /// 贵重刀具 / 近战、枪匠需要的高价材料与工具，另有原来那 7 件。
        /// 与游戏自带的「奢侈品 / LUXURY_ITEM」类别不是一回事：那一类是
        /// 买卖与展览用的奢侈品标签，本池只是拾荒时按概率抽取的物品清单。
        /// 它们不进基础表，只有贵重品出现率大于 0 且声望达到 80 时才进最终表。
        /// </summary>
        private static readonly string[] HighValue =
        {
            // 原有 7 件
            "blue_blood_bag", "water_filter_adv",
            "portable_water_purifier", "bottled_water_premium", "black_injector",
            "rare_electronic", "module_extractor_advanced",
            // 芯片类
            "printer_chip_pistol_double", "printer_chip_heavy_handmade_pistol_frame",
            "printer_chip_combat_machete", "printer_chip_gun_part", "printer_chip_10_mag_ext",
            // 贵重枪支
            "revolver", "stun_gun", "smg", "shotgun",
            // 贵重刀具 / 近战
            "combat_machete", "stun_baton",
            // 枪匠需要的高价材料与工具（X 光扫描仪只注册、游戏内无任何获取途径，0.3.72 移除）
            "advanced_flux_agent",
            // 0.3.65 起：正品免疫宁（160）与几件 100–200 的「普货」。
            // 同一 id 仍留在普通表里当「大货」，贵重品这边是另一条独立槽位。
            "large_purple_injector", "glock_receiver", "heavy_handmade_pistol",
            "surgery_tool", "module_extractor",
            // 0.3.66 起：正品海明威香烟、酵母生物反应器（酿酒线的高价件）。
            "packet_red_cigarette", "wine_yeast_infinite"
        };

        /// <summary>正品免疫宁：贵重品槽位固定给这一件，次品走 InjectorBadId。</summary>
        internal const string InjectorId = "large_purple_injector";

        /// <summary>普通表里的免疫宁次品（过期 / 仿品 / 不能用），生成时再掷具体状态。</summary>
        internal const string InjectorBadId = "large_purple_injector_bad";

        /// <summary>正品海明威香烟（贵重品槽位）。</summary>
        internal const string CigaretteId = "packet_red_cigarette";

        /// <summary>普通表里的假海明威香烟。</summary>
        internal const string CigaretteFakeId = "packet_red_cigarette_fake";

        /// <summary>普通表里的仿品食品券。</summary>
        internal const string StampFakeId = "food_stamp_fake";

        /// <summary>小瓶酒精：不是目录 id，要调 PreBuildChemHelper 现做。</summary>
        internal const string MiniAlcoholId = "mini_alcohol_bottle";

        /// <summary>贵重品池（按展示顺序）；告示牌用它拼「贵重品」分组。</summary>
        internal static string[] HighValueIds { get { return HighValue; } }

        internal static bool IsHighValue(string itemId)
        {
            return Array.IndexOf(HighValue, itemId) >= 0;
        }

        /// <summary>
        /// Per-area valuable mix, in Region.All order (gate, scrap, refuse,
        /// medical, weapons). 每个区都能掉到池里的全部四类，只是权重偏向本区主题。
        /// </summary>
        private static readonly int[][] HighWeights =
        {
            new[] { 2, 2, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1 }, // Main Gate
            new[] { 1, 1, 1, 1, 1, 3, 4, 2, 2, 2, 3, 2, 1, 1, 1, 1, 3, 3, 2, 1, 1, 1, 1, 3, 1, 2 }, // Scrap Pile
            new[] { 3, 3, 3, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 2, 1, 1, 1, 1, 3, 1 }, // Refuse Lane
            new[] { 4, 1, 1, 1, 5, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 3, 2, 1, 4, 1, 1, 4, 1, 1, 1 }, // Medical Waste
            new[] { 1, 2, 2, 2, 3, 1, 3, 2, 2, 3, 2, 3, 3, 3, 2, 2, 4, 3, 2, 1, 3, 3, 1, 1, 1, 1 }  // Weapon Dump
        };

        /// <summary>
        /// Same figures as the balance document (GameItem.unitValue, boxes folded
        /// into their contents, random modules counted at 70).
        /// </summary>
        private static readonly Dictionary<string, float> UnitValues =
            new Dictionary<string, float>
            {
                { "junk", 3f },
                { "glass_shard", 2f },
                { "system_module_ruined", 30f },
                { "pipe_weapon", 15f },
                { "empty_beer_bottle", 5f },
                { "scratcher", 20f },
                { "scrap_metal", 10f },
                { "nuts_metal", 14f },
                { "flux_agent", 4f },
                { "printer_plastic", 12f },
                { "wire", 16f },
                { "gun_part", 25f },
                { "common_electronic", 33f },
                { "kotton_fabric", 65f },
                { "random_performance_module", 70f },
                { "random_efficiency_module", 70f },
                { "random_quality_module", 70f },
                { "system_module_performance", 70f },
                { "system_module_eco", 100f },
                { "system_module_fineness", 100f },
                { "screwdriver", 25f },
                { "wire_cutter", 40f },
                { "module_extractor", 100f },
                { "welder", 80f },
                { "turbo_booster", 35f },
                { "crowbar", 75f },
                { "caffeine_pill", 10f },
                { "toothpaste", 15f },
                { "water_filter", 25f },
                { "shampoo", 30f },
                { "paper_towel", 35f },
                { "toilet_paper", 25f },
                { "full_water_test_strip_box", 31f },
                { "full_water_purification_tablet_box", 55f },
                { "glass_shard_shiv", 8f },
                { "box_cutter", 28f },
                { "kitchen_knife", 45f },
                { "galaxy_blend", 25f },
                { "energy_drink", 18f },
                { "soda_red", 15f },
                { "cat_bar", 15f },
                { "li_eat_snackbar", 17f },
                { "red_beer", 18f },
                { "processed_milk", 16f },
                { "processed_juice", 14f },
                { "processed_meat", 25f },
                { "zerochew", 8f },
                { "cup_noodle", 20f },
                { "nudka", 85f },
                { "beis_icecream", 40f },
                { "bandage_item", 8f },
                { "hemostatic_bandage_item", 16f },
                { "topical_bandage_item", 16f },
                { "blood_bag", 33f },
                { "rubbing_alcohol", 35f },
                { "salve", 80f },
                { "surgery_tool", 200f },
                { "birth_control_pill", 15f },
                { "fanta_pill", 12f },
                { "phagimycin_pill", 25f },
                { "zyanide_pill", 33f },
                { "smelling_salt", 15f },
                { "dream_dust", 18f },
                { "nightmare_dust", 36f },
                { "common_medical", 40f },
                { "metal_ingot", 50f },
                { "system_module_corrupt", 45f },
                { "blank_module", 60f },
                { "combat_knife", 75f },
                { "kitchen_cleaver", 65f },
                { "stun_baton", 165f },
                { "hatchet", 55f },
                { "skinning_knife", 100f },
                { "combat_machete", 185f },
                { "printed_gun", 40f },
                { "handmade_pistol", 120f },
                { "heavy_handmade_pistol", 160f },
                { "glock_receiver", 200f },
                { "revolver", 240f },
                { "stun_gun", 250f },
                { "smg", 280f },
                { "shotgun", 310f },
                { "small_pistol_ammo", 20f },
                { "small_pistol_ammo_p", 26f },
                { "small_pistol_ammo_nl", 14f },
                { "heavy_pistol_ammo", 30f },
                { "heavy_pistol_ammo_p", 36f },
                { "heavy_pistol_ammo_nl", 24f },
                { "lou_etched_round", 30f },
                { "rds", 60f },
                { "rds_makeshift", 25f },
                { "silencer", 55f },
                { "silencer2", 90f },
                { "silencer_makeshift", 15f },
                { "barrel", 60f },
                { "compensator", 50f },
                { "mag_10mm", 30f },
                { "mag_10mm_high", 40f },
                { "mag_10mm_ext", 50f },
                { "mag_22", 15f },
                { "mag_22_high", 25f },
                { "mag_22_ext", 35f },
                { "mag_10mm_smg", 55f },
                { "mag_10mm_smg_high", 70f },
                { "mag_10mm_smg_box", 85f },
                { "laser", 50f },
                { "laser2", 100f },
                { "laser_toy", 18f },
                { "bipod", 40f },
                { "gl", 150f },
                { "grip", 40f },
                { "stock", 40f },
                { "short_stock", 35f },
                { "internal_springs", 40f },
                { "internal_springs_improved", 20f },
                { "trigger_group", 50f },
                { "match_barrel", 75f },
                { "pistol_switch", 40f },
                { "ammo_display", 70f },
                { "grip_gel", 70f },
                { "blue_blood_bag", 250f },
                { "water_filter_adv", 350f },
                { "portable_water_purifier", 325f },
                { "bottled_water_premium", 325f },
                { "black_injector", 350f },
                { "rare_electronic", 350f },
                { "module_extractor_advanced", 350f },
                // 贵重品池新增：芯片、贵重枪械、贵重近战、枪匠高价材料与工具。
                { "printer_chip_pistol_double", 400f },
                { "printer_chip_heavy_handmade_pistol_frame", 450f },
                { "printer_chip_combat_machete", 480f },
                { "printer_chip_gun_part", 300f },
                { "printer_chip_10_mag_ext", 400f },
                { "advanced_flux_agent", 300f },
                // 0.3.65：正品免疫宁 160；次品掷过期 80 / 仿品 40 / 不能用 16，按 40/35/25 取均值。
                { "large_purple_injector", 160f },
                { "large_purple_injector_bad", 50f },
                // 0.3.66：烟、酒瓶与酿酒原料；小瓶酒精按瓶 + 酒精液体估 25。
                { "packet_red_cigarette", 80f },
                { "packet_red_cigarette_fake", 40f },
                { "food_stamp_fake", 3f },
                { "mini_alcohol_bottle", 25f },
                { "wine_bottle", 12f },
                { "wine_berry", 14f },
                { "wine_yeast", 10f },
                { "wine_yeast_red", 8f },
                { "wine_superyeast", 20f },
                { "wine_yeast_infinite", 250f },
                // 80 档补给箱按满箱内容估的均价，只用于界面的期望价值。
                { "loot_crate_engineering", 450f },
                { "loot_crate_service", 350f },
                { "loot_crate_medical", 500f },
                { "loot_crate_security", 600f },
                { "eng_keycard", 60f },
                { "ser_keycard", 60f },
                { "med_keycard", 60f },
                { "sec_keycard", 60f }
            };

    }

    /// <summary>
    /// One area's runtime distribution: final probabilities plus the derived
    /// expected value. Rebuilt whenever a setting changes.
    /// </summary>
    internal sealed class EffectiveTable
    {
        internal readonly string[] Items;
        internal readonly float[] Probs;
        internal readonly bool[] IsHigh;
        internal readonly float HighChance;
        internal readonly float JunkChance;
        internal readonly float Ev;

        /// <summary>
        /// 贵重品在 Items / Probs 里的起始下标与件数。贵重品槽位可能和普通表里的
        /// id 重名（0.3.65 起 100–200 的几件两边都有），告示板靠这两个值区分。
        /// </summary>
        internal readonly int HighStart;
        internal readonly int HighCount;

        private readonly bool[] isJunk;
        private readonly int[] order;
        private readonly int junkIndex;

        internal EffectiveTable(string[] items, float[] probs, bool[] isHigh, bool[] isJunk,
            float highChance, float junkChance, float ev, int highStart, int highCount)
        {
            Items = items;
            Probs = probs;
            IsHigh = isHigh;
            this.isJunk = isJunk;
            HighChance = highChance;
            JunkChance = junkChance;
            Ev = ev;
            HighStart = highStart;
            HighCount = highCount;
            junkIndex = Array.IndexOf(items, "junk");

            order = new int[items.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b)
            {
                int compare = probs[b].CompareTo(probs[a]);
                return compare != 0 ? compare : string.CompareOrdinal(items[a], items[b]);
            });
        }

        internal int Count { get { return Items.Length; } }
        internal int IndexAt(int i) { return order[i]; }
        internal float ProbabilityAt(int i) { return Probs[order[i]]; }
        internal string ItemAt(int i) { return Items[order[i]]; }

        internal int PickIndex(float junkScale)
        {
            float sum = 0f;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                sum += i == junkIndex ? Probs[i] * junkScale : Probs[i];
            }
            if (sum <= 0f) return -1;

            float roll = UnityEngine.Random.value * sum;
            int last = -1;
            for (int i = 0; i < Items.Length; ++i)
            {
                if (Probs[i] <= 0f) continue;
                last = i;
                roll -= i == junkIndex ? Probs[i] * junkScale : Probs[i];
                if (roll < 0f) return i;
            }
            return last;
        }
    }
}
