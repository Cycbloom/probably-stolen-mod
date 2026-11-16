using UnityEngine;

namespace DumpingGroundMap
{
    /// <summary>Authored frames in the 1774x887 transparent atlas; no runtime texture mutation.</summary>
    internal static class MapArt
    {
        internal static Rect Frame(string id)
        {
            // Pixel coordinates below use the image's top-left origin. UV uses bottom-left.
            switch (id)
            {
                case "gate": return Uv(12, 188, 427, 220);
                case "scrap": return Uv(455, 161, 423, 245);
                case "refuse": return Uv(892, 175, 428, 233);
                case "med": return Uv(1334, 172, 428, 227);
                case "weapons": return Uv(13, 556, 427, 264);
                case "electronics": return Uv(452, 592, 429, 225);
                case "vehicles": return Uv(893, 589, 429, 230);
                case "water": return Uv(1335, 562, 428, 258);
                default: return new Rect(0f, 0f, 0f, 0f);
            }
        }

        private static Rect Uv(float x, float y, float width, float height)
        {
            return new Rect(x / 1774f, 1f - (y + height) / 887f, width / 1774f, height / 887f);
        }

        internal static float Width(string id)
        {
            // Smaller back-row structures preserve depth and leave the perimeter visible.
            switch (id)
            {
                case "gate": case "refuse": return 0.24f;
                case "med": return 0.21f;
                case "scrap": return 0.22f;
                case "weapons": case "water": return 0.19f;
                case "vehicles": return 0.26f;
                default: return 0.25f;
            }
        }

        internal static float Height(string id)
        {
            Rect frame = Frame(id);
            return frame.width > 0f ? Width(id) * frame.height / frame.width : 0f;
        }
    }
}
