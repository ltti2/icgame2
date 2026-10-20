using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.Time;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class NightSkiingTests
    {
        [Test]
        public void Sunset_IsRealisticForVermont_AndLaterInMarch()
        {
            var cfg = SimConfig.CreateDefault().Time;
            Daylight.SunTimes(cfg, 0, Weekday.Tuesday, out int rise, out int set); // Dec 1
            Assert.IsTrue(set >= 16 * 60 && set <= 16 * 60 + 30, "Dec 1 sunset around 4:15 PM, was " + set);
            Assert.IsTrue(rise >= 6 * 60 + 50 && rise <= 7 * 60 + 20, "Dec 1 sunrise around 7:05 AM, was " + rise);

            Daylight.SunTimes(cfg, 110, Weekday.Tuesday, out _, out int marchSet); // late March, daylight saving
            Assert.Greater(marchSet, 19 * 60, "late March sunsets come after 7 PM on the clock");
        }

        [Test]
        public void Light_LightsTilesAroundIt_AndCostsMoney()
        {
            var sim = TestGames.NewSmallGame();
            var e = sim.World.ResortEntrance;
            var tile = new Int2(e.X - 12, e.Y - 3);
            long cash = sim.World.Economy.Cash;

            var result = sim.ExecuteImmediate(new PlaceLightCommand(LightSize.Large, tile));
            Assert.IsTrue(result.Ok, result.Error);
            Assert.Less(sim.World.Economy.Cash, cash);
            Assert.IsTrue(sim.LightingSystem.IsTileLit(tile.X, tile.Y));
            Assert.IsTrue(sim.LightingSystem.IsTileLit(tile.X + 8, tile.Y), "a large light reaches ~10 tiles");
            Assert.IsFalse(sim.LightingSystem.IsTileLit(tile.X + 14, tile.Y));

            var light = sim.World.Lights[0];
            Assert.IsTrue(sim.ExecuteImmediate(new RemoveLightCommand(light.Id)).Ok);
            Assert.AreEqual(0, sim.Map.GetLightLevel(tile.X, tile.Y));
            Assert.AreEqual(0, sim.Map.GetOccupant(tile.X, tile.Y));
        }

        [Test]
        public void Light_CannotGoInTheMiddleOfATrail()
        {
            var sim = TestGames.NewSmallGame();
            var (_, run) = TestGames.BuildStarterResort(sim);
            var result = sim.Preview(new PlaceLightCommand(LightSize.Small, run.Tiles[run.Tiles.Count / 2]));
            Assert.IsFalse(result.Ok);
        }

        [Test]
        public void UnlitLift_ClosesAtTheDayClose()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 10 * 60); // 7:30 -> 17:30, dark
            Assert.IsTrue(sim.LightingSystem.IsDark);
            Assert.AreEqual(LiftStatus.AfterHours, lift.Status, "nobody rides up into the dark");
        }

        [Test]
        public void LitTrail_KeepsItsLiftRunningUntilTheNightClose()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, run) = TestGames.BuildStarterResort(sim);
            Assert.Greater(TestGames.LightStarterRun(sim), 4);
            Assert.GreaterOrEqual(run.LitFraction, sim.Config.Lighting.RunLitFraction);

            TestGames.RunMinutes(sim, 11 * 60); // 18:30
            Assert.IsTrue(sim.LightingSystem.IsDark);
            Assert.IsTrue(sim.LightingSystem.LightsOn);
            Assert.AreEqual(LiftStatus.Open, lift.Status);
            Assert.Greater(sim.World.Guests.Count, 0, "night skiers");

            TestGames.RunMinutes(sim, 3 * 60); // 21:30
            Assert.AreEqual(LiftStatus.AfterHours, lift.Status);
            Assert.IsFalse(sim.LightingSystem.LightsOn);
        }

        [Test]
        public void NightSkiingSwitchedOff_EverythingClosesAtTheDayClose()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, _) = TestGames.BuildStarterResort(sim);
            TestGames.LightStarterRun(sim);
            sim.ExecuteImmediate(new SetNightSkiingCommand(false));
            TestGames.RunMinutes(sim, 11 * 60);
            Assert.AreEqual(LiftStatus.AfterHours, lift.Status);
            Assert.IsFalse(sim.LightingSystem.LightsOn);
        }

        [Test]
        public void AtNight_SkiersPreferTheLitTrail()
        {
            var sim = TestGames.NewSmallGame();
            var (lift, lit) = TestGames.BuildStarterResort(sim);
            var dark = TestGames.PaintSecondStarterRun(sim);
            TestGames.LightStarterRun(sim, LightSize.Small);
            Assert.IsTrue(sim.LightingSystem.HasLights(lit), "lit share " + lit.LitFraction);
            Assert.IsFalse(sim.LightingSystem.HasLights(dark), "only the first trail has lights, but lit share " + dark.LitFraction);

            TestGames.RunMinutes(sim, 9 * 60 + 45); // 17:15: dark, night session
            Assert.IsTrue(sim.LightingSystem.IsDark);
            int litBefore = lit.SkiersToday, darkBefore = dark.SkiersToday;
            TestGames.RunMinutes(sim, 3 * 60);
            int litNight = lit.SkiersToday - litBefore, darkNight = dark.SkiersToday - darkBefore;
            Assert.Greater(litNight, 10, "people ski the lit trail at night");
            Assert.Less(darkNight, litNight / 4 + 1, "the dark trail is only skied by those who must");
        }
    }
}
