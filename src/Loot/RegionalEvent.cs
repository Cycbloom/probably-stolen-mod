using System;
using System.Globalization;

namespace DumpingGroundMap
{
    internal sealed class RegionalEvent
    {
        internal readonly string RegionId;
        private readonly string titleKey, storyKey, goodsKey;
        private RegionalEvent(string id, string title, string story, string goods)
        {
            RegionId = id; titleKey = title; storyKey = story; goodsKey = goods;
        }
        internal string Title { get { return Copy.Get(titleKey); } }
        internal float JunkMultiplier
        {
            get { return RegionId == "scrap" || RegionId == "med" ? 0.6f
                : RegionId == "water" ? 0.85f : RegionId == "vehicles" ? 0.8f : 0.75f; }
        }
        internal string Description
        {
            get
            {
                string effect = Copy.Get("regionalevent_ordinary_junk_weight")
                    + ((1f - JunkMultiplier) * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
                if (RegionId == "gate") effect += Copy.Get("regionalevent_wound_risk_20");
                else effect += " · " + Copy.Get(goodsKey)
                    + Copy.Get("regionalevent_weight_up_to_50");
                if (RegionId == "water") effect += Copy.Get("regionalevent_fill_center_55_to_65");
                return effect;
            }
        }
        internal string Tooltip
        {
            get { return Title + "\n" + Copy.Get(storyKey) + "\n" + Description
                + Copy.Get("regionalevent_today_only_applies_to_ordinary_main_loot"); }
        }
        internal bool Targets(Region region, string itemId)
        {
            int category = region.Loot.CategoryOf(itemId);
            switch (RegionId)
            {
                case "refuse": return category == 1 || category == 2;
                case "scrap": return category == 1;
                case "electronics": return category == 1 || (category == 3 && itemId != "system_module_ruined" && itemId != "system_module_corrupt");
                case "vehicles": return category == 2 || category == 3;
                case "med": return category == 1 || category == 2;
                case "weapons": return category == 3 || category == 4;
                case "water": return PumpWater.IsContainer(itemId);
                default: return false;
            }
        }
        internal static readonly RegionalEvent[] All =
        {
            new RegionalEvent("gate", "event_gate_title", "event_gate_story", ""),
            new RegionalEvent("scrap", "event_scrap_title", "event_scrap_story", "event_scrap_goods"),
            new RegionalEvent("refuse", "event_refuse_title", "event_refuse_story", "event_refuse_goods"),
            new RegionalEvent("med", "event_med_title", "event_med_story", "event_med_goods"),
            new RegionalEvent("weapons", "event_weapons_title", "event_weapons_story", "event_weapons_goods"),
            new RegionalEvent("electronics", "event_electronics_title", "event_electronics_story", "event_electronics_goods"),
            new RegionalEvent("vehicles", "event_vehicles_title", "event_vehicles_story", "event_vehicles_goods"),
            new RegionalEvent("water", "event_water_title", "event_water_story", "event_water_goods")
        };
        internal static RegionalEvent For(string regionId) { return Array.Find(All, e => e.RegionId == regionId); }
    }

    internal static class RegionalEventRules
    {
        internal static float WoundChance(float original, bool active) { return active ? original * 0.8f : original; }

        internal static EffectiveTable Apply(Region region, EffectiveTable original, RegionalEvent effect)
        {
            if (effect == null || effect.RegionId != region.Id || original.WithoutRegionalEvent != null) return original;
            float[] probs = (float[])original.Probs.Clone();
            float[] generic = (float[])original.Probs.Clone();
            float[] values = new float[original.Count];
            float mass = 0f, junk = 0f, normal = 0f, targetTotal = 0f;
            for (int i = 0; i < original.Count; ++i)
            {
                values[i] = LootCatalog.ExpectedValueOf(original.Items[i], region.Id);
                if (original.Slots[i].Kind != LootKind.Ordinary) continue;
                mass += probs[i];
                if (original.Items[i] == "junk") { junk += probs[i]; continue; }
                normal += probs[i];
                if (region.Id == "water" && PumpWater.IsContainer(original.Items[i]))
                    values[i] = PumpWater.ExpectedValue(original.Items[i], LootCatalog.ValueOf(original.Items[i]), true);
                probs[i] *= effect.Targets(region, original.Items[i]) ? 1.5f : 1f;
                targetTotal += probs[i];
            }
            float denominator = junk * effect.JunkMultiplier + normal;
            float newJunk = denominator > 0f ? mass * junk * effect.JunkMultiplier / denominator : junk;
            float available = Math.Max(0f, mass - newJunk);
            for (int i = 0; i < probs.Length; ++i)
            {
                if (original.Slots[i].Kind != LootKind.Ordinary) continue;
                if (original.Items[i] == "junk") probs[i] = generic[i] = newJunk;
                else
                {
                    probs[i] = targetTotal > 0f ? available * probs[i] / targetTotal : 0f;
                    generic[i] = normal > 0f ? available * original.Probs[i] / normal : 0f;
                }
            }
            float expected = Dot(probs, values), safe = Dot(generic, values);
            // Thematic events must not become a monetary penalty at 0% junk or
            // other unusual user settings. Keep the strongest affordable bias.
            if (expected < original.Ev && safe > expected)
            {
                float strength = Math.Max(0f, Math.Min(1f, (safe - original.Ev) / (safe - expected)));
                for (int i = 0; i < probs.Length; ++i) probs[i] = generic[i] + strength * (probs[i] - generic[i]);
                expected = Dot(probs, values);
            }
            int junkSlot = Array.IndexOf(original.Items, "junk");
            return new EffectiveTable(original.Items, probs, original.IsHigh, original.HighChance,
                junkSlot >= 0 ? probs[junkSlot] : 0f, expected, original.HighStart, original.HighCount,
                region, original.JunkScale, original, effect, values,
                original.ForeignStart, original.ForeignCount);
        }
        private static float Dot(float[] probabilities, float[] values)
        {
            float total = 0f;
            for (int i = 0; i < probabilities.Length; ++i) total += probabilities[i] * values[i];
            return total;
        }
    }
}
