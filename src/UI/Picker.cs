using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    internal sealed class Picker
    {
        private const string WindowId = Core.Id + ":picker";
        internal static readonly Color Accent = new Color32(242, 201, 115, 255);
        internal static readonly Color Ink = new Color32(225, 235, 227, 255);
        /// <summary>未解锁地标的字色与边框色，比正常态暗一档。</summary>
        private static readonly Color LockedInk = new Color32(126, 138, 131, 255);
        private static readonly Color LockedRing = new Color32(58, 72, 66, 255);
        private static readonly Color RingInk = new Color32(95, 131, 117, 255);
        private static Picker current;

        /// <summary>
        /// 本次进入垃圾场时选定的区域。选定后一直沿用，直到离开该地点，
        /// 所以同一次停留里连续拾荒不再弹地图。
        /// </summary>
        internal static Region ChosenRegion { get; private set; }
        private static DumpingGroundLocation chosenAt;
        private readonly MapUIManager map;
        private readonly Action resume;
        /// <summary>
        /// 非空表示这张地图是「前往垃圾场」时弹出的：确认后继续原版 Travel 进入。
        /// 为空则是地点面板里点「拾荒」的兜底弹窗。
        /// </summary>
        private DumpingGroundLocation target;
        private OverviewMap overview;
        private Region hovered, inspected;
        private Vector2 selectedPoint;
        private Region selected;
        private CustomUIWindow window;
        private GameObject shade;
        private CustomUIElement description, confirm, reputationLabel, notice, eventSummary;

        private Picker(MapUIManager map, Action resume)
        {
            this.map = map;
            this.resume = resume;
        }

        internal static bool IsAtDump(MapUIManager map)
        {
            return map != null && map.Current is DumpingGroundLocation && !map.IsInTransit;
        }

        internal static bool Intercept(MapUIManager map, Action resume)
        {
            if (!Core.Enabled || !IsAtDump(map) || !ScavHelper.CanScavenge())
                return false;
            // 本次停留已经选过区域：不再弹地图，直接执行原有的拾荒。
            if (ChosenRegion != null && chosenAt == map.Current)
                return false;
            if (ChosenRegion != null)
                ResetChoice();
            if (current != null && current.window != null && current.window.IsVisible)
                return true;
            if (CustomUIManager.Instance == null)
                return false;

            Close();
            Picker picker = new Picker(map, resume);
            current = picker;
            try
            {
                picker.Build();
                return true;
            }
            catch (Exception ex)
            {
                Close();
                ModLogger.Error("Could not open map: " + ex);
                return false;
            }
        }

        /// <summary>
        /// 前往垃圾场时先弹区域地图：确认后由 resume 重新调用原版 Travel 完成"进入"。
        /// 返回 true 表示这次 Travel 暂缓执行。
        /// </summary>
        internal static bool InterceptTravel(MapUIManager map, DumpingGroundLocation destination)
        {
            if (!Core.Enabled || map == null || destination == null) return false;
            // 上一趟可能已耗尽次数；原版到达后才 ResetScavenging，不能用旧次数
            // 决定是否弹进场选区。这里只校验原版 Travel 本身的前置条件。
            if (map.IsInTransit || !destination.IsAvailable())
                return false;
            // 已经在本次行程里选过这块区域（确认后重新调 Travel 的那一次）。
            if (ChosenRegion != null && chosenAt == destination)
                return false;
            if (ChosenRegion != null)
                ResetChoice();
            if (current != null && current.window != null && current.window.IsVisible)
                return true;
            if (CustomUIManager.Instance == null)
                return false;

            Close();
            Picker picker = new Picker(map, delegate { map.Travel(destination); });
            picker.target = destination;
            current = picker;
            try
            {
                picker.Build();
                return true;
            }
            catch (Exception ex)
            {
                Close();
                ModLogger.Error("Could not open map: " + ex);
                return false;
            }
        }

        internal static void Close()
        {
            LootNoticeView.Close();
            Picker picker = current;
            if (picker == null) return;
            if (picker.window != null && picker.window.IsAlive)
                picker.window.Close();
            else
                picker.Cleanup();
        }

        private void Cleanup()
        {
            LootNoticeView.Close();
            if (current == this) current = null;
            if (shade != null)
            {
                shade.SetActive(false);
                UnityEngine.Object.Destroy(shade);
                shade = null;
            }
        }

        internal static void CheckLocation(MapUIManager map)
        {
            // 前往途中弹出的地图（target 非空）不要求人已经在垃圾场。
            if (current != null && (current.map != map
                || (current.target == null && !IsAtDump(map))
                || current.window == null || !current.window.IsVisible))
                Close();

            // 离开垃圾场（回家后 Current 变空、换去别的地点、读档后场景重建）时丢掉本次选择。
            if (ChosenRegion != null && !map.IsInTransit && map.Current != chosenAt)
                ResetChoice(false);
        }

        internal static void ResetChoice(bool restoreBackdrop = true)
        {
            if (restoreBackdrop) RegionBackdrop.Restore();
            ChosenRegion = null;
            chosenAt = null;
        }

        private void Build()
        {
            CustomUIManager ui = CustomUIManager.Instance;
            CustomUIBuilder b = ui.CreateWindow(WindowId, Copy.Get("picker_dumping_ground_search_map"),
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            window = b.Handle;
            Vector2 canvasSize = new Vector2(1920f, 1080f);
            Canvas canvas = window.Rect.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (canvasRect != null && canvasRect.rect.width > 0f && canvasRect.rect.height > 0f)
                canvasSize = canvasRect.rect.size;
            Vector2 mapSize = OverviewSize(canvasSize.x, canvasSize.y);
            b.SetSize(mapSize.x + 60f, mapSize.y + 340f).Center()
                .SetDraggable(false).SetCloseOnEscape(true).OnClose(Cleanup);

            shade = new GameObject("DumpingGroundMapShade", typeof(RectTransform), typeof(Image));
            RectTransform cover = (RectTransform)shade.transform;
            cover.SetParent(window.Rect.parent, false);
            cover.anchorMin = Vector2.zero;
            cover.anchorMax = Vector2.one;
            cover.offsetMin = Vector2.zero;
            cover.offsetMax = Vector2.zero;
            cover.SetSiblingIndex(window.Rect.GetSiblingIndex());
            shade.GetComponent<Image>().color = new Color32(9, 16, 19, 183);

            b.AddLabel(Copy.Get("picker_hover_to_preview_click_a_landmark_or"));
            eventSummary = b.Last;
            eventSummary.SetTooltip(Copy.Get("picker_hover_for_today_s_effects_and_ev"));
            Size(b.Last, -1, 36);
            if (b.Last.text != null)
            {
                b.Last.text.fontSize = 16;
                b.Last.text.enableAutoSizing = true;
                b.Last.text.fontSizeMin = 13;
                b.Last.text.fontSizeMax = 16;
                b.Last.text.enableWordWrapping = true;
            }
            b.AddSpace(6);
            Sprite background = ui.GetModSprite(Core.Id, "dumping_ground_map_base", true)
                ?? ui.GetModSprite(Core.Id, "dumping_ground_map_art", true)
                ?? ui.GetModSprite(Core.Id, "dumping_ground_map", true);
            b.AddImage(background, mapSize.x, mapSize.y, "map");
            CustomUIElement mapPanel = b.Last;
            Size(mapPanel, mapSize.x, mapSize.y);
            mapPanel.SetImageColor(background == null ? (Color)new Color32(39, 60, 56, 255) : Color.white);
            // Keep click targets tied to the same rectangle as the map image.
            RectTransform graphic = mapPanel.image.rectTransform;
            // Anchors on pins and notice buttons use this exact full-image rectangle.
            mapPanel.image.type = Image.Type.Simple;
            mapPanel.image.preserveAspect = false;

            mapPanel.image.raycastTarget = false;
            overview = new OverviewMap(graphic, mapSize, ui, window, Hover, ChoosePoint);

            b.AddSpace(6).AddLabel("", "reputation");
            reputationLabel = b.Last;
            Size(reputationLabel, -1, 24);
            if (reputationLabel.text != null) reputationLabel.text.alignment = TextAlignmentOptions.MidlineLeft;
            if (reputationLabel.text != null)
            {
                reputationLabel.text.enableAutoSizing = true;
                reputationLabel.text.fontSizeMin = 11;
                reputationLabel.text.fontSizeMax = 16;
                reputationLabel.text.enableWordWrapping = false;
                reputationLabel.text.overflowMode = TextOverflowModes.Ellipsis;
            }
            RefreshReputation();

            b.AddSpace(8).AddLabel("", "description");
            description = b.Last;
            Size(description, -1, 104);
            if (description.text != null) { description.text.fontSize = 17; description.text.enableAutoSizing = true; description.text.fontSizeMin = 13; description.text.fontSizeMax = 17; }
            if (description.text != null) description.text.alignment = TextAlignmentOptions.MidlineLeft;
            if (description.text != null)
            {
                description.text.enableWordWrapping = true;
                description.text.overflowMode = TextOverflowModes.Ellipsis;
            }
            b.AddSpace(8).BeginRow(12, TextAnchor.MiddleRight);
            b.AddButton(Copy.Get("picker_loot_chances"), delegate
            {
                Region focus = hovered ?? inspected ?? selected;
                if (focus != null) LootNoticeView.Open(focus);
            });
            notice = b.Last;
            Size(notice, Mathf.Min(164f, mapSize.x * 0.32f), 36);
            b.AddButton(Copy.Get("picker_cancel"), Close);
            Size(b.Last, Mathf.Min(130f, mapSize.x * 0.22f), 36);
            b.AddButton(target != null
                ? Copy.Get("picker_enter_this_area")
                : Copy.Get("picker_start_search"), StartSearch);
            confirm = b.Last;
            Size(confirm, Mathf.Min(248f, mapSize.x * 0.38f), 36);
            b.End();
            Select(null);
            b.Show();
        }

        internal static Vector2 OverviewSize(float canvasWidth, float canvasHeight)
        {
            // Overlay UI uses a 1920-wide reference canvas. Reserve the title,
            // description and action row, then fit the 2:1 artwork without cropping.
            float availableWidth = Mathf.Min(1440f, canvasWidth - 64f) - 60f;
            float availableHeight = Mathf.Min(1040f, canvasHeight - 48f) - 340f;
            float width = Mathf.Max(2f, Mathf.Min(availableWidth, availableHeight * 2f));
            return new Vector2(width, width * 0.5f);
        }

        internal static void Size(CustomUIElement element, float width, float height)
        {
            element.SetPreferredSize(width, height);
            LayoutElement layout = element.root.GetComponent<LayoutElement>();
            if (layout != null)
            {
                if (width > 0f) layout.minWidth = width;
                if (height > 0f) layout.minHeight = height;
                layout.flexibleHeight = 0;
            }
        }

        /// <summary>
        /// 部分物品的显示名不等于 item_&lt;id&gt;_name：绷带类固定用 item_bandage_name 等，
        /// 满盒物品复用了容器物品的名字词条。这里按游戏工厂里的实际取值做映射。
        /// </summary>
        private static readonly Dictionary<string, string> NameKeys =
            new Dictionary<string, string>
            {
                { "bandage_item", "item_bandage_name" },
                { "hemostatic_bandage_item", "item_hemostatic_bandage_name" },
                { "topical_bandage_item", "item_topical_bandage_name" },
                { "full_water_test_strip_box", "item_water_test_container_name" },
                { "full_water_purification_tablet_box", "item_water_tablet_container_name" }
            };

        internal static string ItemName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return "";
            if (identifier == WaterSupplyPack.HalfTest)
                return ItemName("full_water_test_strip_box") + Copy.Get("picker_half_pack_5_strips");
            if (identifier == WaterSupplyPack.HalfTablets)
                return ItemName("full_water_purification_tablet_box") + Copy.Get("picker_half_bottle_5_tablets");

            // 先按游戏自己的取名途径：物品目录或预构建工厂里的 GameItem 已经带着
            // 本地化后的 name，其它模组的物品也是这样注册的。
            string name = ResolvedNames.Get(LocHelper.GetCurrentLocaleCode(), identifier, ResolveItemName);
            // 查不到就把 id 尾段整理成可读文本，不把 mod.item_id 原样丢给玩家。
            if (name == null) return FallbackName(identifier);

            return name;
        }

        /// <summary>已解析的显示名，避免告示牌每次刷新都重新构造物品对象。</summary>
        private static readonly ItemNameCache ResolvedNames = new ItemNameCache();

        private static string ResolveItemName(string identifier)
        {
            return GameItemName(identifier) ?? LocalizedName(identifier);
        }

        /// <summary>
        /// 用游戏自己的物品目录/预构建工厂取显示名（生成物品时走的也是这条途径），
        /// 查不到或名字不可用时返回 null。
        /// </summary>
        private static string GameItemName(string identifier)
        {
            GameItem item = null;
            try
            {
                Dictionary<string, Func<GameItem>> prebuilt = PreBuiltItemHelper.preBuiltItems;
                Func<GameItem> factory;
                if (prebuilt != null && prebuilt.TryGetValue(identifier, out factory) && factory != null)
                    item = factory();
                if (item == null)
                    item = DirectoryMaster.Create<GameItem>(identifier);
            }
            catch (Exception)
            {
                return null;
            }
            if (item == null) return null;

            string name = item.name;
            if (string.IsNullOrEmpty(name) || name == identifier) return null;
            string table, missingKey;
            if (LocHelper.TryParseMissingTranslation(name, out table, out missingKey)) return null;
            return name;
        }

        /// <summary>
        /// 目录里没有这个 id 时退回词条约定：先试完整 id，再试去掉模组前缀的尾段。
        /// </summary>
        private static string LocalizedName(string identifier)
        {
            string name = LocalizeItemKey(identifier);
            if (name != null) return name;
            int dot = identifier.LastIndexOf('.');
            if (dot > 0 && dot + 1 < identifier.Length)
                return LocalizeItemKey(identifier.Substring(dot + 1));
            return null;
        }

        private static string LocalizeItemKey(string identifier)
        {
            string key;
            if (!NameKeys.TryGetValue(identifier, out key))
                key = "item_" + identifier + "_name";

            string name = LocHelper.GetLocalizedItem(key);
            if (string.IsNullOrEmpty(name)) return null;
            string table, missingKey;
            if (LocHelper.TryParseMissingTranslation(name, out table, out missingKey)) return null;
            return name;
        }

        /// <summary>
        /// 随机模组的物品 id 不是目录里的实体物品，游戏运行时才生成具体模组，
        /// 因此没有名称词条，GetLocalizedItem 会返回"缺词条"提示文本。
        /// </summary>
        private static string FallbackName(string identifier)
        {
            if (identifier == "random_performance_module") return Copy.Get("picker_random_performance_module");
            if (identifier == "random_efficiency_module") return Copy.Get("picker_random_efficiency_module");
            if (identifier == "random_quality_module") return Copy.Get("picker_random_quality_module");
            // 100 档的补给箱是预构建容器，没有独立词条，沿用同类箱子的叫法。
            if (identifier == "loot_crate_engineering") return Copy.Get("picker_engineering_crate");
            if (identifier == "loot_crate_service") return Copy.Get("picker_service_crate");
            if (identifier == "loot_crate_medical") return Copy.Get("picker_medical_crate");
            if (identifier == "loot_crate_security") return Copy.Get("picker_security_crate");
            return PrettyIdentifier(identifier);
        }

        /// <summary>
        /// 兜底显示名：去掉模组前缀，把 id 尾段的下划线拆成空格并首字母大写，
        /// 例如 better_scavenging.searchlight_battery → Searchlight Battery。
        /// </summary>
        private static string PrettyIdentifier(string identifier)
        {
            string tail = identifier;
            int dot = tail.LastIndexOf('.');
            if (dot > 0 && dot + 1 < tail.Length) tail = tail.Substring(dot + 1);
            string[] parts = tail.Split(new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return identifier;

            StringBuilder text = new StringBuilder(tail.Length);
            for (int i = 0; i < parts.Length; ++i)
            {
                if (i > 0) text.Append(' ');
                string part = parts[i];
                text.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) text.Append(part, 1, part.Length - 1);
            }
            return text.ToString();
        }

        internal static string Percent(float probability)
        {
            float pct = probability * 100f;
            if (pct < 0.1f) return "<0.1%";
            if (pct < 10f) return pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return Mathf.RoundToInt(pct).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void RefreshReputation()
        {
            if (reputationLabel == null) return;
            int unlocked = 0;
            foreach (Region region in Region.All) if (region.Unlocked) ++unlocked;
            reputationLabel.SetText(Copy.Get("picker_reputation") + Reputation.Points
                + "  ·  " + Copy.Get("picker_available") + unlocked + "/" + Region.All.Length
                + "  ·  " + Copy.Get("picker_base_reward_target") + (LootBalance.Stage(Reputation.Points) * 10) + "%");
            reputationLabel.SetTextColor(Accent);
        }

        private void Hover(Region region)
        {
            if (current != this) return;
            hovered = region;
            RefreshDetails();
        }

        private void ChoosePoint(Region region, Vector2 point)
        {
            if (current != this) return;
            selectedPoint = point;
            Select(region);
        }

        private void Select(Region region)
        {
            if (current != this) return;
            Region previous = selected;
            inspected = region;
            selected = region != null && region.Unlocked ? region : null;
            if (overview != null) overview.SetSelection(selected, selectedPoint);
            RefreshDetails();
            SoundFeedback.Selection(previous, selected);
        }

        private void RefreshDetails()
        {
            if (description == null || confirm == null) return;
            Region focus = hovered ?? inspected ?? selected;
            string details;
            if (focus == null)
                details = Copy.Get("picker_choose_a_search_area_on_the_map");
            else
            {
                details = (focus == selected ? Copy.Get("picker_selected") : Copy.Get("picker_preview"))
                    + focus.Name + " · " + focus.Detail;
                if (!focus.Unlocked)
                    details += Copy.Get("picker_needs_reputation") + focus.RequiredReputation
                        + Copy.Get("picker_current") + Reputation.Points + ")";
                else
                {
                    EffectiveTable table = Tuning.TableFor(focus);
                    if (table.RegionalEvent != null) details += "\n" + RegionalEvents.Details(focus, table);
                    else details += "\n" + Copy.Get("picker_no_regional_event_main_drop_ev")
                        + table.Ev.ToString("0.00", CultureInfo.InvariantCulture) + Copy.Get("picker_credits_item");
                }
            }
            if (eventSummary != null) eventSummary.SetText(RegionalEvents.TodaySummary());
            description.SetTooltip(details);
            description.SetText(details + "\n" + Copy.Get("picker_searches_left") + RemainingSearches());
            notice.SetInteractable(focus != null);
            confirm.SetText(selected == null ? Copy.Get("picker_choose_an_area")
                : target != null ? Copy.Get("picker_enter") + selected.Name
                : Copy.Get("picker_search") + selected.Name);
            if (confirm.text != null)
            {
                confirm.text.enableAutoSizing = true;
                confirm.text.fontSizeMin = 12;
                confirm.text.fontSizeMax = 18;
                confirm.text.enableWordWrapping = false;
                confirm.text.overflowMode = TextOverflowModes.Ellipsis;
            }
            confirm.SetTooltip(selected == null ? "" : selected.Name);
            confirm.SetInteractable(selected != null && selected.Unlocked
                && (target != null || ScavHelper.CanScavenge()));
            RefreshReputation();
        }

        internal static void RefreshIfOpen()
        {
            if (current == null || current.window == null || !current.window.IsAlive) return;
            if (current.overview != null) current.overview.Refresh();
            current.RefreshDetails();
        }

        private int RemainingSearches()
        {
            // 进场地图预览新一趟上限；场内兜底地图才显示当前剩余次数。
            return Math.Max(0, target != null ? ScavHelper.GetMaxScavAttempts() : ScavHelper.GetScavTimeLeft());
        }

        private void StartSearch()
        {
            if (current != this || selected == null) return;
            if (!selected.Unlocked)
            {
                Select(selected);
                return;
            }

            Region choice = selected;
            DumpingGroundLocation destination = target;
            if (destination != null)
            {
                // 前往途中：确认后按原版流程继续 Travel，选定区域随行程带入。
                if (!Core.Enabled || !destination.IsAvailable() || map.IsInTransit)
                {
                    Close();
                    return;
                }
                Close();
                ChosenRegion = choice;
                chosenAt = destination;
                StashFind.ResetTrip();
                resume();
                return;
            }

            // 兜底：已经在垃圾场里（读档等边缘情况）从「拾荒」按钮弹的地图。
            if (!Core.Enabled || !IsAtDump(map) || !ScavHelper.CanScavenge())
            {
                Close();
                return;
            }
            DumpingGroundLocation location = map.Current as DumpingGroundLocation;
            Close();
            // 记住这次选择，本次停留里后续拾荒直接沿用（Intercept 不会再弹地图）。
            ChosenRegion = choice;
            chosenAt = location;
            RegionBackdrop.Refresh(map);
            StashFind.ResetTrip();
            resume();
        }
    }
}
