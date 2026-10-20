using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Buildings
{
    /// <summary>
    /// Where a building may stand. Buildings (lodges, ticket offices, rentals... milestone M4) must sit
    /// entirely on a walking area: guests reach them on foot, never on skis across a trail. This is the one
    /// check the future BuildBuildingCommand and its tool preview will share.
    ///
    /// Walking areas under a building cannot be erased (WalkingAreaSystem.QuotePaint refuses), so a placed
    /// building always stays reachable.
    /// </summary>
    public static class BuildingRules
    {
        public static CommandResult CanPlaceFootprint(GameSimulation sim, Int2 origin, int width, int height)
        {
            var map = sim.Map;
            if (width <= 0 || height <= 0) return CommandResult.Fail("A building needs a footprint.");
            float maxSlope = sim.Config.Walking.BuildingMaxSlopePercent;
            for (int y = origin.Y; y < origin.Y + height; y++)
                for (int x = origin.X; x < origin.X + width; x++)
                {
                    if (!map.InBounds(x, y)) return CommandResult.Fail("Outside the map.");
                    if (!map.IsOwned(x, y)) return CommandResult.Fail("Buildings go on land you own.");
                    if (map.GetSurface(x, y) != SurfaceType.WalkingArea)
                        return CommandResult.Fail("Buildings must stand on a walking area. Paint one first.");
                    if (map.GetOccupant(x, y) != 0) return CommandResult.Fail("Something is already built here.");
                    if (TerrainQueries.SlopePercent(map, sim.TerrainScale, x, y) > maxSlope)
                        return CommandResult.Fail("Too steep to build on. Flatten it first.");
                }
            return CommandResult.Success();
        }
    }
}
