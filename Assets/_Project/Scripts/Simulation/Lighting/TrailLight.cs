using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Lighting
{
    /// <summary>
    /// One light pole beside a trail. It occupies its tile (OccupantKind.Light) and lights the tiles around it
    /// (Chunk.LightLevel) while the lights are on. Stats come from <see cref="LightDefinition"/> by size.
    /// </summary>
    public sealed class TrailLight
    {
        public int Id;
        public LightSize Size;
        public Int2 Tile;
    }
}
