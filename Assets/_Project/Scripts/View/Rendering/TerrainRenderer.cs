using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.World;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// One GameObject per chunk with a terrain mesh and a tree mesh. Each frame, chunks whose simulation
    /// version counters moved are rebuilt, within a per-frame budget, visible chunks first.
    /// Unity's renderer culls off-screen chunks by their bounds.
    /// </summary>
    public sealed class TerrainRenderer : MonoBehaviour
    {
        [Tooltip("Work units per frame. A full rebuild costs 4, a colour refresh 1.")]
        [SerializeField] private int frameBudget = 48;

        private sealed class ChunkView
        {
            public Chunk Chunk;
            public GameObject Root;
            public Mesh TerrainMesh;
            public Mesh TreeMesh;
            public MeshRenderer TerrainRenderer;
            public int BuiltShape = -1;
            public int BuiltSurface = -1;
            public int BuiltEpoch = -1;
            /// <summary>Which tree tiles had snow when the tree sprites were last chosen.</summary>
            public ulong TreeSnow;
        }

        private SimulationRunner _runner;
        private ViewState _view;
        private GameSimulation _sim;
        private TerrainColorizer _colorizer;
        private readonly List<ChunkView> _chunks = new List<ChunkView>();
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private Camera _camera;
        private int _lastRunsVersion = -1;
        private int _lastParcelsVersion = -1;

        public void Initialize(SimulationRunner runner, ViewState view, Camera cam)
        {
            _runner = runner;
            _view = view;
            _camera = cam;
            _runner.SimulationChanged += Bind;
            WorldAtlas.LayoutChanged += OnAtlasLayoutChanged;
            if (_runner.Sim != null) Bind(_runner.Sim);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.SimulationChanged -= Bind;
            WorldAtlas.LayoutChanged -= OnAtlasLayoutChanged;
            DestroyChunks();
        }

        /// <summary>Edited tree sprites changed size: rebuild every chunk with the new UVs.</summary>
        private void OnAtlasLayoutChanged()
        {
            if (_sim != null) Bind(_sim);
        }

        private void Bind(GameSimulation sim)
        {
            DestroyChunks();
            _sim = sim;
            _colorizer = new TerrainColorizer(sim, _view);
            _lastRunsVersion = -1;

            foreach (var chunk in sim.Map.Chunks)
            {
                var root = new GameObject($"Chunk {chunk.ChunkX},{chunk.ChunkY}");
                root.transform.SetParent(transform, false);

                var view = new ChunkView
                {
                    Chunk = chunk,
                    Root = root,
                    TerrainMesh = MeshBuffer.CreateDynamicMesh($"Terrain {chunk.ChunkX},{chunk.ChunkY}"),
                    TreeMesh = MeshBuffer.CreateDynamicMesh($"Trees {chunk.ChunkX},{chunk.ChunkY}"),
                };
                view.TerrainRenderer = AddMeshObject(root, "Terrain", view.TerrainMesh);
                AddMeshObject(root, "Trees", view.TreeMesh);
                _chunks.Add(view);
            }
        }

        private static MeshRenderer AddMeshObject(GameObject parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        private void DestroyChunks()
        {
            foreach (var c in _chunks)
            {
                if (c.Root != null) Destroy(c.Root);
                if (c.TerrainMesh != null) Destroy(c.TerrainMesh);
                if (c.TreeMesh != null) Destroy(c.TreeMesh);
            }
            _chunks.Clear();
        }

        private void LateUpdate()
        {
            if (_sim == null) return;

            // Run edits and land purchases change colours across many chunks.
            if (_sim.World.RunsVersion != _lastRunsVersion || _sim.World.ParcelsVersion != _lastParcelsVersion)
            {
                _lastRunsVersion = _sim.World.RunsVersion;
                _lastParcelsVersion = _sim.World.ParcelsVersion;
                _view.InvalidateColors();
            }

            _colorizer.BeginPass();
            int budget = frameBudget;
            var planes = GeometryUtility.CalculateFrustumPlanes(_camera);

            // Pass 1: visible chunks. Pass 2: everything else with what is left.
            for (int pass = 0; pass < 2 && budget > 0; pass++)
            {
                foreach (var cv in _chunks)
                {
                    if (budget <= 0) break;
                    bool built = cv.BuiltShape >= 0;
                    bool visible = !built || GeometryUtility.TestPlanesAABB(planes, cv.TerrainRenderer.bounds);
                    if ((pass == 0) != visible) continue;
                    budget -= Refresh(cv);
                }
            }
        }

        /// <summary>Brings one chunk up to date. Returns the work cost spent.</summary>
        private int Refresh(ChunkView cv)
        {
            var chunk = cv.Chunk;
            if (cv.BuiltShape != chunk.ShapeVersion)
            {
                ChunkMeshBuilder.BuildTerrain(_buffer, _sim, chunk, _colorizer);
                _buffer.ApplyTo(cv.TerrainMesh);
                ChunkMeshBuilder.BuildTrees(_buffer, _sim, chunk);
                _buffer.ApplyTo(cv.TreeMesh);
                cv.TreeSnow = ChunkMeshBuilder.TreeSnowSignature(_sim, chunk);
                cv.BuiltShape = chunk.ShapeVersion;
                cv.BuiltSurface = chunk.SurfaceVersion;
                cv.BuiltEpoch = _view.ColorEpoch;
                return 4;
            }
            if (cv.BuiltSurface != chunk.SurfaceVersion || cv.BuiltEpoch != _view.ColorEpoch)
            {
                ChunkMeshBuilder.BuildColors(_buffer, _sim, chunk, _colorizer);
                _buffer.ApplyColorsAndUvs(cv.TerrainMesh);
                cv.BuiltSurface = chunk.SurfaceVersion;
                cv.BuiltEpoch = _view.ColorEpoch;

                // Snow arrived on (or melted off) some tree tiles: swap those trees' sprites.
                ulong treeSnow = ChunkMeshBuilder.TreeSnowSignature(_sim, chunk);
                if (treeSnow != cv.TreeSnow)
                {
                    cv.TreeSnow = treeSnow;
                    ChunkMeshBuilder.BuildTreeUvs(_buffer, _sim, chunk);
                    _buffer.ApplyUvsOnly(cv.TreeMesh);
                    return 2;
                }
                return 1;
            }
            return 0;
        }

        /// <summary>Forces every chunk to rebuild (e.g. after changing the projection or palette in the editor).</summary>
        public void RebuildAll()
        {
            foreach (var c in _chunks) c.BuiltShape = -1;
        }
    }
}
