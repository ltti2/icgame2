using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class MountainAndRoutingTests
    {
        // ---------- Terrain ----------

        [Test]
        public void Scenery_SurroundsMap_AndMeetsItSeamlessly()
        {
            var sim = TestGames.NewSmallGame();
            var scenery = sim.World.Scenery;
            var map = sim.Map;
            Assert.IsNotNull(scenery);
            Assert.AreEqual(sim.Config.Map.SceneryPadding, scenery.Padding);
            Assert.IsTrue(scenery.IsSceneryTile(-1, 10));
            Assert.IsTrue(scenery.IsSceneryTile(map.Width, map.Height));
            Assert.IsFalse(scenery.IsSceneryTile(5, 5), "map tiles are not scenery");

            // The first ring of scenery corners must be close to the map edge (no cliffs at the seam).
            for (int i = 0; i <= map.Width; i += 8)
            {
                int edge = map.GetVertexHeight(i, 0);
                int outside = scenery.GetVertexHeight(map, i, -1);
                Assert.LessOrEqual(System.Math.Abs(edge - outside), sim.Config.Terrain.MaxStepPerEdge);
            }
        }

        [Test]
        public void Scenery_IsRegeneratedIdenticallyAfterLoad()
        {
            var a = TestGames.NewSmallGame(9);
            GameSimulation b;
            using (var ms = new System.IO.MemoryStream())
            {
                IceCoast.Simulation.Persistence.SaveSystem.Save(a, ms);
                ms.Position = 0;
                b = IceCoast.Simulation.Persistence.SaveSystem.Load(ms, a.Config);
            }
            var sa = a.World.Scenery;
            var sb = b.World.Scenery;
            for (int i = 0; i < sa.VertexHeight.Length; i += 97) Assert.AreEqual(sa.VertexHeight[i], sb.VertexHeight[i]);
            for (int i = 0; i < sa.TreeDensity.Length; i += 89) Assert.AreEqual(sa.TreeDensity[i], sb.TreeDensity[i]);
        }

        [Test]
        public void UntouchedVermontMountain_IsDenselyForested()
        {
            var sim = TestGames.NewSmallGame();
            var map = sim.Map;
            int forested = 0, total = 0;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.IsOwned(x, y)) continue; // the starter area is partly cleared
                    total++;
                    if (map.GetTreeDensity(x, y) > 0) forested++;
                }
            Assert.Greater(forested, total * 8 / 10, "most of the mountain should be wooded");
        }

        [Test]
        public void Summit_IsTheHighestPointOfTheMap()
        {
            var cfg = SimConfig.CreateDefault();
            cfg.Map.Width = 256;
            cfg.Map.Height = 256;
            cfg.Map.SceneryPadding = 16;
            var sim = SimulationFactory.CreateNewGame(cfg);
            var map = sim.Map;
            int bestX = 0, bestY = 0;
            float best = float.MinValue;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    float h = map.TileCenterHeight(x, y);
                    if (h > best) { best = h; bestX = x; bestY = y; }
                }
            Assert.Less(bestX, map.Width / 3, "summit should be in the top-left");
            Assert.Less(bestY, map.Height / 3, "summit should be in the top-left");
        }

        // ---------- Routing ----------

        /// <summary>
        /// Base rope tow plus an upper T-bar whose bottom station sits beside the middle of the base run.
        /// Guests must be able to reach and lap the upper lift, not just the lift at the base.
        /// </summary>
        [Test]
        public void Guests_UseMidMountainLift()
        {
            var sim = BuildTwoLiftMountain(out var lower, out var upper, out var lowerRun, out var upperRun);
            TestGames.RunMinutes(sim, 5 * 60);

            Assert.Greater(lower.RidersToday, 30, "base lift should be busy");
            Assert.Greater(upper.RidersToday, 30, "guests should go on to ride the mid-mountain lift");
            Assert.Greater(upperRun.SkiersToday, 20, "and ski the upper run");
        }

        [Test]
        public void RunExits_IncludeLiftBottomsBesideTheRun()
        {
            var sim = BuildTwoLiftMountain(out _, out var upper, out var lowerRun, out _);
            bool found = false;
            foreach (var e in lowerRun.Exits)
                if (e.Kind == RunExitKind.Lift && e.TargetId == upper.Id) found = true;
            Assert.IsTrue(found, "the lower run passes the upper lift's bottom station");

            // Exits are in skiing order: highest first.
            for (int i = 1; i < lowerRun.Exits.Count; i++)
                Assert.GreaterOrEqual(lowerRun.Exits[i - 1].Distance, lowerRun.Exits[i].Distance);
        }

        [Test]
        public void RouteValues_ValueTheLowerLiftForWhatItLeadsTo()
        {
            var sim = BuildTwoLiftMountain(out var lower, out var upper, out _, out var upperRun);
            // Make the upper run what experts want, and the lower one plainly beginner terrain.
            sim.ExecuteImmediate(new SetRunRatingCommand(upperRun.Id, Difficulty.DoubleBlack));
            sim.World.Time.TotalMinutes = 10 * 60; // lifts open
            sim.LiftSystem.RefreshStatuses();
            var routes = sim.GuestSystem.Routes;

            float expertUpper = routes.Lift(SkillLevel.Expert, upper.Id);
            float expertLower = routes.Lift(SkillLevel.Expert, lower.Id);
            Assert.Greater(expertLower, -1f, "the base lift is worth riding for an expert because it leads to the upper lift");
            Assert.Greater(expertUpper, expertLower, "the upper lift itself is worth more");
        }

        private static GameSimulation BuildTwoLiftMountain(out Lift lower, out Lift upper, out Run lowerRun, out Run upperRun)
        {
            var cfg = TestGames.SmallConfig(11);
            cfg.SandboxMode = true; // unlock the T-bar, ignore costs
            // Keep the base area small so the upper lift can only be reached through the network
            // (skiing past it, or walking over from the lower lift's top), not straight from the parking lot.
            cfg.Guests.BaseAreaRadius = 10f;
            cfg.Guests.LiftSearchRadius = 14f;
            var sim = SimulationFactory.CreateNewGame(cfg);
            foreach (var p in sim.World.Parcels) if (!p.Owned) sim.ParcelSystem.Grant(p);

            var e = sim.World.ResortEntrance;
            // "along" = tiles up the diagonal toward the summit, "across" = sideways offset.
            Int2 At(int along, int across) => new Int2(e.X - along + across / 2, e.Y - along - across / 2 - (across & 1));

            (lower, lowerRun) = TestGames.BuildStarterResort(sim);

            var build = sim.ExecuteImmediate(new BuildLiftCommand(LiftType.TBar, At(16, -4), At(56, -4)));
            Assert.IsTrue(build.Ok, "upper lift: " + build.Error);
            upper = sim.World.Lifts[1];

            var tiles = new List<Int2>();
            for (int y = 0; y < sim.Map.Height; y++)
                for (int x = 0; x < sim.Map.Width; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int across = (x - y) - (e.X - e.Y);
                    if (along >= 17f && along <= 57f && across >= -12 && across <= -6) tiles.Add(new Int2(x, y));
                }
            sim.ExecuteImmediate(new ClearTreesCommand(tiles));
            var paint = sim.ExecuteImmediate(new CreateRunCommand(tiles));
            Assert.IsTrue(paint.Ok, "upper run: " + paint.Error);
            upperRun = sim.World.Runs[1];
            Assert.AreEqual(upper.Id, upperRun.StartLiftId, "upper run starts at the upper lift's top");
            return sim;
        }

        // ---------- Trails connected to trails ----------

        /// <summary>A glade alongside the main trail that ends partway down, touching it.</summary>
        [Test]
        public void GladeBesideTrail_IsSkied()
        {
            var sim = BuildConnectedTrails(out var main, out var glade, out _, out _, gap: 1);
            TestGames.RunMinutes(sim, 5 * 60);
            Assert.Greater(glade.SkiersToday, 15, $"glade skiers: {glade.SkiersToday}, main: {main.SkiersToday}");
        }

        /// <summary>An upper trail off the lift top that flows into a second trail, which reaches the lift.</summary>
        [Test]
        public void TrailChain_ToLift_IsSkied()
        {
            var sim = BuildConnectedTrails(out _, out _, out var upper, out var lower);
            Assert.AreEqual(RunIssues.None, upper.Issues & (RunIssues.WalkOut | RunIssues.NoLiftAtTop), "upper: " + upper.Issues);
            TestGames.RunMinutes(sim, 5 * 60);
            Assert.Greater(upper.SkiersToday, 15, $"upper skiers: {upper.SkiersToday}");
            Assert.Greater(lower.SkiersToday, 15, $"lower skiers: {lower.SkiersToday}");
        }

        /// <summary>
        /// Trails painted with a one-tile gap between them still connect, and a glade that runs alongside a
        /// trail and peels back into it partway down has an exit at the bottom, not just at the top.
        /// </summary>
        [Test]
        public void Connections_TolerateGaps_AndRecordWhereTrailsMerge()
        {
            var sim = BuildConnectedTrails(out var main, out var glade, out var upper, out var lower, gap: 1);
            bool upperToLower = false;
            foreach (var e in upper.Exits) if (e.Kind == RunExitKind.Run && e.TargetId == lower.Id) upperToLower = true;
            Assert.IsTrue(upperToLower, "upper trail should connect to the lower one across a one-tile gap");

            int gladeExitsToMain = 0;
            float lowest = float.MaxValue;
            foreach (var e in glade.Exits)
                if (e.Kind == RunExitKind.Run && e.TargetId == main.Id) { gladeExitsToMain++; lowest = System.Math.Min(lowest, e.Distance); }
            Assert.GreaterOrEqual(gladeExitsToMain, 2, "glade should have an entry-side and a merge-side connection");
            Assert.Less(lowest, 4f, "the merge-side exit should be near the glade's bottom");

            Assert.AreEqual(RunIssues.None, upper.Issues & (RunIssues.WalkOut | RunIssues.NoLiftAtTop), "upper: " + upper.Issues);
            Assert.AreEqual(RunIssues.None, glade.Issues & (RunIssues.WalkOut | RunIssues.NoLiftAtTop), "glade: " + glade.Issues);
        }

        [Test]
        public void TrailChain_AcrossGap_IsSkied()
        {
            var sim = BuildConnectedTrails(out _, out _, out var upper, out var lower, gap: 1);
            TestGames.RunMinutes(sim, 5 * 60);
            Assert.Greater(upper.SkiersToday, 15, $"upper skiers: {upper.SkiersToday}");
            Assert.Greater(lower.SkiersToday, 15, $"lower skiers: {lower.SkiersToday}");
        }

        /// <summary>A trail that leads nowhere is still skied (the designer's mistake) and flagged.</summary>
        [Test]
        public void DeadEndTrail_IsFlagged_AndGuestsWalkOut()
        {
            var sim = BuildConnectedTrails(out _, out _, out var upper, out var lower, gap: 1);
            sim.ExecuteImmediate(new DeleteRunCommand(lower.Id));
            Assert.IsTrue((upper.Issues & RunIssues.WalkOut) != 0, "a trail with no way down should be flagged");
        }

        /// <summary>
        /// Rope tow up the corridor; main trail beside it (across 2..8); a glade on the far side of the main
        /// trail (across 9..12) that stops short of the bottom; and a two-part chain on the other side of the
        /// tow (across -10..-4): an upper trail from the lift top that ends where a lower trail begins.
        /// </summary>
        private static GameSimulation BuildConnectedTrails(out Run main, out Run glade, out Run upper, out Run lower, int gap = 0)
        {
            var cfg = TestGames.SmallConfig(5);
            cfg.SandboxMode = true;
            cfg.Guests.SkillWeights = new[] { 0f, 10f, 30f, 30f, 20f, 10f }; // enough strong skiers for a glade
            cfg.Guests.BaseAreaRadius = 8f; // on this small map the default base area would swallow the lower trails
            var sim = SimulationFactory.CreateNewGame(cfg);
            var e = sim.World.ResortEntrance;

            List<Int2> Band(float alongMin, float alongMax, int acrossMin, int acrossMax)
            {
                var tiles = new List<Int2>();
                for (int y = 0; y < sim.Map.Height; y++)
                    for (int x = 0; x < sim.Map.Width; x++)
                    {
                        float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                        int across = (x - y) - (e.X - e.Y);
                        if (along >= alongMin && along <= alongMax && across >= acrossMin && across <= acrossMax) tiles.Add(new Int2(x, y));
                    }
                return tiles;
            }

            (_, main) = TestGames.BuildStarterResort(sim);

            // The glade's far end bends back toward the main trail, so it rejoins partway down.
            var gladeTiles = Band(6f, 22f, 9 + gap, 12 + gap);
            Assert.IsTrue(sim.ExecuteImmediate(new CreateRunCommand(gladeTiles, "Glade")).Ok, "glade");
            glade = sim.World.Runs[sim.World.Runs.Count - 1];

            var upperTiles = Band(13f, 26f, -10, -4);
            sim.ExecuteImmediate(new ClearTreesCommand(upperTiles));
            Assert.IsTrue(sim.ExecuteImmediate(new CreateRunCommand(upperTiles, "Upper")).Ok, "upper");
            upper = sim.World.Runs[sim.World.Runs.Count - 1];

            var lowerTiles = Band(2f, 12.5f - gap, -10, -4);
            sim.ExecuteImmediate(new ClearTreesCommand(lowerTiles));
            Assert.IsTrue(sim.ExecuteImmediate(new CreateRunCommand(lowerTiles, "Lower")).Ok, "lower");
            lower = sim.World.Runs[sim.World.Runs.Count - 1];
            return sim;
        }

        // ---------- Units ----------

        [Test]
        public void Units_DefaultToImperial()
        {
            var sim = TestGames.NewSmallGame();
            Assert.AreEqual(UnitSystem.Imperial, sim.Units.System);
            Assert.AreEqual("4,242 ft", sim.Units.Elevation(1293f));
            Assert.AreEqual("32°F", sim.Units.Temperature(0f));
            Assert.AreEqual("14°F", sim.Units.Temperature(-10f));
            Assert.AreEqual("12 in", sim.Units.SnowDepth(30f));
            Assert.AreEqual("31 mph", sim.Units.Speed(50f));

            sim.Units.System = UnitSystem.Metric;
            Assert.AreEqual("1,293 m", sim.Units.Elevation(1293f));
            Assert.AreEqual("-10°C", sim.Units.Temperature(-10f));
        }
    }
}
