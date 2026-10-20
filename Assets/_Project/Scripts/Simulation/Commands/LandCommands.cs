using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Terrain;

namespace IceCoast.Simulation.Commands
{
    public sealed class BuyParcelCommand : ISimCommand
    {
        public readonly int ParcelId;
        public BuyParcelCommand(int parcelId) { ParcelId = parcelId; }

        public CommandResult Validate(GameSimulation sim) => sim.ParcelSystem.ValidatePurchase(sim.World.GetParcel(ParcelId));
        public void Execute(GameSimulation sim) => sim.ParcelSystem.Purchase(sim.World.GetParcel(ParcelId));
    }

    public sealed class TerraformCommand : ISimCommand
    {
        public readonly Int2 Vertex;
        public readonly int Radius;
        public readonly TerraformMode Mode;
        public readonly int Steps;
        private long _cost;

        public TerraformCommand(Int2 vertex, int radius, TerraformMode mode, int steps = 1)
        {
            Vertex = vertex;
            Radius = radius;
            Mode = mode;
            Steps = steps;
        }

        public CommandResult Validate(GameSimulation sim)
        {
            var result = sim.TerraformSystem.Plan(Vertex, Radius, Mode, Steps);
            _cost = result.Cost;
            return result;
        }

        public void Execute(GameSimulation sim) => sim.TerraformSystem.ApplyPlan(_cost);
    }

    public sealed class ClearTreesCommand : ISimCommand
    {
        public readonly List<Int2> Tiles;
        private long _cost, _lumber;

        public ClearTreesCommand(IEnumerable<Int2> tiles) { Tiles = new List<Int2>(tiles); }

        public CommandResult Validate(GameSimulation sim)
        {
            var result = sim.TreeSystem.QuoteClear(Tiles, out _lumber);
            _cost = result.Cost;
            return result;
        }

        public void Execute(GameSimulation sim) => sim.TreeSystem.Clear(Tiles, _cost, _lumber);
    }

    public sealed class SetTicketPriceCommand : ISimCommand
    {
        public readonly int Price;
        public SetTicketPriceCommand(int price) { Price = price; }

        public CommandResult Validate(GameSimulation sim)
            => Price < 0 || Price > 1000 ? CommandResult.Fail("Ticket price must be between $0 and $1000.") : CommandResult.Success();

        public void Execute(GameSimulation sim) => sim.World.Economy.TicketPrice = Price;
    }
}
