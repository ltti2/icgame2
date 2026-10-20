using System.IO;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Persistence;
using IceCoast.Simulation.Snow;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class SimulationTests
    {
        [Test]
        public void FullDay_GuestsArriveSkiAndPay()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            long cashAfterBuild = sim.World.Economy.Cash;

            // From 7:30 to 13:00: arrivals, queues, laps.
            TestGames.RunMinutes(sim, (int)(5.5 * 60));

            var today = sim.World.Economy.Today;
            Assert.Greater(today.GuestsArrived, 20, "guests should come once there is a lift");
            Assert.Greater(sim.World.Lifts[0].RidersToday, 20, "guests should ride the lift");
            Assert.Greater(sim.World.Runs[0].SkiersToday, 10, "guests should finish runs");
            Assert.Greater(sim.World.Economy.Cash, cashAfterBuild, "ticket sales should outpace costs");
        }

        [Test]
        public void EndOfDay_EveryoneGoesHome_AndLedgerRollsOver()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 17 * 60); // through midnight

            Assert.AreEqual(1, sim.World.Time.Day);
            Assert.AreEqual(1, sim.World.Economy.History.Count);
            Assert.AreEqual(0, sim.World.Guests.Count, "nobody should still be on the hill overnight");
            var day0 = sim.World.Economy.History[0];
            Assert.AreEqual(day0.GuestsArrived, day0.GuestsDeparted);
        }

        [Test]
        public void SameSeedSameCommands_SameState()
        {
            ulong RunOnce()
            {
                var sim = TestGames.NewSmallGame(7);
                TestGames.BuildStarterResort(sim);
                TestGames.RunMinutes(sim, 180);
                return StateHasher.Hash(sim);
            }

            Assert.AreEqual(RunOnce(), RunOnce());
        }

        [Test]
        public void SaveLoad_ContinuesIdentically()
        {
            var a = TestGames.NewSmallGame(3);
            TestGames.BuildStarterResort(a);
            TestGames.RunMinutes(a, 150); // mid-morning: guests queueing, riding and skiing

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                SaveSystem.Save(a, ms);
                bytes = ms.ToArray();
            }
            GameSimulation b;
            using (var ms = new MemoryStream(bytes)) b = SaveSystem.Load(ms, a.Config);

            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state right after load");

            TestGames.RunMinutes(a, 120);
            TestGames.RunMinutes(b, 120);
            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state 2 hours after load");
        }

        [Test]
        public void SaveLoad_AtNight_WithLightsCarsAndWalkingAreas_ContinuesIdentically()
        {
            var a = TestGames.NewSmallGame(5);
            TestGames.BuildStarterResort(a);
            TestGames.LightStarterRun(a);
            var e = a.World.ResortEntrance;
            a.ExecuteImmediate(new PaintWalkingAreaCommand(new[] { new IceCoast.Simulation.Core.Int2(e.X - 2, e.Y + 9), new IceCoast.Simulation.Core.Int2(e.X - 1, e.Y + 9) }, false));
            TestGames.RunMinutes(a, 10 * 60 + 15); // 17:45: dark, lights on, night skiers, cars coming and going

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                SaveSystem.Save(a, ms);
                bytes = ms.ToArray();
            }
            GameSimulation b;
            using (var ms = new MemoryStream(bytes)) b = SaveSystem.Load(ms, a.Config);

            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state right after load");
            Assert.AreEqual(a.LightingSystem.LightsOn, b.LightingSystem.LightsOn);
            Assert.AreEqual(a.World.Runs[0].LitFraction, b.World.Runs[0].LitFraction);
            Assert.AreEqual(IceCoast.Simulation.World.SurfaceType.WalkingArea, b.Map.GetSurface(e.X - 2, e.Y + 9));

            TestGames.RunMinutes(a, 120);
            TestGames.RunMinutes(b, 120);
            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state 2 hours after load");
        }

        [Test]
        public void Commands_AreAppliedAtNextTick()
        {
            var sim = TestGames.NewSmallGame();
            sim.Enqueue(new SetTicketPriceCommand(55));
            Assert.AreEqual(sim.Config.Economy.StartingTicketPrice, sim.World.Economy.TicketPrice);
            sim.Tick();
            Assert.AreEqual(55, sim.World.Economy.TicketPrice);
        }

        [Test]
        public void SandboxMode_DoesNotSpendMoney()
        {
            var cfg = TestGames.SmallConfig();
            cfg.SandboxMode = true;
            var sim = SimulationFactory.CreateNewGame(cfg);
            long before = sim.World.Economy.Cash;
            TestGames.BuildStarterResort(sim);
            Assert.AreEqual(before, sim.World.Economy.Cash);
        }

        [Test]
        public void SnowQuality_StateMachine()
        {
            var cfg = new SimConfig.SnowSettings();
            Assert.AreEqual(SnowQuality.Bare, SnowSystem.NextQuality(SnowQuality.Powder, 2f, 0, 0, -5f, cfg));
            Assert.AreEqual(SnowQuality.Crud, SnowSystem.NextQuality(SnowQuality.Powder, 50f, cfg.CrudThreshold, 0, -5f, cfg));
            Assert.AreEqual(SnowQuality.Packed, SnowSystem.NextQuality(SnowQuality.Crud, 50f, cfg.PackedThreshold, 0, -5f, cfg));
            Assert.AreEqual(SnowQuality.Ice, SnowSystem.NextQuality(SnowQuality.Packed, 50f, cfg.IceThreshold, 0, -5f, cfg));
            Assert.AreEqual(SnowQuality.Slush, SnowSystem.NextQuality(SnowQuality.Packed, 50f, 0, 100, 3f, cfg));
            Assert.AreEqual(SnowQuality.Ice, SnowSystem.NextQuality(SnowQuality.Slush, 50f, 0, 100, -4f, cfg), "refreeze");
        }

        [Test]
        public void SkierTraffic_WearsTheSnow()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 5 * 60);

            int worn = 0;
            foreach (var t in run.Tiles)
            {
                var q = sim.Map.GetSnowQuality(t.X, t.Y);
                if (q == SnowQuality.Packed || q == SnowQuality.Crud || q == SnowQuality.Ice) worn++;
            }
            Assert.Greater(worn, run.Tiles.Count / 3);
        }

        [Test]
        public void BuyingLand_RequiresAdjacencyAndCash()
        {
            var sim = TestGames.NewSmallGame();
            var corner = sim.ParcelSystem.GetParcelAt(0, 0);         // far from the starter parcel
            Assert.IsFalse(sim.Preview(new BuyParcelCommand(corner.Id)).Ok);

            var e = sim.World.ResortEntrance;
            var neighbour = sim.ParcelSystem.GetParcelAt(e.X - 64, e.Y);
            sim.World.Economy.Cash = 10_000_000;
            var result = sim.ExecuteImmediate(new BuyParcelCommand(neighbour.Id));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.IsTrue(neighbour.Owned);
            Assert.IsTrue(sim.Map.IsOwned(neighbour.X + 5, neighbour.Y + 5));
        }

        [Test]
        public void Terraform_RaisesAndCharges()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var v = new IceCoast.Simulation.Core.Int2(e.X - 10, e.Y + 5);
            int before = sim.Map.GetVertexHeight(v.X, v.Y);
            long cash = sim.World.Economy.Cash;
            var result = sim.ExecuteImmediate(new TerraformCommand(v, 0, IceCoast.Simulation.Terrain.TerraformMode.Raise));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(before + 1, sim.Map.GetVertexHeight(v.X, v.Y));
            Assert.Less(sim.World.Economy.Cash, cash);
        }
    }
}
