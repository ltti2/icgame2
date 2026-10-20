using System;
using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Guests
{
    /// <summary>
    /// The guest state machine. Each guest is an index into <see cref="GuestPool"/>.
    ///
    /// Flow: arrive -> walk to a lift -> queue -> ride -> pick a run (or walk to another lift) -> ski tile by
    /// tile -> at every exit on the way down (a lift bottom beside the run, a junction with another run)
    /// decide whether to peel off -> at the end, decide again -> eventually head home.
    ///
    /// Decisions use <see cref="RouteValues"/>, so guests plan across the lift network: an expert at the base
    /// rides the lower lift to reach a mid-mountain lift that serves black terrain, and laps that upper lift
    /// instead of skiing all the way back down every time.
    ///
    /// Lifts call back into this system (OnBoardedLift / OnArrivedAtTop / OnQueueClosed) so that
    /// lift logic and guest logic stay in their own files.
    ///
    /// Guests arrive by car and walk back to it to go home. On walking areas they walk (skis off) and can reach
    /// any lift opening onto the area. After dark they avoid unlit trails unless there is no other way down.
    /// </summary>
    public sealed class GuestSystem : ISimSystem
    {
        /// <summary>Exits are taken when the skier gets within this flow distance of them.</summary>
        private const float ExitReach = 1.0f;
        /// <summary>Weight of the remaining skiing on a run when comparing "keep going" with exits.</summary>
        private const float LeavingRunScale = 3f;

        private readonly GameSimulation _sim;
        private readonly List<int> _nearRunIds = new List<int>();
        private readonly List<Int2> _nearJoins = new List<Int2>();
        private readonly List<float> _nearDistances = new List<float>();

        public GuestSystem(GameSimulation sim)
        {
            _sim = sim;
            Routes = new RouteValues(sim);
        }

        /// <summary>Route values for the lift network (cached, rebuilt when the mountain changes).</summary>
        public RouteValues Routes { get; }

        /// <summary>Stragglers still on the hill this long after the last lift stops are sent home.</summary>
        private int SweepMinuteOfDay => Math.Min(23 * 60 + 30, Math.Max(19 * 60, _sim.Config.Time.NightCloseMinute + 60));

        private GuestPool Pool => _sim.World.Guests;
        private WorldMap Map => _sim.World.Map;
        private SimRandom Rng => _sim.World.Rng;
        private float TicksPerMinute => _sim.Config.Time.TicksPerGameMinute;
        private float SkiSpeedPerTick => _sim.Config.Guests.SkiSpeedTilesPerMinute / TicksPerMinute;
        private float WalkSpeedPerTick => _sim.Config.Guests.WalkSpeedTilesPerMinute / TicksPerMinute;

        public void Tick()
        {
            var pool = Pool;
            if (_sim.Clock.MinuteAdvanced && _sim.World.Time.MinuteOfDay == SweepMinuteOfDay)
                SendEveryoneHome();

            int interval = Math.Max(1, _sim.Config.Guests.NeedsUpdateIntervalTicks);
            long tick = _sim.World.Tick;

            for (int i = 0; i < pool.HighWater; i++)
            {
                if (!pool.Alive[i]) continue;

                // Staggered needs update: each guest once per interval, spread across ticks.
                if ((tick + i) % interval == 0) UpdateNeeds(i);

                switch (pool.State[i])
                {
                    case GuestState.WalkingToLift: TickWalkingToLift(i); break;
                    case GuestState.Queueing:
                        // The wait only counts once the line is moving: early birds expect to wait for opening.
                        var queuedFor = _sim.World.GetLift(pool.TargetId[i]);
                        if (queuedFor != null && queuedFor.IsRunning) pool.StateTimer[i]++;
                        break;
                    case GuestState.Resting: TickMeandering(i); break;
                    case GuestState.Skiing: TickSkiing(i); break;
                    case GuestState.Leaving: TickLeaving(i); break;
                }
            }
        }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        /// <summary>Called by ArrivalSystem once a guest has paid and stepped out of the car.</summary>
        public void OnArrived(int g) => DecideNext(g);

        private void StartLeaving(int g)
        {
            Pool.State[g] = GuestState.Leaving;
            Pool.TargetId[g] = 0;
        }

        private void Depart(int g)
        {
            var ledger = _sim.World.Economy.Today;
            ledger.GuestsDeparted++;
            ledger.HappinessSum += Pool.Happiness[g];
            int car = Pool.Car[g];
            Pool.Despawn(g);
            _sim.ParkingSystem.OnGuestGone(car); // last one out drives the car home
        }

        private void SendEveryoneHome()
        {
            var pool = Pool;
            for (int i = 0; i < pool.HighWater; i++)
                if (pool.Alive[i] && pool.State[i] != GuestState.Riding && pool.State[i] != GuestState.Queueing)
                    Depart(i);
        }

        private void UpdateNeeds(int g)
        {
            var pool = Pool;
            var time = _sim.World.Time;

            // Cold: below -8 C (18 F) you slowly lose warmth unless inside a cabin.
            var tile = CurrentTile(g);
            float temp = _sim.WeatherSystem.TemperatureAt(tile.X, tile.Y);
            bool sheltered = pool.State[g] == GuestState.Riding && IsOnProtectedLift(g);
            if (temp < -8f && !sheltered) pool.Warmth[g] = SimMath.AddClamped(pool.Warmth[g], -1);
            else pool.Warmth[g] = SimMath.AddClamped(pool.Warmth[g], 1);
            if (pool.Warmth[g] < 30) AddHappiness(g, -0.15f);

            if (pool.State[g] == GuestState.Queueing)
            {
                float waited = pool.StateTimer[g] / TicksPerMinute;
                if (waited > 5f) AddHappiness(g, -0.2f);
            }

            bool closingSoon = time.MinuteOfDay >= _sim.TimeSystem.ClosingMinute - 15;
            if (time.MinuteOfDay >= pool.DepartureMinute[g] || pool.Energy[g] < 15 || closingSoon)
                pool.Flags[g] |= GuestFlags.WantsToLeave;
        }

        private bool IsOnProtectedLift(int g)
        {
            var lift = _sim.World.GetLift(Pool.TargetId[g]);
            return lift != null && _sim.Config.GetLift(lift.Type).WeatherProtected;
        }

        private void AddHappiness(int g, float delta)
            => Pool.Happiness[g] = SimMath.Clamp(Pool.Happiness[g] + delta, 0f, 100f);

        private bool WantsToGoHome(int g)
            => (Pool.Flags[g] & GuestFlags.WantsToLeave) != 0 || (!_sim.TimeSystem.LiftsOperating && !BeforeOpening);

        /// <summary>Morning, before the lifts open: early birds are here to wait, not to go home.</summary>
        private bool BeforeOpening
        {
            get
            {
                int m = _sim.World.Time.MinuteOfDay;
                var t = _sim.Config.Time;
                return m >= t.EarlyArrivalsStartMinute && m < t.LiftsOpenMinute;
            }
        }

        /// <summary>Too early even to line up.</summary>
        private bool BeforeLineUp
        {
            get
            {
                var t = _sim.Config.Time;
                return BeforeOpening && _sim.World.Time.MinuteOfDay < t.LiftsOpenMinute - t.LineUpBeforeOpeningMinutes;
            }
        }

        private Int2 CurrentTile(int g)
            => new Int2(SimMath.Clamp((int)Pool.PosX[g], 0, Map.Width - 1), SimMath.Clamp((int)Pool.PosY[g], 0, Map.Height - 1));

        // =====================================================================
        // Deciding where to go next
        // =====================================================================

        /// <summary>
        /// The guest is standing somewhere (base area, a lift top, the end of a run) and picks their next move:
        /// a lift within walking distance, a run they can ski onto from here, or home.
        /// </summary>
        /// <param name="atTop">The lift they just rode, if deciding at its top station.</param>
        /// <param name="allowOffPiste">False when called from an off-piste dead end, to prevent loops.</param>
        private void DecideNext(int g, Lift atTop = null, bool allowOffPiste = true)
        {
            var pool = Pool;
            var cfg = _sim.Config.Guests;
            pool.Flags[g] &= ~(GuestFlags.OnRun | GuestFlags.Scared | GuestFlags.Fallen);

            bool leaving = WantsToGoHome(g);
            var here = CurrentTile(g);
            bool atBase = atTop == null && _sim.RunSystem.IsAtBase(here);
            if (leaving && atBase)
            {
                StartLeaving(g);
                return;
            }

            // Early birds: wander the base until it's time to line up.
            if (atBase && !leaving && BeforeLineUp)
            {
                StartMeandering(g);
                return;
            }

            var skill = pool.Skill[g];
            float bestScore = float.NegativeInfinity;
            Lift bestLift = null;
            Run bestRun = null;
            Int2 bestJoin = Int2.Zero;

            // Lifts within walking distance (far at the base, short on the mountain), plus every lift that
            // opens onto the walking area the guest is standing on, however far across it.
            float liftRadius = atBase ? cfg.LiftSearchRadius : cfg.LiftWalkRadius;
            float hereHeight = Map.TileCenterHeight(here.X, here.Y);
            var zone = _sim.WalkingAreaSystem.ZoneNear(here, WalkZoneReach);
            foreach (var lift in _sim.World.Lifts)
            {
                if (!_sim.LiftSystem.Boardable(lift) || lift == atTop) continue;
                float walk = Distance(g, lift.BottomStation);
                bool onZone = zone != null && zone.LiftBottoms.Contains(lift.Id);
                if (walk > liftRadius && !onZone) continue;

                float value = leaving ? LeavingLiftValue(lift) : Routes.Lift(skill, lift.Id);
                float climb = (Map.TileCenterHeight(lift.BottomStation.X, lift.BottomStation.Y) - hereHeight) * _sim.TerrainScale.MetersPerStep;
                float score = GuestChoices.ScoreLift(value, lift.EstimatedWaitMinutes, walk, climb, cfg.ChoiceRandomness, Rng.Range(-1f, 1f));
                if (score > bestScore) { bestScore = score; bestLift = lift; bestRun = null; }
            }

            // Runs you can ski onto from here: only tiles that aren't above you. At a lift top a short
            // side-step up to the start of a trail is fine; at the bottom of a run nobody hikes back up.
            CollectNearbyRuns(here, cfg.RunConnectRadius, hereHeight + (atTop != null ? MaxClimbToRunAtLiftTop : MaxClimbToRun));
            for (int i = 0; i < _nearRunIds.Count; i++)
            {
                var run = _sim.World.GetRun(_nearRunIds[i]);
                if (run == null || !run.Open || _nearDistances[i] < 3f) continue;
                // Don't hop back onto the bottom few tiles of the run you just finished.
                if (run.Id == pool.LastRunId[g] && _nearDistances[i] < 15f) continue;
                float score = RunOptionScore(g, run, _nearDistances[i], _nearJoins[i].Distance(here), leaving);
                if (score > bestScore) { bestScore = score; bestRun = run; bestJoin = _nearJoins[i]; bestLift = null; }
            }

            // Heading home but nothing here leads down: hike out on foot.
            if (leaving && bestScore < 0.15f)
            {
                StartLeaving(g);
                return;
            }

            // Nothing on offer that this guest wants.
            bool nothing = bestLift == null && bestRun == null;
            bool allBad = !leaving && bestScore < GuestChoices.Unacceptable + 0.5f;

            if (atBase && nothing && BeforeOpening)
            {
                StartMeandering(g); // nothing to line up for yet
                return;
            }

            if (atBase && (nothing || allBad))
            {
                if (pool.RunsSkied[g] == 0) AddHappiness(g, -15f); // drove up for nothing
                StartLeaving(g);
                return;
            }

            if (nothing)
            {
                if (atTop != null)
                {
                    // Nothing marked off the top: they pick their own way down.
                    AddHappiness(g, -5f);
                    GoOffPiste(g);
                    return;
                }
                WalkOutOrGiveUp(g, allowOffPiste);
                return;
            }

            if (allBad && atTop != null) AddHappiness(g, -6f); // "how do I get down from here?"

            if (bestLift != null) WalkTo(g, bestLift);
            else StartRun(g, bestRun, bestJoin);
        }

        /// <summary>Score of skiing <paramref name="run"/> from a point with <paramref name="joinDistance"/> left to go.</summary>
        private float RunOptionScore(int g, Run run, float joinDistance, float walkTiles, bool leaving)
        {
            var cfg = _sim.Config.Guests;
            if (leaving)
                return Routes.RunWayDown(run, joinDistance) * LeavingRunScale * DarkWayDownFactor(run) + Rng.Range(-1f, 1f) * 0.1f;

            float remaining = SimMath.Clamp(joinDistance / Math.Max(1f, run.PathLength), 0.15f, 1f);
            float downstream = Routes.Downstream(Pool.Skill[g], run, joinDistance);
            return GuestChoices.ScoreRun(Pool, g, run, remaining, downstream, walkTiles, cfg.ChoiceRandomness, Rng.Range(-1f, 1f))
                   - DarkPenalty(run);
        }

        /// <summary>After dark, unlit trails are skied only when there is nothing better (see LightingSystem).</summary>
        private float DarkPenalty(Run run) => _sim.LightingSystem.IsRunDark(run) ? _sim.Config.Lighting.DarkRunPenalty : 0f;

        /// <summary>Heading home in the dark, a lit way down is preferred; a dark one still beats none.</summary>
        private float DarkWayDownFactor(Run run) => _sim.LightingSystem.IsRunDark(run) ? 0.5f : 1f;

        /// <summary>A guest within this many tiles of a walking area counts as standing on it.</summary>
        private const float WalkZoneReach = 1.5f;

        /// <summary>Riding a lift up to go home only makes sense if nothing else leads down.</summary>
        private float LeavingLiftValue(Lift lift)
        {
            float gamma = _sim.Config.Guests.RouteLookahead;
            return Routes.LiftWayDown(lift.Id) * gamma * gamma * LeavingRunScale - 1f;
        }

        /// <summary>Height steps a guest will climb to get onto a run after finishing one (practically none).</summary>
        private const float MaxClimbToRun = 0.5f;
        /// <summary>Height steps a guest will side-step up from a lift's top station to reach a trail.</summary>
        private const float MaxClimbToRunAtLiftTop = 3f;

        /// <summary>
        /// Distinct runs with a tile within <paramref name="radius"/> of a point. For each, the join tile is the
        /// highest one in reach that is no higher than <paramref name="maxHeight"/>, so the guest gets as much
        /// of the run as possible without climbing back up the hill to get it.
        /// </summary>
        private void CollectNearbyRuns(Int2 center, float radius, float maxHeight)
        {
            _nearRunIds.Clear();
            _nearJoins.Clear();
            _nearDistances.Clear();
            int r = (int)Math.Ceiling(radius);
            float r2 = radius * radius;
            for (int y = center.Y - r; y <= center.Y + r; y++)
            {
                for (int x = center.X - r; x <= center.X + r; x++)
                {
                    if (!Map.InBounds(x, y)) continue;
                    float dx = x - center.X, dy = y - center.Y;
                    if (dx * dx + dy * dy > r2) continue;
                    int runId = Map.GetRunId(x, y);
                    if (runId == 0) continue;
                    if (Map.TileCenterHeight(x, y) > maxHeight) continue;
                    var run = _sim.World.GetRun(runId);
                    if (run == null) continue;
                    float d = run.DistanceAt(Map.TileKey(x, y));
                    int idx = _nearRunIds.IndexOf(runId);
                    if (idx < 0)
                    {
                        _nearRunIds.Add(runId);
                        _nearJoins.Add(new Int2(x, y));
                        _nearDistances.Add(d);
                    }
                    else if (d > _nearDistances[idx])
                    {
                        _nearJoins[idx] = new Int2(x, y);
                        _nearDistances[idx] = d;
                    }
                }
            }
        }

        private void WalkTo(int g, Lift lift)
        {
            Pool.State[g] = GuestState.WalkingToLift;
            Pool.TargetId[g] = lift.Id;
        }

        /// <summary>Stuck somewhere with no run or lift in reach: trudge to the nearest lift, or give up.</summary>
        private void WalkOutOrGiveUp(int g, bool allowOffPiste)
        {
            var lift = _sim.LiftSystem.FindNearestBottom(Pool.PosX[g], Pool.PosY[g], _sim.Config.Guests.LiftSearchRadius, l => l.IsRunning);
            if (lift != null && !WantsToGoHome(g))
            {
                Pool.Flags[g] |= GuestFlags.WalkingOut;
                AddHappiness(g, -6f);
                WalkTo(g, lift);
                return;
            }
            if (allowOffPiste && !_sim.RunSystem.IsAtBase(CurrentTile(g)))
            {
                GoOffPiste(g);
                return;
            }
            AddHappiness(g, -4f);
            StartLeaving(g);
        }

        private void TickWalkingToLift(int g)
        {
            var pool = Pool;
            var lift = _sim.World.GetLift(pool.TargetId[g]);
            if (lift == null || !_sim.LiftSystem.Boardable(lift))
            {
                DecideNext(g);
                return;
            }

            // Walk to the back of the corral (LiftSystem lays the queue out 4 abreast behind the station).
            float dx = lift.TopStation.X - lift.BottomStation.X, dy = lift.TopStation.Y - lift.BottomStation.Y;
            float len = Math.Max(0.001f, (float)Math.Sqrt(dx * dx + dy * dy));
            float back = 0.8f + (lift.QueueCount / 4) * 0.35f;
            float tx = lift.BottomStation.X + 0.5f - dx / len * back;
            float ty = lift.BottomStation.Y + 0.5f - dy / len * back;

            float speed = TravelSpeed(g, tx, ty, WalkSpeedPerTick * ((pool.Flags[g] & GuestFlags.WalkingOut) != 0 ? 0.8f : 1f));
            if (MoveToward(g, tx, ty, speed))
            {
                pool.Flags[g] &= ~GuestFlags.WalkingOut;
                pool.State[g] = GuestState.Queueing;
                pool.StateTimer[g] = 0;
                _sim.LiftSystem.Enqueue(lift, g);
            }
        }

        // =====================================================================
        // Early birds
        // =====================================================================

        /// <summary>Strolling speed while killing time, as a fraction of walking speed.</summary>
        private const float StrollSpeedFactor = 0.6f;

        private void StartMeandering(int g)
        {
            Pool.State[g] = GuestState.Resting;
            Pool.TargetId[g] = 0;
            PickStrollTarget(g);
        }

        /// <summary>Somewhere nearby on the base's walking area (or near the entrance) to wander to.</summary>
        private void PickStrollTarget(int g)
        {
            var pool = Pool;
            var e = _sim.World.ResortEntrance;
            Int2 target = new Int2(e.X + Rng.Range(-3, 4), e.Y + Rng.Range(-3, 4));
            for (int tries = 0; tries < 8; tries++)
            {
                int x = (int)pool.PosX[g] + Rng.Range(-6, 7), y = (int)pool.PosY[g] + Rng.Range(-6, 7);
                if (Map.IsWalkable(x, y) && _sim.RunSystem.IsAtBase(new Int2(x, y))) { target = new Int2(x, y); break; }
            }
            target = new Int2(SimMath.Clamp(target.X, 0, Map.Width - 1), SimMath.Clamp(target.Y, 0, Map.Height - 1));
            pool.WaypointKey[g] = Map.TileKey(target.X, target.Y);
            pool.StateTimer[g] = 0;
        }

        /// <summary>
        /// Wander, stop a while (coffee, boots, chatting), wander again. Once it's time to line up (or they've
        /// had enough), decide what to do, once a minute.
        /// </summary>
        private void TickMeandering(int g)
        {
            var pool = Pool;
            if (_sim.Clock.MinuteAdvanced && (!BeforeLineUp || WantsToGoHome(g)))
            {
                DecideNext(g);
                return;
            }

            if (pool.StateTimer[g] > 0)
            {
                if (--pool.StateTimer[g] == 0) PickStrollTarget(g);
                return;
            }

            var target = Map.FromTileKey(pool.WaypointKey[g]);
            float tx = target.X + 0.5f, ty = target.Y + 0.5f;
            if (MoveToward(g, tx, ty, WalkSpeedPerTick * StrollSpeedFactor))
                pool.StateTimer[g] = (int)(Rng.Range(2f, 8f) * TicksPerMinute); // pause here
        }

        /// <summary>Back to the car (or the entrance, for guests who didn't drive) and home.</summary>
        private void TickLeaving(int g)
        {
            float tx, ty;
            if (!_sim.ParkingSystem.TryGetCarPosition(Pool.Car[g], out tx, out ty))
            {
                var e = _sim.World.ResortEntrance;
                tx = e.X + 0.5f;
                ty = e.Y + 0.5f;
            }
            if (MoveToward(g, tx, ty, TravelSpeed(g, tx, ty, WalkSpeedPerTick * 1.3f)))
                Depart(g);
        }

        /// <summary>
        /// How fast a guest gets somewhere off a run (to a lift line, back to the car). On snow they keep their
        /// skis on: they glide when it's downhill and skate or side-step otherwise. Only on bare ground do they
        /// take their skis off and walk.
        /// </summary>
        private float TravelSpeed(int g, float tx, float ty, float walkSpeed)
        {
            var here = CurrentTile(g);
            var t = Map.Locate(here.X, here.Y);
            if (t.Chunk.SnowDepth[t.Index] < _sim.Config.Snow.BareDepthCm) return walkSpeed;
            if (t.Chunk.Surface[t.Index] != (byte)SurfaceType.Natural) return walkSpeed; // skis off on walking areas
            int ttx = SimMath.Clamp((int)tx, 0, Map.Width - 1), tty = SimMath.Clamp((int)ty, 0, Map.Height - 1);
            float drop = Map.TileCenterHeight(here.X, here.Y) - Map.TileCenterHeight(ttx, tty);
            return drop > 0.5f ? Math.Max(walkSpeed, SkiSpeedPerTick * GlideSpeedFactor) : walkSpeed;
        }

        /// <summary>Gliding downhill to a lift, as a fraction of skiing speed.</summary>
        private const float GlideSpeedFactor = 0.75f;

        // =====================================================================
        // Lift callbacks
        // =====================================================================

        public void OnBoardedLift(int g, Lift lift)
        {
            var pool = Pool;
            float waited = pool.StateTimer[g] / TicksPerMinute;
            if (waited > 10f) AddHappiness(g, -(waited - 10f) * 0.25f);
            pool.State[g] = GuestState.Riding;
            pool.TargetId[g] = lift.Id;
            pool.StateTimer[g] = 0;
        }

        public void OnArrivedAtTop(int g, Lift lift)
        {
            var pool = Pool;
            pool.Teleport(g, lift.TopStation.X + 0.5f, lift.TopStation.Y + 0.5f);
            pool.State[g] = GuestState.ChoosingRun;
            DecideNext(g, atTop: lift);
        }

        public void OnQueueClosed(int g, Lift lift)
        {
            AddHappiness(g, -2f);
            Pool.StateTimer[g] = 0;
            DecideNext(g);
        }

        /// <summary>
        /// Tiles were added to or erased from a run. Skiers heading for a tile that is no longer on it re-join the
        /// run at the nearest tile below them (OnEnterTile does that when OnRun is cleared).
        /// </summary>
        public void OnRunReshaped(int runId)
        {
            var pool = Pool;
            var run = _sim.World.GetRun(runId);
            if (run == null) { OnRunRemoved(runId); return; }
            for (int i = 0; i < pool.HighWater; i++)
            {
                if (!pool.Alive[i] || pool.State[i] != GuestState.Skiing || pool.TargetId[i] != runId) continue;
                if (!run.ContainsTile(pool.WaypointKey[i])) pool.Flags[i] &= ~GuestFlags.OnRun;
            }
        }

        public void OnRunRemoved(int runId)
        {
            var pool = Pool;
            for (int i = 0; i < pool.HighWater; i++)
            {
                if (!pool.Alive[i] || pool.State[i] != GuestState.Skiing || pool.TargetId[i] != runId) continue;
                GoOffPiste(i);
            }
        }

        /// <summary>
        /// Run exits were rebuilt (a lift or run was added, removed or reshaped). Re-sync every skier's
        /// position in their run's exit list.
        /// </summary>
        public void OnConnectionsChanged()
        {
            var pool = Pool;
            for (int i = 0; i < pool.HighWater; i++)
            {
                if (!pool.Alive[i] || pool.State[i] != GuestState.Skiing || pool.TargetId[i] == 0) continue;
                var run = _sim.World.GetRun(pool.TargetId[i]);
                if (run == null) continue;
                float d = run.DistanceAt(pool.WaypointKey[i]);
                if (d == float.MaxValue) d = run.DistanceAt(Map.TileKey(run.Start.X, run.Start.Y));
                pool.ExitCursor[i] = (byte)Math.Min(255, RunSystem.FirstExitBelow(run, d));
            }
        }

        // =====================================================================
        // Skiing
        // =====================================================================

        private void StartRun(int g, Run run, Int2 join)
        {
            var pool = Pool;
            pool.State[g] = GuestState.Skiing;
            pool.TargetId[g] = run.Id;
            pool.WaypointKey[g] = Map.TileKey(join.X, join.Y);
            pool.Steps[g] = 0;
            pool.Flags[g] &= ~(GuestFlags.OnRun | GuestFlags.Scared | GuestFlags.Fallen);
            float joinDistance = run.DistanceAt(Map.TileKey(join.X, join.Y));
            pool.ExitCursor[g] = (byte)Math.Min(255, RunSystem.FirstExitBelow(run, joinDistance));
            ChooseLine(g, run, joinDistance, firstLook: true);
        }

        // ---------- Picking a line across the run ----------

        /// <summary>Lanes a run is split into when a skier sizes it up.</summary>
        private const int LineLanes = 6;
        /// <summary>How far down the run (flow distance, tiles) a skier looks when picking a line.</summary>
        private const float LineLookAhead = 24f;
        /// <summary>Most tiles sampled per look, so huge runs stay cheap.</summary>
        private const int LineMaxSamples = 160;
        /// <summary>Skiers rethink their line every this many tiles.</summary>
        private const int LineRethinkSteps = 9;
        /// <summary>Least flow-distance gain (per tile moved) a carving step must make: always downhill.</summary>
        private const float MinCarveProgress = 0.05f;

        private readonly float[] _laneAppeal = new float[LineLanes];
        private readonly int[] _laneSamples = new int[LineLanes];

        /// <summary>
        /// Looks down the next stretch of the run and picks which part of it to ski: the side with snow this
        /// skier likes (experts hunt powder stashes, beginners want groomed), away from the tracked-out,
        /// crowded line, with plenty of personal taste mixed in. Re-run every few turns so skiers drift across
        /// the trail as conditions change instead of all following one line.
        /// </summary>
        private void ChooseLine(int g, Run run, float fromDistance, bool firstLook)
        {
            var pool = Pool;
            var skill = pool.Skill[g];
            Array.Clear(_laneAppeal, 0, LineLanes);
            Array.Clear(_laneSamples, 0, LineLanes);

            int lo = RunSystem.LowerBoundByDistance(run, fromDistance - LineLookAhead);
            int hi = RunSystem.LowerBoundByDistance(run, fromDistance + 1f);
            int stride = Math.Max(1, (hi - lo) / LineMaxSamples);
            for (int k = lo; k < hi; k += stride)
            {
                int slot = run.SlotsByDistance[k];
                var tile = run.Tiles[slot];
                var t = Map.Locate(tile.X, tile.Y);
                int lane = SimMath.Clamp((int)(run.Across[slot] * LineLanes), 0, LineLanes - 1);
                _laneAppeal[lane] += GuestChoices.SnowAppeal(skill, (SnowQuality)t.Chunk.SnowQuality[t.Index], t.Chunk.Traffic[t.Index]);
                _laneSamples[lane]++;
            }

            float current = pool.Line[g];
            float taste = firstLook ? 0.9f : 0.35f; // a fresh look at a run is mostly personal preference
            int bestLane = -1;
            float bestScore = float.NegativeInfinity;
            for (int lane = 0; lane < LineLanes; lane++)
            {
                if (_laneSamples[lane] == 0) continue;
                float centre = (lane + 0.5f) / LineLanes;
                float score = _laneAppeal[lane] / _laneSamples[lane] + Rng.Range(-1f, 1f) * taste;
                if (!firstLook) score -= Math.Abs(centre - current) * 0.8f; // don't cut across the whole trail on a whim
                if (score > bestScore) { bestScore = score; bestLane = lane; }
            }

            if (bestLane < 0) pool.Line[g] = firstLook ? Rng.Range(0.1f, 0.9f) : current;
            else pool.Line[g] = (bestLane + Rng.Range(0.15f, 0.85f)) / LineLanes;
        }

        /// <summary>Tiles between a run tile and where the skier wants to be across the run right now.</summary>
        private static float LineError(Run run, int key, float target)
        {
            float width = run.WidthAt(key);
            return Math.Abs(run.AcrossAt(key) - target) * width;
        }

        private void GoOffPiste(int g)
        {
            var pool = Pool;
            var tile = CurrentTile(g);
            pool.State[g] = GuestState.Skiing;
            pool.TargetId[g] = 0;
            pool.Flags[g] &= ~GuestFlags.OnRun;
            pool.WaypointKey[g] = Map.TileKey(tile.X, tile.Y);
        }

        private void TickSkiing(int g)
        {
            var pool = Pool;
            if ((pool.Flags[g] & GuestFlags.Fallen) != 0)
            {
                if (--pool.StateTimer[g] <= 0) pool.Flags[g] &= ~GuestFlags.Fallen;
                return;
            }

            var target = Map.FromTileKey(pool.WaypointKey[g]);
            float tx = target.X + 0.5f, ty = target.Y + 0.5f;

            // Spread skiers across the run with a per-guest lane offset perpendicular to travel.
            if ((pool.Flags[g] & GuestFlags.OnRun) != 0)
            {
                float dx = tx - pool.PosX[g], dy = ty - pool.PosY[g];
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.3f)
                {
                    tx += -dy / len * pool.Lane[g];
                    ty += dx / len * pool.Lane[g];
                }
            }

            float speed = SkiSpeed(g, CurrentTile(g), target);
            if (MoveToward(g, tx, ty, speed, arriveRadius: 0.35f))
                OnEnterTile(g, target);
        }

        private float SkiSpeed(int g, Int2 from, Int2 to)
        {
            var runs = _sim.Config.Runs;
            float hFrom = Map.TileCenterHeight(from.X, from.Y);
            float hTo = Map.TileCenterHeight(to.X, to.Y);
            if (hTo > hFrom + 0.5f) return WalkSpeedPerTick * runs.UphillSpeedFactor; // side-stepping uphill

            float slope = TerrainQueries.SlopePercent(Map, _sim.TerrainScale, to.X, to.Y);
            float slopeFactor = slope < runs.FlatGrade ? 0.35f : SimMath.Clamp(0.4f + slope / 40f, 0.4f, 1.8f);
            float skillFactor = 0.65f + 0.1f * (int)Pool.Skill[g];

            float snowFactor;
            switch (Map.GetSnowQuality(to.X, to.Y))
            {
                case SnowQuality.Ice: snowFactor = 1.15f; break;
                case SnowQuality.Groomed: snowFactor = 1.05f; break;
                case SnowQuality.Powder: snowFactor = 0.85f; break;
                case SnowQuality.Crud: snowFactor = 0.85f; break;
                case SnowQuality.Slush: snowFactor = 0.7f; break;
                case SnowQuality.Bare: snowFactor = 0.5f; break;
                default: snowFactor = 1f; break;
            }
            int trees = Map.GetTreeDensity(to.X, to.Y);
            float treeFactor = trees > 0 ? 1f / (1f + trees * 0.5f) : 1f;

            return SkiSpeedPerTick * slopeFactor * skillFactor * snowFactor * treeFactor;
        }

        /// <summary>Reached the centre of a new tile: apply its effects, consider exits, pick the next tile.</summary>
        private void OnEnterTile(int g, Int2 tile)
        {
            var pool = Pool;
            var run = pool.TargetId[g] != 0 ? _sim.World.GetRun(pool.TargetId[g]) : null;
            int key = Map.TileKey(tile.X, tile.Y);

            if (run == null)
            {
                // Off-piste: drop onto any open run we stumble across.
                int found = Map.GetRunId(tile.X, tile.Y);
                var foundRun = found != 0 ? _sim.World.GetRun(found) : null;
                if (foundRun != null && foundRun.Open)
                {
                    StartRun(g, foundRun, tile);
                    run = foundRun;
                }
                else
                {
                    if (!ApplyTileEffects(g, tile, null)) return;
                    AddHappiness(g, -0.1f);
                    if (_sim.RunSystem.IsAtBase(tile) || _sim.WalkingAreaSystem.ZoneAt(tile.X, tile.Y) != null)
                    {
                        DecideNext(g, allowOffPiste: false);
                        return;
                    }
                    if (!TryFallLineStep(tile, out var down))
                    {
                        WalkOutOrGiveUp(g, allowOffPiste: false);
                        return;
                    }
                    pool.WaypointKey[g] = Map.TileKey(down.X, down.Y);
                    return;
                }
            }

            if ((pool.Flags[g] & GuestFlags.OnRun) == 0)
            {
                if (!run.ContainsTile(key))
                {
                    var join = _sim.RunSystem.NearestTileNotAbove(run, pool.PosX[g], pool.PosY[g],
                                                                       Map.TileCenterHeight(tile.X, tile.Y) + MaxClimbToRun);
                    pool.WaypointKey[g] = Map.TileKey(join.X, join.Y);
                    return;
                }
                pool.Flags[g] |= GuestFlags.OnRun;
            }

            if (!ApplyTileEffects(g, tile, run)) return;

            if (TryTakeExit(g, run, run.DistanceAt(key))) return;

            if (tile == run.End || !TryCarveStep(g, run, tile, out var next))
            {
                LeaveRun(g, run);
                DecideNext(g);
                return;
            }
            pool.WaypointKey[g] = Map.TileKey(next.X, next.Y);
        }

        /// <summary>
        /// When the skier reaches one or more exits, weigh "keep going" against peeling off to the lift or
        /// run there. Returns true if the guest left the run.
        /// </summary>
        private bool TryTakeExit(int g, Run run, float distanceHere)
        {
            var pool = Pool;
            int cursor = pool.ExitCursor[g];
            if (cursor >= run.Exits.Count || distanceHere > run.Exits[cursor].Distance + ExitReach) return false;

            var cfg = _sim.Config.Guests;
            var skill = pool.Skill[g];
            bool leaving = WantsToGoHome(g);

            // Gather every exit within reach right now.
            int first = cursor;
            float lowestExit = float.MaxValue;
            while (cursor < run.Exits.Count && distanceHere <= run.Exits[cursor].Distance + ExitReach)
            {
                lowestExit = Math.Min(lowestExit, run.Exits[cursor].Distance);
                cursor++;
            }
            pool.ExitCursor[g] = (byte)Math.Min(255, cursor);

            // Option 0: keep skiing this run (valuing only what lies beyond these exits).
            float beyond = Math.Min(distanceHere, lowestExit) - 0.01f;
            float bestScore;
            if (leaving)
                bestScore = Routes.RunWayDown(run, beyond) * LeavingRunScale * DarkWayDownFactor(run) + Rng.Range(-1f, 1f) * 0.1f;
            else
            {
                float remaining = SimMath.Clamp(distanceHere / Math.Max(1f, run.PathLength), 0f, 1f);
                bestScore = GuestChoices.ScoreRun(pool, g, run, remaining, Routes.Downstream(skill, run, beyond), 0f,
                                                  cfg.ChoiceRandomness, Rng.Range(-1f, 1f)) - DarkPenalty(run);
                if (pool.LastRunId[g] != run.Id) bestScore -= 0.25f; // no novelty bonus for the run you're already on
            }

            int chosen = -1;
            for (int i = first; i < cursor; i++)
            {
                var e = run.Exits[i];
                float score;
                if (e.Kind == RunExitKind.Lift)
                {
                    var lift = _sim.World.GetLift(e.TargetId);
                    if (lift == null || !lift.IsRunning) continue;
                    float value = leaving ? LeavingLiftValue(lift) : Routes.Lift(skill, lift.Id);
                    score = GuestChoices.ScoreLift(value, lift.EstimatedWaitMinutes, Distance(g, lift.BottomStation), 0f,
                                                   cfg.ChoiceRandomness, Rng.Range(-1f, 1f));
                }
                else
                {
                    var other = _sim.World.GetRun(e.TargetId);
                    if (other == null || !other.Open) continue;
                    float joinDistance = other.DistanceAt(Map.TileKey(e.JoinTile.X, e.JoinTile.Y));
                    score = RunOptionScore(g, other, joinDistance, 1f, leaving);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    chosen = i;
                }
            }

            if (chosen < 0) return false;

            var exit = run.Exits[chosen];
            LeaveRun(g, run);
            if (exit.Kind == RunExitKind.Lift) WalkTo(g, _sim.World.GetLift(exit.TargetId));
            else StartRun(g, _sim.World.GetRun(exit.TargetId), exit.JoinTile);
            return true;
        }

        /// <summary>
        /// Picks the next tile down the run. Each skier holds their own line across the trail
        /// (<see cref="GuestPool.Line"/>, see <see cref="ChooseLine"/>) and makes S-turns around it, so a wide
        /// run is used edge to edge instead of everyone following the flow field's single shortest path.
        ///
        /// Candidates: run tiles that are not uphill. Score = progress toward the end + getting closer to where
        /// the skier wants to be across the run right now (their line, swung left and right by the turn
        /// rhythm) + a nudge toward snow they like. Beginners make long, wide traverses, experts tight turns.
        /// This spreads traffic, and therefore snow wear, across the whole run.
        /// </summary>
        private bool TryCarveStep(int g, Run run, Int2 from, out Int2 next)
        {
            next = from;
            if (!_sim.RunSystem.TryNextTile(run, from, out var best)) return false;

            var pool = Pool;
            pool.Steps[g]++;
            int fromKey = Map.TileKey(from.X, from.Y);
            float here = run.DistanceAt(fromKey);
            float hHere = Map.TileCenterHeight(from.X, from.Y);
            var skill = pool.Skill[g];

            // Safety valve: someone dawdling far longer than the run is long just takes the fall line.
            if (pool.Steps[g] > run.PathLength * 3f + 30f)
            {
                next = best;
                return true;
            }

            if (pool.Steps[g] % LineRethinkSteps == 0) ChooseLine(g, run, here, firstLook: false);

            float period = 11f - 1.5f * (int)skill;                         // tiles per full turn cycle
            float phase = pool.Lane[g] * 20f + pool.RunsSkied[g] * 1.3f;    // desynchronise guests
            float turn = (float)Math.Sin(pool.Steps[g] / period * 2.0 * Math.PI + phase);
            float swingTiles = 2.6f - 0.3f * (int)skill;                    // half-width of a turn
            float width = run.WidthAt(fromKey);
            float target = SimMath.Clamp(pool.Line[g] + turn * swingTiles / Math.Max(1f, width), 0f, 1f);
            float errHere = LineError(run, fromKey, target);

            // Momentum: the way the skier is already travelling. Turning across the hill is fine; swinging
            // round to face back up it is not something skis do.
            float vx = pool.PosX[g] - pool.PrevX[g], vy = pool.PosY[g] - pool.PrevY[g];
            float vlen = (float)Math.Sqrt(vx * vx + vy * vy);
            bool hasHeading = vlen > 1e-4f;
            if (hasHeading) { vx /= vlen; vy /= vlen; }

            float bestScore = float.NegativeInfinity;
            foreach (var o in Int2.Neighbours8)
            {
                int nx = from.X + o.X, ny = from.Y + o.Y;
                if (!Map.InBounds(nx, ny)) continue;
                int key = Map.TileKey(nx, ny);
                float d = run.DistanceAt(key);
                if (d == float.MaxValue) continue;                           // not on this run
                float hThere = Map.TileCenterHeight(nx, ny);
                if (hThere > hHere + 0.3f) continue;                         // no climbing for style

                float olen = o.X != 0 && o.Y != 0 ? 1.41421f : 1f;
                float progress = (here - d) / olen;
                // Every step must get closer to the bottom. Sideways is done with diagonal steps that still
                // make progress, so on flat run-outs skiers glide on instead of zig-zagging back up the trail.
                if (progress < MinCarveProgress) continue;

                float heading = 0f;
                if (hasHeading)
                {
                    heading = (o.X * vx + o.Y * vy) / olen;
                    if (heading < -0.35f) continue;                          // no U-turns
                }

                var t = Map.Locate(nx, ny);
                float snow = GuestChoices.SnowAppeal(skill, (SnowQuality)t.Chunk.SnowQuality[t.Index], t.Chunk.Traffic[t.Index]);
                float drop = Math.Max(0f, hHere - hThere) / olen;            // gravity: lower is easier
                float score = progress + (errHere - LineError(run, key, target)) * 1.1f + snow * 0.3f
                              + heading * 0.35f + Math.Min(drop, 2f) * 0.15f;
                if (score > bestScore)
                {
                    bestScore = score;
                    next = new Int2(nx, ny);
                }
            }
            if (next == from) next = best;
            return true;
        }

        /// <summary>Lowest neighbouring tile, for guests improvising their way down.</summary>
        private bool TryFallLineStep(Int2 from, out Int2 next)
        {
            next = from;
            float best = Map.TileCenterHeight(from.X, from.Y) - 0.25f;
            bool found = false;
            foreach (var o in Int2.Neighbours8)
            {
                int nx = from.X + o.X, ny = from.Y + o.Y;
                if (!Map.InBounds(nx, ny) || Map.HasFlag(nx, ny, TileFlags.Blocked)) continue;
                float h = Map.TileCenterHeight(nx, ny);
                if (h < best)
                {
                    best = h;
                    next = new Int2(nx, ny);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Traffic, fear, falls and injuries. Returns false if the guest was taken off the hill.</summary>
        private bool ApplyTileEffects(int g, Int2 tile, Run run)
        {
            var pool = Pool;
            var cfg = _sim.Config.Guests;
            var t = Map.Locate(tile.X, tile.Y);
            if (t.Chunk.Traffic[t.Index] < ushort.MaxValue) t.Chunk.Traffic[t.Index]++;

            float gap = RunRater.TileScore(Map, _sim.TerrainScale, tile.X, tile.Y) - RunRater.SkillComfort(pool.Skill[g]);
            if (gap > 0.6f)
            {
                pool.Flags[g] |= GuestFlags.Scared;
                AddHappiness(g, -0.3f);
            }

            if (gap > -1f)
            {
                float injury = cfg.InjuryBaseChancePerTile * SimMath.Exp(gap * cfg.InjuryGapExponent);
                if (Rng.Chance(injury))
                {
                    Injure(g, tile, run);
                    return false;
                }
            }

            float fall = cfg.FallChancePerTile * (1f + Math.Max(0f, gap) * 3f);
            if ((SnowQuality)t.Chunk.SnowQuality[t.Index] == SnowQuality.Ice) fall *= 2f;
            if (Rng.Chance(fall))
            {
                pool.Flags[g] |= GuestFlags.Fallen;
                pool.StateTimer[g] = (int)(2 * TicksPerMinute);
                AddHappiness(g, -2f);
            }
            return true;
        }

        private void Injure(int g, Int2 tile, Run run)
        {
            var economy = _sim.World.Economy;
            var pool = Pool;
            pool.Flags[g] |= GuestFlags.Injured;
            AddHappiness(g, -40f);
            economy.Today.Injuries++;

            string where = run != null ? $"on {run.Name}" : "off-piste";
            if (run != null) run.InjuriesToday++;
            if (run == null || run.InjuriesToday <= 3 || run.InjuriesToday % 10 == 0)
                _sim.Events.Publish(SimEventKind.Injury, EventSeverity.Danger,
                    $"A {SkillName(pool.Skill[g])} skier was injured {where}.", tile, run?.Id ?? 0);

            bool mislabelled = run != null && (run.Issues & RunIssues.Mislabelled) != 0;
            if (Rng.Chance(mislabelled ? 0.35f : 0.04f))
            {
                long amount = Rng.Range(5_000, 40_000) * (mislabelled ? 3L : 1L);
                economy.AddExpense(LedgerCategory.Lawsuits, amount);
                _sim.Events.Publish(SimEventKind.GuestComplaint, EventSeverity.Danger,
                    mislabelled ? $"Sued for ${amount:N0}: \"{run.Name}\" was NOT a {RunRater.Label(run.DisplayedRating)} run."
                                : $"An injured guest sued for ${amount:N0}.", tile);
            }

            // Ski patrol sled to the base, then home.
            var e = _sim.World.ResortEntrance;
            pool.Teleport(g, e.X + 0.5f, e.Y + 0.5f);
            Depart(g);
        }

        /// <summary>
        /// The guest is done with this run, whether they reached the bottom or peeled off at an exit.
        /// Enjoyment and tiredness scale with how much of it they skied.
        /// </summary>
        private void LeaveRun(int g, Run run)
        {
            var pool = Pool;
            float skied = SimMath.Clamp(pool.Steps[g] / Math.Max(1f, run.PathLength), 0.25f, 1f);
            bool scared = (pool.Flags[g] & GuestFlags.Scared) != 0;
            AddHappiness(g, GuestChoices.RunEnjoyment(pool.Skill[g], run, scared) * skied);
            if (scared) pool.AvoidMask[g] |= GuestChoices.AvoidBit(run.Id);
            pool.Flags[g] &= ~(GuestFlags.Scared | GuestFlags.OnRun);
            pool.RunsSkied[g]++;
            pool.LastRunId[g] = run.Id;
            pool.Energy[g] = SimMath.AddClamped(pool.Energy[g], -(int)Math.Round((3 + (int)run.ComputedRating * 2) * skied));
            run.SkiersToday++;
            _sim.World.Economy.Today.RunsCompleted++;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private float Distance(int g, Int2 tile)
        {
            float dx = tile.X + 0.5f - Pool.PosX[g], dy = tile.Y + 0.5f - Pool.PosY[g];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Moves a guest toward a point. Returns true on arrival (and snaps to it).</summary>
        private bool MoveToward(int g, float tx, float ty, float speed, float arriveRadius = 0.05f)
        {
            var pool = Pool;
            float dx = tx - pool.PosX[g], dy = ty - pool.PosY[g];
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist <= Math.Max(speed, arriveRadius))
            {
                if (dist <= speed) pool.SetPosition(g, tx, ty);
                else pool.SetPosition(g, pool.PosX[g] + dx / dist * speed, pool.PosY[g] + dy / dist * speed);
                return true;
            }
            pool.SetPosition(g, pool.PosX[g] + dx / dist * speed, pool.PosY[g] + dy / dist * speed);
            return false;
        }

        public static string SkillName(SkillLevel s) => s == SkillLevel.FirstTimer ? "first-timer" : s.ToString().ToLowerInvariant();
    }
}
