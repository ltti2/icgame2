namespace IceCoast.Simulation.World
{
    /// <summary>
    /// Terrain generated around the playable map so the mountain range continues past the edge.
    /// Pure scenery: never owned, built on, simulated or saved (it is regenerated from the map seed).
    ///
    /// Coordinates are the same as the map's, so they run from -Padding to Width + Padding - 1.
    /// Inside the playable map, always read the real <see cref="WorldMap"/> instead: the scenery arrays
    /// hold the original generated values there, which terraforming may since have changed.
    /// </summary>
    public sealed class Scenery
    {
        public readonly int Padding;
        public readonly int MapWidth, MapHeight;

        /// <summary>Size of the whole generated area in tiles, map included.</summary>
        public readonly int Width, Height;

        /// <summary>Corner heights, (Width + 1) x (Height + 1), indexed by extended vertex coordinates.</summary>
        public readonly short[] VertexHeight;
        public readonly byte[] TreeDensity;
        public readonly byte[] TerrainTypes;
        /// <summary>Per tile <see cref="SurfaceType"/>: where the access road runs out through the scenery.</summary>
        public readonly byte[] Surface;

        public Scenery(int mapWidth, int mapHeight, int padding)
        {
            Padding = padding;
            MapWidth = mapWidth;
            MapHeight = mapHeight;
            Width = mapWidth + 2 * padding;
            Height = mapHeight + 2 * padding;
            VertexHeight = new short[(Width + 1) * (Height + 1)];
            TreeDensity = new byte[Width * Height];
            TerrainTypes = new byte[Width * Height];
            Surface = new byte[Width * Height];
        }

        public int MinX => -Padding;
        public int MinY => -Padding;
        public int MaxX => MapWidth + Padding;  // exclusive
        public int MaxY => MapHeight + Padding; // exclusive

        public bool Contains(int x, int y) => x >= MinX && y >= MinY && x < MaxX && y < MaxY;

        /// <summary>True for tiles that are scenery only (outside the playable map).</summary>
        public bool IsSceneryTile(int x, int y)
            => Contains(x, y) && (x < 0 || y < 0 || x >= MapWidth || y >= MapHeight);

        public int VertexIndex(int vx, int vy) => (vy + Padding) * (Width + 1) + (vx + Padding);
        public int TileIndex(int x, int y) => (y + Padding) * Width + (x + Padding);

        /// <summary>
        /// Corner height in steps. Uses the live map for corners on or inside the map edge so scenery
        /// always meets the playable terrain seamlessly, even after terraforming.
        /// </summary>
        public int GetVertexHeight(WorldMap map, int vx, int vy)
        {
            if (map.VertexInBounds(vx, vy)) return map.GetVertexHeight(vx, vy);
            vx = Clamp(vx, MinX, MaxX);
            vy = Clamp(vy, MinY, MaxY);
            return VertexHeight[VertexIndex(vx, vy)];
        }

        public void GetCorners(WorldMap map, int x, int y, out int h00, out int h10, out int h01, out int h11)
        {
            h00 = GetVertexHeight(map, x, y);
            h10 = GetVertexHeight(map, x + 1, y);
            h01 = GetVertexHeight(map, x, y + 1);
            h11 = GetVertexHeight(map, x + 1, y + 1);
        }

        public float SampleHeight(WorldMap map, float fx, float fy)
        {
            int x = (int)System.Math.Floor(fx), y = (int)System.Math.Floor(fy);
            float tx = fx - x, ty = fy - y;
            GetCorners(map, x, y, out int h00, out int h10, out int h01, out int h11);
            float top = h00 + (h10 - h00) * tx;
            float bottom = h01 + (h11 - h01) * tx;
            return top + (bottom - top) * ty;
        }

        public int GetTreeDensity(int x, int y) => Contains(x, y) ? TreeDensity[TileIndex(x, y)] : 0;
        public SurfaceType GetSurface(int x, int y) => Contains(x, y) ? (SurfaceType)Surface[TileIndex(x, y)] : SurfaceType.Natural;
        public TerrainType GetTerrainType(int x, int y) => Contains(x, y) ? (TerrainType)TerrainTypes[TileIndex(x, y)] : TerrainType.Grass;

        /// <summary>0 at the map edge, 1 at the outer edge of the scenery. Used for atmospheric haze.</summary>
        public float DistanceFromMap01(int x, int y)
        {
            int dx = x < 0 ? -x : x >= MapWidth ? x - MapWidth + 1 : 0;
            int dy = y < 0 ? -y : y >= MapHeight ? y - MapHeight + 1 : 0;
            int d = dx > dy ? dx : dy;
            return Padding <= 0 ? 0f : (float)d / Padding;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
