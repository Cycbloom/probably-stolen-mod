using System;

namespace DumpingGroundMap
{
    /// <summary>Inputs to probability calculation, independent of config and UI state.</summary>
    internal struct LootSettings : IEquatable<LootSettings>
    {
        internal readonly float JunkPercent, HighPercent, SpecialtyPercent, CratePercent, KeycardPercent;

        internal LootSettings(float junk, float high, float specialty, float crate, float card)
        {
            JunkPercent = junk;
            HighPercent = high;
            SpecialtyPercent = specialty;
            CratePercent = crate;
            KeycardPercent = card;
        }

        public bool Equals(LootSettings other)
        {
            return JunkPercent == other.JunkPercent && HighPercent == other.HighPercent
                && SpecialtyPercent == other.SpecialtyPercent && CratePercent == other.CratePercent
                && KeycardPercent == other.KeycardPercent;
        }
    }

    internal static class ProbabilityMath
    {
        internal static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            return Math.Min(max, Math.Max(min, value));
        }
    }
}
