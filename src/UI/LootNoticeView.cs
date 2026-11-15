using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
        private static CustomUIElement boardBonusHeader;
        private static readonly BoardRow[] boardSpecial = new BoardRow[4];
        private static GameObject boardBonusGrid;
        private static CustomUIElement boardEv;

        internal static void Open(Region region)
        {
            Close();
            CustomUIManager ui = CustomUIManager.Instance;
            if (ui == null) return;

            CustomUIBuilder b = ui.CreateWindow(BoardWindowId,
                Copy.Text("拾荒告示板 · ", "Loot Notice · ") + region.Name,
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            boardWindow = b.Handle;
            b.SetSize(620, 552).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(BoardClosed);

            boardRegion = region;
            boardGroups.Clear();

            b.AddLabel(Copy.Text("—— 本区掉落公示 ——", "— Area Loot Notice —"), "board_header");
            b.Last.SetTextColor(Picker.Accent);
            Picker.Size(b.Last, -1, 28);
            ConfigureText(b.Last, 20, false);
            b.AddLabel(region.Detail, "board_detail");
            Picker.Size(b.Last, -1, 42);
            ConfigureText(b.Last, 18, true);
            b.AddLabel(Copy.Text("按大类分组，概率是当前生效值，跟着设置实时变化：",
                "Grouped by category; chances are live and follow the settings:"), "board_caption");
            Picker.Size(b.Last, -1, 26);
            ConfigureText(b.Last, 18, false);
            b.AddSpace(4);

            b.BeginScroll(300);

            EffectiveTable table = Tuning.TableFor(region);
            foreach (LootNoticeGroup source in LootNoticeModel.Groups(table))
            {
                BoardGroup group = new BoardGroup();
                group.High = source.Valuable;
                group.Name = source.Valuable
                    ? Copy.Text("贵重品（80 档 + 贵重品出现率）", "Valuables (tier 80 + valuable chance)")
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
            b.End();
            b.AddSpace(6);
            b.AddLabel(Copy.Text("垃圾事件只抬高“垃圾”，其余物品按比例让位；显示 0.0% 表示当前档位下这件物品不出现。",
                "Junk events only raise \"junk\"; other items yield proportionally. 0.0% means the item cannot appear at the current tier."),
                "board_footer");
            Picker.Size(b.Last, -1, 42);
            ConfigureText(b.Last, 16, true);
            b.Last.SetTooltip(b.Last.GetText());
            b.End();

            b.AddSpace(6);
            b.AddLabel(" ", "board_ev");
            boardEv = b.Last;
            Picker.Size(b.Last, -1, 24);
            ConfigureText(b.Last, 18, false);
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
            RefreshBoard();
        }

        private static void RefreshBoard()
        {
            Region region = boardRegion;
            if (region == null) return;

            EffectiveTable table = Tuning.TableFor(region);
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
            bool tier60 = points >= PerkCatalog.Perk60Threshold;
            bool tier80 = points >= PerkCatalog.Perk80Threshold;

            string crateId = LootCatalog.CrateFor(region.Id);
            string cardId = LootCatalog.CardFor(region.Id);
            float crate = table.ProbabilityOf(LootKind.SupplyCrate);
            float card = table.ProbabilityOf(LootKind.Keycard);

            // 0.3.67 起五区都有大奖样板，这一行只在声望不到 60 时隐藏；
            // 留着 HasJackpot 判断，是为了以后加没有样板的区域时仍然不显示 0。
            bool showJackpot = tier60 && StashFind.HasJackpot(region.Id);
            bool showMinor = tier60 && StashFind.HasMinor(region.Id);
            bool showCrate = tier80 && crate > 0f;
            bool showCard = tier80 && card > 0f;
            SetBoardSpecial(0, showJackpot, Copy.Text("大奖", "Jackpot"), StashFind.CurrentChancePercent);
            SetBoardSpecial(1, showMinor, Copy.Text("小奖", "Minor find"), Tuning.EffectiveMinorPercent);
            SetBoardSpecial(2, showCrate, Copy.Text("物资箱", "Supply crate"), crate * 100f);
            SetBoardSpecial(3, showCard, Copy.Text("钥匙卡", "Keycard"), card * 100f);
            if (boardBonusHeader != null)
                boardBonusHeader.SetVisible(showJackpot || showMinor || showCrate || showCard);
            if (boardBonusGrid != null)
                boardBonusGrid.SetActive(showJackpot || showMinor || showCrate || showCard);

            if (boardEv != null)
                boardEv.SetText(Copy.Text("本区期望价值：", "Expected value: ")
                    + table.Ev.ToString("0.00", CultureInfo.InvariantCulture));
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
            if (percent < 0.01f) return "<0.01%";
            return percent.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>变体补充说明；显示名称和悬停说明都使用完整名称。</summary>
        private static string BoardItemName(string id)
        {
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
            boardEv = null;
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
