using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;
using NUnit.Framework;

namespace IceCoast.Tests
{
    /// <summary>
    /// Lifts load discrete carriers (chairs, bars, cabins) a few guests at a time, and skiers always make
    /// their way down the hill, even across flat run-outs.
    /// </summary>
    [TestFixture]
    public class LiftCarrierAndDownhillTests
    {
        /// <summary>The starter tow, turned into a fixed-grip triple so loading can be tested on the small map.</summary>
        private static (GameSimulation sim, Lift lift) ChairGame()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            var def = sim.Config.GetLift(LiftType.FixedGripChair);
            lift.Type = LiftType.FixedGripChair;
            lift.CapacityPerHour = def.CapacityPerHour;
            sim.World.Time.TotalMinutes = 10 * 60; // mid-morning, lifts open
            sim.LiftSystem.RefreshStatuses();
            for (int i = 0; i < 1500; i++)
            {
                int g = sim.World.Guests.Spawn();
                sim.World.Guests.State[g] = GuestState.Queueing;
                lift.QueueGuests.Enqueue(g);
            }
            return (sim, lift);
        }

        private static void TickLiftOnly(GameSimulation sim, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.World.Tick++;
                sim.LiftSystem.Tick();
            }
        }

        [Test]
        public void ChairLift_LoadsWholeChairsAtATime_NeverOverSeats()
        {
            var (sim, lift) = ChairGame();
            int seats = sim.LiftSystem.Seats(lift);
            Assert.AreEqual(3, seats);

            TickLiftOnly(sim, lift.RideTicks / 2);
            Assert.Greater(lift.Riders.Count, 0);

            var perChair = new Dictionary<double, HashSet<int>>();
            foreach (var r in lift.Riders)
            {
                if (!perChair.TryGetValue(r.BoardClock, out var used)) perChair[r.BoardClock] = used = new HashSet<int>();
                Assert.Less(r.Seat, seats, "seat index on the chair");
                Assert.IsTrue(used.Add(r.Seat), "two riders in one seat");
            }
            int full = 0;
            foreach (var kv in perChair)
            {
                Assert.LessOrEqual(kv.Value.Count, seats);
                if (kv.Value.Count == seats) full++;
            }
            Assert.Greater(full, 0, "with a long line most chairs leave full");
            Assert.Less(full, perChair.Count, "...but not every seat is always filled");

            // Everyone on one chair is at the same point on the line.
            var pool = sim.World.Guests;
            foreach (var r in lift.Riders)
                foreach (var o in lift.Riders)
                    if (o.BoardClock == r.BoardClock)
                    {
                        Assert.AreEqual(pool.PosX[r.GuestIndex], pool.PosX[o.GuestIndex], 1e-4f);
                        Assert.AreEqual(pool.PosY[r.GuestIndex], pool.PosY[o.GuestIndex], 1e-4f);
                    }
        }

        [Test]
        public void ChairLift_CarriesLessThanNameplateFromALongLine()
        {
            var (sim, lift) = ChairGame();
            int before = lift.QueueCount;
            TickLiftOnly(sim, sim.TimeSystem.TicksPerHour);
            int boarded = before - lift.QueueCount;

            float expected = sim.LiftSystem.EffectiveCapacity(lift);
            Assert.Less(expected, lift.CapacityPerHour);
            Assert.AreEqual(expected, boarded, lift.CapacityPerHour * 0.05f, "boarded per hour ~ effective capacity");
            Assert.AreEqual(lift.QueueCount * 60f / expected, lift.EstimatedWaitMinutes, 0.01f, "wait estimate uses real throughput");
        }

        [Test]
        public void WindHold_StopsTheLine_RidersHangInPlace()
        {
            var (sim, lift) = ChairGame();
            TickLiftOnly(sim, lift.RideTicks / 3);
            var pool = sim.World.Guests;
            var rider = lift.Riders.Peek();
            float x = pool.PosX[rider.GuestIndex], y = pool.PosY[rider.GuestIndex];
            double clock = lift.LineClock;

            sim.World.Weather.WindSpeed = 500f;
            lift.Status = LiftStatus.WindHold;
            int queue = lift.QueueCount;
            TickLiftOnly(sim, 200);

            Assert.AreEqual(clock, lift.LineClock, "the rope doesn't move on hold");
            Assert.AreEqual(x, pool.PosX[rider.GuestIndex], 1e-5f);
            Assert.AreEqual(y, pool.PosY[rider.GuestIndex], 1e-5f);
            Assert.AreEqual(queue, lift.QueueCount, "nobody boards on hold");
        }

        /// <summary>
        /// A wide run whose lower half has been flattened into a long run-out, where the old step rule let
        /// skiers zig-zag back up the trail.
        /// </summary>
        private static (GameSimulation sim, Run run) FlatRunOutGame()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var lift = sim.ExecuteImmediate(new BuildLiftCommand(LiftType.RopeTow, new Int2(e.X - 3, e.Y - 3), new Int2(e.X - 25, e.Y - 25)));
            Assert.IsTrue(lift.Ok, lift.Error);

            var map = sim.Map;
            int flatHeight = map.GetVertexHeight(e.X - 12, e.Y - 12);
            var tiles = new List<Int2>();
            for (int y = e.Y - 40; y <= e.Y + 10; y++)
                for (int x = e.X - 40; x <= e.X + 10; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int column = (x - y) - (e.X - e.Y);
                    if (along >= 2f && along <= 26f && column >= 2 && column <= 18 && map.IsOwned(new Int2(x, y)))
                        tiles.Add(new Int2(x, y));
                }
            // Flatten the bottom half of the run (and its corners) to one height.
            foreach (var t in tiles)
            {
                float along = ((e.X - t.X) + (e.Y - t.Y)) * 0.5f;
                if (along > 12f) continue;
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                        map.SetVertexHeight(t.X + dx, t.Y + dy, flatHeight);
            }
            var paint = sim.ExecuteImmediate(new CreateRunCommand(tiles));
            Assert.IsTrue(paint.Ok, paint.Error);
            return (sim, sim.World.Runs[0]);
        }

        [Test]
        public void Skiers_AlwaysHeadDownTheRun_EvenOnTheFlats()
        {
            var (sim, run) = FlatRunOutGame();
            sim.World.Time.TotalMinutes = 9 * 60;
            var pool = sim.World.Guests;
            var map = sim.Map;

            var lastKey = new Dictionary<int, int>();
            int steps = 0, climbs = 0, backwards = 0;
            int ticks = 150 * sim.Config.Time.TicksPerGameMinute;
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick();
                for (int i = 0; i < pool.HighWater; i++)
                {
                    bool carving = pool.Alive[i] && pool.State[i] == GuestState.Skiing && pool.TargetId[i] == run.Id
                                   && (pool.Flags[i] & GuestFlags.OnRun) != 0;
                    if (!carving) { lastKey.Remove(i); continue; }
                    int key = pool.WaypointKey[i];
                    if (!lastKey.TryGetValue(i, out int prev)) { lastKey[i] = key; continue; }
                    if (prev == key) continue;
                    lastKey[i] = key;
                    if (!run.ContainsTile(prev) || !run.ContainsTile(key)) continue;

                    steps++;
                    if (run.DistanceAt(key) > run.DistanceAt(prev) + 1e-3f) backwards++;
                    var a = map.FromTileKey(prev);
                    var b = map.FromTileKey(key);
                    if (map.TileCenterHeight(b.X, b.Y) > map.TileCenterHeight(a.X, a.Y) + 0.31f) climbs++;
                }
            }

            Assert.Greater(steps, 300, "skiers should have skied the run");
            Assert.AreEqual(0, backwards, "a skier stepped back up the run");
            Assert.AreEqual(0, climbs, "a skier climbed while carving");
            Assert.Greater(run.SkiersToday, 10, "and they still reach the bottom");
        }

        [Test]
        public void Junctions_NeverJoinAnotherRunUphill()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            var e = sim.World.ResortEntrance;
            // A second trail right beside the first, so they touch along their whole length.
            var tiles = new List<Int2>();
            for (int y = e.Y - 32; y <= e.Y + 6; y++)
                for (int x = e.X - 32; x <= e.X + 6; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int across = (x - y) - (e.X - e.Y);
                    if (along >= 2f && along <= 26f && across >= 9 && across <= 13 && sim.Map.IsOwned(new Int2(x, y))) tiles.Add(new Int2(x, y));
                }
            Assert.IsTrue(sim.ExecuteImmediate(new CreateRunCommand(tiles)).Ok);

            var map = sim.Map;
            int junctions = 0;
            foreach (var run in sim.World.Runs)
                foreach (var exit in run.Exits)
                {
                    if (exit.Kind != RunExitKind.Run) continue;
                    junctions++;
                    Assert.LessOrEqual(map.TileCenterHeight(exit.JoinTile.X, exit.JoinTile.Y),
                                       map.TileCenterHeight(exit.Tile.X, exit.Tile.Y) + 0.5f, "junction climbs onto the other run");
                }
            Assert.Greater(junctions, 0);
        }

        [Test]
        public void OnSnow_GuestsSkiDownToTheLift_OnlyWalkOnBareGround()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            sim.World.Time.TotalMinutes = 10 * 60;
            sim.LiftSystem.RefreshStatuses();
            var pool = sim.World.Guests;
            var e = sim.World.ResortEntrance;
            var start = new Int2(e.X - 12, e.Y - 12); // up the tow line from the bottom station
            Assert.Greater(sim.Map.TileCenterHeight(start.X, start.Y),
                           sim.Map.TileCenterHeight(lift.BottomStation.X, lift.BottomStation.Y) + 2f);

            float Moved(bool snow)
            {
                var t = sim.Map.Locate(start.X, start.Y);
                t.Chunk.SnowDepth[t.Index] = (ushort)(snow ? 60 : 0);
                int g = pool.Spawn();
                pool.Skill[g] = SkillLevel.Intermediate;
                pool.Teleport(g, start.X + 0.5f, start.Y + 0.5f);
                pool.State[g] = GuestState.WalkingToLift;
                pool.TargetId[g] = lift.Id;
                float x = pool.PosX[g], y = pool.PosY[g];
                sim.GuestSystem.Tick();
                float d = (float)System.Math.Sqrt((pool.PosX[g] - x) * (pool.PosX[g] - x) + (pool.PosY[g] - y) * (pool.PosY[g] - y));
                pool.Despawn(g);
                return d;
            }

            float walkPerTick = sim.Config.Guests.WalkSpeedTilesPerMinute / sim.Config.Time.TicksPerGameMinute;
            Assert.AreEqual(walkPerTick, Moved(false), 1e-4f, "bare ground: skis off, walking pace");
            Assert.Greater(Moved(true), walkPerTick * 1.5f, "on snow: gliding down to the lift");
        }

        [Test]
        public void Carriers_HaveClearSpaceBetweenThem()
        {
            var cfg = SimConfig.CreateDefault();
            foreach (var def in cfg.Lifts)
            {
                if (def.Type == LiftType.MagicCarpet) continue; // a belt: riders stand close together
                float spacing = def.SpeedTilesPerMinute * def.SeatsPerCarrier * 60f / def.CapacityPerHour;
                Assert.GreaterOrEqual(spacing, 0.85f, $"{def.DisplayName} carriers are only {spacing:0.00} tiles apart");
            }
        }
    }
}
