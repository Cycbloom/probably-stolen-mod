namespace DumpingGroundMap
{
    /// <summary>Two complete loot profiles; unrelated UI and debug settings are excluded.</summary>
    internal sealed class LootPreset
    {
        internal readonly float Junk, High, Crate, Card, Minor, MinorValue, StashCap;
        internal readonly int SpecialtyTier;
        internal float SpecialtyPercent { get { return (SpecialtyTier + 1) * 25f; } }

        private LootPreset(float junk, float high, float crate, float card, float minor,
            float minorValue, float stashCap, int specialtyTier)
        {
            Junk = junk; High = high; Crate = crate; Card = card; Minor = minor;
            MinorValue = minorValue; StashCap = stashCap; SpecialtyTier = specialtyTier;
        }

        private static readonly LootPreset defaults = new LootPreset(Tuning.DefaultJunkPercent,
            Tuning.DefaultHighPercent, Tuning.DefaultSpecialPercent, Tuning.DefaultSpecialPercent,
            Tuning.DefaultMinorPercent, Tuning.DefaultMinorValue, Tuning.DefaultStashCap, Tuning.DefaultSpecialtyTier);
        private static readonly LootPreset enhanced = new LootPreset(50f, 5f, 1f, 1f, 5f, 250f, 2f, 3);

        internal static LootPreset For(int index) { return index == 1 ? enhanced : defaults; }
        internal LootSettings Settings { get { return new LootSettings(Junk, High, SpecialtyPercent, Crate, Card); } }
    }
}
