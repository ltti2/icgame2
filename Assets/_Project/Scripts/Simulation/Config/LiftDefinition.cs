using System;

namespace IceCoast.Simulation.Config
{
    public enum LiftType : byte
    {
        RopeTow,
        MagicCarpet,
        TBar,
        FixedGripChair,
        DetachableChair,
        DetachableBubble,
        Gondola,
    }

    /// <summary>
    /// Tunable stats for one lift type. Plain [Serializable] data so Unity can show it in the
    /// inspector (via SimConfigAsset) without the simulation referencing UnityEngine.
    /// </summary>
    [Serializable]
    public class LiftDefinition
    {
        public LiftType Type;
        public string DisplayName;

        public int CapacityPerHour;
        /// <summary>
        /// Guests per carrier: 1 for a rope tow handle, 2 on a T-bar, 3 on a fixed-grip triple, 4 on a
        /// detachable quad, 6 on a bubble six-pack, 8 in a gondola cabin. Carriers pass the loading point at
        /// CapacityPerHour / SeatsPerCarrier per hour.
        /// </summary>
        public int SeatsPerCarrier = 1;
        /// <summary>
        /// Chance (0..1) that each seat after the first is filled when a carrier loads from a long line.
        /// Singles and slow loaders mean real lifts never run quite full, so lines are longer than the
        /// nameplate capacity suggests.
        /// </summary>
        public float LoadingEfficiency = 0.9f;
        /// <summary>Line speed in tiles per game minute. Time is compressed like RCT, so this is not 1:1 with reality.</summary>
        public float SpeedTilesPerMinute;

        public long BaseCost;
        public long CostPerTile;
        public long OperatingCostPerHour;

        public int MinLengthTiles;
        public int MaxLengthTiles;
        /// <summary>Maximum average gradient of the line, in percent. Surface lifts cannot climb cliffs.</summary>
        public float MaxGradePercent;

        public int TowerSpacingTiles;
        /// <summary>Cable height above ground in metres (used by the view and for tree clearance).</summary>
        public float CableHeightMeters;

        public float PowerDrawKw;
        public int StaffRequired;
        /// <summary>Lift goes on wind hold above this wind speed (km/h).</summary>
        public float WindLimitKmh;
        public float Comfort;          // 0..1, used for guest satisfaction while riding
        public bool WeatherProtected;  // bubbles and cabins: no cold penalty
        public bool CanTurn;           // gondolas only (future: angle stations)

        public float RequiredReputation;
    }
}
