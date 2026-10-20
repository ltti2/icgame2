using IceCoast.Simulation.Guests;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class EarlyBirdTests
    {
        private static int Count(IceCoast.Simulation.GameSimulation sim, GuestState state)
        {
            var pool = sim.World.Guests;
            int n = 0;
            for (int i = 0; i < pool.HighWater; i++) if (pool.Alive[i] && pool.State[i] == state) n++;
            return n;
        }

        [Test]
        public void EarlyBirds_TrickleIn_WanderThenLineUpBeforeOpening()
        {
            var sim = TestGames.NewSmallGame();
            TestGames.BuildStarterResort(sim);
            TestGames.RunMinutes(sim, 23 * 60); // 7:30 -> 6:30 the next morning
            Assert.AreEqual(1, sim.World.Time.Day);

            TestGames.RunMinutes(sim, 90); // 8:00, before the rush starts
            int early = sim.World.Economy.Today.GuestsArrived;
            Assert.Greater(early, 0, "a few keen skiers show up early");
            Assert.Less(early, 60, "just a few, not the morning rush");
            Assert.Greater(Count(sim, GuestState.Resting), 0, "they wander the base");
            Assert.AreEqual(0, Count(sim, GuestState.Queueing), "too early to line up");

            TestGames.RunMinutes(sim, 45); // 8:45
            Assert.Greater(Count(sim, GuestState.Queueing), 0, "lining up for first chair");
            Assert.AreEqual(0, sim.World.Economy.Today.GuestsDeparted, "nobody gave up and went home");

            TestGames.RunMinutes(sim, 20); // 9:05
            Assert.Greater(sim.World.Lifts[0].RidersToday, 0, "first chair at opening");
        }
    }
}
