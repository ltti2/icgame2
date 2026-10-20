using System;
using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Terrain;

namespace IceCoast.Simulation.Parking
{
    /// <summary>
    /// Guests drive to the resort. Each arriving party needs a free parking space: if every lot is full the
    /// car turns around and they never buy a ticket (GDD: the parking lot is a capacity gate on arrivals).
    ///
    /// A car drives in along the road to its space (<see cref="CarState.Arriving"/>); when it parks, its party
    /// steps out and starts their day (ArrivalSystem.SpawnGuest). Guests going home walk back to their own car
    /// and get in. When the last of the party has left (walked to the car, been taken off the hill by ski
    /// patrol, or gone home at the end of the day) the car drives back out and the space frees up.
    ///
    /// Spaces are taken in fill order (closest to the lodge first). Everything here is saved.
    /// </summary>
    public sealed class ParkingSystem : ISimSystem
    {
        /// <summary>Spaces per lot are packed into a guest's car id together with the lot id.</summary>
        private const int MaxSpacesPerLot = 1024;
        public const int NoCar = -1;
        public const int CarColors = 6;

        private readonly GameSimulation _sim;
        private int _fullWarnedDay = -1;

        public ParkingSystem(GameSimulation sim) { _sim = sim; }

        public static int PackCar(int lotId, int space) => lotId * MaxSpacesPerLot + space;
        public static int LotOf(int car) => car / MaxSpacesPerLot;
        public static int SpaceOf(int car) => car % MaxSpacesPerLot;

        public ParkingLot GetLot(int id)
        {
            foreach (var lot in _sim.World.ParkingLots) if (lot.Id == id) return lot;
            return null;
        }

        public ParkingLot LotAt(int x, int y)
        {
            foreach (var lot in _sim.World.ParkingLots) if (lot.Contains(x, y)) return lot;
            return null;
        }

        public int TotalCapacity
        {
            get { int n = 0; foreach (var lot in _sim.World.ParkingLots) n += lot.Capacity; return n; }
        }

        public int TotalOccupied
        {
            get { int n = 0; foreach (var lot in _sim.World.ParkingLots) n += lot.Occupied; return n; }
        }

        public void Tick()
        {
            foreach (var lot in _sim.World.ParkingLots)
            {
                var spaces = lot.Spaces;
                for (int i = 0; i < spaces.Length; i++)
                {
                    switch (spaces[i].State)
                    {
                        case CarState.Arriving:
                            if (++spaces[i].Timer >= spaces[i].DriveTicks) Park(lot, i);
                            break;
                        case CarState.Departing:
                            if (++spaces[i].Timer >= spaces[i].DriveTicks)
                            {
                                spaces[i].State = CarState.Empty;
                                spaces[i].Timer = 0;
                            }
                            break;
                    }
                }
            }
        }

        // =====================================================================
        // Arrivals and departures
        // =====================================================================

        /// <summary>
        /// A car with <paramref name="partySize"/> people pulls in. Returns false (and books them as turned
        /// away) if every space is taken.
        /// </summary>
        public bool TryAdmitCar(int partySize)
        {
            var world = _sim.World;
            foreach (var lot in world.ParkingLots)
            {
                var spaces = lot.Spaces;
                for (int i = 0; i < spaces.Length; i++)
                {
                    if (spaces[i].State != CarState.Empty) continue;
                    spaces[i].State = CarState.Arriving;
                    spaces[i].Timer = 0;
                    spaces[i].DriveTicks = DriveTicks(lot, i);
                    spaces[i].PartySize = (byte)partySize;
                    spaces[i].Occupants = 0;
                    spaces[i].Color = (byte)world.Rng.Range(0, CarColors);
                    return true;
                }
            }

            var today = world.Economy.Today;
            today.CarsTurnedAway++;
            today.GuestsTurnedAway += partySize;
            if (_fullWarnedDay != world.Time.Day)
            {
                _fullWarnedDay = world.Time.Day;
                var first = world.ParkingLots.Count > 0 ? world.ParkingLots[0].Entrance : world.ResortEntrance;
                _sim.Events.Publish(SimEventKind.General, EventSeverity.Warning,
                    "The parking lot is full! Cars are turning around and going home.", first);
            }
            return false;
        }

