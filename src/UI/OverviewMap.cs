using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace DumpingGroundMap
{
    /// <summary>Layered artwork and a single map-wide pointer surface.</summary>
    internal sealed class OverviewMap
    {
        private readonly RectTransform root;
        private readonly Graphic[] art = new Graphic[Region.All.Length];
        private readonly bool artAvailable;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[Region.All.Length];
        private readonly CustomUIElement[] eventBadges = new CustomUIElement[Region.All.Length];
        private readonly GameObject[] eventRoots = new GameObject[Region.All.Length];
        private readonly RectTransform[] nodes = new RectTransform[Region.All.Length];
        private readonly Action<Region> hover;
        private readonly Action<Region, Vector2> click;
        private readonly RectTransform marker;
        private readonly GameObject markerObject;
        private readonly TextMeshProUGUI markerText;
        private Region selected, hovered;

        internal OverviewMap(RectTransform parent, Vector2 mapSize, CustomUIManager ui, CustomUIWindow window,
            Action<Region> onHover, Action<Region, Vector2> onClick)
        {
            root = parent;
            hover = onHover;
            click = onClick;
            Sprite atlas = ui.GetModSprite(Core.Id, "dumping_ground_landmarks", true);
            artAvailable = atlas != null;
            // Draw upper rows first; lower landmarks naturally sit in front.
            int[] order = new int[Region.All.Length];
            for (int i = 0; i < order.Length; ++i) order[i] = i;
            Array.Sort(order, delegate(int a, int b) { return Region.All[b].Y.CompareTo(Region.All[a].Y); });
            foreach (int i in order)
            {
                Region r = Region.All[i];
                RectTransform node = Rect("Landmark " + r.Id, parent, new Vector2(r.X, r.Y));
                node.pivot = new Vector2(0.5f, 0f);
                node.sizeDelta = new Vector2(mapSize.x * MapArt.Width(r.Id), mapSize.y * MapArt.Height(r.Id));
                RawImage picture = node.gameObject.AddComponent<RawImage>();
                if (atlas != null)
                {
                    picture.texture = atlas.texture;
                    Rect f = MapArt.Frame(r.Id);
                    Rect sheet = atlas.rect;
                    picture.uvRect = new Rect((sheet.x + f.x * sheet.width) / atlas.texture.width,
                        (sheet.y + f.y * sheet.height) / atlas.texture.height,
                        f.width * sheet.width / atlas.texture.width, f.height * sheet.height / atlas.texture.height);
                }
                picture.raycastTarget = false;
                picture.color = artAvailable ? Color.white : Color.clear;
                art[i] = picture;
                nodes[i] = node;

                RectTransform labelRect = Rect("Name " + r.Id, parent, new Vector2(r.X, r.Y));
                labelRect.anchoredPosition = new Vector2(0f, -12f);
                labelRect.sizeDelta = new Vector2(Mathf.Min(180f, mapSize.x * 0.28f), 25f);
                Image labelShade = labelRect.gameObject.AddComponent<Image>();
                labelShade.color = new Color32(24, 22, 29, 215);
                labelShade.raycastTarget = false;
                CustomUIBuilder b = new CustomUIBuilder(ui, window, labelRect);
                b.AddLabel(r.Name, "name_" + r.Id);
                CustomUIElement label = b.Last;
                label.Rect.anchorMin = Vector2.zero;
                label.Rect.anchorMax = Vector2.one;
                label.Rect.offsetMin = new Vector2(3, 1);
                label.Rect.offsetMax = new Vector2(-3, -1);
                if (label.text != null)
                {
                    label.text.fontSize = 17;
                    label.text.enableAutoSizing = true;
                    label.text.fontSizeMin = 10;
                    label.text.fontSizeMax = 17;
                    label.text.enableWordWrapping = false;
                    label.text.overflowMode = TextOverflowModes.Ellipsis;
                    label.text.alignment = TextAlignmentOptions.Center;
                    label.text.raycastTarget = false;
                    labels[i] = label.text;
                }
                RectTransform eventRect = Rect("Event " + r.Id, parent, new Vector2(r.X, r.Y));
                eventRect.anchoredPosition = new Vector2(0f, -35f);
                eventRect.sizeDelta = new Vector2(Mathf.Min(180f, mapSize.x * 0.28f), 21f);
                Image eventShade = eventRect.gameObject.AddComponent<Image>();
                eventShade.color = new Color32(65, 47, 21, 242);
                eventShade.raycastTarget = false;
                CustomUIBuilder eventBuilder = new CustomUIBuilder(ui, window, eventRect);
                eventBuilder.AddLabel("", "event_" + r.Id);
                CustomUIElement badge = eventBuilder.Last;
                badge.Rect.anchorMin = Vector2.zero; badge.Rect.anchorMax = Vector2.one;
                badge.Rect.offsetMin = new Vector2(3, 0); badge.Rect.offsetMax = new Vector2(-3, 0);
                if (badge.text != null)
                {
                    badge.text.fontSize = 14;
                    badge.text.enableAutoSizing = true;
                    badge.text.fontSizeMin = 10; badge.text.fontSizeMax = 14;
                    badge.text.enableWordWrapping = false;
                    badge.text.overflowMode = TextOverflowModes.Ellipsis;
                    badge.text.alignment = TextAlignmentOptions.Center;
                    badge.text.color = Picker.Accent;
                    badge.text.raycastTarget = false;
                }
                eventBadges[i] = badge;
                eventRoots[i] = eventRect.gameObject;
            }

            marker = Rect("Search X", parent, Vector2.zero);
            marker.sizeDelta = new Vector2(38, 38);
            CustomUIBuilder markerBuilder = new CustomUIBuilder(ui, window, marker);
            markerBuilder.AddLabel("×", "search_marker");
            CustomUIElement mark = markerBuilder.Last;
            mark.Rect.anchorMin = Vector2.zero;
            mark.Rect.anchorMax = Vector2.one;
            mark.Rect.offsetMin = mark.Rect.offsetMax = Vector2.zero;
            markerText = mark.text;
            if (markerText != null)
            {
                markerText.fontSize = 42;
                markerText.fontStyle = FontStyles.Bold;
                markerText.alignment = TextAlignmentOptions.Center;
                markerText.color = Picker.Accent;
                markerText.raycastTarget = false;
                markerText.outlineWidth = 0.2f;
                markerText.outlineColor = new Color32(24, 20, 24, 255);
            }
            markerObject = marker.gameObject;
            markerObject.SetActive(false);

            RectTransform surface = Rect("Map pointer surface", parent, Vector2.zero);
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = surface.offsetMax = Vector2.zero;
            Image receiver = surface.gameObject.AddComponent<Image>();
            receiver.color = Color.clear;
            receiver.raycastTarget = true;
            surface.gameObject.AddComponent<MapPointerSurface>().Setup(this, surface);
            Paint();
        }

        private static RectTransform Rect(string name, RectTransform parent, Vector2 anchor)
        {
            RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            return rect;
        }

        internal void SetSelection(Region region, Vector2 point)
        {
            selected = region;
            markerObject.SetActive(region != null);
            if (region != null)
            {
                // Mark the actual ground click. Building clicks mark its ground anchor
                // so the X never covers the distinctive roof or machine silhouette.
                if (point.y > region.Y + 0.045f) point = new Vector2(region.X, region.Y + 0.015f);
                marker.anchorMin = marker.anchorMax = point;
                marker.anchoredPosition = new Vector2(0f, 7f);
            }
            Paint();
        }

        internal void Pointer(Vector2 screen, Camera camera, bool pressed)
        {
            Vector2 p;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, camera, out p)) return;
            Rect bounds = root.rect;
            Vector2 uv = new Vector2((p.x - bounds.xMin) / bounds.width, (p.y - bounds.yMin) / bounds.height);
            int index = MapLayout.Pick(uv.x, uv.y);
            Region region = index >= 0 ? Region.All[index] : null;
            if (region != hovered)
            {
                hovered = region;
                Paint();
                hover(region);
            }
            if (pressed) click(region, uv);
        }

        internal void Exit()
        {
            if (hovered == null) return;
            hovered = null;
            Paint();
            hover(null);
        }

        internal void Animate()
        {
            float step = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            for (int i = 0; i < nodes.Length; ++i)
            {
                Region r = Region.All[i];
                float scale = r == hovered ? (r.Unlocked ? 1.055f : 1.025f) : 1f;
                nodes[i].localScale = Vector3.Lerp(nodes[i].localScale, new Vector3(scale, scale, 1f), step);
            }
        }

        internal void Refresh() { Paint(); }

        private void Paint()
        {
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region r = Region.All[i];
                bool locked = !r.Unlocked;
                bool lit = r == hovered || r == selected;
                RegionalEvent effect = RegionalEvents.ActiveFor(r);
                if (eventRoots[i] != null) eventRoots[i].SetActive(effect != null);
                if (effect != null && eventBadges[i] != null)
                    eventBadges[i].SetText(Copy.Get("regionalevents_today") + effect.Title);
                if (artAvailable)
                    art[i].color = locked ? new Color(0.66f, 0.67f, 0.70f, 1f)
                        : lit ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f);
                if (labels[i] != null)
                {
                    labels[i].text = r.Name + (locked ? " · " + r.RequiredReputation : "");
                    labels[i].color = lit ? Picker.Accent : locked ? (Color)new Color32(149, 152, 154, 255) : Picker.Ink;
                }
            }
        }
    }

    internal sealed class MapPointerSurface : MonoBehaviour, IPointerEnterHandler,
        IPointerExitHandler, IPointerClickHandler
    {
        private OverviewMap owner;
        private Camera eventCamera;
        private bool inside;
        internal void Setup(OverviewMap map, RectTransform surface) { owner = map; }
        public void OnPointerEnter(PointerEventData data)
        {
            inside = true;
            eventCamera = data.enterEventCamera;
            owner.Pointer(data.position, eventCamera, false);
        }
        public void OnPointerExit(PointerEventData data) { inside = false; owner.Exit(); }
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                owner.Pointer(data.position, data.pressEventCamera, true);
        }
        private void Update()
        {
            if (owner == null) return;
            if (inside) owner.Pointer(Input.mousePosition, eventCamera, false);
            owner.Animate();
        }
        private void OnDisable() { inside = false; if (owner != null) owner.Exit(); }
    }
}
