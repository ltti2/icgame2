using System;
using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Terrain
{
    /// <summary>
    /// Where the starting base village, its parking lot and the access road go, worked out from the resort
    /// entrance. Shared by the terrain generator (which flattens and paves them) and by the parking system
    /// (which lays out the spaces), so the two always agree.
    ///
    /// Grid +x is toward the screen's lower right; the summit is up-left (-x,-y). From the entrance:
    ///  - a half-disc of walking area on the valley side (the base plaza; the uphill side stays snow for
    ///    trails to run out onto),
    ///  - the lot just beyond it along +x: stall rows either side of a driving aisle on the entrance's row,
    ///    four spaces per stall tile,
    ///  - the road leaving the far end of the aisle along +x, off the map and on into the scenery.
    /// </summary>
    public sealed class BaseLayout
    {
        public const int SpacesPerStallTile = 4;
        private const int StallRowsEachSide = 2;
        private const int PlazaRadius = 7;
        private const int LotGap = 6;

        public Int2 Entrance;
        /// <summary>Lot rectangle (tiles).</summary>
        public int LotX, LotY, LotWidth, LotHeight;
        public int AisleY;
        /// <summary>Road polyline, from the far end in the scenery to where it meets the lot (tile coordinates).</summary>
        public readonly List<Int2> Road = new List<Int2>();

        public int RoadY => AisleY;
        public int RoadStartX => LotX + LotWidth;

        public static BaseLayout For(int mapWidth, int mapHeight, Int2 entrance, int lotCapacity, int sceneryRoadLength)
        {
            var b = new BaseLayout { Entrance = entrance };
            int stallTiles = (Math.Max(1, lotCapacity) + SpacesPerStallTile - 1) / SpacesPerStallTile;
            int rows = StallRowsEachSide * 2;
            b.LotWidth = Math.Max(2, (stallTiles + rows - 1) / rows);
            b.LotHeight = rows + 1;
            b.LotX = entrance.X + LotGap;
            b.AisleY = entrance.Y;
            b.LotY = entrance.Y - StallRowsEachSide;

            // Keep the lot on the map (small test maps).
            if (b.LotX + b.LotWidth > mapWidth - 1) b.LotX = Math.Max(0, mapWidth - 1 - b.LotWidth);

            int roadEnd = mapWidth + Math.Max(0, sceneryRoadLength);
            b.Road.Add(new Int2(roadEnd, b.AisleY));
            b.Road.Add(new Int2(b.LotX + b.LotWidth, b.AisleY));
            return b;
        }

        public bool InLot(int x, int y) => x >= LotX && y >= LotY && x < LotX + LotWidth && y < LotY + LotHeight;

        /// <summary>Tiles of road from the lot to <paramref name="xEnd"/> (exclusive), along the aisle row.</summary>
        public bool OnRoad(int x, int y, int xEnd) => y == RoadY && x >= RoadStartX && x < xEnd;

        /// <summary>Within a tile of the road (cleared shoulders).</summary>
        public bool OnRoadShoulder(int x, int y, int xEnd) => Math.Abs(y - RoadY) <= 1 && x >= RoadStartX - 1 && x < xEnd;

        /// <summary>The base plaza: valley-side half of a disc around the entrance, outside the lot.</summary>
        public bool InPlaza(int x, int y)
        {
            int dx = x - Entrance.X, dy = y - Entrance.Y;
            if (dx * dx + dy * dy > PlazaRadius * PlazaRadius) return false;
            if (dx + dy < -1) return false; // uphill half stays snow for trails to run out onto
            return !InLot(x, y);
        }

        /// <summary>
        /// Lays the plaza, lot and road surfaces on the map, clears trees from them (and the road shoulders) and
        /// plows them. Used at world generation and when upgrading an old save.
        /// </summary>
        public void ApplySurfaces(WorldMap map)
        {
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    SurfaceType s;
                    if (InLot(x, y)) s = SurfaceType.Parking;
                    else if (OnRoad(x, y, map.Width)) s = SurfaceType.Road;
                    else if (InPlaza(x, y) && map.GetRunId(x, y) == 0) s = SurfaceType.WalkingArea;
                    else
                    {
                        if (OnRoadShoulder(x, y, map.Width)) ClearTrees(map, x, y);
                        continue;
                    }
                    if (map.GetRunId(x, y) != 0) continue;                                    // never pave over a trail (old saves)
                    if (map.GetOccupant(x, y) != 0 && s != SurfaceType.WalkingArea) continue; // or under a lift
                    map.SetSurface(x, y, s);
                    ClearTrees(map, x, y);
                    if (s != SurfaceType.WalkingArea)
                    {
                        var t = map.Locate(x, y);
                        t.Chunk.SnowDepth[t.Index] = 0;
                        t.Chunk.SnowQuality[t.Index] = (byte)SnowQuality.Bare;
                        t.Chunk.TerrainType[t.Index] = (byte)TerrainType.Dirt;
                    }
                }
        }

        private static void ClearTrees(WorldMap map, int x, int y)
        {
            if (map.GetTreeDensity(x, y) > 0) map.SetTreeDensity(x, y, 0);
        }
    }
}
