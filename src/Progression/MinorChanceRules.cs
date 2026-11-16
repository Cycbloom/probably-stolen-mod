using System;

namespace DumpingGroundMap
{
    /// <summary>One independent AIMD update per eligible search, shared across areas.</summary>
    internal static class MinorChanceRules
    {
        internal const float Step = 0.1f;
        internal static float StartFor(float cap) { return Math.Min(0.3f, Math.Max(0f, cap) * 0.1f); }

        internal static float Current(float window, float cap, bool adaptive)
        {
            if (float.IsNaN(cap) || cap <= 0f) return 0f;
            if (!adaptive) return cap;
            if (float.IsNaN(window) || float.IsInfinity(window)) window = StartFor(cap);
            return Math.Min(cap, Math.Max(StartFor(cap), window));
        }

        internal static void AfterSearch(ref float window, ref int phase, float cap, bool adaptive, bool won)
        {
            if (!adaptive || cap <= 0f || float.IsNaN(cap)) return;
            window = Current(window, cap, true);
            if (won)
            {
                window = Math.Max(StartFor(cap), window * 0.5f);
                phase = 1;
            }
            else if (phase == 0 && window < cap * 0.5f)
            {
                window *= 2f;
                if (window >= cap * 0.5f) phase = 1;
            }
            else window += Step;
            window = Math.Min(cap, window);
        }
    }
}
