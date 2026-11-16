using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Map-space hit areas; UV origin is at the bottom left, independent of screen size.</summary>
    internal static class MapLayout
    {
        internal static bool Inside(float x, float y)
        {
            return x >= 0.035f && x <= 0.965f && y >= 0.065f && y <= 0.955f;
        }

        internal static int Pick(float x, float y)
        {
            if (!Inside(x, y)) return -1;
            // Buildings are selectable above their ground anchor. Prefer the frontmost
            // silhouette where two generous authored hit areas meet.
            int hit = -1;
            float front = 2f;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region r = Region.All[i];
                if (Mathf.Abs(x - r.X) <= MapArt.Width(r.Id) * 0.5f + 0.008f && y >= r.Y - 0.008f
                    && y <= r.Y + MapArt.Height(r.Id) + 0.005f && r.Y < front)
                {
                    hit = i;
                    front = r.Y;
                }
            }
            if (hit >= 0) return hit;
            // Small patches of ground around each landmark also belong to that area.
            // A finite radius leaves the enclosing scrap walls and remote gaps inert.
            float best = 1f;
            for (int i = 0; i < Region.All.Length; ++i)
            {
                Region r = Region.All[i];
                float dx = (x - r.X) / 0.16f;
                float dy = (y - r.Y) / 0.085f;
                float distance = dx * dx + dy * dy;
                if (distance <= best) { best = distance; hit = i; }
            }
            return hit;
        }

        internal static Vector2 LandmarkPoint(Region region)
        {
            return new Vector2(region.X, region.Y);
        }
    }
}
