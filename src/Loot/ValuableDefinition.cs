namespace DumpingGroundMap
{
    /// <summary>Item and regional weights stay together when a pool entry is edited.</summary>
    internal sealed class ValuableDefinition
    {
        internal readonly string Id;
        private readonly int gate, scrap, refuse, medical, weapons;
        private readonly int electronics, vehicles, water;

        internal ValuableDefinition(string id, int gate, int scrap, int refuse, int medical,
            int weapons, int electronics, int vehicles, int water)
        {
            Id = id;
            this.gate = gate;
            this.scrap = scrap;
            this.refuse = refuse;
            this.medical = medical;
            this.weapons = weapons;
            this.electronics = electronics;
            this.vehicles = vehicles;
            this.water = water;
        }

        internal int WeightFor(string regionId)
        {
            switch (regionId)
            {
                case "gate": return gate;
                case "scrap": return scrap;
                case "refuse": return refuse;
                case "med": return medical;
                case "weapons": return weapons;
                case "electronics": return electronics;
                case "vehicles": return vehicles;
                case "water": return water;
                // 未知区域不再抛异常：按 1 处理，保证贵重品概率份额仍有非零分母。
                default: return 1;
            }
        }
    }
}
