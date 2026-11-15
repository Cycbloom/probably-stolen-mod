using HarmonyLib;
using UnityEngine;

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

        private static EmporiumEntry appliedOwner;
        private static bool applied;
        private static bool visible;
        private static bool scavenging;
        private static bool overflowNoticed;

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
                Debug.LogWarning("[DGM-GROUND] 跳过：地面尺寸还没算出来");
                return;
            }

            // 兼容：只有原版 12×9 才接手，别的 mod 改过形状就完全不动。
            if (shapeWidth != VanillaWidth || shapeHeight != VanillaHeight)
            {
                Debug.Log("[DGM-GROUND] 跳过：地面形状是 " + shapeWidth + "x" + shapeHeight
                    + "，不是原版 12x9（可能已被其它 mod 接管）");
                return;
            }

            // 只放不缩：地面里已经有东西时不改形状，避免挤掉或丢失物品。
            if (ground.childItems.Count > 0)
            {
                Debug.Log("[DGM-GROUND] 跳过：地面已有 " + ground.childItems.Count
                    + " 件物品，保持现状不缩");
                return;
            }

            int wantWidth = Mathf.Clamp(Tuning.GroundWidth, Tuning.GroundWidthMin, Tuning.GroundWidthMax);
            int wantHeight = Mathf.Clamp(Tuning.GroundHeight, Tuning.GroundHeightMin, Tuning.GroundHeightMax);
            if (wantWidth <= VanillaWidth && wantHeight <= VanillaHeight)
            {
                Debug.Log("[DGM-GROUND] 跳过：设置是原版尺寸 " + wantWidth + "x" + wantHeight);
                return;
            }

            int canvasWidth;
            int canvasHeight;
            if (!CanvasSize(out canvasWidth, out canvasHeight))
            {
                Debug.LogWarning("[DGM-GROUND] 跳过：拿不到窗口画布尺寸");
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
                Debug.LogWarning("[DGM-GROUND] 画布宽 " + canvasWidth + " 放不下 " + wantWidth
                    + " 格，压到 " + width + " 格");
            }
            if (height > fitHeight)
            {
                height = Mathf.Max(VanillaHeight, fitHeight);
                Debug.LogWarning("[DGM-GROUND] 画布高 " + canvasHeight + " 放不下 " + wantHeight
                    + " 格，压到 " + height + " 格");
            }
            if (width <= VanillaWidth && height <= VanillaHeight)
            {
                Debug.Log("[DGM-GROUND] 跳过：画布太小，放不下比原版更大的地面");
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

            Debug.Log("[DGM-GROUND] 地面 " + VanillaWidth + "x" + VanillaHeight
                + " → " + width + "x" + height
                + "，窗口 " + window.widthPixels + "x" + window.heightPixels
                + "，画布 " + canvasWidth + "x" + canvasHeight);
        }

        /// <summary>
        /// 放大后的重排：优先留在原位置；如果纵向会压到前后柜台（商店区），
        /// 就整块挪到左右两侧里横向重叠更小的一边，并保证不超出画布。
        /// </summary>
        internal static void Relayout(EmporiumEntry entry)
        {
            if (!applied || !ReferenceEquals(entry, appliedOwner)) return;
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

            float x = canvasWidth / 2.1f - width / 2f;
            float y = height + Margin * 2 <= canvasHeight
                ? -(canvasHeight - height - Margin)
                : -(canvasHeight - height);

            float shopLeft;
            float shopTop;
            float shopRight;
            float shopBottom;
            if (ShopRect(entry, out shopLeft, out shopTop, out shopRight, out shopBottom))
            {
                // anchoredPosition 的 y 向下为负，换算成「从画布顶边往下」的行号。
                float top = -y - height;
                float bottom = -y;
                if (bottom > shopTop && top < shopBottom)
                {
                    float left = Margin;
                    float right = canvasWidth - Margin - width;
                    float overlapNow = Overlap(x, x + width, shopLeft, shopRight);
                    float overlapLeft = Overlap(left, left + width, shopLeft, shopRight);
                    float overlapRight = Overlap(right, right + width, shopLeft, shopRight);
                    if (overlapLeft < overlapNow && overlapLeft <= overlapRight) x = left;
                    else if (overlapRight < overlapNow) x = right;
                }
            }

            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, canvasWidth - width));
            window.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        private static float Overlap(float left, float right, float shopLeft, float shopRight)
        {
            float start = Mathf.Max(left, shopLeft);
            float end = Mathf.Min(right, shopRight);
            return end > start ? end - start : 0f;
        }

        /// <summary>前后柜台两个窗口合起来当成「商店区」。</summary>
        private static bool ShopRect(EmporiumEntry entry, out float left, out float top,
            out float right, out float bottom)
        {
            left = top = float.MaxValue;
            right = bottom = float.MinValue;
            bool any = Include(entry.backInvWindow, ref left, ref top, ref right, ref bottom);
            any |= Include(entry.frontInvWindow, ref left, ref top, ref right, ref bottom);
            if (!any) left = top = right = bottom = 0f;
            return any;
        }

        private static bool Include(PixelWindow window, ref float left, ref float top,
            ref float right, ref float bottom)
        {
            if (window == null || window.rectTransform == null) return false;
            if (window.widthPixels <= 0 || window.heightPixels <= 0) return false;
            Vector2 position = window.rectTransform.anchoredPosition;
            left = Mathf.Min(left, position.x);
            top = Mathf.Min(top, -position.y);
            right = Mathf.Max(right, position.x + window.widthPixels);
            bottom = Mathf.Max(bottom, -position.y + window.heightPixels);
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
            scavenging = true;
            overflowNoticed = false;
        }

        internal static void EndScavenge()
        {
            scavenging = false;
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
            applied = false;
            appliedOwner = null;
            visible = false;
        }

        /// <summary>
        /// 地面放不下时 TryAcceptAll 会把剩下的件数返回给调用方，而拾荒那边直接丢掉。
        /// 这里只在「这次拾荒」里第一次发生时提示一次，避免满地面时刷屏。
        /// </summary>
        internal static void OnRejected(GraphNodeStorage storage)
        {
            if (!Core.Enabled || !scavenging || overflowNoticed) return;
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || !ReferenceEquals(storage, entry.afterhourInventory)) return;
            overflowNoticed = true;
            string text = Copy.Text(
                "地面放满了，这次有战利品没能放进来，已经被丢掉。",
                "The ground is full, so some loot could not be placed and was dropped.");
            if (Reputation.Points >= Reputation.Perk40Threshold)
            {
                text += Copy.Text(
                    " 离开时可以花 50 信用点请拾荒者帮手，把地面上的东西搬回店里。",
                    " When leaving, you can hire a scavenger helper for 50 credits to carry what is left back to the store.");
            }
            else
            {
                text += Copy.Text(
                    "先把地面的东西搬进口袋或背包。",
                    " Move items into your pockets or backpack first.");
            }
            StashFind.ShowNotice(text, "ground-full");
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "SetupAfterhourInv")]
    internal static class AfterhourGroundPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            if (!Core.Enabled) return;
            try
            {
                GroundExpand.ResetForSetup();
                GroundExpand.Apply(__instance);
            }
            catch (System.Exception error)
            {
                // 这里在 EmporiumEntry.Start 中间执行，抛出去会把整个商店界面建一半就断掉，
                // 所以只记日志、放弃扩展，宁可地面保持原版尺寸。
                Debug.LogWarning("[DGM-GROUND] 跳过：初始化地面时出错 " + error);
            }
        }
    }

    /// <summary>画布尺寸变化时原生 Validate 会把地面放回右下角；这里按放大后的尺寸重新摆。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "Validate")]
    internal static class AfterhourGroundLayoutPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            // 只在拾荒场景、地面窗口真正显示时才重排；商店里的 Validate 一律不碰。
            if (!Core.Enabled || !GroundExpand.Visible) return;
            GroundExpand.Relayout(__instance);
        }
    }

    /// <summary>进入拾荒场景、地面窗口显示时才补一次接管与摆位。</summary>
    [HarmonyPatch(typeof(EmporiumEntry), "ShowAfterhourInv")]
    internal static class AfterhourGroundShowPatch
    {
        private static void Postfix(EmporiumEntry __instance)
        {
            if (!Core.Enabled) return;
            GroundExpand.SetVisible(true);
            GroundExpand.Apply(__instance);
            GroundExpand.Relayout(__instance);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "HideAfterhourInv")]
    internal static class AfterhourGroundHidePatch
    {
        private static void Postfix()
        {
            GroundExpand.SetVisible(false);
        }
    }

    [HarmonyPatch(typeof(ScavHelper), "ScavengeDumpingGrounds")]
    internal static class ScavengeGroundScopePatch
    {
        private static void Prefix() { GroundExpand.BeginScavenge(); }
        private static void Postfix() { GroundExpand.EndScavenge(); }
    }

    [HarmonyPatch(typeof(GraphUtils), "TryAcceptAll")]
    internal static class GroundOverflowPatch
    {
        private static void Postfix(GraphNodeStorage currentNode, int __result)
        {
            if (__result <= 0) return;
            GroundExpand.OnRejected(currentNode);
        }
    }
}