        /// <summary>The car reached its space: the party gets out.</summary>
        private void Park(ParkingLot lot, int space)
        {
            ref var s = ref lot.Spaces[space];
            s.State = CarState.Parked;
            s.Timer = 0;
            int car = PackCar(lot.Id, space);
            int spawned = _sim.ArrivalSystem.SpawnParty(car, s.X, s.Y, s.PartySize);
            s.Occupants = (byte)spawned;
            if (spawned == 0) StartDeparting(lot, space); // resort full of people: drive straight home
        }

        /// <summary>One of the car's party has left the resort (got in, or was carried off).</summary>
        public void OnGuestGone(int car)
        {
            if (car == NoCar) return;
            var lot = GetLot(LotOf(car));
            int space = SpaceOf(car);
            if (lot == null || space >= lot.Spaces.Length) return;
            ref var s = ref lot.Spaces[space];
            if (s.Occupants > 0) s.Occupants--;
            if (s.Occupants == 0 && s.State == CarState.Parked) StartDeparting(lot, space);
        }

        private void StartDeparting(ParkingLot lot, int space)
        {
            ref var s = ref lot.Spaces[space];
            s.State = CarState.Departing;
            s.Timer = 0;
            s.DriveTicks = DriveTicks(lot, space);
        }

        /// <summary>Where a guest's car is parked (continuous tile coordinates). False if they have none.</summary>
        public bool TryGetCarPosition(int car, out float x, out float y)
        {
            x = y = 0f;
            if (car == NoCar) return false;
            var lot = GetLot(LotOf(car));
            int space = SpaceOf(car);
            if (lot == null || space >= lot.Spaces.Length) return false;
            x = lot.Spaces[space].X;
            y = lot.Spaces[space].Y;
            return true;
        }

        // =====================================================================
        // Geometry
        // =====================================================================

        /// <summary>
        /// The drive from the far end of the road to a space, as a polyline in continuous tile coordinates:
        /// along the road (tile centres), into the lot at its entrance, along the aisle, then into the stall.
        /// </summary>
        public void GetDrivePath(ParkingLot lot, int space, List<float> xs, List<float> ys)
        {
            xs.Clear();
            ys.Clear();
            foreach (var p in _sim.World.RoadPath) { xs.Add(p.X + 0.5f); ys.Add(p.Y + 0.5f); }
            var s = lot.Spaces[space];
            float aisleY = lot.AisleY + 0.5f;
            xs.Add(lot.Entrance.X + 0.5f); ys.Add(aisleY);
            xs.Add(s.X); ys.Add(aisleY);
            xs.Add(s.X); ys.Add(s.Y);
        }

        private readonly List<float> _px = new List<float>(8), _py = new List<float>(8);

        /// <summary>Length (tiles) of the drive from the end of the road to a space.</summary>
        public float DrivePathLength(ParkingLot lot, int space)
        {
            GetDrivePath(lot, space, _px, _py);
            float len = 0f;
            for (int i = 1; i < _px.Count; i++)
            {
                float dx = _px[i] - _px[i - 1], dy = _py[i] - _py[i - 1];
                len += (float)Math.Sqrt(dx * dx + dy * dy);
            }
            return len;
        }

        /// <summary>
        /// Where a space's car is and which way it points (unit vector), for drawing. Parked cars sit in their
        /// stall; arriving and departing cars are placed along the drive by their timer. False if the space is
        /// empty.
        /// </summary>
        public bool TryGetCarPose(ParkingLot lot, int space, float tickFraction, out float x, out float y, out float dirX, out float dirY)
        {
            var s = lot.Spaces[space];
            x = s.X; y = s.Y; dirX = 0f; dirY = s.Facing;
            if (s.State == CarState.Empty) return false;
            if (s.State == CarState.Parked) return true;

            GetDrivePath(lot, space, _px, _py);
            float total = 0f;
            for (int i = 1; i < _px.Count; i++)
                total += (float)Math.Sqrt((_px[i] - _px[i - 1]) * (_px[i] - _px[i - 1]) + (_py[i] - _py[i - 1]) * (_py[i] - _py[i - 1]));
            float t = s.DriveTicks <= 0 ? 1f : Math.Min(1f, (s.Timer + tickFraction) / s.DriveTicks);
            bool leaving = s.State == CarState.Departing;
            float along = (leaving ? 1f - t : t) * total;

            for (int i = 1; i < _px.Count; i++)
            {
                float dx = _px[i] - _px[i - 1], dy = _py[i] - _py[i - 1];
                float seg = (float)Math.Sqrt(dx * dx + dy * dy);
                if (seg < 1e-5f) continue;
                if (along <= seg || i == _px.Count - 1)
                {
                    float f = Math.Min(1f, along / seg);
                    x = _px[i - 1] + dx * f;
                    y = _py[i - 1] + dy * f;
                    dirX = dx / seg * (leaving ? -1f : 1f);
                    dirY = dy / seg * (leaving ? -1f : 1f);
                    // Backing out of the stall: the car still faces the way it was parked.
                    if (leaving && i == _px.Count - 1) { dirX = 0f; dirY = s.Facing; }
                    return true;
                }
                along -= seg;
            }
            return true;
        }

