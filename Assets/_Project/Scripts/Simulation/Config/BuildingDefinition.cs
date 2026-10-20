using System;

namespace IceCoast.Simulation.Config
{
    public enum BuildingType : byte
    {
        WaffleCabin,
    }

    /// <summary>
    /// Tunable stats for one kind of building. Plain [Serializable] data so Unity can show it in the inspector
    /// (via SimConfigAsset). Food buildings sell one item at a player-set price.
    /// </summary>
    [Serializable]
    public class BuildingDefinition
    {
        public BuildingType Type;
        public string DisplayName;

        /// <summary>Footprint in tiles. Every tile must be an owned, flat walking area.</summary>
        public int FootprintWidth = 1;
        public int FootprintHeight = 1;

        /// <summary>Paid once when it is built.</summary>
        public long BuildCost;
        /// <summary>Staff and power, per game hour while it is open.</summary>
        public long OperatingCostPerHour;
        /// <summary>Ingredients for each item sold.</summary>
        public int IngredientCostPerSale;

        /// <summary>Price a new building starts at.</summary>
        public int DefaultPrice;
        /// <summary>Guests happily pay up to this. Cheaper is a treat, dearer puts people off.</summary>
        public int FairPrice;
        /// <summary>At or above this price nobody buys.</summary>
        public int MaxPrice;

        /// <summary>Minutes at the window for each customer (one is served at a time).</summary>
        public float ServiceMinutes;
        /// <summary>Minutes a guest spends eating before heading back out.</summary>
        public float EatMinutes;
        /// <summary>Happiness gained from eating here at the fair price (more if cheaper, less if dearer).</summary>
        public float HappinessBoost;
        /// <summary>Energy regained while eating.</summary>
        public int EnergyRestore;
        /// <summary>Guests won't join a line longer than this.</summary>
        public int MaxQueue;

        public float RequiredReputation;
    }
}
