using IceCoast.Simulation;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.View.Iso;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Builds the terrain and tree meshes for one chunk. Slopes are generated directly from corner heights,
    /// so there is no "slope tile art explosion": any combination of corner heights just works.
    ///
    /// Terrain layout: 4 vertices per tile, tiles in row-major order. Colour-only updates rely on this
    /// layout being stable, so BuildColors must loop in the same order as BuildTerrain.
    /// </summary>
    public static class ChunkMeshBuilder
    {
        public static void BuildTerrain(MeshBuffer buffer, GameSimulation sim, Chunk chunk, TerrainColorizer colorizer)
        {
            buffer.Clear();
            var map = sim.Map;
            int x0 = chunk.ChunkX * Chunk.Size, y0 = chunk.ChunkY * Chunk.Size;

            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    int x = x0 + lx, y = y0 + ly;
                    map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
                    Vector3 a = IsoProjection.ToWorld(x, y, h00);          // top
                    Vector3 b = IsoProjection.ToWorld(x + 1, y, h10);      // right
                    Vector3 c = IsoProjection.ToWorld(x + 1, y + 1, h11);  // bottom
                    Vector3 d = IsoProjection.ToWorld(x, y + 1, h01);      // left
                    var color = colorizer.TileColor(x, y);
                    var detail = colorizer.TileDetail(x, y, h00, h10, h01, h11);
                    int first = buffer.VertexCount;
                    AddTile(buffer, sim, x, y, a, b, c, d, h00, h10, h01, h11, color, detail);
                    SetLampAlpha(buffer, first, colorizer, x, y, h00, h10, h01, h11);
                }
            }
        }

        /// <summary>
        /// Trail-light glow rides in the vertex alpha (255 = no lamp light, 0 = full; see IceCoastUnlit.shader),
        /// per corner so it fades smoothly. Corner order matches AddTile's winding.
        /// </summary>
        private static void SetLampAlpha(MeshBuffer buffer, int first, TerrainColorizer colorizer, int x, int y, int h00, int h10, int h01, int h11)
        {
            float la = colorizer.CornerLamp(x, y), lb = colorizer.CornerLamp(x + 1, y);
            float lc = colorizer.CornerLamp(x + 1, y + 1), ld = colorizer.CornerLamp(x, y + 1);
            if (la + lb + lc + ld <= 0f) return;
            bool main = IsoProjection.SplitAlongMainDiagonal(h00, h10, h01, h11);
            SetAlpha(buffer, first + 0, main ? la : lb);
            SetAlpha(buffer, first + 1, main ? lb : lc);
            SetAlpha(buffer, first + 2, main ? lc : ld);
            SetAlpha(buffer, first + 3, main ? ld : la);
        }

        private static void SetAlpha(MeshBuffer buffer, int index, float lamp)
        {
            var c = buffer.Colors[index];
            c.a = LampAlpha(lamp);
            buffer.Colors[index] = c;
        }

        /// <summary>Vertex alpha for an amount of lamp light (0..1).</summary>
        public static byte LampAlpha(float lamp) => (byte)Mathf.Clamp(Mathf.RoundToInt(255f * (1f - lamp)), 0, 255);

        /// <summary>
        /// One terrain tile with its slope normal (for sunlight) and ground texture.
        /// The quad splits along its first->third vertex, so the winding is rotated to pick the flatter diagonal.
        /// Corners: a = (x,y) top, b = (x+1,y) right, c = (x+1,y+1) bottom, d = (x,y+1) left.
        /// </summary>
        public static void AddTile(MeshBuffer buffer, GameSimulation sim, int x, int y, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                   int h00, int h10, int h01, int h11, Color32 color, SurfaceDetail detail)
        {
            var normal = TerrainShading.TileNormal(sim, h00, h10, h01, h11);
            WorldAtlas.TileUvs(detail, x, y, out var uvA, out var uvB, out var uvD, out var uvC);
            if (IsoProjection.SplitAlongMainDiagonal(h00, h10, h01, h11))
                buffer.AddTerrainQuad(a, b, c, d, normal, color, uvA, uvB, uvC, uvD);
            else
                buffer.AddTerrainQuad(b, c, d, a, normal, color, uvB, uvC, uvD, uvA);
        }

        /// <summary>UVs of a tile in the same vertex order AddTile wrote them.</summary>
        private static void AddTileUvs(MeshBuffer buffer, int x, int y, int h00, int h10, int h01, int h11, SurfaceDetail detail)
        {
            WorldAtlas.TileUvs(detail, x, y, out var uvA, out var uvB, out var uvD, out var uvC);
            if (IsoProjection.SplitAlongMainDiagonal(h00, h10, h01, h11))
            {
                buffer.Uvs.Add(uvA); buffer.Uvs.Add(uvB); buffer.Uvs.Add(uvC); buffer.Uvs.Add(uvD);
            }
            else
            {
                buffer.Uvs.Add(uvB); buffer.Uvs.Add(uvC); buffer.Uvs.Add(uvD); buffer.Uvs.Add(uvA);
            }
        }

        /// <summary>
        /// Recomputes colours and ground textures (snow quality changes both), in exactly the vertex order
        /// BuildTerrain produced.
        /// </summary>
        public static void BuildColors(MeshBuffer buffer, GameSimulation sim, Chunk chunk, TerrainColorizer colorizer)
        {
            buffer.Colors.Clear();
            buffer.Uvs.Clear();
            var map = sim.Map;
            int x0 = chunk.ChunkX * Chunk.Size, y0 = chunk.ChunkY * Chunk.Size;
            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    int x = x0 + lx, y = y0 + ly;
                    var color = colorizer.TileColor(x, y);
                    int first = buffer.Colors.Count;
                    buffer.Colors.Add(color); buffer.Colors.Add(color); buffer.Colors.Add(color); buffer.Colors.Add(color);
                    map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
                    AddTileUvs(buffer, x, y, h00, h10, h01, h11, colorizer.TileDetail(x, y, h00, h10, h01, h11));
                    SetLampAlpha(buffer, first, colorizer, x, y, h00, h10, h01, h11);
                }
            }
        }

        /// <summary>
        /// Trees are per-tile density, drawn as 1..4 tree slots per tile (each a tree or a stand of three) at stable, hashed positions
        /// (see <see cref="TreeMesh"/>). A tree has snow on it when its tile has snow on the ground.
        /// </summary>
        public static void BuildTrees(MeshBuffer buffer, GameSimulation sim, Chunk chunk)
        {
            buffer.Clear();
            buffer.IncludeNormals = false; // trees are lit like flat ground; skipping normals saves memory
            var map = sim.Map;
            int x0 = chunk.ChunkX * Chunk.Size, y0 = chunk.ChunkY * Chunk.Size;
            int seed = sim.Config.Map.Seed;
            int bare = sim.Config.Snow.BareDepthCm;
            var owned = Color32Of(255);
            var dim = Palette.DimUnowned(owned);

            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    int i = Chunk.TileIndex(lx, ly);
                    int density = chunk.TreeDensity[i];
                    if (density == 0) continue;
                    int x = x0 + lx, y = y0 + ly;
                    bool isOwned = (chunk.Flags[i] & (byte)TileFlags.Owned) != 0;
                    bool snowy = chunk.SnowDepth[i] >= bare;

                    for (int k = 0; k < density; k++)
                    {
                        TreeMesh.Jitter(x, y, seed, k, out float jx, out float jy, out _);
                        float h = map.SampleHeight(x + jx, y + jy);
                        Vector3 foot = IsoProjection.ToWorld(x + jx, y + jy, h);
                        foot.z -= IsoProjection.DepthPerDiagonal * 0.6f; // in front of its own tile
                        TreeMesh.Pick(x, y, seed, k, h, sim.Config.Terrain, out var kind, out int variant, out bool cluster);
                        TreeMesh.Add(buffer, foot, kind, variant, snowy, cluster, isOwned ? owned : dim);
                    }
                }
            }
        }

        /// <summary>
        /// Swaps every tree between its snowy and bare sprite to match the ground, in exactly the vertex order
        /// BuildTrees produced. Cheap: UVs only.
        /// </summary>
        public static void BuildTreeUvs(MeshBuffer buffer, GameSimulation sim, Chunk chunk)
        {
            buffer.Uvs.Clear();
            var map = sim.Map;
            int x0 = chunk.ChunkX * Chunk.Size, y0 = chunk.ChunkY * Chunk.Size;
            int seed = sim.Config.Map.Seed;
            int bare = sim.Config.Snow.BareDepthCm;
            for (int ly = 0; ly < Chunk.Size; ly++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    int i = Chunk.TileIndex(lx, ly);
                    int density = chunk.TreeDensity[i];
                    if (density == 0) continue;
                    int x = x0 + lx, y = y0 + ly;
                    bool snowy = chunk.SnowDepth[i] >= bare;
                    for (int k = 0; k < density; k++)
                    {
                        TreeMesh.Jitter(x, y, seed, k, out float jx, out float jy, out _);
                        TreeMesh.Pick(x, y, seed, k, map.SampleHeight(x + jx, y + jy), sim.Config.Terrain, out var kind, out int variant, out bool cluster);
                        var cell = TreeMesh.Sprite(kind, variant, snowy, cluster);
                        buffer.Uvs.Add(cell.Uv0);
                        buffer.Uvs.Add(new Vector2(cell.Uv1.x, cell.Uv0.y));
                        buffer.Uvs.Add(new Vector2((cell.Uv0.x + cell.Uv1.x) * 0.5f, cell.Uv1.y));
                    }
                }
        }

        /// <summary>A fingerprint of which tree tiles in a chunk have snow, to know when tree sprites must swap.</summary>
        public static ulong TreeSnowSignature(GameSimulation sim, Chunk chunk)
        {
            int bare = sim.Config.Snow.BareDepthCm;
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < chunk.TreeDensity.Length; i++)
            {
                if (chunk.TreeDensity[i] == 0) continue;
                bool snowy = chunk.SnowDepth[i] >= bare;
                h = (h ^ (ulong)(i * 2 + (snowy ? 1 : 0))) * 1099511628211UL;
            }
            return h;
        }

        private static Color32 Color32Of(byte v) => new Color32(v, v, v, 255);
    }
}
