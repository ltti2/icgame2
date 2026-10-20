using System;
using System.Collections.Generic;

namespace IceCoast.Simulation.Guests
{
    public enum SkillLevel : byte
    {
        FirstTimer,
        Beginner,
        Intermediate,
        Advanced,
        Expert,
        Pro,
    }

    public enum GuestState : byte
    {
        Inactive,
        Arriving,
        BuyingTicket,
        RentingGear,
        WalkingToLift,
        Queueing,
        Riding,
        ChoosingRun,
        Skiing,
        Resting,
        Eating,
        Warming,
        Leaving,
    }

    [Flags]
    public enum GuestFlags : byte
    {
        None = 0,
        Injured = 1 << 0,
        WalkingOut = 1 << 1,
        OnRun = 1 << 2,       // reached the run's tiles (vs. traversing to its start)
        Fallen = 1 << 3,
        /// <summary>Got in over their head on the current run.</summary>
        Scared = 1 << 4,
        WantsToLeave = 1 << 5,
    }

    /// <summary>
    /// All guests as a structure of arrays. A guest is just an index; slots are recycled via a free list.
    /// This keeps a few thousand guests cache-friendly and trivially serializable.
    ///
    /// Positions are continuous tile coordinates (tile (3,4) spans [3,4) x [4,5)).
    /// Prev* hold last tick's position so the view can interpolate between ticks.
    /// </summary>
    public sealed class GuestPool
    {
        public readonly int Capacity;

        public readonly bool[] Alive;
        public readonly GuestState[] State;
        public readonly SkillLevel[] Skill;
        public readonly GuestFlags[] Flags;
        public readonly byte[] Energy;
        public readonly byte[] Hunger;
        public readonly byte[] Warmth;
        public readonly float[] Happiness; // 0..100, float so small per-tick effects accumulate
        public readonly int[] Money;

        public readonly float[] PosX, PosY, PrevX, PrevY;
        /// <summary>Lateral offset in tiles so skiers spread across the run instead of single file.</summary>
        public readonly float[] Lane;
        /// <summary>
        /// The line this skier wants to hold across their current run (0 = one edge, 1 = the other, see
        /// Run.Across). Picked when they start a run and re-picked as they ski, chasing better snow.
        /// </summary>
        public readonly float[] Line;

        public readonly int[] TargetId;        // lift id or run id depending on state
        /// <summary>Tile key of an intermediate destination (e.g. where to join a run). -1 = none.</summary>
        public readonly int[] WaypointKey;
        public readonly int[] StateTimer;      // ticks remaining / elapsed in current state
        public readonly int[] ArrivalMinute;
        public readonly int[] DepartureMinute;
        public readonly short[] RunsSkied;
        /// <summary>Tiles travelled on the current run; drives the rhythm of S-turns.</summary>
        public readonly short[] Steps;
        /// <summary>Index into the current run's Exits of the next exit this guest has not yet passed.</summary>
        public readonly byte[] ExitCursor;
        public readonly int[] LastRunId;
        /// <summary>Runs this guest got scared on: bit (runId % 64). Cheap, slightly lossy memory.</summary>
        public readonly ulong[] AvoidMask;
        /// <summary>The car this guest came in (ParkingSystem.PackCar), or -1. They walk back to it to go home.</summary>
        public readonly int[] Car;

        private readonly Stack<int> _free;
        public int HighWater { get; private set; }
        public int Count { get; private set; }

        public GuestPool(int capacity)
        {
            Capacity = capacity;
            Alive = new bool[capacity];
            State = new GuestState[capacity];
            Skill = new SkillLevel[capacity];
            Flags = new GuestFlags[capacity];
            Energy = new byte[capacity];
            Hunger = new byte[capacity];
            Warmth = new byte[capacity];
            Happiness = new float[capacity];
            Money = new int[capacity];
            PosX = new float[capacity];
            PosY = new float[capacity];
            PrevX = new float[capacity];
            PrevY = new float[capacity];
            Lane = new float[capacity];
            Line = new float[capacity];
            TargetId = new int[capacity];
            WaypointKey = new int[capacity];
            StateTimer = new int[capacity];
            ArrivalMinute = new int[capacity];
            DepartureMinute = new int[capacity];
            RunsSkied = new short[capacity];
            Steps = new short[capacity];
            ExitCursor = new byte[capacity];
            LastRunId = new int[capacity];
            AvoidMask = new ulong[capacity];
            Car = new int[capacity];
            _free = new Stack<int>(capacity);
        }

        public bool IsFull => Count >= Capacity;

        /// <summary>Allocates a slot and resets it. Returns -1 when full.</summary>
        public int Spawn()
        {
            int i;
            if (_free.Count > 0) i = _free.Pop();
            else if (HighWater < Capacity) i = HighWater++;
            else return -1;

            Alive[i] = true;
            State[i] = GuestState.Arriving;
            Skill[i] = SkillLevel.Beginner;
            Flags[i] = GuestFlags.None;
            Energy[i] = 100;
            Hunger[i] = 0;
            Warmth[i] = 100;
            Happiness[i] = 60f;
            Money[i] = 0;
            PosX[i] = PosY[i] = PrevX[i] = PrevY[i] = 0f;
            Lane[i] = 0f;
            Line[i] = 0.5f;
            TargetId[i] = 0;
            WaypointKey[i] = -1;
            StateTimer[i] = 0;
            ArrivalMinute[i] = 0;
            DepartureMinute[i] = 0;
            RunsSkied[i] = 0;
            Steps[i] = 0;
            ExitCursor[i] = 0;
            LastRunId[i] = 0;
            AvoidMask[i] = 0;
            Car[i] = -1;
            Count++;
            return i;
        }

        public void Despawn(int i)
        {
            if (!Alive[i]) return;
            Alive[i] = false;
            State[i] = GuestState.Inactive;
            _free.Push(i);
            Count--;
        }

        public void SetPosition(int i, float x, float y)
        {
            PosX[i] = x;
            PosY[i] = y;
        }

        /// <summary>Copies current positions to Prev* at the start of a tick so the view can interpolate.</summary>
        public void SnapshotPositions()
        {
            Array.Copy(PosX, PrevX, HighWater);
            Array.Copy(PosY, PrevY, HighWater);
        }

        public void Teleport(int i, float x, float y)
        {
            PosX[i] = PrevX[i] = x;
            PosY[i] = PrevY[i] = y;
        }

        /// <summary>
        /// Free-list order matters for determinism: two worlds with the same history must hand out the same
        /// slots. The save system writes this out so a loaded game continues identically.
        /// Returned top-of-stack first.
        /// </summary>
        public int[] GetFreeListSnapshot() => _free.ToArray();

        /// <summary>Used by the save system after the per-guest arrays have been filled in.</summary>
        public void RestoreAllocation(int highWater, int[] freeListTopFirst)
        {
            HighWater = highWater;
            _free.Clear();
            for (int i = freeListTopFirst.Length - 1; i >= 0; i--) _free.Push(freeListTopFirst[i]);
            Count = 0;
            for (int i = 0; i < highWater; i++)
                if (Alive[i]) Count++;
        }
    }
}
