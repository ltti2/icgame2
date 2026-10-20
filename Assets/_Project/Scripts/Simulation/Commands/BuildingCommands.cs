using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Commands
{
    /// <summary>Put up a building (a Waffle Cabin) with its footprint's top-left at <see cref="Origin"/>.</summary>
    public sealed class PlaceBuildingCommand : ISimCommand
    {
        public readonly BuildingType Type;
        public readonly Int2 Origin;
        private long _cost;

        public PlaceBuildingCommand(BuildingType type, Int2 origin)
        {
            Type = type;
            Origin = origin;
        }

        public CommandResult Validate(GameSimulation sim)
        {
            var result = sim.BuildingSystem.QuotePlace(Type, Origin);
            _cost = result.Cost;
            return result;
        }

        public void Execute(GameSimulation sim) => sim.BuildingSystem.Place(Type, Origin, _cost);
    }

    /// <summary>Knock a building down (no refund).</summary>
    public sealed class DemolishBuildingCommand : ISimCommand
    {
        public readonly int BuildingId;
        public DemolishBuildingCommand(int buildingId) { BuildingId = buildingId; }

        public CommandResult Validate(GameSimulation sim)
            => sim.BuildingSystem.GetBuilding(BuildingId) != null ? CommandResult.Success() : CommandResult.Fail("That building no longer exists.");

        public void Execute(GameSimulation sim) => sim.BuildingSystem.Demolish(sim.BuildingSystem.GetBuilding(BuildingId));
    }

    /// <summary>Set what a food building charges for one item.</summary>
    public sealed class SetBuildingPriceCommand : ISimCommand
    {
        public const int MaxPrice = 100;
        public readonly int BuildingId;
        public readonly int Price;

        public SetBuildingPriceCommand(int buildingId, int price)
        {
            BuildingId = buildingId;
            Price = price;
        }

        public CommandResult Validate(GameSimulation sim)
        {
            if (sim.BuildingSystem.GetBuilding(BuildingId) == null) return CommandResult.Fail("That building no longer exists.");
            if (Price < 0 || Price > MaxPrice) return CommandResult.Fail($"Price must be between $0 and ${MaxPrice}.");
            return CommandResult.Success();
        }

        public void Execute(GameSimulation sim)
        {
            sim.BuildingSystem.GetBuilding(BuildingId).Price = Price;
            sim.World.BuildingsVersion++;
        }
    }

    /// <summary>Open or close a building. Closed: no sales and no running cost.</summary>
    public sealed class SetBuildingOpenCommand : ISimCommand
    {
        public readonly int BuildingId;
        public readonly bool Open;

        public SetBuildingOpenCommand(int buildingId, bool open)
        {
            BuildingId = buildingId;
            Open = open;
        }

        public CommandResult Validate(GameSimulation sim)
            => sim.BuildingSystem.GetBuilding(BuildingId) != null ? CommandResult.Success() : CommandResult.Fail("That building no longer exists.");

        public void Execute(GameSimulation sim)
        {
            sim.BuildingSystem.GetBuilding(BuildingId).PlayerOpen = Open;
            sim.World.BuildingsVersion++;
        }
    }
}
