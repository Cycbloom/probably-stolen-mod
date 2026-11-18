using System;

namespace DumpingGroundMap
{
    /// <summary>Reuse vanilla clips and sources, including the game's volume and pause controls.</summary>
    internal static class SoundFeedback
    {
        internal static void Selection(Region previous, Region selected)
        {
            if (selected != null && selected != previous && selected.Unlocked)
                Play("selection", "click_vintage", false);
        }

        internal static void ReputationGained(int before, int after)
        {
            foreach (Region region in Region.All)
            {
                if (before < region.RequiredReputation && after >= region.RequiredReputation)
                {
                    // Several regions can open together; one cue per actual gain.
                    Play("region-unlocked", "module_insert", false);
                    return;
                }
            }
        }

        internal static void Jackpot()
        {
            // Scavenging uses source 2. Source 3 preserves its debris sound;
            // an unlock earned by the same reward uses the primary source.
            Play("jackpot", "scanner_activate", true);
        }

        private static void Play(string action, string clip, bool thirdSource)
        {
            if (!Core.Enabled) return;
            try
            {
                bool played = PlayNative(clip, thirdSource);
                if (played) ModLogger.Info("[sound] event=" + action + " clip=" + clip);
                else ModLogger.Warning("[sound] unavailable: event=" + action + " clip=" + clip);
            }
            catch (Exception ex)
            {
                // Feedback must not interrupt selection, reputation or a paid reward.
                ModLogger.Warning("[sound] failed: event=" + action + ": " + ex.Message);
            }
        }

        private static bool PlayNative(string clip, bool thirdSource)
        {
            // Never initialize Unity audio during loading or after shutdown.
            if (!AudioManager.Exists) return false;
            AudioManager audio = AudioManager.Instance;
            return thirdSource ? audio.Play3(clip) : audio.Play(clip);
        }
    }
}
