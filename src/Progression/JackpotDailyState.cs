using System;

namespace DumpingGroundMap
{
    /// <summary>Additive increase over five missed days; multiplicative decrease after a winning day.</summary>
    internal sealed class JackpotDailyState
    {
        internal const int DaysToCap = 5;
        internal float Chance = JackpotChanceRules.Start;
        internal float CycleStart = JackpotChanceRules.Start;
        internal float Cap;
        internal int Day = -1;
        internal int MissDays;

        internal bool Advance(int today, float cap, bool adaptive, int lastHitDay)
        {
            if (today < 0 || cap <= 0f || float.IsNaN(cap)) return false;
            if (!adaptive)
            {
                bool changed = Day != today;
                Day = today; // Paused calendar days never become catch-up growth.
                return changed;
            }
            if (Day < 0 || today < Day || Cap <= 0f || float.IsNaN(Cap))
            {
                Chance = JackpotChanceRules.Current(Chance, cap, true);
                CycleStart = Chance;
                MissDays = 0; Cap = cap; Day = today;
                return true;
            }
            if (Cap != cap)
            {
                // Resolve an earlier winning day before rebasing a changed cap.
                if (today > Day) Advance(today, Cap, true, lastHitDay);
                Chance = JackpotChanceRules.Current(Chance, cap, true);
                CycleStart = Chance; MissDays = 0; Cap = cap; Day = today;
                return true;
            }
            if (today == Day) return false;
            Chance = JackpotChanceRules.Current(Chance, cap, true);
            CycleStart = JackpotChanceRules.Current(CycleStart, cap, true);
            MissDays = Math.Max(0, Math.Min(DaysToCap, MissDays));
            while (Day < today)
            {
                // At most one unresolved winning day can exist. Once capped,
                // skip long stretches instead of looping over every missed day.
                if (MissDays >= DaysToCap && Chance >= cap)
                {
                    if (lastHitDay >= Day && lastHitDay < today) Day = lastHitDay;
                    else { Day = today; break; }
                }
                if (Day == lastHitDay)
                {
                    Chance = JackpotChanceRules.Current(Chance * 0.5f, cap, true);
                    CycleStart = Chance; MissDays = 0;
                }
                else
                {
                    MissDays = Math.Min(DaysToCap, MissDays + 1);
                    Chance = CycleStart + (cap - CycleStart) * MissDays / DaysToCap;
                    Chance = JackpotChanceRules.Current(Chance, cap, true);
                }
                ++Day;
            }
            return true;
        }
    }
}
