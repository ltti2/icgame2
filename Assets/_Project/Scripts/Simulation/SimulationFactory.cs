using IceCoast.Simulation.Config;
using IceCoast.Simulation.Terrain;

namespace IceCoast.Simulation
{
    /// <summary>Builds ready-to-run simulations. The only place that knows the new-game recipe.</summary>
    public static class SimulationFactory
    {
        public static GameSimulation CreateNewGame(SimConfig config)
        {
            var world = new WorldState(config.Map.Width, config.Map.Height, config.Guests.MaxGuests, (ulong)config.Map.Seed);
            var sim = new GameSimulation(config, world);

            world.Time.TotalMinutes = config.Time.StartMinuteOfDay;
            world.Economy.Cash = config.Economy.StartingCash;
            world.Economy.TicketPrice = config.Economy.StartingTicketPrice;
            world.Economy.Reputation = config.Economy.StartingReputation;
            world.Economy.Today.Day = 0;

            TerrainGenerator.Generate(sim);
            sim.ParcelSystem.CreateGrid();

            var starter = sim.ParcelSystem.GetParcelAt(world.ResortEntrance.X, world.ResortEntrance.Y);
            sim.ParcelSystem.Grant(starter);

            // The road in and the lot guests park in (the terrain generator already paved them).
            sim.ParkingSystem.CreateLot(sim.ParkingSystem.DefaultLayout(), config.Parking.DefaultLotCapacity, "Main Lot");

            sim.WeatherSystem.Initialize();
            sim.RebuildDerivedState();
            return sim;
        }
    }
}
