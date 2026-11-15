using System;

namespace DumpingGroundMap
{
    /// <summary>Item and regional weights stay together when a pool entry is edited.</summary>
    internal sealed class ValuableDefinition
    {
        internal readonly string Id;
        private readonly int gate, scrap, refuse, medical, weapons;

        internal ValuableDefinition(string id, int gate, int scrap, int refuse, int medical, int weapons)
        {
            Id = id;
            this.gate = gate;
            this.scrap = scrap;
            this.refuse = refuse;
            this.medical = medical;
            this.weapons = weapons;
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
                default: throw new ArgumentException("Unknown region: " + regionId);
            }
        }
    }
}