        public float RoadLength()
        {
            var road = _sim.World.RoadPath;
            float len = 0f;
            for (int i = 1; i < road.Count; i++) len += road[i - 1].Distance(road[i]);
            return len;
        }

        private int DriveTicks(ParkingLot lot, int space)
        {
            float perTick = _sim.Config.Parking.CarSpeedTilesPerMinute / _sim.Config.Time.TicksPerGameMinute;
            return Math.Max(1, (int)Math.Ceiling(DrivePathLength(lot, space) / Math.Max(1e-3f, perTick)));
        }

        // =====================================================================
        // Construction
        // =====================================================================

        /// <summary>
        /// Builds the starting lot on the paved rectangle the terrain generator laid out (see
        /// <see cref="BaseLayout"/>), and records the road. Spaces are ordered closest-to-the-lodge first.
        /// </summary>
        public ParkingLot CreateLot(BaseLayout layout, int capacity, string name)
        {
            var world = _sim.World;
            var lot = new ParkingLot
            {
                Id = world.NextLotId++,
                Name = name,
                X = layout.LotX,
                Y = layout.LotY,
                Width = layout.LotWidth,
                Height = layout.LotHeight,
                AisleY = layout.AisleY,
                Entrance = new Int2(layout.LotX + layout.LotWidth - 1, layout.AisleY),
            };

            // Stall tiles hold 2x2 cars, pulled in nose first (away from the aisle). Order: nearest the lodge
            // (small x) first, then nearest the aisle.
            var spaces = new List<(float order, ParkingSpace space)>();
            for (int ty = lot.Y; ty < lot.Y + lot.Height; ty++)
            {
                if (ty == lot.AisleY) continue;
                sbyte facing = (sbyte)(ty < lot.AisleY ? -1 : 1);
                for (int tx = lot.X; tx < lot.X + lot.Width; tx++)
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float x = tx + 0.25f + 0.5f * sx, y = ty + 0.25f + 0.5f * sy;
                            float order = x * 100f + Math.Abs(y - (lot.AisleY + 0.5f)) + (y < lot.AisleY ? 0f : 0.01f);
                            spaces.Add((order, new ParkingSpace { X = x, Y = y, Facing = facing }));
                        }
            }
            spaces.Sort((a, b) => a.order.CompareTo(b.order));
            int n = Math.Min(Math.Min(capacity, spaces.Count), MaxSpacesPerLot);
            lot.Spaces = new ParkingSpace[n];
            for (int i = 0; i < n; i++) lot.Spaces[i] = spaces[i].space;

            world.ParkingLots.Add(lot);
            world.RoadPath.Clear();
            world.RoadPath.AddRange(layout.Road);
            world.ParkingVersion++;
            return lot;
        }

        /// <summary>The road and lot every new resort starts with (also added to saves from before parking existed).</summary>
        public BaseLayout DefaultLayout()
        {
            var cfg = _sim.Config;
            // The road runs on through the scenery to the very edge of the world, so it never dead-ends in view.
            int sceneryRoad = Math.Max(0, cfg.Map.SceneryPadding - 1);
            return BaseLayout.For(_sim.Map.Width, _sim.Map.Height, _sim.World.ResortEntrance, cfg.Parking.DefaultLotCapacity, sceneryRoad);
        }
    }
}
