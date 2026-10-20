using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.View.CameraControl;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Elevation contour lines, toggled with C or the HUD. Lines every 100 ft, with bolder index lines every
    /// 500 ft (20 m / 100 m in metric): bunched lines = steep, wide spacing = gentle, and the way they bend
    /// shows which way the ground falls.
    ///
    /// Drawn as real line geometry (marching squares over each tile's corner heights) rather than in the
    /// terrain shader, so they can sit just above the forest canopy; on a densely wooded mountain a
    /// surface-only line would be hidden by trees. Terrain in front still hides lines behind it.
    /// Line width is constant on screen, so meshes rebuild (budgeted) when the zoom level changes.
    /// </summary>
    public sealed class ContourRenderer : MonoBehaviour
    {
        [SerializeField] private float minorWidthPx = 1.6f;
        [SerializeField] private float majorWidthPx = 2.6f;
        [SerializeField] private Color32 minorColor = new Color32(56, 72, 100, 255);
        [SerializeField] private Color32 majorColor = new Color32(38, 50, 74, 255);
        [Tooltip("Chunk rebuilds per frame.")]
        [SerializeField] private int chunksPerFrame = 24;

        private const int MajorEvery = 5; // index line every 5th contour (500 ft / 100 m)

        private sealed class ChunkLines
        {
            public Chunk Chunk;
            public Mesh Mesh;
            public int BuiltShape = -1;
            public int BuiltKey = -1;
        }

        private SimulationRunner _runner;
        private ViewState _view;
        private IsoCameraController _camera;
        private GameSimulation _sim;
        private GameObject _root;
        private readonly List<ChunkLines> _chunks = new List<ChunkLines>();
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private readonly List<Vector2> _crossings = new List<Vector2>(4);

        public void Initialize(SimulationRunner runner, ViewState view, IsoCameraController cam)
        {
            _runner = runner;
            _view = view;
            _camera = cam;
            _runner.SimulationChanged += Bind;
            if (_runner.Sim != null) Bind(_runner.Sim);
        }

        private void OnDestroy()
        {
            if (_runner != null) _runner.SimulationChanged -= Bind;
            Clear();
        }

        private void Bind(GameSimulation sim)
        {
            Clear();
            _sim = sim;
            _root = new GameObject("Contour Lines");
            _root.transform.SetParent(transform, false);
            foreach (var chunk in sim.Map.Chunks)
            {
                var go = new GameObject($"Contours {chunk.ChunkX},{chunk.ChunkY}");
                go.transform.SetParent(_root.transform, false);
                var mesh = MeshBuffer.CreateDynamicMesh(go.name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = RenderMaterials.Lines;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _chunks.Add(new ChunkLines { Chunk = chunk, Mesh = mesh });
            }
            _root.SetActive(false);
        }

        private void Clear()
        {
            foreach (var c in _chunks) if (c.Mesh != null) Destroy(c.Mesh);
            _chunks.Clear();
            if (_root != null) Destroy(_root);
            _root = null;
        }

        private void LateUpdate()
        {
            if (_sim == null || _root == null) return;
            bool show = _view.ShowContours;
            if (_root.activeSelf != show) _root.SetActive(show);
            if (!show) return;

            // Anything that changes line spacing or width invalidates every chunk.
            int key = ((int)_sim.Units.System * 1000) + Mathf.RoundToInt(_camera.Zoom * 100f);
            int budget = chunksPerFrame;
            foreach (var c in _chunks)
            {
                if (budget <= 0) break;
                if (c.BuiltShape == c.Chunk.ShapeVersion && c.BuiltKey == key) continue;
                Build(c);
                c.BuiltShape = c.Chunk.ShapeVersion;
                c.BuiltKey = key;
                budget--;
            }
        }

        private void Build(ChunkLines c)
        {
            var map = _sim.Map;
            float metersPerStep = _sim.TerrainScale.MetersPerStep;
            bool imperial = _sim.Units.System == UnitSystem.Imperial;
            float intervalMeters = imperial ? 100f / UnitFormatter.FeetPerMeter : 20f;
            float interval = intervalMeters / metersPerStep; // in height steps

            float pxToWorld = 1f / (IsoProjection.PixelsPerUnit * _camera.Zoom);
            float minorWidth = minorWidthPx * pxToWorld, majorWidth = majorWidthPx * pxToWorld;

            _buffer.Clear();
            int x0 = c.Chunk.ChunkX * Chunk.Size, y0 = c.Chunk.ChunkY * Chunk.Size;
            for (int y = y0; y < y0 + Chunk.Size; y++)
            {
                for (int x = x0; x < x0 + Chunk.Size; x++)
                {
                    map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
                    int lo = Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h01, h11));
                    int hi = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
                    if (lo == hi) continue;

                    for (int k = Mathf.CeilToInt(lo / interval); k * interval <= hi; k++)
                    {
                        float level = k * interval;
                        if (level <= lo) continue;
                        bool major = k % MajorEvery == 0;
                        AddLevel(x, y, h00, h10, h11, h01, level, major ? majorWidth : minorWidth, major ? majorColor : minorColor);
                    }
                }
            }
            _buffer.ApplyTo(c.Mesh);
        }

        /// <summary>Marching squares for one tile and one contour level.</summary>
        private void AddLevel(int x, int y, int h00, int h10, int h11, int h01, float level, float width, Color32 color)
        {
            _crossings.Clear();
            Cross(x, y, h00, x + 1, y, h10, level);           // top edge
            Cross(x + 1, y, h10, x + 1, y + 1, h11, level);   // right edge
            Cross(x + 1, y + 1, h11, x, y + 1, h01, level);   // bottom edge
            Cross(x, y + 1, h01, x, y, h00, level);           // left edge

            if (_crossings.Count == 2) Segment(_crossings[0], _crossings[1], level, width, color);
            else if (_crossings.Count == 4)
            {
                // Saddle: decide the pairing from the tile centre.
                bool centreAbove = (h00 + h10 + h11 + h01) * 0.25f >= level;
                bool topLeftAbove = h00 >= level;
                if (centreAbove == topLeftAbove)
                {
                    Segment(_crossings[0], _crossings[1], level, width, color);
                    Segment(_crossings[2], _crossings[3], level, width, color);
                }
                else
                {
                    Segment(_crossings[0], _crossings[3], level, width, color);
                    Segment(_crossings[1], _crossings[2], level, width, color);
                }
            }
        }

        private void Cross(int ax, int ay, int ha, int bx, int by, int hb, float level)
        {
            if ((ha >= level) == (hb >= level)) return;
            float t = (level - ha) / (hb - ha);
            _crossings.Add(new Vector2(ax + (bx - ax) * t, ay + (by - ay) * t));
        }

        private void Segment(Vector2 a, Vector2 b, float level, float width, Color32 color)
        {
            Vector3 pa = IsoProjection.ToWorld(a.x, a.y, level);
            Vector3 pb = IsoProjection.ToWorld(b.x, b.y, level);
            // Just in front of the trees on this tile (they are biased 0.6 diagonals), behind anything nearer.
            pa.z -= IsoProjection.DepthPerDiagonal * 1.0f;
            pb.z -= IsoProjection.DepthPerDiagonal * 1.0f;
            _buffer.AddLine(pa, pb, width, color);
        }
    }
}
