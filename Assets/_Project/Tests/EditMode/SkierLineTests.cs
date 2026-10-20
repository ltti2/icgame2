using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    /// <summary>Skiers should use the whole width of a trail and pick their line by the snow.</summary>
    [TestFixture]
    public class SkierLineTests
    {
        private const int FirstColumn = 2, LastColumn = 22;

        /// <summary>
        /// The starter rope tow with a run about 20 columns wide beside it. "Column" = (x - y) offset from the
        /// tow's diagonal, so it measures position across the run.
        /// </summary>
        private static (GameSimulation sim, Run run, Int2 entrance) WideRunGame()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var lift = sim.ExecuteImmediate(new BuildLiftCommand(LiftType.RopeTow, new Int2(e.X - 3, e.Y - 3), new Int2(e.X - 25, e.Y - 25)));
            Assert.IsTrue(lift.Ok, lift.Error);

            var tiles = new List<Int2>();
            for (int y = e.Y - 40; y <= e.Y + 10; y++)
                for (int x = e.X - 40; x <= e.X + 10; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int column = Column(e, x, y);
                    if (along >= 2f && along <= 26f && column >= FirstColumn && column <= LastColumn && sim.Map.IsOwned(new Int2(x, y)))
                        tiles.Add(new Int2(x, y));
                }
            var paint = sim.ExecuteImmediate(new CreateRunCommand(tiles));
            Assert.IsTrue(paint.Ok, paint.Error);
            return (sim, sim.World.Runs[0], e);
        }

        private static int Column(Int2 e, int x, int y) => (x - y) - (e.X - e.Y);

        /// <summary>Runs the game, sampling where skiers on the middle of the run are, by column.</summary>
        private static int[] SampleColumns(GameSimulation sim, Run run, Int2 e, int minutes, System.Action everyFewTicks = null)
        {
            var hist = new int[LastColumn + 2];
            var pool = sim.World.Guests;
            int ticks = minutes * sim.Config.Time.TicksPerGameMinute;
            for (int t = 0; t < ticks; t++)
            {
                if (t % 50 == 0) everyFewTicks?.Invoke();
                sim.Tick();
                if (t % 5 != 0) continue;
                for (int i = 0; i < pool.HighWater; i++)
                {
                    if (!pool.Alive[i] || pool.State[i] != GuestState.Skiing || pool.TargetId[i] != run.Id) continue;
                    int x = (int)pool.PosX[i], y = (int)pool.PosY[i];
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int column = Column(e, x, y);
                    if (along < 8f || along > 22f || column < 0 || column >= hist.Length) continue;
                    hist[column]++;
                }
            }
            return hist;
        }

        private static float Share(int[] hist, int from, int to)
        {
            int total = 0, part = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                total += hist[i];
                if (i >= from && i <= to) part += hist[i];
            }
            return total == 0 ? 0f : part / (float)total;
        }

        [Test]
        public void Run_HasAcrossCoordinatesSpanningItsWidth()
        {
            var (_, run, _) = WideRunGame();
            float min = 1f, max = 0f;
            foreach (var a in run.Across) { min = System.Math.Min(min, a); max = System.Math.Max(max, a); }
            Assert.Less(min, 0.05f);
            Assert.Greater(max, 0.95f);
            Assert.AreEqual(run.Tiles.Count, run.SlotsByDistance.Length);
        }

        [Test]
        public void Skiers_SpreadAcrossTheWholeWidthOfAWideRun()
        {
            var (sim, run, e) = WideRunGame();
            var hist = SampleColumns(sim, run, e, 4 * 60);

            // Before skiers held their own lines, nearly everyone funnelled down columns 3-8 and the far
            // third of the trail stayed empty.
            float nearSide = Share(hist, FirstColumn, 8);
            float farSide = Share(hist, 16, LastColumn);
            Assert.Greater(farSide, 0.15f, $"far side share {farSide:0.00}");
            Assert.Less(nearSide, 0.55f, $"near side share {nearSide:0.00}");
        }

        [Test]
        public void Skiers_AvoidTheIcyHalfOfARun()
        {
            var (sim, run, e) = WideRunGame();
            var hist = SampleColumns(sim, run, e, 3 * 60, () =>
            {
                foreach (var t in run.Tiles)
                {
                    var r = sim.Map.Locate(t.X, t.Y);
                    r.Chunk.SnowQuality[r.Index] = (byte)(Column(e, t.X, t.Y) >= 12 ? SnowQuality.Ice : SnowQuality.Groomed);
                }
            });

            float icy = Share(hist, 13, LastColumn);
            Assert.Less(icy, 0.15f, $"share on ice {icy:0.00}");
        }

        [Test]
        public void ExpertsLikePowder_BeginnersLikeCorduroy()
        {
            Assert.Greater(GuestChoices.SnowAppeal(SkillLevel.Expert, SnowQuality.Powder, 0),
                           GuestChoices.SnowAppeal(SkillLevel.Expert, SnowQuality.Groomed, 0));
            Assert.Greater(GuestChoices.SnowAppeal(SkillLevel.Beginner, SnowQuality.Groomed, 0),
                           GuestChoices.SnowAppeal(SkillLevel.Beginner, SnowQuality.Powder, 0));
            Assert.Greater(GuestChoices.SnowAppeal(SkillLevel.Expert, SnowQuality.Powder, 0),
                           GuestChoices.SnowAppeal(SkillLevel.Expert, SnowQuality.Powder, 6), "tracked-out powder is worse");
        }
    }
}
