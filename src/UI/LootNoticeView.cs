using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DumpingGroundMap
{
    internal static class LootNoticeView
    {
        private const string BoardWindowId = Core.Id + ":board";
        private const float RowHeight = 28f;
        private static CustomUIWindow boardWindow;

        /// <summary>名称可省略，概率始终占独立的一列；悬停可查看完整内容。</summary>
        private sealed class BoardRow
        {
            internal GameObject Root;
            internal CustomUIElement Name;
            internal CustomUIElement Chance;

            internal void SetText(string name, string chance)
            {
                Name.SetText(name);
                Chance.SetText(chance);
                string full = name + "  ·  " + chance;
                Name.SetTooltip(full);
                Chance.SetTooltip(full);
            }

            internal void SetVisible(bool visible)
            {
                Root.SetActive(visible);
            }
        }
        /// <summary>告示板上的一块大类：标题标签 + 组内条目。</summary>
        private sealed class BoardGroup
        {
            internal string Name;
            internal BoardRow Row;
            internal GameObject Grid;
            internal readonly List<BoardItem> Items = new List<BoardItem>();
            internal bool High;
        }

        /// <summary>告示板上的一件物品：只存 id 和标签，概率每次刷新时重算。</summary>
        private sealed class BoardItem
        {
            internal string Id;
            internal int Slot;
            internal BoardRow Row;
        }

        private static Region boardRegion;
        private static readonly List<BoardGroup> boardGroups = new List<BoardGroup>();
        private static readonly List<BoardItem> boardSupplyItems = new List<BoardItem>();
        private static CustomUIElement boardBonusHeader;
        private static readonly BoardRow[] boardSpecial = new BoardRow[4];
        private static GameObject boardBonusGrid;
        private static CustomUIElement boardEv;
        private static EffectiveTable boardEvSource;
        private static MainExpectation boardEvParts;
        private static string boardMinorContents;
        private static CustomUIElement boardCaption;
        private static CustomUIElement boardEvent;

        internal static void Open(Region region)
        {
            Close();
            CustomUIManager ui = CustomUIManager.Instance;
            if (ui == null) return;

            CustomUIBuilder b = ui.CreateWindow(BoardWindowId,
                Copy.Get("lootnoticeview_loot_notice") + region.Name,
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            boardWindow = b.Handle;
            b.SetSize(620, 640).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(BoardClosed);

            boardRegion = region;
            List<string> minorNames = new List<string>();
            foreach (string id in StashFind.MinorBox.ItemsFor(region.Id)) minorNames.Add(Picker.ItemName(id));
            boardMinorContents = string.Join(Copy.Get("lootnoticeview_"), minorNames.ToArray());
            boardGroups.Clear();
            boardSupplyItems.Clear();

            b.AddLabel(Copy.Get("lootnoticeview_area_loot_notice"), "board_header");
            b.Last.SetTextColor(Picker.Accent);
            Picker.Size(b.Last, -1, 28);
            ConfigureText(b.Last, 20, false);
            b.AddLabel(region.Detail, "board_detail");
            Picker.Size(b.Last, -1, 42);
            ConfigureText(b.Last, 18, true);
            b.AddLabel("", "board_event");
            boardEvent = b.Last;
            Picker.Size(boardEvent, -1, 64);
            ConfigureText(boardEvent, 16, true);
            boardEvent.SetTextColor(Picker.Accent);
            b.AddLabel(" ", "board_ev");
            boardEv = b.Last;
            Picker.Size(b.Last, -1, 120);
            ConfigureText(b.Last, 20, true);
            if (b.Last.text != null) b.Last.text.richText = true;
            b.Last.SetTextColor(Picker.Accent);
            b.AddSpace(6);
            b.AddLabel(Copy.Get("lootnoticeview_grouped_by_category_chances_are_live_and"), "board_caption");
            Picker.Size(b.Last, -1, 26);
            boardCaption = b.Last;
            ConfigureText(b.Last, 18, false);
            b.AddSpace(4);

            b.BeginScroll(228);

            EffectiveTable table = Tuning.TableFor(region);
            foreach (LootNoticeGroup source in LootNoticeModel.Groups(table))
            {
                BoardGroup group = new BoardGroup();
                group.High = source.Valuable;
                group.Name = source.Valuable
                    ? Copy.Get("lootnoticeview_valuables_tier_80_valuable_chance")
                    : source.Foreign ? Copy.Get("lootnoticeview_foreign_other_mods")
                    : source.Category >= 0 ? region.Loot.CategoryName(source.Category)
                    : Copy.Get("lootnoticeview_other_vanilla_items_at_lower_specialty");
                foreach (int slot in source.Slots)
                {
                    BoardItem item = new BoardItem();
                    item.Id = table.Slots[slot].ItemId;
                    item.Slot = slot;
                    group.Items.Add(item);
                }
                boardGroups.Add(group);
            }

            for (int g = 0; g < boardGroups.Count; ++g)
            {
                BoardGroup group = boardGroups[g];
                group.Row = AddRow(b, "board_group_" + g);
                group.Row.Name.SetTextColor(Picker.Accent);
                group.Row.Chance.SetTextColor(Picker.Accent);
                b.BeginGrid(2, 270, RowHeight, 8);
                group.Grid = b.CurrentContainer.gameObject;
                for (int i = 0; i < group.Items.Count; ++i)
                {
                    group.Items[i].Row = AddRow(b, "board_gi_" + g + "_" + i);
                }
                b.End();
                b.AddSpace(2);
            }

            b.AddSpace(4);
            b.AddLabel(Copy.Get("lootnoticeview_tier_bonuses"), "board_bonus_header");
            boardBonusHeader = b.Last;
            boardBonusHeader.SetTextColor(Picker.Accent);
            Picker.Size(b.Last, -1, RowHeight);
            ConfigureText(b.Last, 18, false);
            b.BeginGrid(2, 270, RowHeight, 8);
            boardBonusGrid = b.CurrentContainer.gameObject;
            for (int i = 0; i < boardSpecial.Length; ++i)
            {
                boardSpecial[i] = AddRow(b, "board_special_" + i);
            }
            for (int i = table.SpecialStart; i < table.Count; ++i)
            {
                BoardItem item = new BoardItem();
                item.Id = table.Items[i];
                item.Slot = i;
                item.Row = AddRow(b, "board_supply_" + i);
                boardSupplyItems.Add(item);
            }
            b.End();
            b.AddSpace(6);
            b.AddLabel(Copy.Get("lootnoticeview_current_events_are_included_changing_junk_weight"),
                "board_footer");
            Picker.Size(b.Last, -1, 42);
            ConfigureText(b.Last, 16, true);
            b.Last.SetTooltip(b.Last.GetText());
            if (ModHelper.IsModRunning(ScavengingExpansionBridge.ModId))
            {
                b.AddLabel(Copy.Get("lootnoticeview_scavenging_expansion_skills_can_change_candidates_and"),
                    "board_skill_note");
                Picker.Size(b.Last, -1, 56);
                ConfigureText(b.Last, 16, true);
                b.Last.SetTooltip(b.Last.GetText());
            }
            b.End();

            b.Show();

            RefreshBoard();
        }

        private static BoardRow AddRow(CustomUIBuilder b, string id)
        {
            b.BeginRow(6, TextAnchor.MiddleLeft);
            BoardRow row = new BoardRow();
            row.Root = b.CurrentContainer.gameObject;
            LayoutElement rowLayout = row.Root.AddComponent<LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = RowHeight;
            rowLayout.flexibleWidth = 1;
            rowLayout.flexibleHeight = 0;
            b.AddLabel(" ", id + "_name");
            row.Name = b.Last;
            ConfigureText(row.Name, 18, false);
            Picker.Size(row.Name, -1, RowHeight);
            LayoutElement nameLayout = row.Name.root.GetComponent<LayoutElement>();
            nameLayout.minWidth = nameLayout.preferredWidth = 0;
            nameLayout.flexibleWidth = 1;
            b.AddLabel(" ", id + "_chance");
            row.Chance = b.Last;
            ConfigureText(row.Chance, 18, false);
            if (row.Chance.text != null) row.Chance.text.alignment = TextAlignmentOptions.MidlineRight;
            Picker.Size(row.Chance, 72, RowHeight);
            row.Chance.root.GetComponent<LayoutElement>().flexibleWidth = 0;
            b.End();
            return row;
        }

        private static void ConfigureText(CustomUIElement label, float fontSize, bool wrap)
        {
            if (label.text == null) return;
            label.text.enableAutoSizing = false;
            label.text.fontSize = fontSize;
            label.text.enableWordWrapping = wrap;
            label.text.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            label.text.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>
        /// 就地刷新告示板：概率、每组小计、期望价值，以及 60 / 80 档才出现的四行。
        /// 标签只建一次，所以拉滑条时不会整块重建。
        /// </summary>
        internal static void RefreshIfOpen()
        {
            if (boardWindow == null || !boardWindow.IsAlive) return;
            bool perf = PerfLog.Enabled;
            long startedAt = perf ? Stopwatch.GetTimestamp() : 0L;
            RefreshBoard();
            if (perf)
            {
                PerfLog.Line("board refresh region=" + (boardRegion == null ? "-" : boardRegion.Id)
                    + " " + PerfLog.Ms(startedAt) + "ms");
            }
        }

        private static void RefreshBoard()
        {
            Region region = boardRegion;
            if (region == null) return;

            EffectiveTable table = Tuning.TableFor(region);
            if (boardEvent != null)
            {
                RegionalEvent effect = table.RegionalEvent;
                boardEvent.SetText(effect == null ? Copy.Get("lootnoticeview_no_regional_event_today")
                    : Copy.Get("lootnoticeview_today_s_event") + effect.Title + Copy.Get("lootnoticeview_ends_today")
                        + "\n" + effect.Description);
                boardEvent.SetTooltip(effect == null ? "" : effect.Tooltip);
            }
            if (boardCaption != null)
                boardCaption.SetText(Copy.Get("lootnoticeview_live_chances_yard_wide_junk_weight")
                    + table.JunkScale.ToString("0.00", CultureInfo.InvariantCulture));
            for (int g = 0; g < boardGroups.Count; ++g)
            {
                BoardGroup group = boardGroups[g];
                float sum = 0f;
                for (int i = 0; i < group.Items.Count; ++i)
                {
                    BoardItem item = group.Items[i];
                    float probability = table.Probs[item.Slot];
                    sum += probability;
                    if (item.Row != null)
                        item.Row.SetText(BoardItemName(item.Id), BoardPercent(probability));
                }
                if (group.Row != null)
                {
                    group.Row.SetText(group.Name, BoardPercent(sum));
                    if (group.High) SetBoardGroupVisible(group, sum > 0f);
                }
            }

            int points = Reputation.Points;
            bool suppliesUnlocked = points >= PerkCatalog.Perk100Threshold;

            string crateId = LootCatalog.CrateFor(region.Id);
            string cardId = LootCatalog.CardFor(region.Id);
            float crate = table.ProbabilityOf(LootKind.SupplyCrate);
            float card = table.ProbabilityOf(LootKind.Keycard);

            // Below 60 both rewards still have reduced chances; show those actual odds.
            bool showJackpot = StashFind.HasJackpot(region.Id);
            bool showMinor = StashFind.HasMinor(region.Id);
            RewardExpectation rewards = new RewardExpectation(StashFind.PreviewJackpotPercent(region),
                showMinor ? StashFind.PreviewMinorPercent(region) : 0f,
                RewardExpectation.MeanJackpotValue(StashTemplate.For(region.Id)),
                StashFind.MinorBox.ExpectedValue(region.Id, Tuning.MinorValue));
            bool showCrate = suppliesUnlocked && crate > 0f;
            bool showCard = suppliesUnlocked && card > 0f;
            SetBoardSpecial(0, showJackpot, Copy.Get("lootnoticeview_jackpot"), rewards.JackpotProbability * 100f);
            SetBoardSpecial(1, showMinor, Copy.Get("lootnoticeview_minor_find"), rewards.MinorProbability * 100f);
            if (showJackpot)
            {
                string jackpotTip = Tuning.StashWindowEnabled
                    ? Copy.Get("lootnoticeview_jackpot_odds_update_once_per_calendar_day")
                    : Copy.Get("lootnoticeview_daily_window_is_off_uses_the_effective");
                boardSpecial[0].Name.SetTooltip(jackpotTip);
                boardSpecial[0].Chance.SetTooltip(jackpotTip);
            }
            if (showMinor)
            {
                string minorTip = Copy.Get("lootnoticeview_area_minor_reward_crates") + boardMinorContents
                    + Copy.Get("lootnoticeview_one_type_per_crate_chosen_at_random");
                minorTip += "\n" + (Tuning.MinorWindowEnabled
                    ? Copy.Get("lootnoticeview_per_search_window_misses_increase_odds_wins")
                    : Copy.Get("lootnoticeview_minor_window_is_off_uses_the_effective"))
                    + Copy.Get("lootnoticeview_current_roll_chance") + BoardPercentValue(StashFind.CurrentMinorChancePercent)
                    + Copy.Get("lootnoticeview_effective_cap") + BoardPercentValue(Tuning.EffectiveMinorPercent);
                boardSpecial[1].Name.SetTooltip(minorTip);
                boardSpecial[1].Chance.SetTooltip(minorTip);
            }
            SetBoardSpecial(2, showCrate, Copy.Get("lootnoticeview_all_crates"), crate * 100f);
            SetBoardSpecial(3, showCard, Copy.Get("lootnoticeview_all_keycards"), card * 100f);
            foreach (BoardItem item in boardSupplyItems)
            {
                float probability = table.Probs[item.Slot];
                item.Row.SetVisible(suppliesUnlocked && probability > 0f);
                if (suppliesUnlocked && probability > 0f) item.Row.SetText(Picker.ItemName(item.Id), BoardPercent(probability));
            }
            if (boardBonusHeader != null)
                boardBonusHeader.SetVisible(showJackpot || showMinor || showCrate || showCard);
            if (boardBonusGrid != null)
                boardBonusGrid.SetActive(showJackpot || showMinor || showCrate || showCard);

            if (boardEv != null)
            {
                if (!ReferenceEquals(boardEvSource, table))
                {
                    boardEvSource = table;
                    boardEvParts = MainExpectation.From(table, region.Id);
                }
                MainExpectation parts = boardEvParts;
                boardEv.SetText(Copy.Get("lootnoticeview_main_drop_expected_total")
                    + table.Ev.ToString("0.00", CultureInfo.InvariantCulture) + Copy.Get("lootnoticeview_credits")
                    + "\n<size=16>" + Copy.Get("lootnoticeview_base") + parts.Base.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Get("lootnoticeview_rare") + parts.Valuable.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Get("lootnoticeview_crates_cards") + parts.Supply.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=16>" + Copy.Get("lootnoticeview_today_s_regional_uplift")
                    + parts.Event.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=15>" + Copy.Get("lootnoticeview_bonus_ev_search_jackpot")
                    + rewards.JackpotEv.ToString("0.00", CultureInfo.InvariantCulture) + Copy.Get("lootnoticeview_minor")
                    + rewards.MinorEv.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=13>" + Copy.Get("lootnoticeview_rare_crates_are_net_uplifts_tools_and") + "</size>");
                string valueNote = Copy.Get("lootnoticeview_units_credits_item_base_rare_crates_use")
                    + BoardPercent(parts.ValuableProbability) + Copy.Get("lootnoticeview_crate_card_chance")
                    + BoardPercent(parts.SupplyProbability)
                    + Copy.Get("lootnoticeview_mean_jackpot_payout")
                    + rewards.JackpotValue.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Get("lootnoticeview_current_chance") + BoardPercentValue(rewards.JackpotProbability * 100f)
                    + Copy.Get("lootnoticeview_mean_minor_payout")
                    + rewards.MinorValue.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Get("lootnoticeview_effective_chance") + BoardPercentValue(rewards.MinorProbability * 100f)
                    + Copy.Get("lootnoticeview_bonus_ev_is_per_search_at_current");
                if (table.ForeignCount > 0)
                {
                    valueNote += Copy.Get("lootnoticeview_foreign_other_mods_entries_have_no_value");
                }
                boardEv.SetTooltip(valueNote);
            }
        }

        private static void SetBoardGroupVisible(BoardGroup group, bool visible)
        {
            if (group.Row != null) group.Row.SetVisible(visible);
            if (group.Grid != null) group.Grid.SetActive(visible);
        }

        private static void SetBoardSpecial(int index, bool visible, string name, float percent)
        {
            BoardRow row = boardSpecial[index];
            if (row == null) return;
            row.SetVisible(visible);
            if (visible) row.SetText(name, BoardPercentValue(percent));
        }

        /// <summary>告示板上的概率：常规值走 Percent；低于 0.1% 时多给两位，避免整池都显示 &lt;0.1%。</summary>
        private static string BoardPercent(float probability)
        {
            float pct = probability * 100f;
            if (pct <= 0f) return "0.0%";
            if (pct < 0.1f) return pct.ToString("0.00", CultureInfo.InvariantCulture) + "%";
            return Picker.Percent(probability);
        }

        /// <summary>已经换算成百分数的值（大奖 / 小奖 / 箱子 / 钥匙卡）。</summary>
        private static string BoardPercentValue(float percent)
        {
            if (percent <= 0f) return "0.00%";
            if (percent < 0.01f) return "<0.01%";
            return percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>变体补充说明；显示名称和悬停说明都使用完整名称。</summary>
        private static string BoardItemName(string id)
        {
            if (id == HydraUsage.ItemId)
                return Picker.ItemName(id) + Copy.Get("lootnoticeview_1_3_1_2_or_full_uses");
            if (id == LootCatalog.InjectorBadId)
                return Copy.Get("lootnoticeview_immunivax_injector_expired_counterfeit_unusable");
            if (id == LootCatalog.CigaretteFakeId)
                return Copy.Get("lootnoticeview_hemingway_cigarettes_counterfeit");
            if (id == LootCatalog.StampFakeId)
                return Copy.Get("lootnoticeview_food_stamp_counterfeit");

            return Picker.ItemName(id);
        }

        internal static void Close()
        {
            CustomUIWindow window = boardWindow;
            boardWindow = null;
            ResetBoardState();
            if (window != null && window.IsAlive) window.Close();
        }

        /// <summary>清掉告示板的标签引用；窗口自己关闭时也走这里。</summary>
        private static void ResetBoardState()
        {
            boardRegion = null;
            boardGroups.Clear();
            boardSupplyItems.Clear();
            boardEv = null;
            boardEvSource = null;
            boardEvParts = null;
            boardMinorContents = null;
            boardCaption = null;
            boardEvent = null;
            boardBonusHeader = null;
            boardBonusGrid = null;
            for (int i = 0; i < boardSpecial.Length; ++i) boardSpecial[i] = null;
        }

        private static void BoardClosed()
        {
            boardWindow = null;
            ResetBoardState();
        }

    }
}
