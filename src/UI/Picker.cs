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
        private readonly CustomUIElement[] pins = new CustomUIElement[Region.All.Length];
        private readonly Image[] pinRings = new Image[Region.All.Length];
        private Region selected;
        private CustomUIWindow window;
        private GameObject shade;
        private CustomUIElement description, confirm, reputationLabel;

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
                Debug.LogError("[DumpingGroundMap] Could not open map: " + ex);
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
            if (map.IsInTransit || !destination.IsAvailable() || !ScavHelper.CanScavenge())
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
                Debug.LogError("[DumpingGroundMap] Could not open map: " + ex);
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
                ResetChoice();
        }

        internal static void ResetChoice()
        {
            ChosenRegion = null;
            chosenAt = null;
        }

        private void Build()
        {
            CustomUIManager ui = CustomUIManager.Instance;
            CustomUIBuilder b = ui.CreateWindow(WindowId, Copy.Text("垃圾场 · 拾荒地图", "Dumping Ground · Search Map"),
                CustomUIManager.LAYER_OVERLAY, null, CustomUIManager.Style.Meta);
            window = b.Handle;
            b.SetSize(740, 624).Center().SetDraggable(false).SetCloseOnEscape(true).OnClose(Cleanup);

            shade = new GameObject("DumpingGroundMapShade", typeof(RectTransform), typeof(Image));
            RectTransform cover = (RectTransform)shade.transform;
            cover.SetParent(window.Rect.parent, false);
            cover.anchorMin = Vector2.zero;
            cover.anchorMax = Vector2.one;
            cover.offsetMin = Vector2.zero;
            cover.offsetMax = Vector2.zero;
            cover.SetSiblingIndex(window.Rect.GetSiblingIndex());
            shade.GetComponent<Image>().color = new Color32(9, 16, 19, 183);

            b.AddLabel(Copy.Text("选一个地标即进入该区域；进入后点“拾荒”直接按该区的物品表拾取，本次停留内不再询问。告示板可查看该区物品概率。",
                "Pick a landmark to enter that area; after that, Search uses its table for the rest of the visit. Open a notice board to see its item chances."));
            Size(b.Last, -1, 44);
            b.AddSpace(6);
            Sprite background = ui.GetModSprite(Core.Id, "dumping_ground_map", true);
            b.AddImage(background, 680, 328, "map");
            CustomUIElement mapPanel = b.Last;
            Size(mapPanel, -1, 328);
            mapPanel.SetImageColor(background == null ? (Color)new Color32(39, 60, 56, 255) : Color.white);
            // Keep click targets tied to the same rectangle as the map image.
            RectTransform graphic = mapPanel.image.rectTransform;

            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                Vector2 anchor = new Vector2(region.X, region.Y);
                RectTransform pinRect = PinRect(graphic, "Landmark " + (i + 1), anchor);
                Image ring = pinRect.gameObject.AddComponent<Image>();
                Sprite ringSprite = ui.GetSprite("border_white");
                if (ringSprite != null)
                {
                    ring.sprite = ringSprite;
                    ring.type = Image.Type.Sliced;
                }
                ring.raycastTarget = false;
                pinRings[i] = ring;
                CustomUIBuilder pinBuilder = new CustomUIBuilder(ui, window, pinRect);
                // 未解锁的地标在编号后带上门槛，按钮同时压暗，点开仍会解释原因。
                string pinLabel = (i + 1) + "  " + region.Name;
                if (region.RequiredReputation > 0) pinLabel += " · " + region.RequiredReputation;
                pinBuilder.AddButton(pinLabel, () => Select(region), "area_" + i);
                CustomUIElement pin = pinBuilder.Last;
                pin.Rect.anchorMin = Vector2.zero;
                pin.Rect.anchorMax = Vector2.one;
                pin.Rect.offsetMin = new Vector2(2, 2);
                pin.Rect.offsetMax = new Vector2(-2, -2);
                pins[i] = pin;

                RectTransform boardRect = PinRect(graphic, "Notice " + (i + 1), anchor);
                boardRect.anchoredPosition = new Vector2(0f, -40f);
                boardRect.sizeDelta = new Vector2(110f, 26f);
                CustomUIBuilder boardBuilder = new CustomUIBuilder(ui, window, boardRect);
                boardBuilder.AddButton(Copy.Text("告示板", "Notice"), () =>
                {
                    Select(region);
                    LootNoticeView.Open(region);
                }, "area_board_" + i);
                CustomUIElement boardButton = boardBuilder.Last;
                boardButton.Rect.anchorMin = Vector2.zero;
                boardButton.Rect.anchorMax = Vector2.one;
                boardButton.Rect.offsetMin = Vector2.zero;
                boardButton.Rect.offsetMax = Vector2.zero;
            }

            b.AddSpace(6).AddLabel("", "reputation");
            reputationLabel = b.Last;
            Size(reputationLabel, -1, 24);
            if (reputationLabel.text != null) reputationLabel.text.alignment = TextAlignmentOptions.MidlineLeft;
            RefreshReputation();

            b.AddSpace(8).AddLabel("", "description");
            description = b.Last;
            Size(description, -1, 64);
            if (description.text != null) description.text.alignment = TextAlignmentOptions.MidlineLeft;
            b.AddSpace(8).BeginRow(12, TextAnchor.MiddleRight);
            b.AddButton(Copy.Text("取消", "Cancel"), Close);
            Size(b.Last, 130, 36);
            b.AddButton(target != null
                ? Copy.Text("进入该区域", "Enter this area")
                : Copy.Text("开始拾荒", "Start search"), StartSearch);
            confirm = b.Last;
            Size(confirm, 178, 36);
            b.End();
            Select(null);
            b.Show();
        }

        private static RectTransform PinRect(RectTransform parent, string name, Vector2 anchor)
        {
            RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(172, 42);
            return rect;
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
            string key;
            if (!NameKeys.TryGetValue(identifier, out key))
                key = "item_" + identifier + "_name";

            string name = LocHelper.GetLocalizedItem(key);
            string table, missingKey;
            if (string.IsNullOrEmpty(name) || LocHelper.TryParseMissingTranslation(name, out table, out missingKey))
                return FallbackName(identifier);
            return name;
        }

        /// <summary>
        /// 随机模组的物品 id 不是目录里的实体物品，游戏运行时才生成具体模组，
        /// 因此没有名称词条，GetLocalizedItem 会返回"缺词条"提示文本。
        /// </summary>
        private static string FallbackName(string identifier)
        {
            if (identifier == "random_performance_module") return Copy.Text("随机性能模组", "Random Performance Module");
            if (identifier == "random_efficiency_module") return Copy.Text("随机效率模组", "Random Efficiency Module");
            if (identifier == "random_quality_module") return Copy.Text("随机品质模组", "Random Quality Module");
            // 80 档的补给箱是预构建容器，没有独立词条，沿用同类箱子的叫法。
            if (identifier == "loot_crate_engineering") return Copy.Text("工程箱", "Engineering Crate");
            if (identifier == "loot_crate_service") return Copy.Text("服务箱", "Service Crate");
            if (identifier == "loot_crate_medical") return Copy.Text("医疗箱", "Medical Crate");
            if (identifier == "loot_crate_security") return Copy.Text("安保箱", "Security Crate");
            return identifier;
        }

        internal static string Percent(float probability)
        {
            float pct = probability * 100f;
            if (pct < 0.1f) return "<0.1%";
            if (pct < 10f) return pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return Mathf.RoundToInt(pct).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>地图下方那行：当前声望 + 各高级区门槛。</summary>
        private void RefreshReputation()
        {
            if (reputationLabel == null) return;
            string text = Copy.Text("声望 ", "Reputation ") + Reputation.Points;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region region = Region.All[i];
                if (region.RequiredReputation <= 0) continue;
                text += "  ·  " + region.Name + " " + region.RequiredReputation;
            }
            reputationLabel.SetText(text);
            reputationLabel.SetTextColor(Accent);
        }

        private void PaintPins()
        {
            for (int i = 0; i < pins.Length; ++i)
            {
                Region pinRegion = Region.All[i];
                bool active = pinRegion == selected;
                bool locked = !pinRegion.Unlocked;
                pins[i].SetTextColor(active ? Accent : (locked ? LockedInk : Ink));
                pinRings[i].color = active ? Accent : (locked ? LockedRing : RingInk);
            }
        }

        private void Select(Region region)
        {
            if (current != this) return;
            if (region != null && !region.Unlocked)
            {
                // 锁定区：不选中，只解释门槛，避免“点了没反应”。
                selected = null;
                PaintPins();
                int left = Math.Max(0, ScavHelper.GetScavTimeLeft());
                string need = Copy.Text("需要声望 ", "Needs ") + region.RequiredReputation
                    + Copy.Text(" 才能进入「", " reputation to enter ") + region.Name
                    + Copy.Text("」；当前声望 ", "\"; you have ") + Reputation.Points + "。";
                description.SetText(need + "\n" + Copy.Text("剩余拾荒次数：", "Searches left: ") + left);
                confirm.SetInteractable(false);
                return;
            }

            selected = region;
            PaintPins();

            int remainingSearches = Math.Max(0, ScavHelper.GetScavTimeLeft());
            string remaining = Copy.Text("剩余拾荒次数：", "Searches left: ") + remainingSearches;
            if (selected == null)
            {
                string hint = target != null
                    ? Copy.Text("点击地图上的编号地标，再点“进入该区域”前往此地。", "Select a numbered landmark, then choose \"Enter this area\".")
                    : Copy.Text("点击地图上的编号地标查看掉落倾向。", "Select a numbered landmark to see its loot bias.");
                description.SetText(hint + "\n" + remaining);
            }
            else
            {
                description.SetText(selected.Detail + "\n" + remaining);
            }
            confirm.SetInteractable(selected != null && (target != null || ScavHelper.CanScavenge()));
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
            StashFind.ResetTrip();
            resume();
        }
    }
}
