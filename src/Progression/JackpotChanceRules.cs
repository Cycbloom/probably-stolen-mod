using System;

namespace DumpingGroundMap
{
    /// <summary>Shared chance calculation for the actual roll and the loot notice.</summary>
    internal static class JackpotChanceRules
    {
        internal const float Start = 0.05f;
        internal const float Step = 0.05f;

        internal static float Current(float window, float cap, bool useWindow)
        {
            if (!useWindow) return cap;
            if (float.IsNaN(window)) window = Start;
            return Math.Min(cap, Math.Max(Start, window));
        }

        internal static void AfterHit(ref float window, ref int phase, float cap, bool useWindow)
        {
            if (!useWindow) return; // Fixed mode does not erase saved window progress.
            window = Current(Current(window, cap, true) * 0.5f, cap, true);
            phase = 1;
        }

        internal static void AfterMiss(ref float window, ref int phase, float cap, bool useWindow)
        {
            if (!useWindow) return;
            window = Current(window, cap, true);
            if (phase == 0 && window < cap * 0.5f)
            {
                window *= 2f;
                if (window >= cap * 0.5f) phase = 1;
            }
            else
            {
                window += Step;
                phase = 1;
            }
            window = Math.Min(window, cap);
        }
    }
}
