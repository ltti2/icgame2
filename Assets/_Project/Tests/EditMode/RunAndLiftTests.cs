using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class RunAndLiftTests
    {
        [Test]
        public void RatingTable_MatchesDesignDoc()
        {
            var cfg = new SimConfig.RunSettings();
            Assert.AreEqual(Difficulty.Green, RunRater.Rate(cfg, 10f, 15f));
            Assert.AreEqual(Difficulty.Blue, RunRater.Rate(cfg, 10f, 30f));       // steepest section bumps it
            Assert.AreEqual(Difficulty.Black, RunRater.Rate(cfg, 35f, 50f));
            Assert.AreEqual(Difficulty.DoubleBlack, RunRater.Rate(cfg, 45f, 65f));
            Assert.AreEqual(Difficulty.TripleBlack, RunRater.Rate(cfg, 55f, 80f));
        }

        [Test]
        public void StarterResort_BuildsAndConnects()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, run) = TestGames.BuildStarterResort(sim);

            Assert.AreEqual(lift.Id, run.StartLiftId, "lift top should feed the run");
            Assert.AreEqual(lift.Id, run.EndLiftId, "run should end at the lift bottom");
            Assert.IsTrue(lift.RunsFromTop.Contains(run.Id));
            Assert.AreEqual(RunIssues.None, run.Issues & (RunIssues.WalkOut | RunIssues.NoLiftAtTop));
            Assert.LessOrEqual((int)run.ComputedRating, (int)Difficulty.Blue, "starter terrain should be beginner-friendly");
        }

        [Test]
        public void FlowField_LeadsFromStartToEnd()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);

            var p = run.Start;
            int steps = 0;
            while (p != run.End && steps < run.Tiles.Count)
            {
                Assert.IsTrue(sim.RunSystem.TryNextTile(run, p, out p), $"flow field dead-ends at {p}");
                steps++;
            }
            Assert.AreEqual(run.End, p);
        }

        [Test]
        public void Run_RejectsUnownedAndDisconnectedTiles()
        {
            var sim = TestGames.NewSmallGame();
            var unowned = new List<Int2> { new Int2(1, 1), new Int2(2, 2), new Int2(3, 3), new Int2(4, 4) };
            Assert.IsFalse(sim.Preview(new CreateRunCommand(unowned)).Ok);

            var e = sim.World.ResortEntrance;
            var split = new List<Int2> { new Int2(e.X - 10, e.Y - 10), new Int2(e.X - 11, e.Y - 11), new Int2(e.X - 20, e.Y - 10), new Int2(e.X - 21, e.Y - 10) };
            Assert.IsFalse(sim.Preview(new CreateRunCommand(split)).Ok);
        }

        [Test]
        public void Lift_MustGoUphill()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var downhill = sim.Preview(new BuildLiftCommand(LiftType.RopeTow, new Int2(e.X - 25, e.Y - 25), new Int2(e.X - 3, e.Y - 3)));
            Assert.IsFalse(downhill.Ok);
        }

        [Test]
        public void Lift_RejectsTooShortAndUnownedLines()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            Assert.IsFalse(sim.Preview(new BuildLiftCommand(LiftType.RopeTow, e, new Int2(e.X - 2, e.Y - 2))).Ok);
            Assert.IsFalse(sim.Preview(new BuildLiftCommand(LiftType.RopeTow, new Int2(70, 70), new Int2(40, 40))).Ok);
        }

        [Test]
        public void Lift_LockedByReputation()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var result = sim.Preview(new BuildLiftCommand(LiftType.DetachableChair, new Int2(e.X - 3, e.Y - 3), new Int2(e.X - 30, e.Y - 30)));
            Assert.IsFalse(result.Ok);
            StringAssertContains(result.Error, "reputation");
        }

        [Test]
        public void Lift_LoadsAtConfiguredCapacity()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            sim.World.Time.TotalMinutes = 10 * 60; // mid-morning, lifts open
            sim.LiftSystem.RefreshStatuses();

            // Fake a long queue of placeholder guests.
            for (int i = 0; i < 500; i++)
            {
                int g = sim.World.Guests.Spawn();
                sim.World.Guests.State[g] = IceCoast.Simulation.Guests.GuestState.Queueing;
                lift.QueueGuests.Enqueue(g);
            }

            // One game hour = capacity per hour boarded (within rounding).
            int before = lift.QueueCount;
            for (int t = 0; t < sim.TimeSystem.TicksPerHour; t++)
            {
                // Isolate the lift: just load, don't run guests.
                sim.World.Tick++;
                sim.LiftSystem.Tick();
            }
            int boarded = before - lift.QueueCount;
            Assert.AreEqual(lift.CapacityPerHour, boarded, 2);
        }

        [Test]
        public void MislabelledRun_IsFlagged()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            // Pretend the computed rating was harder than green, then sign it green.
            run.ComputedRating = Difficulty.Black;
            sim.ExecuteImmediate(new SetRunRatingCommand(run.Id, Difficulty.Green));
            Assert.IsTrue((run.Issues & RunIssues.Mislabelled) != 0);
            Assert.AreEqual(Difficulty.Green, run.DisplayedRating);
        }

        private static void StringAssertContains(string text, string fragment)
            => Assert.IsTrue(text != null && text.ToLowerInvariant().Contains(fragment), $"\"{text}\" should mention \"{fragment}\"");
    }
}
