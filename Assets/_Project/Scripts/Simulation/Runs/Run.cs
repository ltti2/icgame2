using System;
using System.Collections.Generic;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Runs
{
    public enum Difficulty : byte
    {
        Green,
        Blue,
        Black,
        DoubleBlack,
        TripleBlack,
    }

    public enum RunExitKind : byte
    {
        /// <summary>The run passes close to a lift's bottom station: skiers can stop and ride it.</summary>
        Lift,
        /// <summary>The run touches another run: skiers can switch to it.</summary>
        Run,
    }

    /// <summary>
    /// A point partway down a run where a skier can leave it: for a lift (often mid-mountain) or another run.
    /// Exits are what turn a set of separate trails into a connected mountain.
    /// </summary>
    public struct RunExit
    {
        public RunExitKind Kind;
        /// <summary>Lift id or run id.</summary>
        public int TargetId;
        /// <summary>The tile on this run where the exit is.</summary>
        public Int2 Tile;
        /// <summary>For run exits: the tile on the other run where the skier joins it.</summary>
        public Int2 JoinTile;
        /// <summary>Flow distance to this run's end at <see cref="Tile"/>. Exits are sorted by this, descending.</summary>
        public float Distance;
    }

    [Flags]
    public enum RunIssues : byte
    {
        None = 0,
        NoLiftAtTop = 1 << 0,
        /// <summary>No way down to a lift or the base, even through other runs: guests must walk out.</summary>
        WalkOut = 1 << 1,
        HasUphill = 1 << 2,
        Icy = 1 << 3,
        /// <summary>Signposted easier than it really is. Guests will get hurt.</summary>
        Mislabelled = 1 << 4,
    }

    /// <summary>
    /// A player-designated trail. Tiles are stored in paint order; the flow field
    /// (distance-to-end per tile) drives skier movement.
    /// </summary>
    public sealed class Run
    {
        public int Id;
        public string Name;
        public List<Int2> Tiles = new List<Int2>();

        /// <summary>Tile key (map.TileKey) -> slot in the per-tile arrays below.</summary>
        public readonly Dictionary<int, int> SlotByKey = new Dictionary<int, int>();
        /// <summary>Flow-field cost to reach the end, per slot. float.MaxValue = unreachable.</summary>
        public float[] DistanceToEnd = Array.Empty<float>();
        /// <summary>
        /// Position across the run per slot: 0 = one edge, 1 = the other, measured perpendicular to the way
        /// down so it follows the trail as it bends. Lets each skier hold their own line. Derived, not saved.
        /// </summary>
        public float[] Across = Array.Empty<float>();
        /// <summary>Width of the run (tiles) at each slot's cross-section. Converts Across into tiles.</summary>
        public float[] CrossWidth = Array.Empty<float>();
        /// <summary>Slots ordered by DistanceToEnd (bottom first; unreachable slots last). Derived, not saved.</summary>
        public int[] SlotsByDistance = Array.Empty<int>();

        public Int2 Start;
        public Int2 End;

        public Difficulty ComputedRating;
        /// <summary>Player override for the signposted rating. Null = use computed.</summary>
        public Difficulty? RatingOverride;
        public Difficulty DisplayedRating => RatingOverride ?? ComputedRating;

        public float AvgSlope;
        public float MaxSlope;
        /// <summary>Continuous difficulty (0 = green .. 4 = triple black, can exceed). Drives injuries.</summary>
        public float DifficultyScore;
        public float PathLength;
        public float AverageWidth;

        public float AvgSnowQuality = 1f; // 0..1, refreshed periodically by RunSystem
        public bool Open = true;
        public int GroomPriority;

        public int StartLiftId;  // lift whose top feeds this run (0 = none)
        public int EndLiftId;    // lift at the bottom (0 = none)
        /// <summary>Run that this run flows into, when it ends on another run instead of a lift.</summary>
        public int ContinuesIntoRunId;
        /// <summary>Ends in the base area, where guests can reach any base lift or go home.</summary>
        public bool EndsAtBase;
        /// <summary>Reachable from some lift's top, directly or through other runs.</summary>
        public bool FedByLift;
        /// <summary>Leads to a lift or the base, directly or through other runs.</summary>
        public bool ReachesLift;
        /// <summary>Walking area (WalkZone id) the run ends on, 0 = none. Guests there can walk to its lifts.</summary>
        public int EndZoneId;

        /// <summary>Share of the run's tiles lit by trail lights (0..1). Derived by LightingSystem, not saved.</summary>
        public float LitFraction;

        /// <summary>
        /// Every place along the run where skiers can peel off, in the order they reach them (highest first).
        /// Rebuilt by RunSystem.RefreshConnections.
        /// </summary>
        public readonly List<RunExit> Exits = new List<RunExit>();
        public RunIssues Issues;

        // Stats
        public int SkiersToday;
        public int InjuriesToday;

        public bool ContainsTile(int key) => SlotByKey.ContainsKey(key);

        public float DistanceAt(int key) => SlotByKey.TryGetValue(key, out int slot) ? DistanceToEnd[slot] : float.MaxValue;

        /// <summary>Across-the-run position (0..1) of a tile, or 0.5 if it is not on this run.</summary>
        public float AcrossAt(int key) => SlotByKey.TryGetValue(key, out int slot) && slot < Across.Length ? Across[slot] : 0.5f;

        /// <summary>Run width in tiles at a tile's cross-section (at least 1).</summary>
        public float WidthAt(int key) => SlotByKey.TryGetValue(key, out int slot) && slot < CrossWidth.Length ? CrossWidth[slot] : 1f;
    }
}
