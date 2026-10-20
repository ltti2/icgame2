using System;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.World
{
    /// <summary>A resolved tile location: the chunk that owns it plus the index into its arrays.</summary>
    public readonly struct TileRef
    {
        public readonly Chunk Chunk;
        public readonly int Index;

        public TileRef(Chunk chunk, int index)
        {
            Chunk = chunk;
            Index = index;
        }
    }

    /// <summary>
    /// The tile grid. Owns the chunk array and provides coordinate helpers.
    /// Systems that touch many tiles should resolve a <see cref="TileRef"/> once and index the
    /// chunk arrays directly; the convenience getters are for occasional queries.
    /// </summary>
    public sealed class WorldMap
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int ChunksX;
        public readonly int ChunksY;
        public readonly Chunk[] Chunks;

        public WorldMap(int width, int height)
        {
            if (width % Chunk.Size != 0 || height % Chunk.Size != 0)
                throw new ArgumentException($"Map size must be a multiple of {Chunk.Size}");
            Width = width;
            Height = height;
            ChunksX = width / Chunk.Size;
            ChunksY = height / Chunk.Size;
            Chunks = new Chunk[ChunksX * ChunksY];
            for (int cy = 0; cy < ChunksY; cy++)
                for (int cx = 0; cx < ChunksX; cx++)
                    Chunks[cy * ChunksX + cx] = new Chunk(cx, cy);
        }

        // ---------- Coordinates ----------

        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public bool InBounds(Int2 p) => InBounds(p.X, p.Y);
        public bool VertexInBounds(int vx, int vy) => (uint)vx <= (uint)Width && (uint)vy <= (uint)Height;

        /// <summary>Unique integer key for a tile, handy for hash sets and flat lookups.</summary>
        public int TileKey(int x, int y) => y * Width + x;
        public Int2 FromTileKey(int key) => new Int2(key % Width, key / Width);

        public Chunk GetChunk(int cx, int cy) => Chunks[cy * ChunksX + cx];
        public Chunk ChunkAtTile(int x, int y) => Chunks[(y >> Chunk.SizeShift) * ChunksX + (x >> Chunk.SizeShift)];

        public TileRef Locate(int x, int y)
        {
            var chunk = Chunks[(y >> Chunk.SizeShift) * ChunksX + (x >> Chunk.SizeShift)];
            return new TileRef(chunk, Chunk.TileIndex(x & Chunk.SizeMask, y & Chunk.SizeMask));
        }

        public TileRef Locate(Int2 p) => Locate(p.X, p.Y);

        // ---------- Heights (vertices) ----------

        /// <summary>Height of grid corner (vx, vy) in steps. Clamps out-of-range coordinates to the edge.</summary>
        public int GetVertexHeight(int vx, int vy)
        {
            vx = SimMath.Clamp(vx, 0, Width);
            vy = SimMath.Clamp(vy, 0, Height);
            int cx = Math.Min(vx >> Chunk.SizeShift, ChunksX - 1);
            int cy = Math.Min(vy >> Chunk.SizeShift, ChunksY - 1);
            var chunk = Chunks[cy * ChunksX + cx];
            return chunk.VertexHeight[Chunk.VertexIndex(vx - cx * Chunk.Size, vy - cy * Chunk.Size)];
        }

        /// <summary>
        /// Writes a vertex height into every chunk that stores a copy of it (up to 4 on chunk corners)
        /// and marks those chunks as changed.
        /// </summary>
        public void SetVertexHeight(int vx, int vy, int height)
        {
            if (!VertexInBounds(vx, vy)) return;
            short h = (short)SimMath.Clamp(height, short.MinValue, short.MaxValue);

            int cxHi = Math.Min(vx >> Chunk.SizeShift, ChunksX - 1);
            int cyHi = Math.Min(vy >> Chunk.SizeShift, ChunksY - 1);
            // A vertex on a chunk boundary also lives in the chunk to its left / above.
            int cxLo = (vx & Chunk.SizeMask) == 0 && vx > 0 ? cxHi - 1 : cxHi;
            int cyLo = (vy & Chunk.SizeMask) == 0 && vy > 0 ? cyHi - 1 : cyHi;
            if (vx == Width) cxLo = cxHi;
            if (vy == Height) cyLo = cyHi;

            for (int cy = cyLo; cy <= cyHi; cy++)
            {
                for (int cx = cxLo; cx <= cxHi; cx++)
                {
                    var chunk = Chunks[cy * ChunksX + cx];
                    int lx = vx - cx * Chunk.Size;
                    int ly = vy - cy * Chunk.Size;
                    if ((uint)lx > Chunk.Size || (uint)ly > Chunk.Size) continue;
                    chunk.VertexHeight[Chunk.VertexIndex(lx, ly)] = h;
                    chunk.MarkShapeChanged();
                }
            }
        }

        /// <summary>The four corner heights of tile (x, y): h00=(x,y) h10=(x+1,y) h01=(x,y+1) h11=(x+1,y+1).</summary>
        public void GetCorners(int x, int y, out int h00, out int h10, out int h01, out int h11)
        {
            var chunk = ChunkAtTile(x, y);
            int lx = x & Chunk.SizeMask, ly = y & Chunk.SizeMask;
            var vh = chunk.VertexHeight;
            int i = Chunk.VertexIndex(lx, ly);
            h00 = vh[i];
            h10 = vh[i + 1];
            h01 = vh[i + Chunk.VertexStride];
            h11 = vh[i + Chunk.VertexStride + 1];
        }

        public float TileCenterHeight(int x, int y)
        {
            GetCorners(x, y, out int a, out int b, out int c, out int d);
            return (a + b + c + d) * 0.25f;
        }

        /// <summary>Bilinear height at a continuous grid position (tile units). Used for rendering guests on slopes.</summary>
        public float SampleHeight(float fx, float fy)
        {
            fx = SimMath.Clamp(fx, 0f, Width - 0.001f);
            fy = SimMath.Clamp(fy, 0f, Height - 0.001f);
            int x = (int)fx, y = (int)fy;
            float tx = fx - x, ty = fy - y;
            GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
            float top = h00 + (h10 - h00) * tx;
            float bottom = h01 + (h11 - h01) * tx;
            return top + (bottom - top) * ty;
        }

        // ---------- Tile convenience accessors ----------

        public TileFlags GetFlags(int x, int y)
        {
            var t = Locate(x, y);
            return (TileFlags)t.Chunk.Flags[t.Index];
        }

        public bool HasFlag(int x, int y, TileFlags flag) => (GetFlags(x, y) & flag) != 0;
        public bool IsOwned(int x, int y) => InBounds(x, y) && HasFlag(x, y, TileFlags.Owned);
        public bool IsOwned(Int2 p) => IsOwned(p.X, p.Y);

        public void SetOwned(int x, int y, bool owned)
        {
            var t = Locate(x, y);
            byte f = t.Chunk.Flags[t.Index];
            bool was = (f & (byte)TileFlags.Owned) != 0;
            if (was == owned) return;
            t.Chunk.Flags[t.Index] = owned ? (byte)(f | (byte)TileFlags.Owned) : (byte)(f & ~(byte)TileFlags.Owned);
            t.Chunk.OwnedTileCount += owned ? 1 : -1;
            t.Chunk.MarkShapeChanged();
        }

        public void SetFlag(int x, int y, TileFlags flag, bool on)
        {
            var t = Locate(x, y);
            byte f = t.Chunk.Flags[t.Index];
            t.Chunk.Flags[t.Index] = on ? (byte)(f | (byte)flag) : (byte)(f & ~(byte)flag);
            t.Chunk.MarkSurfaceChanged();
        }

        public int GetTreeDensity(int x, int y)
        {
            var t = Locate(x, y);
            return t.Chunk.TreeDensity[t.Index];
        }

        public void SetTreeDensity(int x, int y, int density)
        {
            var t = Locate(x, y);
            t.Chunk.TreeDensity[t.Index] = (byte)SimMath.Clamp(density, 0, 255);
            t.Chunk.MarkShapeChanged();
        }

        public ushort GetOccupant(int x, int y)
        {
            var t = Locate(x, y);
            return t.Chunk.OccupantId[t.Index];
        }

        public void SetOccupant(int x, int y, ushort occupant)
        {
            var t = Locate(x, y);
            t.Chunk.OccupantId[t.Index] = occupant;
            t.Chunk.MarkShapeChanged();
        }

        public int GetRunId(int x, int y)
        {
            var t = Locate(x, y);
            return t.Chunk.RunId[t.Index];
        }

        public void SetRunId(int x, int y, int runId)
        {
            var t = Locate(x, y);
            t.Chunk.RunId[t.Index] = (ushort)runId;
            t.Chunk.MarkSurfaceChanged();
        }

        public SurfaceType GetSurface(int x, int y)
        {
            var t = Locate(x, y);
            return (SurfaceType)t.Chunk.Surface[t.Index];
        }

        public SurfaceType GetSurface(Int2 p) => GetSurface(p.X, p.Y);

        public void SetSurface(int x, int y, SurfaceType surface)
        {
            var t = Locate(x, y);
            if (t.Chunk.Surface[t.Index] == (byte)surface) return;
            t.Chunk.Surface[t.Index] = (byte)surface;
            t.Chunk.MarkSurfaceChanged();
        }

        /// <summary>Guests walk here without skis: walking areas and parking lots.</summary>
        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var s = GetSurface(x, y);
            return s == SurfaceType.WalkingArea || s == SurfaceType.Parking;
        }

        /// <summary>Roads and parking lots: plowed, paved, and off limits to trails and lift structures.</summary>
        public bool IsPaved(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var s = GetSurface(x, y);
            return s == SurfaceType.Road || s == SurfaceType.Parking;
        }

        public int GetLightLevel(int x, int y)
        {
            var t = Locate(x, y);
            return t.Chunk.LightLevel[t.Index];
        }

        public TerrainType GetTerrainType(int x, int y)
        {
            var t = Locate(x, y);
            return (TerrainType)t.Chunk.TerrainType[t.Index];
        }

        public SnowQuality GetSnowQuality(int x, int y)
        {
            var t = Locate(x, y);
            return (SnowQuality)t.Chunk.SnowQuality[t.Index];
        }

        public int GetSnowDepth(int x, int y)
        {
            var t = Locate(x, y);
            return t.Chunk.SnowDepth[t.Index];
        }
    }
}
