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

        /// <summary>80 档：补给箱与钥匙卡的绝对概率，各自一个设置项（默认 1%、范围 0–5%）。</summary>
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
        /// 地面窗口贴哪个位置：九宫格，0 左上 / 1 上中 / 2 右上 / 3 左中 / 4 居中 /
        /// 5 右中 / 6 左下 / 7 下中 / 8 右下；默认左下。
        /// 只在拾荒场景摆放，商店界面不碰。
        /// </summary>
        internal const string GroundAlignKey = "ground_align";
        internal const int DefaultGroundAlign = 6;
        private static readonly string[] GroundAlignZh =
            { "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下" };
        private static readonly string[] GroundAlignEn =
            { "Top left", "Top middle", "Top right", "Middle left", "Center", "Middle right",
              "Bottom left", "Bottom middle", "Bottom right" };
        private static string[] groundAlignLabels;

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
                if (groundAlignLabels == null)
                {
                    groundAlignLabels = new string[GroundAlignZh.Length];
                    for (int i = 0; i < groundAlignLabels.Length; ++i)
                        groundAlignLabels[i] = Copy.Text(GroundAlignZh[i], GroundAlignEn[i]);
                }
                return groundAlignLabels;
            }
        }

        /// <summary>地面贴边位置（0–8）；配置项缺失或值对不上时按默认左下。</summary>
        internal static int GroundAlign
        {
            get
            {
                ConfigEntry<string> entry = groundAlignEntry;
                if (entry == null) return DefaultGroundAlign;
                int index = System.Array.IndexOf(GroundAlignLabels, entry.Value);
                return index < 0 ? DefaultGroundAlign : index;
            }
        }

        /// <summary>下拉框换档：立即写进配置项，下次进拾荒场景按新位置摆放。</summary>
        internal static void SetGroundAlignLive(int index)
        {
            ConfigEntry<string> entry = groundAlignEntry;
            if (entry == null) return;
            string[] labels = GroundAlignLabels;
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
                    delegate { return Copy.Text("垃圾出现率倍率（%，0–150）", "Junk chance multiplier (%, 0-150)"); },
                    JunkMin, JunkMax,
                    delegate
                    {
                        return Copy.Text(
                            "范围0–150%，默认100%。\n100%：常态50%垃圾；150%：75%垃圾。\n50%：25%垃圾；0%：不出垃圾。\n声望0/20/40/60的基础收益按100%倍率校准，\n分别为12.24/13.46/14.69/15.91，大小奖另算。\n夜间事件仍会改变垃圾权重。",
                            "Range 0-150%, default 100%.\n100% gives 50% junk; 150% gives 75%.\n50% gives 25% junk; 0% removes junk.\nBase rewards at reputation 0/20/40/60 are calibrated at 100%:\n12.24/13.46/14.69/15.91, excluding bonuses.\nNight events still modify junk weights.");
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
                            "声望达到80后，每次主掉落出补给箱的总概率。\n默认1%，范围0–5%，按0.1吸附；0%不出。\n八区均有箱子，具体箱型按本区权重分配。\n箱子种类增多不会叠加总概率；份额从非垃圾普通物品中扣除。",
                            "Total supply-crate chance per main roll, active at 80 reputation.\nDefault 1%, range 0-5%, 0.1 steps; 0% disables it.\nAll eight areas have regional crate-type weights.\nMore types share this total; they replace non-junk ordinary items.");
                    },
                    false);

                cardEntry = config.Define(KeycardPercentKey, DefaultSpecialPercent, SectionName,
                    delegate { return Copy.Text("钥匙卡概率（%，0–5）", "Keycard chance (%, 0-5)"); },
                    SpecialPercentMin, SpecialPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "声望达到80后，每次主掉落出钥匙卡的总概率。\n默认1%，范围0–5%，按0.1吸附；0%不出。\n八区均有卡片，类型权重与本区箱型配套。\n箱子、卡片独立掉落；份额从非垃圾普通物品中扣除。",
                            "Total keycard chance per main roll, active at 80 reputation.\nDefault 1%, range 0-5%, 0.1 steps; 0% disables it.\nCard-type weights match each area's native crate locks.\nCrates and cards roll independently and replace non-junk ordinary items.");
                    },
                    false);

                minorEntry = config.Define(MinorPercentKey, DefaultMinorPercent, SectionName,
                    delegate { return Copy.Text("小奖概率上限（%，0–10）", "Minor prize chance cap (%, 0-10)"); },
                    MinorPercentMin, MinorPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "小奖窗口上限，默认3%，范围0–10%，0%停用。\n开启下方小奖窗口时，概率逐次搜索增长，中奖后减半；关闭时直接用本值。\n声望不足60时，当前实际概率仍乘1/10（上限3%对应实际最高0.3%）。\n按本区奖池翻出一整箱同一种物品，价值看「小奖整箱价值」。\n大奖优先，告示牌的小奖实际概率已扣除大奖占用。\n小奖物品照常计入声望，不发新闻、不改价格，也不影响大奖窗口。",
                            "Minor-prize window cap: default 3%, range 0-10%; 0% disables it.\nWith the minor window on, odds grow per search and halve after a win; off uses this cap directly.\nBelow 60 reputation, current odds are multiplied by 1/10 (a 3% cap gives at most 0.3%).\nA whole crate of one regional item type; payout follows Minor prize crate value.\nJackpots have priority; the notice shows minor odds after that exclusion.\nItems count toward reputation. No news, market effect, or change to the jackpot window.");
                    },
                    false);

                minorWindowEntry = config.Define(MinorWindowKey, DefaultMinorWindow, SectionName,
                    delegate { return Copy.Text("启用小奖概率拥塞窗口（每次搜索）", "Use an adaptive minor-prize window (per search)"); },
                    delegate
                    {
                        return Copy.Text("开启：默认从0.3%开始，未中先翻倍，达到上限一半后每次搜索增加0.1个百分点；中奖后减半。\n起点取上限的1/10且不超过0.3%。每次有效搜索更新一次，各区域共用、随存档保存。\n关闭：直接使用上方概率上限，暂停窗口进度。\n声望不到60时，当前实际概率仍按1/10生效。上限0时不抽奖、不增长。",
                            "On: starts at 0.3% with the default cap. Misses first double the chance, then add 0.1 percentage points per search after reaching half the cap. Wins halve it.\nStart is one tenth of the cap, at most 0.3%. One update per eligible search, shared across areas and saved with the run.\nOff: uses the cap directly and pauses saved progress.\nBelow 60 reputation, actual odds remain one tenth. A zero cap disables rolls and growth.");
                    }, false);
                minorWindowEntry.OnChanged += delegate(bool value) { Invalidated(); };

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

                stashWindowEntry = config.Define(StashWindowKey, DefaultStashWindow, SectionName,
                    delegate { return Copy.Text("启用大奖概率拥塞窗口（每天）", "Use an adaptive jackpot window (daily)"); },
                    delegate
                    {
                        return Copy.Text(
                            "开启：当天概率固定，每个自然日只更新一次。\n从本轮起点线性增长，连续5个未中大奖的日子达到上限；没去拾荒的日子也计入。\n中奖后次日概率减半，再用5个未中日增长到上限。最低0.05%。\n默认上限1%时：0.05→0.24→0.43→0.62→0.81→1.00%。\n关闭：直接用下方上限，暂停日窗口进度。声望不足60仍受0.1%上限限制。\n每趟最多一次大奖；同一天多次搜索不增加窗口概率。",
                            "On: odds stay fixed throughout the day, with one update per calendar day.\nLinear growth from the cycle start reaches the cap after 5 days without a jackpot, including non-scavenging days.\nA winning day halves odds the next day, then starts another 5-day recovery. Minimum 0.05%.\nAt the default 1% cap: 0.05, 0.24, 0.43, 0.62, 0.81, 1.00%.\nOff uses the cap directly and pauses progress. Below 60 reputation, the cap remains 0.1%.\nAt most one jackpot per visit; more searches do not increase the daily odds.");
                    }, false);

                stashCapEntry = config.Define(StashCapKey, DefaultStashCap, SectionName,
                    delegate { return Copy.Text("爆大奖概率上限（%，0.1–5.0）", "Jackpot chance cap (%, 0.1-5.0)"); },
                    StashCapMin, StashCapMax,
                    delegate
                    {
                        return Copy.Text(
                            "大奖概率的上限。\n启用拥塞窗口时，概率动态增长到这个上限；关闭时，每次直接使用这个概率。\n声望不到 60 时，实际生效的上限仍压到 0.1%。\n适用于配置了储藏样板的全部八个区域。每趟最多中一次大奖。",
                            "Jackpot chance cap.\nWith the adaptive window enabled, the chance grows toward this cap; when disabled, every roll uses it directly.\nThe effective cap is still limited to 0.1% below 60 reputation.\nApplies to all eight areas with stash templates. At most one jackpot per visit.");
                    },
                    false);

                foreignEnabledEntry = config.Define(ForeignEnabledKey, DefaultForeignEnabled, SectionName,
                    delegate { return Copy.Text("允许其它模组的拾荒物品出现", "Allow other mods' scavenge items"); },
                    delegate
                    {
                        return Copy.Text(
                            "本模组按区域接管了拾荒掉落，其它模组加进原版拾荒表的物品默认不会出现。\n开启后，这些外来条目会按它们在原版表里的相对权重参与掉落。\n只读取原版表组，不修改任何模组的数据。\n外来条目在告示板里单列一组「外来条目（其它模组）」。",
                            "This mod takes over scavenging per region, so items other mods added to the native loot tables would never drop.\nWhen enabled, those entries join the roll with the relative weights they have in the native group.\nThe native tables are only read, never modified.\nNeeds a scene reload to take effect after installing or removing such a mod.");
                    },
                    false);

                foreignPercentEntry = config.Define(ForeignPercentKey, DefaultForeignPercent, SectionName,
                    delegate { return Copy.Text("外来条目份额上限（%，0–8）", "Foreign item share cap (%, 0-8)"); },
                    ForeignPercentMin, ForeignPercentMax,
                    delegate
                    {
                        return Copy.Text(
                            "每次拾荒里，外来条目最多占多少概率。\n这是上限而不是固定摊派：如果它们在原版表里本来只占 0.8%，就按 0.8% 出。\n份额优先从非专精部分扣除（垃圾，以及普通物品里由原版表插值来的那份），专精物品最后才动。\n0% 等于关闭；值钱的外来物品越多，单次收益抬得越明显。\n默认 5%。",
                            "Upper bound on how much of one haul foreign entries may take.\nThis is a cap, not a fixed allotment: if they naturally hold 0.8% of the native table, they get 0.8%.\nThe share comes out of the non-specialty part first (junk, plus the vanilla half of ordinary items); specialty items are touched last.\n0% disables it; the more valuable the foreign items, the more they raise per-search returns.\nDefault 5%.");
                    },
                    false);

                foreignVanillaEntry = config.Define(ForeignVanillaKey, DefaultForeignVanilla, SectionName,
                    delegate { return Copy.Text("外来条目也纳入未收录的原版物品", "Also include vanilla items this mod omits"); },
                    delegate
                    {
                        return Copy.Text(
                            "默认关闭：只有其它模组新增的条目算外来。\n开启后，本模组各区域表里没收录的原版物品也会进入外来池。\n用于排查遗漏，可能把刻意剔除的原版物品放回来，日常不建议开。",
                            "Off by default: only entries added by other mods count as foreign.\nWhen on, vanilla items missing from this mod's regional tables also join the pool.\nUseful for auditing omissions; it can reintroduce items that were excluded on purpose, so it is not recommended for normal play.");
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

                groundAlignEntry = config.Define(GroundAlignKey, GroundAlignLabels[DefaultGroundAlign],
                    GroundSection,
                    delegate { return Copy.Text("地面对齐", "Ground alignment"); },
                    GroundAlignLabels,
                    delegate
                    {
                        return Copy.Text(
                            "拾荒时地面窗口贴在哪里。\n九个位置：左上、上中、右上、左中、居中、右中、左下、下中、右下。\n默认左下，避开右侧的时间与按钮面板。\n靠右时如果会压住右侧原生面板，会自动往左让开。\n改完下次进拾荒场景生效；商店界面不受影响。",
                            "Where the ground window sits while scavenging.\nNine spots: top left, top middle, top right, middle left, center, middle right, bottom left, bottom middle, bottom right.\nDefault bottom left, clear of the time and button panel on the right.\nOn the right side it moves left automatically when it would cover that panel.\nApplies the next time you enter the scavenging scene; the shop UI is never touched.");
                    },
                    false);

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
            AfterhourLocation location = AfterhourLocation.Get("dumping_ground");
            float modifier = location == null ? 0f : location.GetModifier("junk_chance");
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
