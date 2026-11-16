using System;

namespace DumpingGroundMap
{
    internal sealed class MainExpectation
    {
        internal float Base, Valuable, Supply, Event, Total, ValuableProbability, SupplyProbability;

        internal static MainExpectation From(EffectiveTable table, string regionId)
        {
            if (table.WithoutRegionalEvent != null)
            {
                MainExpectation before = From(table.WithoutRegionalEvent, regionId);
                before.Event = table.EventEv;
                before.Total = table.Ev;
                return before;
            }
            MainExpectation result = new MainExpectation();
            float nonJunkValue = 0f, nonJunkProbability = 0f;
            for (int i = 0; i < table.Count; ++i)
            {
                float p = table.Probs[i];
                float value = p * LootCatalog.ExpectedValueOf(table.Items[i], regionId);
                switch (table.Slots[i].Kind)
                {
                    case LootKind.Ordinary:
                        result.Base += value;
                        if (table.Items[i] != "junk") { nonJunkValue += value; nonJunkProbability += p; }
                        break;
                    case LootKind.Valuable:
                        result.Valuable += value;
                        result.ValuableProbability += p;
                        break;
                    default:
                        result.Supply += value;
                        result.SupplyProbability += p;
                        break;
                }
            }
            // Rare slots replace non-junk ordinary rolls. Restore that counterfactual
            // value to Base and subtract it from each uplift to avoid double counting.
            float replacement = nonJunkProbability > 0f ? nonJunkValue / nonJunkProbability : 0f;
            result.Base += replacement * (result.ValuableProbability + result.SupplyProbability);
            result.Valuable -= replacement * result.ValuableProbability;
            result.Supply -= replacement * result.SupplyProbability;
            result.Total = result.Base + result.Valuable + result.Supply;
            return result;
        }
    }

    internal sealed class RewardExpectation
    {
        internal readonly float JackpotValue, MinorValue, JackpotProbability, MinorProbability;
        internal float JackpotEv { get { return JackpotValue * JackpotProbability; } }
        internal float MinorEv { get { return MinorValue * MinorProbability; } }

        internal RewardExpectation(float jackpotPercent, float minorPercent, float jackpotValue, float minorValue)
        {
            JackpotProbability = ProbabilityMath.Clamp(jackpotPercent, 0f, 100f) / 100f;
            MinorProbability = (1f - JackpotProbability) * ProbabilityMath.Clamp(minorPercent, 0f, 100f) / 100f;
            JackpotValue = jackpotValue;
            MinorValue = minorValue;
        }

        internal static float MeanJackpotValue(StashTemplate template)
        {
            if (template == null) return 0f;
            float result = 0f;
            if (template.Groups != null)
                foreach (RewardGroup group in template.Groups)
                {
                    if (group == null || group.Ids == null || group.Ids.Length == 0) continue;
                    float sum = 0f;
                    foreach (string id in group.Ids) sum += LootCatalog.ExpectedValueOf(id, null);
                    result += (group.Min + group.Max) * 0.5f * sum / group.Ids.Length;
                }
            if (template.ValuableIds == null || template.ValuableIds.Length == 0) return result;
            int total = 0;
            if (template.ValuableWeights != null && template.ValuableWeights.Length == template.ValuableIds.Length)
                foreach (int weight in template.ValuableWeights) total += weight;
            float weighted = 0f;
            for (int i = 0; i < template.ValuableIds.Length; ++i)
                weighted += LootCatalog.ExpectedValueOf(template.ValuableIds[i], null)
                    * (total > 0 ? template.ValuableWeights[i] : 1);
            return result + weighted / (total > 0 ? total : template.ValuableIds.Length);
        }

        internal static float MeanMinorCount(float target, float unitPrice)
        {
            if (unitPrice <= 0f) return 1f;
            double low = target * StashFind.MinorValueFloor, high = target * StashFind.MinorValueCeil;
            if (high <= low) return (float)Math.Max(StashFind.MinorMinItems,
                Math.Min(StashFind.MinorMaxItems, Math.Floor(low / unitPrice + 0.5d)));
            double count = 0d;
            for (int n = StashFind.MinorMinItems; n <= StashFind.MinorMaxItems; ++n)
            {
                double left = n == StashFind.MinorMinItems ? double.NegativeInfinity : (n - 0.5d) * unitPrice;
                double right = n == StashFind.MinorMaxItems ? double.PositiveInfinity : (n + 0.5d) * unitPrice;
                count += n * Math.Max(0d, Math.Min(high, right) - Math.Max(low, left)) / (high - low);
            }
            return (float)count;
        }
    }
}
