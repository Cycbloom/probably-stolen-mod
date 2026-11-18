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
        internal static event Action Changed;
        internal const float DefaultJunkPercent = 100f;
        internal const float DefaultHighPercent = 3f;
        internal const int DefaultSpecialtyTier = 2;

        /// <summary>100 档：补给箱与钥匙卡的绝对概率，各自一个设置项（默认 1%、范围 0–5%）。</summary>
        internal const string CratePercentKey = "crate_percent";
        internal const string KeycardPercentKey = "keycard_percent";
        internal const float DefaultSpecialPercent = 1f;
        internal const float SpecialPercentMin = 0f;
        internal const float SpecialPercentMax = 5f;

        /// <summary>60 档小奖：每次拾荒独立掷一次，默认 3%、范围 0–10%、0.1 步进。</summary>
        internal const string MinorPercentKey = "minor_percent";
        internal const float DefaultMinorPercent = 3f;
        internal const float MinorPercentMin = 0f;
        internal const float MinorPercentMax = 10f;
        internal const string MinorWindowKey = "minor_window_enabled";
        internal const bool DefaultMinorWindow = true;

        /// <summary>小奖整箱的目标价值：件数按「目标值 ×[0.75,1.25] ÷ 单价」反推。</summary>
        internal const string MinorValueKey = "minor_value";
        internal const float DefaultMinorValue = 200f;
        internal const float MinorValueMin = 50f;
        internal const float MinorValueMax = 400f;
        internal const float MinorValueStep = 10f;

        internal const float JunkMin = 0f;
        internal const float JunkMax = 150f;
        internal const float HighMin = 0f;
        internal const float HighMax = 30f;

        /// <summary>60 档爆大奖的概率上限；声望不到 60 时实际生效值会被压到 0.1%。</summary>
        internal const string StashCapKey = "stash_cap";
        internal const float DefaultStashCap = 1.0f;
        internal const float StashCapMin = 0.1f;
        internal const float StashCapMax = 5.0f;
        internal const string StashWindowKey = "stash_window_enabled";
        internal const bool DefaultStashWindow = true;

        /// <summary>
        /// 其它模组加进原版拾荒组的物品。设置值是每次拾荒的上限份额，实际
        /// 取它与「这些条目在原版组里的自然占比」的较小值；份额优先从非专精
        /// 部分（垃圾 + 普通物品的原版分支）扣，不够才动专精物品。
        /// </summary>
        internal const string ForeignEnabledKey = "foreign_loot_enabled";
        internal const bool DefaultForeignEnabled = true;
        internal const string ForeignPercentKey = "foreign_loot_percent";
        internal const float DefaultForeignPercent = 5f;
        internal const float ForeignPercentMin = 0f;
        internal const float ForeignPercentMax = 8f;
        /// <summary>顺带纳入本模组表没收录的原版物品（排查遗漏用）。</summary>
        internal const string ForeignVanillaKey = "foreign_loot_vanilla";
        internal const bool DefaultForeignVanilla = false;

        private const string SectionName = "dgm_settings_section_loot";
        private const string IntegrationSection = "dgm_settings_section_integration";
        private const string DebugSection = "dgm_settings_section_debug";

        /// <summary>声望飘字开关：只在装了 Reputation Pop 时才有实际效果。</summary>
        internal const string ReputationPopKey = "reputation_pop";
        internal const bool DefaultReputationPop = true;

        /// <summary>调试项。默认关闭；关闭时「设置声望点数」不生效、界面里也会被隐藏。</summary>
        internal const string DebugEnabledKey = "debug_enabled";
        internal const string DebugPointsKey = "debug_points";
        internal const float DebugPointsMin = 0f;
        internal const float DebugPointsMax = 200f;

        /// <summary>性能日志。默认关闭；关闭时调用点只有一次布尔判断。</summary>
        private const string PerfSection = "dgm_settings_section_performance";
        internal const string PerfLogKey = "perf_log";
        internal const bool DefaultPerfLog = false;

        /// <summary>
        /// 拾荒地面：只放大拾荒时整理战利品的那块「地面」。
        /// 12×9 是原版；默认 19×21，宽 12–19、高 9–31。
        /// 尺寸不进存档，改完下次重建地面时生效。
        /// </summary>
        internal const string GroundSection = "dgm_settings_section_ground";
        internal static bool IsSectionKey(string key)
        {
            return key == SectionName || key == IntegrationSection || key == DebugSection
                || key == PerfSection || key == GroundSection;
        }
        internal const string GroundWidthKey = "ground_width";
        internal const string GroundHeightKey = "ground_height";
        internal const int DefaultGroundWidth = 19;
        internal const int DefaultGroundHeight = 21;
        internal const int GroundWidthMin = 12;
        internal const int GroundWidthMax = 19;
        internal const int GroundHeightMin = 9;
        internal const int GroundHeightMax = 31;

        /// <summary>
        /// 地面窗口贴哪个位置：九宫格，0 左上 / 1 上中 / 2 右上 / 3 左中 / 4 居中 /
        /// 5 右中 / 6 左下 / 7 下中 / 8 右下；默认左下。
        /// 只在拾荒场景摆放，商店界面不碰。
        /// </summary>
        internal const string GroundAlignKey = "ground_align";
        internal const int DefaultGroundAlign = 6;
        // Stable legacy config values; only dropdown display text is localized.
        private static readonly string[] GroundAlignValues =
            { "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下" };
        private static readonly string[] LegacyGroundAlignEnglish =
            { "Top left", "Top middle", "Top right", "Middle left", "Center", "Middle right",
              "Bottom left", "Bottom middle", "Bottom right" };


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
        private static ConfigEntry<bool> minorWindowEntry;
        private static ConfigEntry<float> minorValueEntry;
        private static ConfigEntry<float> stashCapEntry;
        private static ConfigEntry<bool> stashWindowEntry;
        private static ConfigEntry<bool> foreignEnabledEntry;
        private static ConfigEntry<float> foreignPercentEntry;
        private static ConfigEntry<bool> foreignVanillaEntry;
        private static ConfigEntry<string> specialtyEntry;
        private static ConfigEntry<bool> reputationPopEntry;
        private static ConfigEntry<bool> debugEnabledEntry;
        private static ConfigEntry<bool> perfLogEntry;
        private static ConfigEntry<float> debugPointsEntry;
        private static ConfigEntry<int> groundWidthEntry;
        private static ConfigEntry<int> groundHeightEntry;
        private static ConfigEntry<string> groundAlignEntry;
        private static int liveGroundWidth;
        private static int liveGroundHeight;
        private static bool debugIgnoredLogged;
        private static readonly LootTableCache cache = new LootTableCache();
        private static readonly Dictionary<string, float> baseEvCache =
            new Dictionary<string, float>();
        private static bool hasLive;
        private static float liveJunk;
        private static float liveHigh;
        private static float liveCrate;
        private static float liveCard;
        private static float liveMinor;
        private static float liveMinorValue;
        private static float liveForeign;
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

        private static bool EntryForeignEnabled()
        {
            return foreignEnabledEntry == null ? DefaultForeignEnabled : foreignEnabledEntry.Value;
        }

        /// <summary>份额上限（%），夹在 0–8 并吸附到 0.1。</summary>
        private static float EntryForeignPercent()
        {
            if (foreignPercentEntry == null) return DefaultForeignPercent;
            float value = foreignPercentEntry.Value;
            if (float.IsNaN(value)) return DefaultForeignPercent;
            return Mathf.Round(Mathf.Clamp(value, ForeignPercentMin, ForeignPercentMax) * 10f) / 10f;
        }

        private static bool EntryForeignVanilla()
        {
            return foreignVanillaEntry == null ? DefaultForeignVanilla : foreignVanillaEntry.Value;
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

        /// <summary>下拉框的九个选项，按当前语言取中 / 英。</summary>
        internal static string[] GroundAlignLabels
        {
            get
            {
                string[] labels = new string[GroundAlignValues.Length];
                for (int i = 0; i < labels.Length; ++i) labels[i] = Copy.Get("ground_align_" + i);
                return labels;
            }
        }
        internal static string[] SpecialtyDisplayLabels
        {
            get
            {
                string[] labels = new string[SpecialtyLabels.Length];
                for (int i = 0; i < labels.Length; ++i) labels[i] = Copy.Get("specialty_tier_" + i);
                return labels;
            }
        }

        /// <summary>地面贴边位置（0–8）；配置项缺失或值对不上时按默认左下。</summary>
        internal static int GroundAlign
        {
            get
            {
                ConfigEntry<string> entry = groundAlignEntry;
                if (entry == null) return DefaultGroundAlign;
                int index = GroundAlignIndex(entry.Value);
                return index < 0 ? DefaultGroundAlign : index;
            }
        }

        internal static int GroundAlignIndex(string value)
        {
            int index = Array.IndexOf(GroundAlignValues, value);
            return index < 0 ? Array.IndexOf(LegacyGroundAlignEnglish, value) : index;
        }

        /// <summary>下拉框换档：立即写进配置项，下次进拾荒场景按新位置摆放。</summary>
        internal static void SetGroundAlignLive(int index)
        {
            ConfigEntry<string> entry = groundAlignEntry;
            if (entry == null) return;
            string[] labels = GroundAlignValues;
            string value = labels[Mathf.Clamp(index, 0, labels.Length - 1)];
            if (entry.Value == value) return;
            entry.Set(value);
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

        /// <summary>性能日志开关：关闭时所有统计点只做一次布尔判断。</summary>
        internal static bool PerfLogEnabled
        {
            get { return perfLogEntry != null && perfLogEntry.Value; }
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

        /// <summary>100 档补给箱概率（%，0–5，0.1 步进）。</summary>
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

        /// <summary>其它模组注入原版拾荒组的物品是否参与掉落。</summary>
        internal static bool ForeignEnabled
        {
            get { return EntryForeignEnabled(); }
        }

        /// <summary>
        /// 外来条目的份额上限（%）。实际份额取它与「这些条目在原版组里的自然
        /// 占比」的较小值；关闭时返回 0。
        /// </summary>
        internal static float ForeignPercent
        {
            get { return ForeignEnabled ? (hasLive ? liveForeign : EntryForeignPercent()) : 0f; }
        }

        /// <summary>是否把本模组表未收录的原版物品也当外来条目（默认关）。</summary>
        internal static bool ForeignVanilla
        {
            get { return EntryForeignVanilla(); }
        }

        /// <summary>小奖实际生效概率上限（%）；窗口当前概率由StashFind读取。</summary>
        internal static float EffectiveMinorPercent
        {
            get
            {
                float percent = MinorPercent;
                percent *= PerkCatalog.MinorMultiplier(Reputation.Points);
                return percent;
            }
        }

        internal static bool MinorWindowEnabled
        {
            get { return minorWindowEntry == null ? DefaultMinorWindow : minorWindowEntry.Value; }
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

        /// <summary>按 0.1 步进吸附并夹在 0–8。</summary>
        internal static float ClampForeignPercent(float value)
        {
            if (float.IsNaN(value)) return DefaultForeignPercent;
            value = Mathf.Round(Mathf.Clamp(value, ForeignPercentMin, ForeignPercentMax) * 10f) / 10f;
            return Mathf.Clamp(value, ForeignPercentMin, ForeignPercentMax);
        }

        internal static float StashCap
        {
            get
            {
                if (stashCapEntry == null) return DefaultStashCap;
                return ClampStashCap(stashCapEntry.Value);
            }
        }

        internal static bool StashWindowEnabled
        {
            get { return stashWindowEntry == null ? DefaultStashWindow : stashWindowEntry.Value; }
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
            RegionLootTable original = ScavLoot.Vanilla;
            ev = 0f;
            for (int i = 0; i < original.Count; ++i)
                ev += original.Weights[i] / (float)original.Total * LootCatalog.ValueOf(original.Items[i]);
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
                    delegate { return Copy.Get("tuning_junk_chance_multiplier_0_150"); },
                    JunkMin, JunkMax,
                    delegate
                    {
                        return Copy.Get("tuning_range_0_150_default_100_100_gives");
                    },
                    false);

                highEntry = config.Define("high_percent", DefaultHighPercent, SectionName,
                    delegate { return Copy.Get("tuning_valuable_find_chance_0_30"); },
                    0f, 30f,
                    delegate
                    {
                        return Copy.Get("tuning_rolled_once_per_search_it_decides_only");
                    },
                    false);

                crateEntry = config.Define(CratePercentKey, DefaultSpecialPercent, SectionName,
                    delegate { return Copy.Get("tuning_supply_crate_chance_0_5"); },
                    SpecialPercentMin, SpecialPercentMax,
                    delegate
                    {
                        return Copy.Get("tuning_total_supply_crate_chance_per_main_roll");
                    },
                    false);

                cardEntry = config.Define(KeycardPercentKey, DefaultSpecialPercent, SectionName,
                    delegate { return Copy.Get("tuning_keycard_chance_0_5"); },
                    SpecialPercentMin, SpecialPercentMax,
                    delegate
                    {
                        return Copy.Get("tuning_total_keycard_chance_per_main_roll_active");
                    },
                    false);

                minorEntry = config.Define(MinorPercentKey, DefaultMinorPercent, SectionName,
                    delegate { return Copy.Get("tuning_minor_prize_chance_cap_0_10"); },
                    MinorPercentMin, MinorPercentMax,
                    delegate
                    {
                        return Copy.Get("tuning_minor_prize_window_cap_default_3_range");
                    },
                    false);

                minorWindowEntry = config.Define(MinorWindowKey, DefaultMinorWindow, SectionName,
                    delegate { return Copy.Get("tuning_use_an_adaptive_minor_prize_window_per"); },
                    delegate
                    {
                        return Copy.Get("tuning_on_starts_at_0_3_with_the");
                    }, false);
                minorWindowEntry.OnChanged += delegate(bool value) { Invalidated(); };

                minorValueEntry = config.Define(MinorValueKey, DefaultMinorValue, SectionName,
                    delegate { return Copy.Get("tuning_minor_prize_crate_value"); },
                    MinorValueMin, MinorValueMax,
                    delegate
                    {
                        return Copy.Get("tuning_sets_how_much_a_minor_prize_crate");
                    },
                    false);

                stashWindowEntry = config.Define(StashWindowKey, DefaultStashWindow, SectionName,
                    delegate { return Copy.Get("tuning_use_an_adaptive_jackpot_window_daily"); },
                    delegate
                    {
                        return Copy.Get("tuning_on_odds_stay_fixed_throughout_the_day");
                    }, false);

                stashCapEntry = config.Define(StashCapKey, DefaultStashCap, SectionName,
                    delegate { return Copy.Get("tuning_jackpot_chance_cap_0_1_5_0"); },
                    StashCapMin, StashCapMax,
                    delegate
                    {
                        return Copy.Get("tuning_jackpot_chance_cap_with_the_adaptive_window");
                    },
                    false);

                foreignEnabledEntry = config.Define(ForeignEnabledKey, DefaultForeignEnabled, SectionName,
                    delegate { return Copy.Get("tuning_allow_other_mods_scavenge_items"); },
                    delegate
                    {
                        return Copy.Get("tuning_this_mod_takes_over_scavenging_per_region");
                    },
                    false);

                foreignPercentEntry = config.Define(ForeignPercentKey, DefaultForeignPercent, SectionName,
                    delegate { return Copy.Get("tuning_foreign_item_share_cap_0_8"); },
                    ForeignPercentMin, ForeignPercentMax,
                    delegate
                    {
                        return Copy.Get("tuning_upper_bound_on_how_much_of_one");
                    },
                    false);

                foreignVanillaEntry = config.Define(ForeignVanillaKey, DefaultForeignVanilla, SectionName,
                    delegate { return Copy.Get("tuning_also_include_vanilla_items_this_mod_omits"); },
                    delegate
                    {
                        return Copy.Get("tuning_off_by_default_only_entries_added_by");
                    },
                    false);

                specialtyEntry = config.Define(SpecialtyKey, SpecialtyLabels[DefaultSpecialtyTier],
                    SectionName,
                    delegate { return Copy.Get("tuning_region_specialty"); },
                    SpecialtyLabels,
                    delegate
                    {
                        return Copy.Get("tuning_controls_how_strongly_junk_aside_a_haul");
                    },
                    false);

                MigrateSpecialtyTier(config);

                groundAlignEntry = config.Define(GroundAlignKey, GroundAlignValues[DefaultGroundAlign],
                    GroundSection,
                    delegate { return Copy.Get("tuning_ground_alignment"); },
                    GroundAlignValues,
                    delegate
                    {
                        return Copy.Get("tuning_where_the_ground_window_sits_while_scavenging");
                    },
                    false);

                string savedAlignment;
                if (TryReadRaw(config, GroundAlignKey, out savedAlignment))
                {
                    int savedIndex = GroundAlignIndex(savedAlignment);
                    if (savedIndex >= 0) groundAlignEntry.Set(GroundAlignValues[savedIndex]);
                }

                groundWidthEntry = config.Define(GroundWidthKey, DefaultGroundWidth, GroundSection,
                    delegate { return Copy.Get("tuning_ground_width_cells_12_19"); },
                    GroundWidthMin, GroundWidthMax,
                    delegate
                    {
                        return Copy.Get("tuning_only_affects_the_ground_where_scavenging_loot_2");
                    },
                    false);

                groundHeightEntry = config.Define(GroundHeightKey, DefaultGroundHeight, GroundSection,
                    delegate { return Copy.Get("tuning_ground_height_cells_9_31"); },
                    GroundHeightMin, GroundHeightMax,
                    delegate
                    {
                        return Copy.Get("tuning_only_affects_the_ground_where_scavenging_loot");
                    },
                    false);

                reputationPopEntry = config.Define(ReputationPopKey, DefaultReputationPop,
                    IntegrationSection,
                    delegate
                    {
                        return ReputationPopBridge.Running
                            ? Copy.Get("tuning_reputation_pop_ups_requires_reputation_pop")
                            : Copy.Get("tuning_reputation_pop_ups_requires_reputation_pop_not");
                    },
                    delegate
                    {
                        return Copy.Get("tuning_after_each_scavenge_pops_this_haul_s");
                    },
                    false);

                reputationPopEntry.OnChanged += delegate(bool value)
                {
                    Debug.Log("[DumpingGroundMap] reputation pop: setting=" + (value ? "on" : "off"));
                };

                debugEnabledEntry = config.Define(DebugEnabledKey, false, DebugSection,
                    delegate { return Copy.Get("tuning_enable_debug_options"); },
                    delegate
                    {
                        return Copy.Get("tuning_turns_on_the_reputation_setter_below_testing");
                    },
                    false);

                debugEnabledEntry.OnChanged += delegate(bool value)
                {
                    if (!value) Reputation.ClearPendingDebugPoints();
                    Debug.Log("[DumpingGroundMap] debug: options " + (value ? "on" : "off"));
                };

                perfLogEntry = config.Define(PerfLogKey, DefaultPerfLog, PerfSection,
                    delegate { return Copy.Get("tuning_performance_log"); },
                    delegate
                    {
                        return Copy.Get("tuning_writes_timing_lines_for_loot_table_builds");
                    },
                    false);

                debugPointsEntry = config.Define(DebugPointsKey, 0f, DebugSection,
                    delegate { return Copy.Get("tuning_set_reputation_points"); },
                    DebugPointsMin, DebugPointsMax,
                    delegate
                    {
                        return Copy.Get("tuning_testing_only_overwrites_the_reputation_stored_in");
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
                stashWindowEntry.OnChanged += delegate(bool value) { Invalidated(); };
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
            Action changed = Changed;
            if (changed != null) changed();
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

        /// <summary>外来条目份额上限滑条预览（0–8%，内存态）。</summary>
        internal static void SetForeignPercentLive(float value)
        {
            EnsureLive();
            liveForeign = ClampForeignPercent(value);
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
            liveForeign = EntryForeignPercent();
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
            float foreign = liveForeign;
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
            if (foreignPercentEntry != null && foreignPercentEntry.Value != foreign)
                foreignPercentEntry.Set(foreign);
            if (groundWidthEntry != null && groundWidthEntry.Value != groundWidth)
                groundWidthEntry.Set(groundWidth);
            if (groundHeightEntry != null && groundHeightEntry.Value != groundHeight)
                groundHeightEntry.Set(groundHeight);
            Invalidated();
        }

        internal static void ResetDefaults()
        {
            ApplyPreset(0);
        }

        internal static void ApplyPreset(int preset)
        {
            LootPreset profile = LootPreset.For(preset);
            hasLive = false;
            SetJunkPercent(profile.Junk);
            SetHighPercent(profile.High);
            SetSpecialtyTier(profile.SpecialtyTier);
            if (crateEntry != null) crateEntry.Set(profile.Crate);
            if (cardEntry != null) cardEntry.Set(profile.Card);
            if (minorEntry != null) minorEntry.Set(profile.Minor);
            if (minorWindowEntry != null) minorWindowEntry.Set(DefaultMinorWindow);
            if (minorValueEntry != null) minorValueEntry.Set(profile.MinorValue);
            if (stashCapEntry != null) stashCapEntry.Set(profile.StashCap);
            if (stashWindowEntry != null) stashWindowEntry.Set(DefaultStashWindow);
            Invalidated();
        }

        internal static EffectiveTable TableFor(Region region)
        {
            Init();
            return PreviewTableFor(region, new LootSettings(JunkPercent, HighPercent, SpecialtyPercent,
                CratePercent, KeycardPercent, ForeignPercent));
        }

        internal static EffectiveTable PreviewTableFor(Region region, LootSettings settings)
        {
            Init();
            bool perk80 = Reputation.Points >= PerkCatalog.Perk80Threshold;
            // Use the native access point so optional skill mods can adjust
            // junk weight during the current search, just as in vanilla.
            float modifier = NativeLootEvents.JunkModifier();
            ForeignLootTable foreign = settings.ForeignPercent > 0f
                ? ForeignLoot.Snapshot(ForeignVanilla)
                : ForeignLootTable.Empty;
            return cache.Get(region, settings, perk80, ProbabilityMath.JunkScaleFromModifier(modifier), Reputation.Points,
                RegionalEvents.ActiveFor(region), foreign);
        }

        /// <summary>
        /// Rolls one item for the area, honouring the vanilla junk event.
        /// </summary>
        internal static string Pick(Region region)
        {
            EffectiveTable table = TableFor(region);
            int index = table.PickIndex();
            if (index < 0) return null;
            return table.Items[index];
        }


    }
}
