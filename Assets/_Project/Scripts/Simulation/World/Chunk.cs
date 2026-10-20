namespace IceCoast.Simulation.World
{
    /// <summary>
    /// 32x32 tile block stored as a structure of arrays. Tile arrays are indexed
    /// <c>ly * Size + lx</c>; vertex arrays <c>vy * VertexStride + vx</c>.
    ///
    /// Vertices on chunk borders are duplicated in each neighbouring chunk (per the design doc).
    /// Always write heights through <see cref="WorldMap.SetVertexHeight"/>, which keeps the copies in sync.
    ///
    /// Instead of a single Dirty bool, the chunk carries version counters. The simulation only ever
    /// increments them; the view remembers the version it last built. This keeps the two layers
    /// decoupled (the view never writes into simulation state) and supports multiple consumers.
    /// </summary>
    public sealed class Chunk
    {
        public const int Size = 32;
        public const int SizeShift = 5;
        public const int SizeMask = Size - 1;
        public const int TileCount = Size * Size;
        public const int VertexStride = Size + 1;
        public const int VertexCount = VertexStride * VertexStride;

        public readonly int ChunkX;
        public readonly int ChunkY;

        // Terrain
        public readonly short[] VertexHeight = new short[VertexCount];
        public readonly byte[] TerrainType = new byte[TileCount];
        public readonly byte[] TreeDensity = new byte[TileCount];

        // Snow
        public readonly ushort[] SnowDepth = new ushort[TileCount];
        public readonly byte[] SnowQuality = new byte[TileCount];
        public readonly byte[] Compaction = new byte[TileCount];
        public readonly byte[] Moisture = new byte[TileCount];

        // Gameplay
        public readonly ushort[] OccupantId = new ushort[TileCount];
        public readonly ushort[] RunId = new ushort[TileCount];
        public readonly byte[] Flags = new byte[TileCount];
        /// <summary>Skier passes since the last snow update. Consumed by SnowSystem.</summary>
        public readonly ushort[] Traffic = new ushort[TileCount];
        /// <summary>What is laid on the ground (<see cref="SurfaceType"/>): walking area, road, parking. Saved (v5).</summary>
        public readonly byte[] Surface = new byte[TileCount];

        /// <summary>
        /// Artificial light reaching each tile from trail lights, 0..255 (LightingSystem). Derived from the
        /// light list, never saved: rebuilt after a load.
        /// </summary>
        public readonly byte[] LightLevel = new byte[TileCount];

        /// <summary>Bumped when geometry, terrain type, trees or ownership change (mesh rebuild).</summary>
        public int ShapeVersion;
        /// <summary>Bumped when snow / run paint changes (colour-only refresh).</summary>
        public int SurfaceVersion;

        /// <summary>Number of owned tiles. Chunks with 0 are skipped by per-tile systems.</summary>
        public int OwnedTileCount;

        public Chunk(int chunkX, int chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
        }

        public bool IsSimulated => OwnedTileCount > 0;

        public static int TileIndex(int lx, int ly) => (ly << SizeShift) | lx;
        public static int VertexIndex(int vx, int vy) => vy * VertexStride + vx;

        public void MarkShapeChanged() { ShapeVersion++; SurfaceVersion++; }
        public void MarkSurfaceChanged() { SurfaceVersion++; }
    }
}
