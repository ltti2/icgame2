using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class ParkingTests
    {
        [Test]
        public void NewGame_HasARoadAndA150SpaceLot()
        {
            var sim = TestGames.NewSmallGame();
            var world = sim.World;
            Assert.AreEqual(1, world.ParkingLots.Count);
            var lot = world.ParkingLots[0];
            Assert.AreEqual(150, lot.Capacity);
            Assert.GreaterOrEqual(world.RoadPath.Count, 2);
            Assert.Greater(world.RoadPath[0].X, sim.Map.Width - 1, "the road comes in from beyond the map edge");

            for (int y = lot.Y; y < lot.Y + lot.Height; y++)
                for (int x = lot.X; x < lot.X + lot.Width; x++)
                    Assert.AreEqual(SurfaceType.Parking, sim.Map.GetSurface(x, y));
            Assert.AreEqual(SurfaceType.Road, sim.Map.GetSurface(sim.Map.Width - 1, lot.AisleY));
            Assert.AreEqual(SurfaceType.WalkingArea, sim.Map.GetSurface(world.ResortEntrance.X, world.ResortEntrance.Y),
                "the base plaza is a walking area");
            foreach (var s in lot.Spaces) Assert.IsTrue(lot.Contains((int)s.X, (int)s.Y));
        }

        [Test]
        public void Lot_IsFlatAndPlowed()
        {
            var sim = TestGames.NewSmallGame();
            var lot = sim.World.ParkingLots[0];
            for (int y = lot.Y; y < lot.Y + lot.Height; y++)
                for (int x = lot.X; x < lot.X + lot.Width; x++)
                {
                    Assert.Less(TerrainQueries.SlopePercent(sim.Map, sim.TerrainScale, x, y), 6f);
                    Assert.AreEqual(0, sim.Map.GetSnowDepth(x, y));
                    Assert.AreEqual(0, sim.Map.GetTreeDensity(x, y));
                }
        }

        [Test]
        public void Guests_ArriveByCar_AndTheLotEmptiesOvernight()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 3 * 60); // 10:30
            var lot = sim.World.ParkingLots[0];
            Assert.Greater(lot.CountIn(CarState.Parked), 5);

            var pool = sim.World.Guests;
            int withCar = 0;
            for (int i = 0; i < pool.HighWater; i++)
                if (pool.Alive[i] && pool.Car[i] != ParkingSystem.NoCar) withCar++;
            Assert.AreEqual(pool.Count, withCar, "everyone came by car");

            TestGames.RunMinutes(sim, 14 * 60); // past midnight
            Assert.AreEqual(0, sim.World.Guests.Count);
            Assert.AreEqual(0, lot.Occupied, "every car has gone home");
        }

        [Test]
        public void FullLot_TurnsCarsAway()
        {
            var cfg = TestGames.SmallConfig();
            cfg.Parking.DefaultLotCapacity = 4;
            var sim = IceCoast.Simulation.SimulationFactory.CreateNewGame(cfg);
            TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 3 * 60);

            var today = sim.World.Economy.Today;
            Assert.AreEqual(4, sim.World.ParkingLots[0].Capacity);
            Assert.Greater(today.GuestsTurnedAway, 20);
            Assert.LessOrEqual(sim.World.Guests.Count, 4 * 4, "at most four people per car get in");
        }

        [Test]
        public void NothingCanBeBuiltOnTheRoadOrLot()
        {
            var sim = TestGames.NewSmallGame();
            var lot = sim.World.ParkingLots[0];
            var inLot = new Int2(lot.X + 1, lot.Y + 1);
            Assert.IsFalse(sim.Preview(new CreateRunCommand(new[] { inLot, inLot + new Int2(1, 0), inLot + new Int2(2, 0), inLot + new Int2(3, 0) })).Ok);
            Assert.IsFalse(sim.Preview(new PlaceLightCommand(LightSize.Small, inLot)).Ok);
            Assert.IsFalse(sim.Preview(new BuildLiftCommand(LiftType.RopeTow, inLot, new Int2(lot.X - 20, lot.Y - 20))).Ok);
            Assert.IsFalse(sim.Preview(new TerraformCommand(new Int2(lot.X + 2, lot.Y + 2), 0, IceCoast.Simulation.Terrain.TerraformMode.Raise)).Ok);
        }
    }
}
