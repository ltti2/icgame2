using System.Collections.Generic;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Lifts
{
    public enum LiftStatus : byte
    {
        Open,
        Closed,       // closed by the player
        AfterHours,   // outside operating hours
        WindHold,
        Broken,
        NoPower,
    }

    /// <summary>
    /// A guest sitting on a carrier (chair, T-bar, cabin...). Everyone on one carrier shares the same
    /// <see cref="BoardClock"/>: the lift's <see cref="Lift.LineClock"/> when that carrier passed the loading
    /// point. <see cref="Seat"/> is which seat across the carrier they took.
    /// </summary>
    public struct LiftRider
    {
        public int GuestIndex;
        public double BoardClock;
        public byte Seat;
    }

    public sealed class Lift
    {
        public int Id;
        public string Name;
        public LiftType Type;
        public Int2 BottomStation;
        public Int2 TopStation;
        public Int2[] Towers;

        public float LengthTiles;
        public int RiseSteps;
        public int RideTicks;
        public int CapacityPerHour;
        public float SpeedTilesPerMinute;

        public LiftStatus Status = LiftStatus.Open;
        /// <summary>The player's switch. Status may still be AfterHours/WindHold when this is true.</summary>
        public bool PlayerOpen = true;

        public float WindLimit;
        public float PowerDraw;
        public int StaffRequired;
        public long OperatingCostPerHour;

        // Runtime
        public readonly Queue<int> QueueGuests = new Queue<int>();
        public readonly Queue<LiftRider> Riders = new Queue<LiftRider>();
        /// <summary>
        /// How far the haul rope has travelled, in ticks of running time. It only advances while the line
        /// is moving, so a wind hold freezes every carrier (and rider) where it is. Carrier k passes the
        /// loading point when the clock reaches k x the carrier interval (see LiftSystem.CarrierInterval).
        /// </summary>
        public double LineClock;
        /// <summary>Index of the next carrier to reach the loading point.</summary>
        public long NextCarrier;
        /// <summary>Unused since carriers load discretely; kept so old saves read cleanly.</summary>
        public float LoadAccumulator;
        public int RidersToday;

        /// <summary>Runs whose start is near the top station. Rebuilt by RunSystem.RefreshConnections.</summary>
        public readonly List<int> RunsFromTop = new List<int>();

        public int QueueCount => QueueGuests.Count;
        public bool IsRunning => Status == LiftStatus.Open;

        /// <summary>
        /// Guests per hour actually carried when the line is long: not every seat gets filled (singles, slow
        /// loaders, missed chairs). Set by LiftSystem from the lift type's seats and loading efficiency.
        /// </summary>
        public float EffectiveCapacityPerHour;

        /// <summary>Expected wait for a guest joining now, in game minutes.</summary>
        public float EstimatedWaitMinutes
        {
            get
            {
                float cap = EffectiveCapacityPerHour > 0f ? EffectiveCapacityPerHour : CapacityPerHour;
                return cap <= 0f ? 999f : QueueGuests.Count * 60f / cap;
            }
        }

        /// <summary>Continuous position along the line, t in [0,1] from bottom to top.</summary>
        public void PointAlong(float t, out float x, out float y)
        {
            x = BottomStation.X + 0.5f + (TopStation.X - BottomStation.X) * t;
            y = BottomStation.Y + 0.5f + (TopStation.Y - BottomStation.Y) * t;
        }
    }
}
