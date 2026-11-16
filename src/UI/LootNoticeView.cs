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
                Copy.Text("拾荒告示板 · ", "Loot Notice · ") + region.Name,
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            boardWindow = b.Handle;
            b.SetSize(620, 640).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(BoardClosed);

            boardRegion = region;
            List<string> minorNames = new List<string>();
            foreach (string id in StashFind.MinorBox.ItemsFor(region.Id)) minorNames.Add(Picker.ItemName(id));
            boardMinorContents = string.Join(Copy.Text("、", ", "), minorNames.ToArray());
            boardGroups.Clear();
            boardSupplyItems.Clear();

            b.AddLabel(Copy.Text("—— 本区掉落公示 ——", "— Area Loot Notice —"), "board_header");
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
            b.AddLabel(Copy.Text("按大类分组，概率是当前生效值，跟着设置实时变化：",
                "Grouped by category; chances are live and follow the settings:"), "board_caption");
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
                    ? Copy.Text("贵重品（80 档 + 贵重品出现率）", "Valuables (tier 80 + valuable chance)")
                    : source.Foreign ? Copy.Text("外来条目（其它模组）", "Foreign (other mods)")
                    : source.Category >= 0 ? region.Loot.CategoryName(source.Category)
                    : Copy.Text("其他（低专精混入的原版物品）", "Other (vanilla items at lower specialty)");
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
            b.AddLabel(Copy.Text("档位加成", "Tier bonuses"), "board_bonus_header");
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
            b.AddLabel(Copy.Text("已计入当前事件：垃圾权重改变后，全部概率重新归一化；0.0% 表示当前条件下不会掉落。",
                "Current events are included: changing junk weight renormalizes all chances. 0.0% means unavailable under current conditions."),
                "board_footer");
            Picker.Size(b.Last, -1, 42);
            ConfigureText(b.Last, 16, true);
            b.Last.SetTooltip(b.Last.GetText());
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
                boardEvent.SetText(effect == null ? Copy.Text("今日无区域事件", "No regional event today")
                    : Copy.Text("今日事件：", "Today's event: ") + effect.Title + Copy.Text(" · 今日结束", " · Ends today")
                        + "\n" + effect.Description);
                boardEvent.SetTooltip(effect == null ? "" : effect.Tooltip);
            }
            if (boardCaption != null)
                boardCaption.SetText(Copy.Text("实际概率 · 全场垃圾权重 ×", "Live chances · yard-wide junk weight ×")
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
            bool tier80 = points >= PerkCatalog.Perk80Threshold;

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
            bool showCrate = tier80 && crate > 0f;
            bool showCard = tier80 && card > 0f;
            SetBoardSpecial(0, showJackpot, Copy.Text("大奖", "Jackpot"), rewards.JackpotProbability * 100f);
            SetBoardSpecial(1, showMinor, Copy.Text("小奖", "Minor find"), rewards.MinorProbability * 100f);
            if (showJackpot)
            {
                string jackpotTip = Tuning.StashWindowEnabled
                    ? Copy.Text("大奖按自然日更新概率，当天固定。连续5个未中大奖的日子达到上限，中奖后次日减半。\n每趟最多中一次；本趟已中时显示0%。",
                        "Jackpot odds update once per calendar day and stay fixed that day. Five missed days reach the cap; a win halves the next day's odds.\nAt most one win per visit; shows 0% after this visit's win.")
                    : Copy.Text("大奖窗口已关闭，直接使用当前生效上限；保存的日窗口进度暂停。",
                        "Daily window is off: uses the effective cap directly while saved progress is paused.");
                boardSpecial[0].Name.SetTooltip(jackpotTip);
                boardSpecial[0].Chance.SetTooltip(jackpotTip);
            }
            if (showMinor)
            {
                string minorTip = Copy.Text("本区小奖整箱：", "Area minor-reward crates: ") + boardMinorContents
                    + Copy.Text("\n命中后随机一种，整箱同类物品；数量按目标价值与单价计算（1–12件）。实际概率已计入大奖优先。",
                        "\nOne type per crate, chosen at random. Count follows target value and unit price (1-12 items). Effective odds include jackpot precedence.");
                minorTip += "\n" + (Tuning.MinorWindowEnabled
                    ? Copy.Text("每次搜索更新窗口；未中增长、中奖减半。", "Per-search window: misses increase odds, wins halve them.")
                    : Copy.Text("小奖窗口关闭，直接使用生效上限。", "Minor window is off: uses the effective cap directly."))
                    + Copy.Text("\n当前抽奖概率：", "\nCurrent roll chance: ") + BoardPercentValue(StashFind.CurrentMinorChancePercent)
                    + Copy.Text("；实际生效上限：", "; effective cap: ") + BoardPercentValue(Tuning.EffectiveMinorPercent);
                boardSpecial[1].Name.SetTooltip(minorTip);
                boardSpecial[1].Chance.SetTooltip(minorTip);
            }
            SetBoardSpecial(2, showCrate, Copy.Text("补给箱合计", "All crates"), crate * 100f);
            SetBoardSpecial(3, showCard, Copy.Text("钥匙卡合计", "All keycards"), card * 100f);
            foreach (BoardItem item in boardSupplyItems)
            {
                float probability = table.Probs[item.Slot];
                item.Row.SetVisible(tier80 && probability > 0f);
                if (tier80 && probability > 0f) item.Row.SetText(Picker.ItemName(item.Id), BoardPercent(probability));
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
                boardEv.SetText(Copy.Text("主掉落期望合计 ≈ ", "Main-drop expected total ≈ ")
                    + table.Ev.ToString("0.00", CultureInfo.InvariantCulture) + Copy.Text(" 信用点", " credits")
                    + "\n<size=16>" + Copy.Text("基础 ", "Base ") + parts.Base.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Text(" + 贵重增益 ", " + rare ") + parts.Valuable.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Text(" + 箱卡增益 ", " + crates/cards ") + parts.Supply.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=16>" + Copy.Text("今日事件增益 +", "Today's regional uplift +")
                    + parts.Event.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=15>" + Copy.Text("奖励期望/次搜索：大奖 +", "Bonus EV/search: jackpot +")
                    + rewards.JackpotEv.ToString("0.00", CultureInfo.InvariantCulture) + Copy.Text(" · 小奖 +", " · minor +")
                    + rewards.MinorEv.ToString("0.00", CultureInfo.InvariantCulture)
                    + "</size>\n<size=13>" + Copy.Text("贵重与箱卡为净增益；工具、额外收获另算",
                        "Rare/crates are net uplifts; tools and extra finds are separate") + "</size>");
                string valueNote = Copy.Text("单位：信用点/件。基础、贵重和箱卡按同日无区域事件拆分，事件增益为当前区域事件前后的差值；四项相加等于当前主掉落期望。全场原版事件已计入。贵重品和箱卡为扣除替代普通掉落后的净增益，箱子按内容估值。\n贵重品概率：",
                    "Units: credits/item. Base/rare/crates use the same day without regional effects. Regional uplift is the event/no-event difference; all four terms sum to current main-drop EV. Native yard-wide events are included. Rare/crates are net uplifts; crates use estimated contents value.\nValuable chance: ")
                    + BoardPercent(parts.ValuableProbability) + Copy.Text("；箱卡概率：", "; crate/card chance: ")
                    + BoardPercent(parts.SupplyProbability)
                    + Copy.Text("\n大奖中奖时平均价值：", "\nMean jackpot payout: ")
                    + rewards.JackpotValue.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Text("；当前概率：", "; current chance: ") + BoardPercentValue(rewards.JackpotProbability * 100f)
                    + Copy.Text("\n小奖中奖时平均价值：", "\nMean minor payout: ")
                    + rewards.MinorValue.ToString("0.00", CultureInfo.InvariantCulture)
                    + Copy.Text("；实际概率：", "; effective chance: ") + BoardPercentValue(rewards.MinorProbability * 100f)
                    + Copy.Text("\n奖励期望按每次搜索、当前概率计算；小奖已计入大奖优先及1–12件限制。大奖后的后续额外收获、工具、额外主掉落和市场效果另算。",
                        "\nBonus EV is per search at current odds; minor rewards include jackpot precedence and the 1-12 item limit. Later extra finds, tools, extra main rolls and market effects are separate.");
                if (table.ForeignCount > 0)
                {
                    valueNote += Copy.Text("\n外来条目（其它模组）没有可用的估值，按 0 信用点计入，所以上面的合计比实际收益偏低。",
                        "\nForeign (other mods) entries have no value data and count as 0 credits, so the total above understates real returns.");
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
                return Picker.ItemName(id) + Copy.Text("（剩余1/3、1/2或全满）", " (1/3, 1/2 or full uses)");
            if (id == LootCatalog.InjectorBadId)
                return Copy.Text("免疫宁注射器（过期/仿品/不能用）",
                    "Immunivax injector (expired/counterfeit/unusable)");
            if (id == LootCatalog.CigaretteFakeId)
                return Copy.Text("一包海明威香烟（仿品）", "Hemingway cigarettes (counterfeit)");
            if (id == LootCatalog.StampFakeId)
                return Copy.Text("食品券（仿品）", "Food stamp (counterfeit)");

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
