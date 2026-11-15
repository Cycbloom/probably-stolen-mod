using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    internal static class LootNoticeView
    {
        private const string BoardWindowId = Core.Id + ":board";
        private static CustomUIWindow boardWindow;
        /// <summary>告示板上的一块大类：标题标签 + 组内条目。</summary>
        private sealed class BoardGroup
        {
            internal string Name;
            internal CustomUIElement Label;
            internal readonly List<BoardItem> Items = new List<BoardItem>();
            internal bool High;
        }

        /// <summary>告示板上的一件物品：只存 id 和标签，概率每次刷新时重算。</summary>
        private sealed class BoardItem
        {
            internal string Id;
            internal int Slot;
            internal CustomUIElement Label;
        }

        private static Region boardRegion;
        private static readonly List<BoardGroup> boardGroups = new List<BoardGroup>();
        private static CustomUIElement boardBonusHeader;
        private static readonly CustomUIElement[] boardSpecial = new CustomUIElement[4];
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
            b.AddLabel(region.Detail, "board_detail");
            Picker.Size(b.Last, -1, 42);
            b.AddLabel(Copy.Text("按大类分组，概率是当前生效值，跟着设置实时变化：",
                "Grouped by category; chances are live and follow the settings:"), "board_caption");
            Picker.Size(b.Last, -1, 26);
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
                b.AddLabel(group.Name, "board_group_" + g);
                group.Label = b.Last;
                group.Label.SetTextColor(Picker.Accent);
                Picker.Size(b.Last, -1, 22);
                b.BeginGrid(2, 286, 22, 4);
                for (int i = 0; i < group.Items.Count; ++i)
                {
                    b.AddLabel(" ", "board_gi_" + g + "_" + i);
                    group.Items[i].Label = b.Last;
                }
                b.End();
                b.AddSpace(2);
            }

            b.AddSpace(4);
            b.AddLabel(Copy.Text("档位加成", "Tier bonuses"), "board_bonus_header");
            boardBonusHeader = b.Last;
            boardBonusHeader.SetTextColor(Picker.Accent);
            Picker.Size(b.Last, -1, 22);
            b.BeginGrid(2, 286, 22, 4);
            for (int i = 0; i < boardSpecial.Length; ++i)
            {
                b.AddLabel(" ", "board_special_" + i);
                boardSpecial[i] = b.Last;
            }
            b.End();
            b.End();

            b.AddSpace(6);
            b.AddLabel(" ", "board_ev");
            boardEv = b.Last;
            Picker.Size(b.Last, -1, 24);
            b.AddLabel(Copy.Text("垃圾事件只抬高“垃圾”，其余物品按比例让位；显示 0.0% 表示当前档位下这件物品不出现。",
                "Junk events only raise \"junk\"; other items yield proportionally. 0.0% means the item cannot appear at the current tier."),
                "board_footer");
            Picker.Size(b.Last, -1, 42);
            b.Show();

            RefreshBoard();
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
                    if (item.Label != null)
                        item.Label.SetText(BoardItemName(item.Id) + "  ·  " + BoardPercent(probability));
                }
                if (group.Label != null)
                {
                    group.Label.SetText(group.Name + "  ·  " + BoardPercent(sum));
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

            if (boardEv != null)
                boardEv.SetText(Copy.Text("本区期望价值：", "Expected value: ")
                    + table.Ev.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private static void SetBoardGroupVisible(BoardGroup group, bool visible)
        {
            if (group.Label != null) group.Label.SetVisible(visible);
            for (int i = 0; i < group.Items.Count; ++i)
                if (group.Items[i].Label != null) group.Items[i].Label.SetVisible(visible);
        }

        private static void SetBoardSpecial(int index, bool visible, string name, float percent)
        {
            CustomUIElement label = boardSpecial[index];
            if (label == null) return;
            label.SetVisible(visible);
            if (visible) label.SetText(name + "  ·  " + BoardPercentValue(percent));
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

        /// <summary>芯片全名太长，截成「打印芯片（…）」以便在 286px 的格子里放下。</summary>
        private static string BoardItemName(string id)
        {
            if (id == LootCatalog.InjectorBadId)
                return Copy.Text("免疫宁注射器（过期/仿品/不能用）",
                    "Immunivax injector (expired/counterfeit/unusable)");
            if (id == LootCatalog.CigaretteFakeId)
                return Copy.Text("一包海明威香烟（仿品）", "Hemingway cigarettes (counterfeit)");
            if (id == LootCatalog.StampFakeId)
                return Copy.Text("食品券（仿品）", "Food stamp (counterfeit)");

            string name = Picker.ItemName(id);
            if (id.StartsWith("printer_chip_", StringComparison.Ordinal))
            {
                int open = name.IndexOf('（');
                if (open > 0)
                    return Copy.Text("打印芯片", "Printer chip") + " " + name.Substring(open);
            }
            return name;
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
            for (int i = 0; i < boardSpecial.Length; ++i) boardSpecial[i] = null;
        }

        private static void BoardClosed()
        {
            boardWindow = null;
            ResetBoardState();
        }

    }
}
