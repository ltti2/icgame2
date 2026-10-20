using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.World;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws the scenery ring around the playable map: the rest of the mountain range, snow-covered and
    /// forested, so the map edge doesn't look like the end of the world. It is not greyed out; a light
    /// optional haze toward the sky colour adds distance.
    ///
    /// Built once per game in 32x32-tile blocks (budgeted over the first frames). Blocks touching the map are
    /// rebuilt if the map's edge chunks change (terraforming at the edge), because scenery reads the live
    /// map heights along the seam.
    /// </summary>
    public sealed class SceneryRenderer : MonoBehaviour
    {
        [Tooltip("Blocks built per frame while loading.")]
        [SerializeField] private int blocksPerFrame = 10;
        [Tooltip("0 = no haze. Fraction blended toward the sky colour at the outer edge of the scenery.")]
        [Range(0f, 0.6f)]
        [SerializeField] private float maxHaze = 0.15f;
        [Tooltip("Tree slots per scenery tile are capped at this. Keep it at the map's maximum (4) so the forest " +
                 "around the map looks the same as the forest on it, all the way to the edge.")]
        [Range(1, 4)]
        [SerializeField] private int maxTreesPerTile = 4;

        private sealed class Block
        {
            public int X0, Y0;
            public bool TouchesMap;
            public GameObject Root;
            public Mesh Terrain, Trees;
            public bool Built;
        }

        private const int BlockSize = 32;

        private SimulationRunner _runner;
        private GameSimulation _sim;
        private readonly List<Block> _blocks = new List<Block>();
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private int _edgeVersion = -1;

        public void Initialize(SimulationRunner runner)
        {
            _runner = runner;
            _runner.SimulationChanged += Bind;
            WorldAtlas.LayoutChanged += OnAtlasLayoutChanged;
            if (_runner.Sim != null) Bind(_runner.Sim);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.SimulationChanged -= Bind;
            WorldAtlas.LayoutChanged -= OnAtlasLayoutChanged;
            DestroyBlocks();
        }

        private void OnAtlasLayoutChanged()
        {
            if (_sim != null) Bind(_sim);
        }

        private void Bind(GameSimulation sim)
        {
            DestroyBlocks();
            _sim = sim;
            _edgeVersion = -1;
            var scenery = sim.World.Scenery;
            if (scenery == null || scenery.Padding <= 0) return;

            var map = sim.Map;
            // Blocks aligned to the map's chunk grid, covering the whole padded area.
            int bx0 = FloorDiv(scenery.MinX, BlockSize), by0 = FloorDiv(scenery.MinY, BlockSize);
            int bx1 = FloorDiv(scenery.MaxX - 1, BlockSize), by1 = FloorDiv(scenery.MaxY - 1, BlockSize);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    int x0 = bx * BlockSize, y0 = by * BlockSize;
                    bool insideMap = x0 >= 0 && y0 >= 0 && x0 + BlockSize <= map.Width && y0 + BlockSize <= map.Height;
                    if (insideMap) continue;

                    var block = new Block
                    {
                        X0 = x0,
                        Y0 = y0,
                        TouchesMap = x0 <= map.Width && y0 <= map.Height && x0 + BlockSize >= 0 && y0 + BlockSize >= 0,
                        Root = new GameObject($"Scenery {bx},{by}"),
                        Terrain = MeshBuffer.CreateDynamicMesh($"Scenery Terrain {bx},{by}"),
                        Trees = MeshBuffer.CreateDynamicMesh($"Scenery Trees {bx},{by}"),
                    };
                    block.Root.transform.SetParent(transform, false);
                    AddMeshObject(block.Root, "Terrain", block.Terrain);
                    AddMeshObject(block.Root, "Trees", block.Trees);
                    _blocks.Add(block);
                }
            }
        }

        private static void AddMeshObject(GameObject parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void DestroyBlocks()
        {
            foreach (var b in _blocks)
            {
                if (b.Root != null) Destroy(b.Root);
                if (b.Terrain != null) Destroy(b.Terrain);
                if (b.Trees != null) Destroy(b.Trees);
            }
            _blocks.Clear();
        }

        private void LateUpdate()
        {
            if (_sim == null || _blocks.Count == 0) return;

            // Terraforming along the map edge changes the seam; rebuild the blocks that touch the map.
            int edgeVersion = MapEdgeVersion();
            if (edgeVersion != _edgeVersion)
            {
                if (_edgeVersion != -1)
                    foreach (var b in _blocks) if (b.TouchesMap) b.Built = false;
                _edgeVersion = edgeVersion;
            }

            int budget = blocksPerFrame;
            foreach (var block in _blocks)
            {
                if (budget <= 0) break;
                if (block.Built) continue;
                Build(block);
                budget--;
            }
        }

        private int MapEdgeVersion()
        {
            var map = _sim.Map;
            int v = 0;
            for (int cx = 0; cx < map.ChunksX; cx++)
                v += map.GetChunk(cx, 0).ShapeVersion + map.GetChunk(cx, map.ChunksY - 1).ShapeVersion;
            for (int cy = 0; cy < map.ChunksY; cy++)
                v += map.GetChunk(0, cy).ShapeVersion + map.GetChunk(map.ChunksX - 1, cy).ShapeVersion;
            return v;
        }

        private void Build(Block block)
        {
            var map = _sim.Map;
            var scenery = _sim.World.Scenery;
            int seed = _sim.World.MapSeed;

            _buffer.Clear();
            for (int y = block.Y0; y < block.Y0 + BlockSize; y++)
            {
                for (int x = block.X0; x < block.X0 + BlockSize; x++)
                {
                    if (!scenery.IsSceneryTile(x, y)) continue;
                    scenery.GetCorners(map, x, y, out int h00, out int h10, out int h01, out int h11);
                    Vector3 a = IsoProjection.ToWorld(x, y, h00);
                    Vector3 b = IsoProjection.ToWorld(x + 1, y, h10);
                    Vector3 c = IsoProjection.ToWorld(x + 1, y + 1, h11);
                    Vector3 d = IsoProjection.ToWorld(x, y + 1, h01);
                    var color = TileColor(scenery, x, y);
                    var detail = scenery.GetTerrainType(x, y) == TerrainType.Rock ? SurfaceDetail.SnowyRock : SurfaceDetail.Powder;
                    if (scenery.GetSurface(x, y) == SurfaceType.Road)
                    {
                        // The access road runs on out of the map (along x).
                        color = Color32.Lerp(Palette.Paint, Palette.Sky, maxHaze * scenery.DistanceFromMap01(x, y));
                        detail = SurfaceDetail.RoadX;
                    }
                    ChunkMeshBuilder.AddTile(_buffer, _sim, x, y, a, b, c, d, h00, h10, h01, h11, color, detail);
                }
            }
            _buffer.ApplyTo(block.Terrain);

            _buffer.Clear();
            _buffer.IncludeNormals = false;
            for (int y = block.Y0; y < block.Y0 + BlockSize; y++)
            {
                for (int x = block.X0; x < block.X0 + BlockSize; x++)
                {
                    if (!scenery.IsSceneryTile(x, y)) continue;
                    float distance = scenery.DistanceFromMap01(x, y);
                    int density = Mathf.Min(maxTreesPerTile, scenery.GetTreeDensity(x, y));
                    if (density == 0) continue;
                    // Haze as a tint: white (no change) fading toward the sky colour with distance.
                    Color32 tint = Color32.Lerp(new Color32(255, 255, 255, 255), Palette.Sky, maxHaze * distance);
                    for (int k = 0; k < density; k++)
                    {
                        TreeMesh.Jitter(x, y, seed, k, out float jx, out float jy, out _);
                        float h = scenery.SampleHeight(map, x + jx, y + jy);
                        Vector3 foot = IsoProjection.ToWorld(x + jx, y + jy, h);
                        foot.z -= IsoProjection.DepthPerDiagonal * 0.6f;
                        // The scenery is always snow-covered, so its trees always carry snow.
                        TreeMesh.Pick(x, y, seed, k, h, _sim.Config.Terrain, out var kind, out int variant, out bool cluster);
                        TreeMesh.Add(_buffer, foot, kind, variant, true, cluster, tint);
                    }
                }
            }
            _buffer.ApplyTo(block.Trees);
            _buffer.IncludeNormals = true;
            block.Built = true;
        }

        /// <summary>Base colour only: sunlight and slope shading are applied by the shader.</summary>
        private Color32 TileColor(Scenery scenery, int x, int y)
        {
            Color32 c = Palette.Powder;
            if (scenery.GetTerrainType(x, y) == TerrainType.Rock) c = Color32.Lerp(c, Palette.Rock, 0.45f);
            if (((x + y) & 1) == 0) c = Palette.Scale(c, 0.985f);
            return Color32.Lerp(c, Palette.Sky, maxHaze * scenery.DistanceFromMap01(x, y));
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
