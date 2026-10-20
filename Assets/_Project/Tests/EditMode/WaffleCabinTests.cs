using System.IO;
using IceCoast.Simulation;
using IceCoast.Simulation.Buildings;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Persistence;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class WaffleCabinTests
    {
        /// <summary>A tile on the starting base plaza (walking area, flat, owned, nothing built).</summary>
        private static Int2 PlazaTile(GameSimulation sim)
        {
            var e = sim.World.ResortEntrance;
            return new Int2(e.X + 2, e.Y + 2);
        }

        private static Building BuildCabin(GameSimulation sim)
        {
            var result = sim.ExecuteImmediate(new PlaceBuildingCommand(BuildingType.WaffleCabin, PlazaTile(sim)));
            Assert.IsTrue(result.Ok, result.Error);
            return sim.World.Buildings[sim.World.Buildings.Count - 1];
        }

        /// <summary>Starter resort plus a cabin on the plaza, run from opening until mid-afternoon.</summary>
        private static (GameSimulation sim, Building cabin) BusyDay(int price, int seed = 42)
        {
            var sim = TestGames.NewSmallGame(seed);
            TestGames.BuildStarterResort(sim);
            var cabin = BuildCabin(sim);
            sim.ExecuteImmediate(new SetBuildingPriceCommand(cabin.Id, price));
            TestGames.RunMinutes(sim, 6 * 60);
            return (sim, cabin);
        }

        [Test]
        public void Placement_NeedsAWalkingArea_AndCharges()
        {
            var sim = TestGames.NewSmallGame();
            var def = sim.Config.GetBuilding(BuildingType.WaffleCabin);
            var e = sim.World.ResortEntrance;

            var offPlaza = new Int2(e.X - 10, e.Y - 10);
            Assert.IsFalse(sim.Preview(new PlaceBuildingCommand(BuildingType.WaffleCabin, offPlaza)).Ok, "not on plain snow");

            long cash = sim.World.Economy.Cash;
            var cabin = BuildCabin(sim);
            Assert.AreEqual(cash - def.BuildCost, sim.World.Economy.Cash);
            Assert.AreEqual(def.DefaultPrice, cabin.Price);
            Assert.AreEqual(OccupantKind.Building, Occupant.KindOf(sim.Map.GetOccupant(cabin.Origin.X, cabin.Origin.Y)));
            Assert.AreEqual(cabin.Id, sim.BuildingSystem.BuildingAt(cabin.Origin.X, cabin.Origin.Y).Id);
            Assert.IsFalse(sim.Preview(new PlaceBuildingCommand(BuildingType.WaffleCabin, cabin.Origin)).Ok, "the tile is taken");
            Assert.IsFalse(sim.Preview(new PaintWalkingAreaCommand(new[] { cabin.Origin }, erase: true)).Ok,
                           "the walking area under a building can't be erased");
        }

        [Test]
        public void HungryGuests_BuyWaffles_AndThePriceIsBooked()
        {
            var (sim, cabin) = BusyDay(price: 8);
            var def = sim.Config.GetBuilding(BuildingType.WaffleCabin);
            var today = sim.World.Economy.Today;

            Assert.Greater(cabin.SoldToday, 10, "guests got hungry and bought waffles");
            Assert.AreEqual(cabin.SoldToday * 8L, cabin.RevenueToday);
            Assert.AreEqual(cabin.RevenueToday, today.Income[(int)LedgerCategory.Food]);
            // Ingredients for every waffle plus the cabin's hourly running cost.
            Assert.GreaterOrEqual(today.Expenses[(int)LedgerCategory.FoodService], cabin.SoldToday * (long)def.IngredientCostPerSale + def.OperatingCostPerHour);
        }

        [Test]
        public void Eating_MakesGuestsHappier_AndLessHungry()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            var cabin = BuildCabin(sim);
            var pool = sim.World.Guests;

            int checkedGuests = 0;
            for (int minute = 0; minute < 6 * 60 && checkedGuests < 5; minute++)
            {
                // Watch for guests reaching the window, and compare them before and after.
                int[] waiting = cabin.Queue.ToArray();
                float[] before = new float[waiting.Length];
                for (int k = 0; k < waiting.Length; k++) before[k] = pool.Happiness[waiting[k]];
                TestGames.RunMinutes(sim, 1);
                for (int k = 0; k < waiting.Length; k++)
                {
                    int g = waiting[k];
                    if (!pool.Alive[g] || pool.State[g] != GuestState.Eating) continue;
                    Assert.Greater(pool.Happiness[g], before[k] - 1f, "a waffle cheers you up");
                    Assert.Less(pool.Hunger[g], 5, "and fills you up");
                    checkedGuests++;
                }
            }
            Assert.Greater(checkedGuests, 0, "someone ate");
        }

        [Test]
        public void CheaperWaffles_MakeGuestsHappier()
        {
            var def = SimConfig.CreateDefault().GetBuilding(BuildingType.WaffleCabin);
            Assert.Greater(BuildingSystem.HappinessFor(def, def.FairPrice / 2), BuildingSystem.HappinessFor(def, def.FairPrice));
            Assert.Greater(BuildingSystem.HappinessFor(def, def.FairPrice), BuildingSystem.HappinessFor(def, def.FairPrice * 2));
            Assert.Greater(BuildingSystem.HappinessFor(def, def.MaxPrice), 0f, "still nice, just less so");
        }

        [Test]
        public void TooExpensive_NobodyBuys_AndThePlayerCanSeeWhy()
        {
            var sim = TestGames.NewSmallGame();
            var def = sim.Config.GetBuilding(BuildingType.WaffleCabin);
            var (_, cabin) = BusyDay(price: def.MaxPrice);
            Assert.AreEqual(0, cabin.SoldToday);
            Assert.Greater(cabin.PricedOutToday, 0, "the window counts the hungry guests the price put off");
        }

        [Test]
        public void HigherPrices_SellFewerWaffles()
        {
            var (_, fair) = BusyDay(price: 8);
            var (_, dear) = BusyDay(price: 15);
            Assert.Greater(fair.SoldToday, dear.SoldToday);
            Assert.Greater(dear.PricedOutToday, fair.PricedOutToday);
        }

        [Test]
        public void ClosedCabin_SellsNothing_AndCostsNothing()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            var cabin = BuildCabin(sim);
            sim.ExecuteImmediate(new SetBuildingOpenCommand(cabin.Id, false));
            TestGames.RunMinutes(sim, 6 * 60);
            Assert.AreEqual(0, cabin.SoldToday);
            Assert.AreEqual(0, sim.World.Economy.Today.Expenses[(int)LedgerCategory.FoodService]);
        }

        [Test]
        public void Demolish_FreesTheTile_AndSendsTheLineAway()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            var cabin = BuildCabin(sim);
            var pool = sim.World.Guests;
            for (int i = 0; i < 6 * 60 && cabin.Queue.Count == 0; i++) TestGames.RunMinutes(sim, 1);
            Assert.Greater(cabin.Queue.Count, 0, "someone lined up");

            Assert.IsTrue(sim.ExecuteImmediate(new DemolishBuildingCommand(cabin.Id)).Ok);
            Assert.AreEqual(0, sim.Map.GetOccupant(cabin.Origin.X, cabin.Origin.Y));
            Assert.AreEqual(0, sim.World.Buildings.Count);
            for (int g = 0; g < pool.HighWater; g++)
            {
                if (!pool.Alive[g]) continue;
                Assert.AreNotEqual(GuestState.WaitingForFood, pool.State[g]);
                Assert.AreNotEqual(GuestState.WalkingToFood, pool.State[g]);
            }
            TestGames.RunMinutes(sim, 30); // nothing breaks afterwards
        }

        [Test]
        public void SaveLoad_WithACabinAndALine_ContinuesIdentically()
        {
            var a = TestGames.NewSmallGame(7);
            TestGames.BuildStarterResort(a);
            var cabin = BuildCabin(a);
            a.ExecuteImmediate(new SetBuildingPriceCommand(cabin.Id, 6));
            TestGames.RunMinutes(a, 4 * 60 + 20);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                SaveSystem.Save(a, ms);
                bytes = ms.ToArray();
            }
            GameSimulation b;
            using (var ms = new MemoryStream(bytes)) b = SaveSystem.Load(ms, a.Config);

            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state right after load");
            var loaded = b.World.Buildings[0];
            Assert.AreEqual(6, loaded.Price);
            Assert.AreEqual(cabin.SoldTotal, loaded.SoldTotal);
            Assert.AreEqual(cabin.Queue.Count, loaded.Queue.Count);
            for (int i = 0; i < cabin.Queue.Count; i++) Assert.AreEqual(cabin.Queue[i], loaded.Queue[i]);

            TestGames.RunMinutes(a, 90);
            TestGames.RunMinutes(b, 90);
            Assert.AreEqual(StateHasher.Hash(a), StateHasher.Hash(b), "state 90 minutes after load");
        }
    }
}
