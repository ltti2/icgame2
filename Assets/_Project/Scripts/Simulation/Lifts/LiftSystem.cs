using System;
using System.Collections.Generic;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Lifts
{
    /// <summary>
    /// Lift operations: status (hours, wind, player switch), loading queues at the configured
    /// capacity, and carrying riders to the top.
    /// </summary>
    public sealed class LiftSystem : ISimSystem
    {
        private readonly GameSimulation _sim;
        private readonly List<LiftRider> _scratchRiders = new List<LiftRider>();

        public LiftSystem(GameSimulation sim) { _sim = sim; }

        /// <summary>
        /// Bumped whenever any lift opens or closes (hours, wind, player). Route planning caches key off it.
        /// Not saved: it only signals "something changed", and caches are rebuilt from scratch after a load.
        /// </summary>
        public int StatusVersion { get; private set; }

        public void Tick()
        {
            if (_sim.Clock.MinuteAdvanced) RefreshStatuses();

            var lifts = _sim.World.Lifts;
            for (int i = 0; i < lifts.Count; i++)
            {
                var lift = lifts[i];
                AdvanceLine(lift);
                LayoutQueue(lift);
            }
        }

        /// <summary>
        /// Stands queued guests in a corral behind the bottom station, 4 abreast. Purely positional, but it
        /// lives in the simulation so queue length is visible on the map without the view knowing queue order.
        /// </summary>
        private void LayoutQueue(Lift lift)
        {
            if (lift.QueueGuests.Count == 0) return;
            float dx = lift.TopStation.X - lift.BottomStation.X;
            float dy = lift.TopStation.Y - lift.BottomStation.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return;
            dx /= len; dy /= len;
            float px = -dy, py = dx; // perpendicular

            var guests = _sim.World.Guests;
            float bx = lift.BottomStation.X + 0.5f, by = lift.BottomStation.Y + 0.5f;
            int i = 0;
            foreach (int g in lift.QueueGuests)
            {
                int row = i / 4, col = i % 4;
                float back = 0.8f + row * 0.35f;
                float side = (col - 1.5f) * 0.3f;
                guests.SetPosition(g, bx - dx * back + px * side, by - dy * back + py * side);
                i++;
            }
        }

        // ---------- Status ----------

        private bool _lineUpWindow;

        /// <summary>The half hour before opening: guests may line up at lifts that will open.</summary>
        public bool InLineUpWindow
        {
            get
            {
                var t = _sim.Config.Time;
                int m = _sim.World.Time.MinuteOfDay;
                return m >= t.LiftsOpenMinute - t.LineUpBeforeOpeningMinutes && m < t.LiftsOpenMinute;
            }
        }

        /// <summary>
        /// Guests can join this lift's line: it is running, or it opens shortly and they are lining up for the
        /// first chair.
        /// </summary>
        public bool Boardable(Lift lift)
            => lift.IsRunning || (InLineUpWindow && lift.PlayerOpen && lift.Status == LiftStatus.AfterHours);

        public void RefreshStatuses()
        {
            bool hours = _sim.TimeSystem.LiftsOperating;
            bool lineUp = InLineUpWindow;
            if (lineUp != _lineUpWindow)
            {
                _lineUpWindow = lineUp;
                StatusVersion++; // lifts become worth walking to before they open
            }
            foreach (var lift in _sim.World.Lifts)
            {
                var before = lift.Status;
                lift.Status = ComputeStatus(lift, hours);
                if (before == lift.Status) continue;
                StatusVersion++;


                if (lift.Status == LiftStatus.WindHold)
                    _sim.Events.Publish(SimEventKind.LiftClosed, EventSeverity.Warning, $"{lift.Name} is on wind hold.", lift.TopStation, lift.Id);
                else if (before == LiftStatus.WindHold && lift.Status == LiftStatus.Open)
                    _sim.Events.Publish(SimEventKind.LiftOpened, EventSeverity.Good, $"{lift.Name} is spinning again.", lift.TopStation, lift.Id);
            }

            // Anyone waiting at a lift that won't be loading (closed, wind hold, done for the day) moves on.
            foreach (var lift in _sim.World.Lifts)
                if (lift.QueueGuests.Count > 0 && !Boardable(lift)) EjectQueue(lift);
        }

        /// <summary>
        /// The player's switch, then hours, then wind. In the night session (and whenever it is dark) only lifts
        /// with a lit trail off the top run: nobody wants to ride up into the dark.
        /// </summary>
        private LiftStatus ComputeStatus(Lift lift, bool hours)
        {
            if (!lift.PlayerOpen) return LiftStatus.Closed;
            if (!hours) return LiftStatus.AfterHours;
            var time = _sim.TimeSystem;
            bool needsLights = !time.DayOperating || _sim.LightingSystem.IsDark;
            if (needsLights && !_sim.LightingSystem.LiftServesLitTerrain(lift)) return LiftStatus.AfterHours;
            if (EffectiveWindAtTop(lift) > lift.WindLimit) return LiftStatus.WindHold;
            return LiftStatus.Open;
        }

        /// <summary>This lift will run in the night session (night skiing on and a lit trail off its top).</summary>
        public bool RunsAtNight(Lift lift)
            => _sim.World.NightSkiing && lift.PlayerOpen && _sim.LightingSystem.LiftServesLitTerrain(lift);

        /// <summary>Wind grows with elevation, which is why upper lifts close first in a blizzard.</summary>
        public float EffectiveWindAtTop(Lift lift)
        {
            float topM = TerrainQueries.ElevationMeters(_sim.Map, _sim.TerrainScale, lift.TopStation.X, lift.TopStation.Y);
            float exposure = 1f + Math.Max(0f, topM - _sim.Config.Weather.BaseElevationMeters) / 800f;
            return _sim.World.Weather.WindSpeed * exposure;
        }

        // ---------- Loading and riding ----------

        /// <summary>Guests per carrier (chair, bar, cabin) for this lift's type.</summary>
        public int Seats(Lift lift) => Math.Max(1, _sim.Config.GetLift(lift.Type).SeatsPerCarrier);

        /// <summary>
        /// Ticks of line travel between carriers at the loading point. A carrier holds <see cref="Seats"/>
        /// guests, so carriers pass at CapacityPerHour / Seats per hour.
        /// </summary>
        public double CarrierInterval(Lift lift)
            => lift.CapacityPerHour <= 0 ? double.MaxValue : Seats(lift) * (double)_sim.TimeSystem.TicksPerHour / lift.CapacityPerHour;

        /// <summary>
        /// Brings a lift's speed and capacity in line with its type's current definition (after the config has
        /// been retuned). The rope keeps its position; riders already aboard carry on from where they are, and the
        /// next carrier starts at the current line position. Does nothing when the lift already matches.
        /// </summary>
        public void ApplyDefinition(Lift lift)
        {
            var def = _sim.Config.GetLift(lift.Type);
            if (def.SpeedTilesPerMinute <= 0f || def.CapacityPerHour <= 0) return;
            int rideTicks = Math.Max(1, (int)Math.Ceiling(lift.LengthTiles / def.SpeedTilesPerMinute * _sim.TimeSystem.TicksPerMinute));
            if (lift.SpeedTilesPerMinute == def.SpeedTilesPerMinute && lift.CapacityPerHour == def.CapacityPerHour && lift.RideTicks == rideTicks)
                return;

            lift.SpeedTilesPerMinute = def.SpeedTilesPerMinute;
            lift.CapacityPerHour = def.CapacityPerHour;
            lift.RideTicks = rideTicks;
            lift.NextCarrier = (long)Math.Ceiling(lift.LineClock / CarrierInterval(lift));
            _sim.World.LiftsVersion++;
        }

        /// <summary>Guests per hour carried from a long line, allowing for seats that go unfilled.</summary>
        public float EffectiveCapacity(Lift lift)
        {
            var def = _sim.Config.GetLift(lift.Type);
            int seats = Math.Max(1, def.SeatsPerCarrier);
            float fill = (1f + (seats - 1) * SimMath.Clamp(def.LoadingEfficiency, 0f, 1f)) / seats;
            return lift.CapacityPerHour * fill;
        }

        /// <summary>True while the haul rope turns: the lift is open, or still bringing riders up after closing.</summary>
        public static bool LineMoving(Lift lift)
        {
            if (lift.Status == LiftStatus.WindHold || lift.Status == LiftStatus.Broken || lift.Status == LiftStatus.NoPower) return false;
            return lift.IsRunning || lift.Riders.Count > 0;
        }

        /// <summary>Progress (0..1) up the line of a carrier that loaded at <paramref name="boardClock"/>.</summary>
        public static float Progress(Lift lift, double boardClock, double lineClock)
            => lift.RideTicks <= 0 ? 1f : (float)Math.Max(0.0, Math.Min(1.0, (lineClock - boardClock) / lift.RideTicks));

        /// <summary>
        /// Advances the haul rope one tick. Every time a carrier reaches the loading point, up to
        /// <see cref="Seats"/> guests from the front of the line sit down on it together. The first seat is
        /// always taken when someone is waiting; each further seat only with the lift type's loading efficiency,
        /// so (like a real lift line) not everyone gets on the next chair.
        /// </summary>
        private void AdvanceLine(Lift lift)
        {
            // On hold, or empty after hours: the rope (and every carrier on it) stands still.
            if (!LineMoving(lift)) return;

            lift.EffectiveCapacityPerHour = EffectiveCapacity(lift);
            double interval = CarrierInterval(lift);
            lift.LineClock += 1.0;

            // Carriers that reached the loading point this tick.
            while (lift.NextCarrier * interval <= lift.LineClock)
            {
                double boardClock = lift.NextCarrier * interval;
                lift.NextCarrier++;
                if (lift.IsRunning) LoadCarrier(lift, boardClock);
            }

            MoveRiders(lift);
        }

        private void LoadCarrier(Lift lift, double boardClock)
        {
            if (lift.QueueGuests.Count == 0) return;
            var def = _sim.Config.GetLift(lift.Type);
            int seats = Math.Max(1, def.SeatsPerCarrier);
            var rng = _sim.World.Rng;

            // Seats are filled from the outside in so a half-full chair looks natural (one rider on each end).
            for (int k = 0; k < seats && lift.QueueGuests.Count > 0; k++)
            {
                if (k > 0 && !rng.Chance(def.LoadingEfficiency)) continue;
                int seat = SeatOrder(k, seats);
                int guest = lift.QueueGuests.Dequeue();
                lift.Riders.Enqueue(new LiftRider { GuestIndex = guest, BoardClock = boardClock, Seat = (byte)seat });
                lift.RidersToday++;
                _sim.World.Economy.Today.LiftRides++;
                _sim.GuestSystem.OnBoardedLift(guest, lift);
            }
        }

        /// <summary>k-th seat to fill on a carrier with <paramref name="seats"/> seats: 0, last, 1, last-1, ...</summary>
        private static int SeatOrder(int k, int seats) => (k & 1) == 0 ? k / 2 : seats - 1 - k / 2;

        private void MoveRiders(Lift lift)
        {
            if (lift.Riders.Count == 0) return;

            var guests = _sim.World.Guests;
            double clock = lift.LineClock;
            foreach (var r in lift.Riders)
            {
                lift.PointAlong(Progress(lift, r.BoardClock, clock), out float x, out float y);
                guests.SetPosition(r.GuestIndex, x, y);
            }

            // Carriers arrive in loading order, so only the front can be done.
            while (lift.Riders.Count > 0 && clock - lift.Riders.Peek().BoardClock >= lift.RideTicks)
            {
                var r = lift.Riders.Dequeue();
                _sim.GuestSystem.OnArrivedAtTop(r.GuestIndex, lift);
            }
        }

        /// <summary>Progress (0..1) of a rider along the line.</summary>
        public float RideProgress(Lift lift, LiftRider rider) => Progress(lift, rider.BoardClock, lift.LineClock);

        public void Enqueue(Lift lift, int guest) => lift.QueueGuests.Enqueue(guest);

        private void EjectQueue(Lift lift)
        {
            while (lift.QueueGuests.Count > 0)
                _sim.GuestSystem.OnQueueClosed(lift.QueueGuests.Dequeue(), lift);
        }

        // ---------- Construction ----------

        public Lift CreateLift(LiftType type, Int2 bottom, Int2 top, LiftQuote quote)
        {
            var world = _sim.World;
            var def = _sim.Config.GetLift(type);
            int id = world.NextLiftId++;
            int sameType = 1;
            foreach (var l in world.Lifts) if (l.Type == type) sameType++;

            var lift = new Lift
            {
                Id = id,
                Name = $"{def.DisplayName} {sameType}",
                Type = type,
                BottomStation = bottom,
                TopStation = top,
                Towers = quote.Towers.ToArray(),
                LengthTiles = quote.LengthTiles,
                RiseSteps = quote.RiseSteps,
                CapacityPerHour = def.CapacityPerHour,
                SpeedTilesPerMinute = def.SpeedTilesPerMinute,
                WindLimit = def.WindLimitKmh,
                PowerDraw = def.PowerDrawKw,
                StaffRequired = def.StaffRequired,
                OperatingCostPerHour = def.OperatingCostPerHour,
            };
            lift.RideTicks = Math.Max(1, (int)Math.Ceiling(lift.LengthTiles / def.SpeedTilesPerMinute * _sim.TimeSystem.TicksPerMinute));
            lift.EffectiveCapacityPerHour = EffectiveCapacity(lift);

            var map = _sim.Map;
            foreach (var t in quote.LineTiles)
                if (map.GetTreeDensity(t.X, t.Y) > 0) map.SetTreeDensity(t.X, t.Y, 0);

            map.SetOccupant(bottom.X, bottom.Y, Occupant.Pack(OccupantKind.LiftStation, id));
            map.SetOccupant(top.X, top.Y, Occupant.Pack(OccupantKind.LiftStation, id));
            foreach (var tower in lift.Towers)
                map.SetOccupant(tower.X, tower.Y, Occupant.Pack(OccupantKind.LiftTower, id));

            world.Economy.AddExpense(LedgerCategory.Construction, quote.Cost);
            world.Lifts.Add(lift);
            world.LiftsVersion++;

            lift.Status = ComputeStatus(lift, _sim.TimeSystem.LiftsOperating);
            _sim.RunSystem.RefreshConnections();
            _sim.Events.Publish(SimEventKind.LiftBuilt, EventSeverity.Good, $"{lift.Name} built for ${quote.Cost:N0}.", bottom, id);
            return lift;
        }

        public void Demolish(Lift lift)
        {
            var world = _sim.World;
            var map = _sim.Map;

            EjectQueue(lift);
            while (lift.Riders.Count > 0)
            {
                var r = lift.Riders.Dequeue();
                // Lowered down by ski patrol: drop them at the bottom station.
                world.Guests.Teleport(r.GuestIndex, lift.BottomStation.X + 0.5f, lift.BottomStation.Y + 0.5f);
                _sim.GuestSystem.OnQueueClosed(r.GuestIndex, lift);
            }

            map.SetOccupant(lift.BottomStation.X, lift.BottomStation.Y, 0);
            map.SetOccupant(lift.TopStation.X, lift.TopStation.Y, 0);
            foreach (var tower in lift.Towers) map.SetOccupant(tower.X, tower.Y, 0);

            world.Lifts.Remove(lift);
            world.LiftsVersion++;
            _sim.RunSystem.RefreshConnections();
        }

        public void SetPlayerOpen(Lift lift, bool open)
        {
            lift.PlayerOpen = open;
            RefreshStatuses();
            _sim.World.LiftsVersion++;
        }

        /// <summary>Hourly running cost for every lift that ran this hour. Called by EconomySystem.</summary>
        public long HourlyOperatingCost()
        {
            long total = 0;
            foreach (var lift in _sim.World.Lifts)
                if (lift.IsRunning || lift.Riders.Count > 0) total += lift.OperatingCostPerHour;
            return total;
        }

        public void ResetDailyStats()
        {
            foreach (var lift in _sim.World.Lifts) lift.RidersToday = 0;
        }

        public Lift FindNearestBottom(float x, float y, float maxDistance, Func<Lift, bool> filter = null)
        {
            Lift best = null;
            float bestD = maxDistance;
            foreach (var lift in _sim.World.Lifts)
            {
                if (filter != null && !filter(lift)) continue;
                float dx = lift.BottomStation.X + 0.5f - x, dy = lift.BottomStation.Y + 0.5f - y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < bestD) { bestD = d; best = lift; }
            }
            return best;
        }
    }
}
