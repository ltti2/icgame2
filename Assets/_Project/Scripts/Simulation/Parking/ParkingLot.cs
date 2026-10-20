using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Parking
{
    public enum CarState : byte
    {
        Empty,
        /// <summary>Driving in from the end of the road to its space.</summary>
        Arriving,
        Parked,
        /// <summary>Everyone has left; driving back out to the end of the road.</summary>
        Departing,
    }

    /// <summary>
    /// One parking space and the car in it. Cars drive in and out along the road (<see cref="Timer"/> counts
    /// ticks of the drive, <see cref="DriveTicks"/> is its length), so the view can place a moving car from the
    /// same numbers the simulation uses.
    /// </summary>
    public struct ParkingSpace
    {
        /// <summary>Centre of the space in continuous tile coordinates.</summary>
        public float X, Y;
        /// <summary>Which way the car's nose points when parked (pulled in nose first): +1 toward +y, -1 toward -y.</summary>
        public sbyte Facing;

        public CarState State;
        /// <summary>Paint job, picked when the car arrives (view picks the sprite).</summary>
        public byte Color;
        /// <summary>People who came in this car.</summary>
        public byte PartySize;
        /// <summary>Of them, how many are still at the resort. The car leaves when this reaches zero.</summary>
        public byte Occupants;
        public int Timer;
        public int DriveTicks;
    }

    /// <summary>
    /// A parking lot: a rectangle of paved tiles with one aisle row the road enters along. Spaces are stored in
    /// fill order (closest to the lodge first), which is the order drivers take them.
    /// </summary>
    public sealed class ParkingLot
    {
        public int Id;
        public string Name;
        public int X, Y, Width, Height;
        /// <summary>Row (absolute y) of the driving aisle. Cars drive along it to reach their space.</summary>
        public int AisleY;
        /// <summary>Tile where the road meets the lot (on the aisle row).</summary>
        public Int2 Entrance;
        public ParkingSpace[] Spaces = new ParkingSpace[0];

        public int Capacity => Spaces.Length;

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

        public int CountIn(CarState state)
        {
            int n = 0;
            foreach (var s in Spaces) if (s.State == state) n++;
            return n;
        }

        /// <summary>Spaces with a car in them or on its way (not free for new arrivals).</summary>
        public int Occupied
        {
            get
            {
                int n = 0;
                foreach (var s in Spaces) if (s.State != CarState.Empty) n++;
                return n;
            }
        }
    }
}
