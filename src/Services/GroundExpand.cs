using HarmonyLib;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DumpingGroundMap
{
    /// <summary>
    /// 拾荒地面扩展（原地放大，不接管别的窗口）。
    /// 只在原版 12×9、且地面空着的时候接手；尺寸只决定下次重建的地面，
    /// 不写存档、不新增存档键。
    /// </summary>
    internal static class GroundExpand
    {
        internal const int VanillaWidth = 12;
        internal const int VanillaHeight = 9;

        /// <summary>窗口离画布边缘至少留这么多像素，避免贴边被裁。</summary>
        private const int Margin = 8;

        /// <summary>让开右侧面板时与它保持的间隙。</summary>
        private const float PanelGap = 8f;

        private static EmporiumEntry appliedOwner;
        private static bool applied;
        private static bool visible;
        private static int scavengeDepth;
        internal static bool SearchActive { get { return scavengeDepth > 0; } }
        private static bool overflowNoticed;
        private static bool furnaceSpaceNoticed;

        internal static void Apply(EmporiumEntry entry)
        {
            if (entry == null) return;
            if (applied && ReferenceEquals(entry, appliedOwner)) return;
            GameGridInventory ground = entry.afterhourInventory;
            PixelWindow window = entry.afterhourWindow;
            if (ground == null || window == null) return;

            int grid = GridSize();
            // 用像素尺寸判断形状，避开 GridShape 里游戏自带、mod 编译环境拿不到的 ValueTuple 类型。
            int shapeWidth = ground.widthPixels / grid;
            int shapeHeight = ground.heightPixels / grid;
            if (shapeWidth <= 0 || shapeHeight <= 0)
            {
                ModLogger.Warning("[DGM-GROUND] 跳过：地面尺寸还没算出来");
                return;
            }

            // 兼容：只有原版 12×9 才接手，别的 mod 改过形状就完全不动。
            if (shapeWidth != VanillaWidth || shapeHeight != VanillaHeight)
            {
                ModLogger.Info("[DGM-GROUND] 跳过：地面形状是 " + shapeWidth + "x" + shapeHeight
                    + "，不是原版 12x9（可能已被其它 mod 接管）");
                return;
            }

            // 只放不缩：地面里已经有东西时不改形状，避免挤掉或丢失物品。
            if (ground.childItems.Count > 0)
            {
                ModLogger.Info("[DGM-GROUND] 跳过：地面已有 " + ground.childItems.Count
                    + " 件物品，保持现状不缩");
                return;
            }

            int wantWidth = Mathf.Clamp(Tuning.GroundWidth, Tuning.GroundWidthMin, Tuning.GroundWidthMax);
            int wantHeight = Mathf.Clamp(Tuning.GroundHeight, Tuning.GroundHeightMin, Tuning.GroundHeightMax);
            if (wantWidth <= VanillaWidth && wantHeight <= VanillaHeight)
            {
                ModLogger.Info("[DGM-GROUND] 跳过：设置是原版尺寸 " + wantWidth + "x" + wantHeight);
                return;
            }

            int canvasWidth;
            int canvasHeight;
            if (!CanvasSize(out canvasWidth, out canvasHeight))
            {
                ModLogger.Warning("[DGM-GROUND] 跳过：拿不到窗口画布尺寸");
                return;
            }

            // 12×9 时窗口里除格子之外的固定部分（边框、口袋、背包、标题）。
            int extraWidth = window.widthPixels - VanillaWidth * grid;
            int extraHeight = window.heightPixels - VanillaHeight * grid;
            int fitWidth = (canvasWidth - Margin * 2 - extraWidth) / grid;
            int fitHeight = (canvasHeight - Margin * 2 - extraHeight) / grid;

            int width = wantWidth;
            int height = wantHeight;
            if (width > fitWidth)
            {
                width = Mathf.Max(VanillaWidth, fitWidth);
                ModLogger.Warning("[DGM-GROUND] 画布宽 " + canvasWidth + " 放不下 " + wantWidth
                    + " 格，压到 " + width + " 格");
            }
            if (height > fitHeight)
            {
                height = Mathf.Max(VanillaHeight, fitHeight);
                ModLogger.Warning("[DGM-GROUND] 画布高 " + canvasHeight + " 放不下 " + wantHeight
                    + " 格，压到 " + height + " 格");
            }
            if (width <= VanillaWidth && height <= VanillaHeight)
            {
                ModLogger.Info("[DGM-GROUND] 跳过：画布太小，放不下比原版更大的地面");
                return;
            }

            ground.SetShape(width, height);
            window.Validate();
            // 先记下接管状态，之后的 Show / Validate 才会做重排。
            appliedOwner = entry;
            applied = true;
            // 这里绝对不能调 entry.Validate(true)：SetupAfterhourInv 是在 EmporiumEntry.Start
            // 中间被调的，那时 RenderHandler 还没准备好，Validate 会空引用并把 Start 打断，
            // 商店界面就只建了一半。位置留给 ShowAfterhourInv / Validate 的 postfix。

            ModLogger.Info("[DGM-GROUND] 地面 " + VanillaWidth + "x" + VanillaHeight
                + " → " + width + "x" + height
                + "，窗口 " + window.widthPixels + "x" + window.heightPixels
                + "，画布 " + canvasWidth + "x" + canvasHeight);
        }

        /// <summary>
        /// 按「地面对齐」的九宫格贴边摆放：左 / 中 / 右决定横向，上 / 中 / 下决定纵向，
        /// 四周留 Margin，并夹在画布内（窗口大于画布时贴左上）。
        /// 靠右时如果会压住右侧的原生面板（剩余时间 / 受伤概率 / 返回 / 拾荒按钮），
        /// 整块往左让到面板左侧再留 PanelGap，保证不挡按钮。
        /// </summary>
        internal static void Relayout(EmporiumEntry entry)
        {
            if (entry == null) return;
            bool owned = applied && ReferenceEquals(entry, appliedOwner);
            PixelWindow window = entry.afterhourWindow;
            if (window == null || window.rectTransform == null) return;

            int canvasWidth = entry.canvasWidth;
            int canvasHeight = entry.canvasHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                if (!CanvasSize(out canvasWidth, out canvasHeight)) return;
            }

            int width = window.widthPixels;
            int height = window.heightPixels;
            if (width <= 0 || height <= 0) return;

            bool furnace = Picker.IsAtDump(MapUIManager.Instance)
                && ModHelper.IsModRunning(ScavengingExpansionBridge.ModId);
            // 没扩地（玩家把地面设回 12×9，或扩地因地面已有物品被跳过）时，
            // 仍要按实际窗口尺寸给焚化炉让位；没有炉体时保持原版/其它模组的摆位。
            if (!owned && !furnace) return;

            int align = Mathf.Clamp(Tuning.GroundAlign, 0, 8);
            int column = align % 3;
            int row = align / 3;

            float maxX = Mathf.Max(0f, canvasWidth - width);
            float x = column == 0 ? Margin
                : column == 2 ? maxX - Margin
                : maxX / 2f;
            x = Mathf.Clamp(x, 0f, maxX);

            float maxTop = Mathf.Max(0f, canvasHeight - height);
            float top = row == 0 ? Margin
                : row == 2 ? maxTop - Margin
                : maxTop / 2f;
            top = Mathf.Clamp(top, 0f, maxTop);

            float alignedX = x;
            if (furnace) x = FurnaceLayoutRules.ReserveLeft(x, canvasWidth, canvasHeight, width, height, Margin);
            x = column == 2 || furnace ? AvoidNativePanel(x, top, width, height, maxX) : x;
            if (furnace && x > alignedX + 0.5f && !furnaceSpaceNoticed)
            {
                furnaceSpaceNoticed = true;
                ModLogger.Info("[DGM-GROUND] Reserved Scavenging Expansion furnace space: ground left=" + x);
            }

            // anchoredPosition 的 y 向下为负。
            window.rectTransform.anchoredPosition = new Vector2(x, -top);
        }

        /// <summary>
        /// 右侧原生面板（_ULScavenging 里的时间 / 受伤提示与返回、拾荒按钮）
        /// 与地面窗口重叠时，把地面整块让到面板左边。
        /// </summary>
        private static float AvoidNativePanel(float x, float top, int width, int height, float maxX)
        {
            MapUIManager map = MapUIManager.Instance;
            RenderHandler handler = RenderHandler.current;
            if (map == null || map.UIScavengingPanel == null
                || handler == null || handler.windowUICanvasObject == null) return x;
            RectTransform canvas = handler.windowUICanvasObject.GetComponent<RectTransform>();
            if (canvas == null) return x;

            float panelLeft;
            float panelTop;
            float panelRight;
            float panelBottom;
            if (!NativePanelRect(map.UIScavengingPanel, canvas,
                out panelLeft, out panelTop, out panelRight, out panelBottom)) return x;

            bool overlapsHorizontally = x + width > panelLeft && x < panelRight;
            bool overlapsVertically = top + height > panelTop && top < panelBottom;
            if (!overlapsHorizontally || !overlapsVertically) return x;

            float moved = panelLeft - PanelGap - width;
            if (moved < Margin) moved = Margin;
            return Mathf.Clamp(moved, 0f, maxX);
        }

        /// <summary>
        /// 把右侧原生面板的可见元素（按钮、文字）合起来当成一个矩形。
        /// 坐标换算成「从画布左边 / 顶边算起」的像素，与 anchoredPosition 同一套。
        /// 铺满整个画布的容器不算，否则地面永远被推走。
        /// </summary>
        private static bool NativePanelRect(GameObject root, RectTransform canvas,
            out float left, out float top, out float right, out float bottom)
        {
            left = top = float.MaxValue;
            right = bottom = float.MinValue;
            Rect canvasRect = canvas.rect;
            bool any = false;

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; ++i)
            {
                if (buttons[i] == null) continue;
                any |= IncludeRect(buttons[i].transform as RectTransform, canvas, canvasRect,
                    ref left, ref top, ref right, ref bottom);
            }
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; ++i)
            {
                if (labels[i] == null) continue;
                any |= IncludeRect(labels[i].transform as RectTransform, canvas, canvasRect,
                    ref left, ref top, ref right, ref bottom);
            }

            if (!any) left = top = right = bottom = 0f;
            return any;
        }

        private static bool IncludeRect(RectTransform target, RectTransform canvas, Rect canvasRect,
            ref float left, ref float top, ref float right, ref float bottom)
        {
            if (target == null) return false;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas, target);
            float l = bounds.min.x - canvasRect.xMin;
            float r = bounds.max.x - canvasRect.xMin;
            float t = canvasRect.yMax - bounds.max.y;
            float b = canvasRect.yMax - bounds.min.y;
            // 跳过铺满画布的容器（例如面板根节点）。
            if (r - l >= canvasRect.width * 0.9f && b - t >= canvasRect.height * 0.9f) return false;
            left = Mathf.Min(left, l);
            top = Mathf.Min(top, t);
            right = Mathf.Max(right, r);
            bottom = Mathf.Max(bottom, b);
            return true;
        }

        private static int GridSize()
        {
            UISettings ui = UISettings.current;
            return ui == null || ui.gridSize <= 0 ? 16 : ui.gridSize;
        }

        private static bool CanvasSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            RenderHandler handler = RenderHandler.current;
            if (handler == null || handler.windowUICanvasObject == null) return false;
            RectTransform canvas = handler.windowUICanvasObject.GetComponent<RectTransform>();
            if (canvas == null) return false;
            Vector2 size = canvas.sizeDelta;
            width = (int)size.x;
            height = (int)size.y;
            return width > 0 && height > 0;
        }

        internal static void BeginScavenge()
        {
            if (scavengeDepth++ == 0) overflowNoticed = false;
        }

        internal static void EndScavenge()
        {
            scavengeDepth = System.Math.Max(0, scavengeDepth - 1);
        }

        internal static void ResetSearch()
        {
            scavengeDepth = 0;
            overflowNoticed = false;
        }

        /// <summary>地面窗口当前是否显示（ShowAfterhourInv / HideAfterhourInv 维护）。</summary>
        internal static bool Visible
        {
            get { return visible; }
        }

        internal static void SetVisible(bool value)
        {
            visible = value;
        }

        /// <summary>SetupAfterhourInv 每次都会重建一块新的地面，接管状态要跟着重置。</summary>
        internal static void ResetForSetup()
        {
            scavengeDepth = 0;
            overflowNoticed = false;
            applied = false;
            furnaceSpaceNoticed = false;
            appliedOwner = null;
            visible = false;
        }

        /// <summary>
        /// 地面放不下时 TryAcceptAll 会把剩下的件数返回给调用方，而拾荒那边直接丢掉。
        /// 这里只在「这次拾荒」里第一次发生时提示一次，避免满地面时刷屏。
        /// </summary>
        internal static void OnRejected(GraphNodeStorage storage)
        {
            if (!Core.Enabled || scavengeDepth == 0 || overflowNoticed) return;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || !ReferenceEquals(storage, entry.afterhourInventory)) return;
            overflowNoticed = true;
            // 装了拾荒拓展时，满出来的战利品会被它整批收进暂存区，腾出空位后
            // 再自动补到地面，并不是当场丢掉；文案按实际行为分支。
            bool stash = ModHelper.IsModRunning(ScavengingExpansionBridge.ModId);
            string text = stash
                ? Copy.Get("groundexpand_the_ground_is_full_so_some_loot_2")
                : Copy.Get("groundexpand_the_ground_is_full_so_some_loot");
            if (Reputation.Points >= PerkCatalog.Perk40Threshold)
            {
                text += Copy.Get("groundexpand_when_you_leave_you_can_pay_a");
                if (stash)
                {
                    text += Copy.Get("groundexpand_the_helper_cannot_reach_the_overflow_storage");
                }
            }
            else
            {
                text += Copy.Get("groundexpand_move_items_into_your_pockets_or_backpack");
            }
            StashFind.ShowNotice(text, "ground-full");
        }
    }

    internal static class FurnaceLayoutRules
    {
        internal static float ReserveLeft(float x, float canvasWidth, float canvasHeight,
            float groundWidth, float groundHeight, float margin)
        {
            // ScrapFurnace.Tick places its body to the LEFT of afterhourWindow:
            // max width 360*scale, gap 12*scale; below 64*scale it hides the root.
            float scale = Math.Min(canvasWidth / 1920f, canvasHeight / 1080f);
            float furnaceWidth = Math.Min(360f * scale, groundHeight * 96f / 88f);
            float room = Math.Max(0f, canvasWidth - groundWidth - margin);
            float required = furnaceWidth + 12f * scale + margin;
            return Math.Max(x, Math.Min(required, room));
        }
    }
}
