using System.Collections.Generic;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Buildings
{
    /// <summary>
    /// A placed building (a Waffle Cabin, for now). It occupies its footprint tiles (OccupantKind.Building)
    /// on a walking area. Food buildings sell one item at <see cref="Price"/> to the guests lined up at the
    /// window, one at a time. Stats come from <see cref="BuildingDefinition"/> by type.
    /// </summary>
    public sealed class Building
    {
        public int Id;
        public BuildingType Type;
        public string Name;
        /// <summary>Footprint's top-left tile (smallest x and y).</summary>
        public Int2 Origin;
        public int Width = 1, Height = 1;

        /// <summary>What one item costs a guest, in dollars. Set by the player.</summary>
        public int Price;
        /// <summary>Player switch. A closed building sells nothing and costs nothing to run.</summary>
        public bool PlayerOpen = true;

        /// <summary>Guests waiting at the window, front first (guest indices).</summary>
        public readonly List<int> Queue = new List<int>();
        /// <summary>Ticks until the guest at the front of the line has been served.</summary>
        public int ServiceTimer;

        public int SoldToday;
        public long RevenueToday;
        /// <summary>Ingredients and running costs booked today.</summary>
        public long CostToday;
        /// <summary>Hungry guests who looked at the price today and decided against it.</summary>
        public int PricedOutToday;
        public int SoldTotal;
        public long RevenueTotal;

        /// <summary>Where guests line up: just outside the window, on the camera side of the footprint.</summary>
        public float WindowX => Origin.X + Width + 0.15f;
        public float WindowY => Origin.Y + Height * 0.5f;

        public bool Covers(int x, int y) => x >= Origin.X && y >= Origin.Y && x < Origin.X + Width && y < Origin.Y + Height;
    }
}
