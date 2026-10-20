using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;

namespace IceCoast.Tests
{
    /// <summary>Shared fixtures: small, fast worlds with a known layout.</summary>
    public static class TestGames
    {
        /// <summary>128x128 map with a gentle mountain so the starter parcel has real beginner terrain.</summary>
        public static SimConfig SmallConfig(int seed = 42)
        {
            var cfg = SimConfig.CreateDefault();
            cfg.Map.Width = 128;
            cfg.Map.Height = 128;
            cfg.Map.Seed = seed;
            // A compressed mountain (about 525 ft of relief) so the small map still has beginner terrain.
            cfg.Terrain.SummitElevationSteps = cfg.Terrain.BaseElevationSteps + 160;
            cfg.Terrain.StuntedForestSteps = cfg.Terrain.BaseElevationSteps + 140;
            cfg.Map.SceneryPadding = 16;
            cfg.Guests.MaxGuests = 1500;
            return cfg;
        }

        public static GameSimulation NewSmallGame(int seed = 42) => SimulationFactory.CreateNewGame(SmallConfig(seed));

        /// <summary>
        /// Builds a rope tow up the cleared beginner corridor and paints a run next to it.
        /// The corridor runs from the entrance toward the summit (the -x,-y diagonal).
        /// </summary>
        public static (Lift lift, Run run) BuildStarterResort(GameSimulation sim)
        {
            var e = sim.World.ResortEntrance;
            var bottom = new Int2(e.X - 3, e.Y - 3);
            var top = new Int2(e.X - 25, e.Y - 25);

            var build = sim.ExecuteImmediate(new BuildLiftCommand(LiftType.RopeTow, bottom, top));
            if (!build.Ok) throw new System.Exception("Starter lift failed: " + build.Error);

            // A solid band parallel to the tow, offset to one side.
            // along = distance uphill from the entrance, across = offset from the tow's diagonal.
            var tiles = new List<Int2>();
            for (int y = e.Y - 32; y <= e.Y + 6; y++)
            {
                for (int x = e.X - 32; x <= e.X + 6; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int across = (x - y) - (e.X - e.Y);
                    if (along >= 2f && along <= 26f && across >= 2 && across <= 8) tiles.Add(new Int2(x, y));
                }
            }

            var paint = sim.ExecuteImmediate(new CreateRunCommand(tiles));
            if (!paint.Ok) throw new System.Exception("Starter run failed: " + paint.Error);

            return (sim.World.Lifts[0], sim.World.Runs[0]);
        }

        /// <summary>A second trail from the starter tow's top, on the other side of the tow (across -8..-2).</summary>
        public static Run PaintSecondStarterRun(GameSimulation sim)
        {
            var e = sim.World.ResortEntrance;
            var tiles = new List<Int2>();
            for (int y = e.Y - 32; y <= e.Y + 6; y++)
                for (int x = e.X - 32; x <= e.X + 6; x++)
                {
                    float along = ((e.X - x) + (e.Y - y)) * 0.5f;
                    int across = (x - y) - (e.X - e.Y);
                    if (along >= 4f && along <= 26f && across >= -8 && across <= -2) tiles.Add(new Int2(x, y));
                }
            var paint = sim.ExecuteImmediate(new CreateRunCommand(tiles));
            if (!paint.Ok) throw new System.Exception("Second run failed: " + paint.Error);
            return sim.World.Runs[sim.World.Runs.Count - 1];
        }

        /// <summary>
        /// Puts medium trail lights along both edges of the starter run (built by BuildStarterResort), every few
        /// tiles, so the whole run is lit. Returns how many were placed.
        /// </summary>
        public static int LightStarterRun(GameSimulation sim, Simulation.Config.LightSize size = Simulation.Config.LightSize.Medium)
        {
            var e = sim.World.ResortEntrance;
            int placed = 0;
            // The run spans across = 2..8 (see BuildStarterResort); light the columns just outside it.
            foreach (int across in new[] { 1, 9 })
                for (int along = 2; along <= 26; along += 5)
                {
                    // along = ((e.X - x) + (e.Y - y)) / 2, across = (x - y) - (e.X - e.Y)
                    int sum = (e.X + e.Y) - 2 * along;
                    int diff = across + (e.X - e.Y);
                    if (((sum + diff) & 1) != 0) sum--;
                    var tile = new Int2((sum + diff) / 2, (sum - diff) / 2);
                    if (sim.ExecuteImmediate(new Simulation.Commands.PlaceLightCommand(size, tile)).Ok) placed++;
                }
            return placed;
        }

        public static void RunTicks(GameSimulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++) sim.Tick();
        }

        public static void RunMinutes(GameSimulation sim, int minutes) => RunTicks(sim, minutes * sim.Config.Time.TicksPerGameMinute);
    }
}
