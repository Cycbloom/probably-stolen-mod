namespace DumpingGroundMap
{
    /// <summary>Native full crates and their real compatible keycards, paired at identical pool weights.</summary>
    internal sealed class SupplyEntry
    {
        internal readonly string CrateId, CardId;
        internal readonly int Weight;
        internal SupplyEntry(string crateId, string cardId, int weight)
        {
            CrateId = crateId;
            CardId = cardId;
            Weight = weight;
        }
    }

    internal static class SupplyPool
    {
        private static SupplyEntry Engineering(int weight) { return new SupplyEntry("loot_crate_engineering", "eng_keycard", weight); }
        private static SupplyEntry Service(int weight) { return new SupplyEntry("loot_crate_service", "ser_keycard", weight); }
        private static SupplyEntry Medical(int weight) { return new SupplyEntry("loot_crate_medical", "med_keycard", weight); }
        private static SupplyEntry Security(int weight) { return new SupplyEntry("loot_crate_security", "sec_keycard", weight); }

        private static readonly SupplyEntry[] gate = { Service(50), Engineering(30), Medical(20) };
        private static readonly SupplyEntry[] refuse = { Service(100) };
        private static readonly SupplyEntry[] scrap = { Engineering(100) };
        private static readonly SupplyEntry[] electronics = { Engineering(90), Security(10) };
        private static readonly SupplyEntry[] vehicles = { Engineering(60), Service(40) };
        private static readonly SupplyEntry[] med = { Medical(100) };
        private static readonly SupplyEntry[] weapons = { Security(100) };
        private static readonly SupplyEntry[] water = { Service(80), Medical(20) };
        private static readonly SupplyEntry[] empty = new SupplyEntry[0];

        internal static SupplyEntry[] For(string regionId)
        {
            switch (regionId)
            {
                case "gate": return gate;
                case "refuse": return refuse;
                case "scrap": return scrap;
                case "electronics": return electronics;
                case "vehicles": return vehicles;
                case "med": return med;
                case "weapons": return weapons;
                case "water": return water;
                default: return empty;
            }
        }
    }
}
