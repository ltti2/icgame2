using System.Collections.Generic;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Commands
{
    /// <summary>
    /// Paint walking area onto tiles (clearing trees), or erase it back to natural ground. One brush stroke is
    /// one command. Tiles already in the requested state are skipped; the stroke is refused as a whole if any
    /// tile can't take it (a trail, the road, too steep, a building on it...).
    /// </summary>
    public sealed class PaintWalkingAreaCommand : ISimCommand
    {
        public readonly List<Int2> Tiles;
        public readonly bool Erase;

        public PaintWalkingAreaCommand(IEnumerable<Int2> tiles, bool erase)
        {
            Tiles = new List<Int2>(tiles);
            Erase = erase;
        }

        public CommandResult Validate(GameSimulation sim) => sim.WalkingAreaSystem.QuotePaint(Tiles, Erase, out _);
        public void Execute(GameSimulation sim) => sim.WalkingAreaSystem.Paint(Tiles, Erase);
    }
}
