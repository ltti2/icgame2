using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Commands
{
    /// <summary>Put up a trail light. Clears any trees on its tile (the cost is in the quote).</summary>
    public sealed class PlaceLightCommand : ISimCommand
    {
        public readonly LightSize Size;
        public readonly Int2 Tile;
        private long _cost;

        public PlaceLightCommand(LightSize size, Int2 tile)
        {
            Size = size;
            Tile = tile;
        }

        public CommandResult Validate(GameSimulation sim)
        {
            var result = sim.LightingSystem.QuotePlace(Size, Tile);
            _cost = result.Cost;
            return result;
        }

        public void Execute(GameSimulation sim) => sim.LightingSystem.Place(Size, Tile, _cost);
    }

    /// <summary>Take a trail light down (no refund).</summary>
    public sealed class RemoveLightCommand : ISimCommand
    {
        public readonly int LightId;
        public RemoveLightCommand(int lightId) { LightId = lightId; }

        public CommandResult Validate(GameSimulation sim)
            => sim.LightingSystem.GetLight(LightId) != null ? CommandResult.Success() : CommandResult.Fail("No light there.");

        public void Execute(GameSimulation sim) => sim.LightingSystem.Remove(sim.LightingSystem.GetLight(LightId));
    }

    /// <summary>
    /// Turn night skiing on or off for the whole resort. Off: the lights stay dark and every lift closes at
    /// the day close.
    /// </summary>
    public sealed class SetNightSkiingCommand : ISimCommand
    {
        public readonly bool Enabled;
        public SetNightSkiingCommand(bool enabled) { Enabled = enabled; }

        public CommandResult Validate(GameSimulation sim) => CommandResult.Success();

        public void Execute(GameSimulation sim)
        {
            sim.World.NightSkiing = Enabled;
            sim.LightingSystem.RefreshFlags();
            sim.LiftSystem.RefreshStatuses();
            sim.World.LightsVersion++;
        }
    }
}
