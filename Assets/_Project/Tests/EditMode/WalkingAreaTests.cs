using System.Collections.Generic;
using IceCoast.Simulation.Buildings;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class WalkingAreaTests
    {
        private static List<Int2> Square(Int2 origin, int size)
        {
            var tiles = new List<Int2>();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) tiles.Add(new Int2(origin.X + x, origin.Y + y));
            return tiles;
        }

        [Test]
        public void PaintAndErase()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var tiles = Square(new Int2(e.X - 2, e.Y + 8), 3);
            long cash = sim.World.Economy.Cash;

            Assert.IsTrue(sim.ExecuteImmediate(new PaintWalkingAreaCommand(tiles, erase: false)).Ok);
            Assert.Less(sim.World.Economy.Cash, cash);
            foreach (var t in tiles)
            {
                Assert.AreEqual(SurfaceType.WalkingArea, sim.Map.GetSurface(t));
                Assert.AreEqual(0, sim.Map.GetTreeDensity(t.X, t.Y), "trees are cleared");
                Assert.IsNotNull(sim.WalkingAreaSystem.ZoneAt(t.X, t.Y));
            }

            Assert.IsTrue(sim.ExecuteImmediate(new PaintWalkingAreaCommand(new[] { tiles[4] }, erase: true)).Ok);
            Assert.AreEqual(SurfaceType.Natural, sim.Map.GetSurface(tiles[4]));
            Assert.AreEqual(SurfaceType.WalkingArea, sim.Map.GetSurface(tiles[0]));
        }

        [Test]
        public void TrailsAndWalkingAreasNeverOverlap()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            Assert.IsFalse(sim.Preview(new PaintWalkingAreaCommand(new[] { run.Tiles[5] }, false)).Ok);

            var e = sim.World.ResortEntrance;
            var plaza = new[] { e, e + new Int2(1, 0), e + new Int2(2, 0), e + new Int2(3, 0) };
            Assert.IsFalse(sim.Preview(new CreateRunCommand(plaza)).Ok, "the base plaza is not a trail");
        }

        [Test]
        public void Buildings_MustStandOnAWalkingArea()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var origin = new Int2(e.X - 2, e.Y + 8);
            Assert.IsFalse(BuildingRules.CanPlaceFootprint(sim, origin, 2, 2).Ok);

            sim.ExecuteImmediate(new PaintWalkingAreaCommand(Square(origin, 3), false));
            Assert.IsTrue(BuildingRules.CanPlaceFootprint(sim, origin, 2, 2).Ok);
            Assert.IsFalse(BuildingRules.CanPlaceFootprint(sim, origin, 4, 2).Ok, "half off the walking area");

            // A building on it pins the walking area in place.
            sim.Map.SetOccupant(origin.X, origin.Y, Occupant.Pack(OccupantKind.Building, 1));
            Assert.IsFalse(sim.Preview(new PaintWalkingAreaCommand(new[] { origin }, true)).Ok);
        }

        [Test]
        public void LiftsOpeningOntoTheSameWalkingArea_AreConnected()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            var zone = sim.WalkingAreaSystem.ZoneNear(sim.World.ResortEntrance, 1f);
            Assert.IsNotNull(zone);
            Assert.IsTrue(zone.IsBase);

            // Pave a strip from the plaza out to the tow's bottom station: now it opens onto the base zone.
            var tiles = new List<Int2>();
            var e = sim.World.ResortEntrance;
            for (int k = 0; k <= 4; k++) tiles.Add(new Int2(e.X - k, e.Y - 1));
            var ok = sim.ExecuteImmediate(new PaintWalkingAreaCommand(tiles, false));
            Assert.IsTrue(ok.Ok, ok.Error);
            zone = sim.WalkingAreaSystem.ZoneAt(e.X, e.Y);
            Assert.IsTrue(zone.LiftBottoms.Contains(lift.Id), "the tow opens onto the plaza");
        }
    }
}
