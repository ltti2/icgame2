using System.Collections.Generic;
using System.Linq;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class RunEditTests
    {
        [Test]
        public void EraseTiles_KeepsTheRunAndItsName()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            string name = run.Name;
            int before = run.Tiles.Count;

            // Shave one edge column (across = 8) off the run.
            var e = sim.World.ResortEntrance;
            var edge = run.Tiles.Where(t => (t.X - t.Y) - (e.X - e.Y) == 8).ToList();
            Assert.Greater(edge.Count, 3);
            var result = sim.ExecuteImmediate(new EditRunTilesCommand(run.Id, null, edge));
            Assert.IsTrue(result.Ok, result.Error);

            Assert.IsTrue(ReferenceEquals(run, sim.World.GetRun(run.Id)), "same run object");
            Assert.AreEqual(name, run.Name);
            Assert.AreEqual(before - edge.Count, run.Tiles.Count);
            foreach (var t in edge) Assert.AreEqual(0, sim.Map.GetRunId(t.X, t.Y));
        }

        [Test]
        public void AddTiles_ExtendsTheRun()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            var e = sim.World.ResortEntrance;
            var extra = run.Tiles.Where(t => (t.X - t.Y) - (e.X - e.Y) == 8).Select(t => new Int2(t.X + 1, t.Y)).ToList();
            int before = run.Tiles.Count;
            var result = sim.ExecuteImmediate(new EditRunTilesCommand(run.Id, extra, null));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(before + extra.Count, run.Tiles.Count);
            foreach (var t in extra) Assert.AreEqual(run.Id, sim.Map.GetRunId(t.X, t.Y));
        }

        [Test]
        public void Erase_ThatSplitsOrEmptiesTheRun_IsRefused()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            var e = sim.World.ResortEntrance;

            // A full cross-cut halfway down splits it in two.
            var cut = run.Tiles.Where(t => ((e.X - t.X) + (e.Y - t.Y)) / 2 == 14).ToList();
            Assert.IsFalse(sim.Preview(new EditRunTilesCommand(run.Id, null, cut)).Ok);

            var all = new List<Int2>(run.Tiles);
            all.RemoveAt(0);
            Assert.IsFalse(sim.Preview(new EditRunTilesCommand(run.Id, null, all)).Ok, "too small: delete instead");
        }

        [Test]
        public void Edit_CannotGrabAnotherRunsTiles()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            var other = TestGames.PaintSecondStarterRun(sim);
            Assert.IsFalse(sim.Preview(new EditRunTilesCommand(run.Id, new[] { other.Tiles[0] }, null)).Ok);
        }
    }
}
