using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.World;
using IceCoast.View.Input;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.CameraControl
{
    /// <summary>
    /// Fixed isometric camera: pan (WASD / arrows / middle-drag / screen edges) and zoom (mouse wheel)
    /// in discrete steps. Zoom levels are whole or half multiples of the art scale so pixel art stays
    /// crisp (zoom 1 = one texel per screen pixel). No rotation, by design.
    ///
    /// The centre of the screen can't leave the map (owned land or not), and zoom stops before the
    /// scenery ring around the map stops filling the screen, so the edge of the world is never seen.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsoCameraController : MonoBehaviour
    {
        public static readonly float[] ZoomLevels = { 0.125f, 0.25f, 0.5f, 1f, 2f, 3f, 4f };

        [SerializeField] private float panSpeedScreensPerSecond = 0.9f;
        [SerializeField] private bool edgeScrolling = false;
        [SerializeField] private float edgeScrollMarginPx = 12f;
        [SerializeField] private int zoomIndex = 3;

        private Camera _camera;
        private SimulationRunner _runner;
        private ViewState _view;
        private Vector3 _dragOriginWorld;
        private bool _dragging;
        private int _lastScreenHeight;

        public float Zoom => ZoomLevels[zoomIndex];
        public bool EdgeScrolling { get => edgeScrolling; set => edgeScrolling = value; }
        /// <summary>Keyboard and edge pan speed in screen heights per second.</summary>
        public float PanSpeed { get => panSpeedScreensPerSecond; set => panSpeedScreensPerSecond = Mathf.Max(0.05f, value); }
        public const float DefaultPanSpeed = 0.9f;

        /// <summary>The part of the world the camera currently shows (world units).</summary>
        public Rect ViewWorldRect
        {
            get
            {
                if (_camera == null) return default;
                float h = _camera.orthographicSize * 2f;
                float w = h * _camera.aspect;
                var p = transform.position;
                return new Rect(p.x - w * 0.5f, p.y - h * 0.5f, w, h);
            }
        }

        /// <summary>Moves the camera so <paramref name="world"/> is in the centre of the screen (minimap clicks).</summary>
        public void CenterOn(Vector2 world)
        {
            transform.position = new Vector3(world.x, world.y, -100f);
            ClampToMap();
        }

        public void Initialize(SimulationRunner runner, ViewState view)
        {
            _camera = GetComponent<Camera>();
            _runner = runner;
            _view = view;
            _camera.orthographic = true;
            _camera.transform.rotation = Quaternion.identity;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
            ApplyZoom();
            _runner.SimulationChanged += sim => FocusOnTile(sim.Map, sim.World.ResortEntrance.X - 12, sim.World.ResortEntrance.Y - 12);
        }

        public void FocusOnTile(WorldMap map, int x, int y)
        {
            var p = IsoProjection.TileCenter(map, Mathf.Clamp(x, 0, map.Width - 1), Mathf.Clamp(y, 0, map.Height - 1));
            transform.position = new Vector3(p.x, p.y, -100f);
            ClampToMap();
        }

        private void Update()
        {
            if (_camera == null) return;
            if (Screen.height != _lastScreenHeight) ApplyZoom(); // window resized
            HandleZoom();
            HandleKeyboardPan();
            HandleDragPan();
            HandleEdgePan();
            ClampToMap();
        }

        private void HandleZoom()
        {
            if (_view.PointerOverUi) return;
            float scroll = GameInput.ScrollDelta;
            if (scroll == 0f) return;

            // Zoom toward the cursor: keep the world point under the mouse fixed.
            Vector3 before = _camera.ScreenToWorldPoint(GameInput.MousePosition);
            zoomIndex = Mathf.Clamp(zoomIndex + (scroll > 0 ? 1 : -1), MinZoomIndex(), ZoomLevels.Length - 1);
            ApplyZoom();
            Vector3 after = _camera.ScreenToWorldPoint(GameInput.MousePosition);
            transform.position += new Vector3(before.x - after.x, before.y - after.y, 0f);
        }

        private void ApplyZoom()
        {
            // orthographicSize is half the view height in world units.
            _lastScreenHeight = Screen.height;
            _camera.orthographicSize = Screen.height / (2f * IsoProjection.PixelsPerUnit * Zoom);
        }

        private void HandleKeyboardPan()
        {
            Vector2 dir = Vector2.zero;
            if (GameInput.Held(GameKey.PanUp)) dir.y += 1;
            if (GameInput.Held(GameKey.PanDown)) dir.y -= 1;
            if (GameInput.Held(GameKey.PanLeft)) dir.x -= 1;
            if (GameInput.Held(GameKey.PanRight)) dir.x += 1;
            if (dir == Vector2.zero) return;
            Pan(dir.normalized * (GameInput.Held(GameKey.Modifier) ? 2.5f : 1f));
        }

        private void HandleEdgePan()
        {
            if (!edgeScrolling || !Application.isFocused) return;
            Vector2 m = GameInput.MousePosition;
            Vector2 dir = Vector2.zero;
            if (m.x <= edgeScrollMarginPx) dir.x -= 1;
            if (m.x >= Screen.width - edgeScrollMarginPx) dir.x += 1;
            if (m.y <= edgeScrollMarginPx) dir.y -= 1;
            if (m.y >= Screen.height - edgeScrollMarginPx) dir.y += 1;
            if (dir != Vector2.zero) Pan(dir.normalized);
        }

        private void Pan(Vector2 dir)
        {
            float viewHeight = _camera.orthographicSize * 2f;
            Vector2 delta = dir * viewHeight * panSpeedScreensPerSecond * Time.unscaledDeltaTime;
            transform.position += new Vector3(delta.x, delta.y, 0f);
        }

        private void HandleDragPan()
        {
            Vector3 mouseWorld = _camera.ScreenToWorldPoint(GameInput.MousePosition);
            if (GameInput.MiddleHeld)
            {
                if (!_dragging)
                {
                    _dragging = true;
                    _dragOriginWorld = mouseWorld;
                }
                else
                {
                    transform.position += new Vector3(_dragOriginWorld.x - mouseWorld.x, _dragOriginWorld.y - mouseWorld.y, 0f);
                }
            }
            else _dragging = false;
        }

        // ---------- Keeping the map in view ----------

        // The playable map's outline as seen on screen: its four edges projected at their real heights
        // (the summit corner sits much higher than the base corner). Rebuilt when the edge is terraformed.
        private readonly List<Vector2> _outline = new List<Vector2>();
        private object _outlineSim;
        private int _outlineVersion = -1;

        /// <summary>
        /// Keeps the centre of the screen over the map (owned or not). Scenery fills the rest of the screen,
        /// so the edge of the world is never in view.
        /// </summary>
        private void ClampToMap()
        {
            var sim = _runner?.Sim;
            if (sim == null) return;
            EnsureZoomCoveredByScenery();
            RebuildOutlineIfNeeded(sim);
            if (_outline.Count < 3) return;

            var p = transform.position;
            var c = new Vector2(p.x, p.y);
            if (InsidePolygon(_outline, c)) return;
            c = NearestOnPolygon(_outline, c);
            transform.position = new Vector3(c.x, c.y, p.z);
        }

        private void RebuildOutlineIfNeeded(GameSimulation sim)
        {
            var map = sim.Map;
            int version = EdgeShapeVersion(map);
            if (ReferenceEquals(_outlineSim, sim) && version == _outlineVersion) return;
            _outlineSim = sim;
            _outlineVersion = version;
            _outline.Clear();

            int w = map.Width, h = map.Height;
            // Clockwise from the top corner (0,0): along +x, down +y, back along -x, up -y.
            for (int x = 0; x < w; x++) _outline.Add(Corner(map, x, 0));
            for (int y = 0; y < h; y++) _outline.Add(Corner(map, w, y));
            for (int x = w; x > 0; x--) _outline.Add(Corner(map, x, h));
            for (int y = h; y > 0; y--) _outline.Add(Corner(map, 0, y));
        }

        private static Vector2 Corner(WorldMap map, int vx, int vy)
        {
            var v = IsoProjection.ToWorld(vx, vy, map.GetVertexHeight(vx, vy));
            return new Vector2(v.x, v.y);
        }

        private static int EdgeShapeVersion(WorldMap map)
        {
            int v = 0;
            for (int cx = 0; cx < map.ChunksX; cx++)
                v += map.GetChunk(cx, 0).ShapeVersion + map.GetChunk(cx, map.ChunksY - 1).ShapeVersion;
            for (int cy = 0; cy < map.ChunksY; cy++)
                v += map.GetChunk(0, cy).ShapeVersion + map.GetChunk(map.ChunksX - 1, cy).ShapeVersion;
            return v;
        }

        private static bool InsidePolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static Vector2 NearestOnPolygon(List<Vector2> poly, Vector2 p)
        {
            Vector2 best = poly[0];
            float bestD = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[j], ab = poly[i] - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                Vector2 q = a + ab * t;
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        /// <summary>
        /// With the screen centre on the map edge, half a screen of scenery must lie beyond it. Scenery reaches
        /// at least Padding tiles past every edge, which is Padding world units vertically and twice that
        /// horizontally, so zoom levels that would show more than that are skipped.
        /// </summary>
        private void EnsureZoomCoveredByScenery()
        {
            int minIndex = MinZoomIndex();
            if (zoomIndex >= minIndex) return;
            zoomIndex = minIndex;
            ApplyZoom();
        }

        private int MinZoomIndex()
        {
            var scenery = _runner?.Sim?.World.Scenery;
            if (scenery == null || scenery.Padding <= 0) return ZoomLevels.Length - 1;
            float reach = scenery.Padding * 0.92f; // a little margin for the haze at the very edge
            for (int i = 0; i < ZoomLevels.Length; i++)
            {
                float halfH = Screen.height / (2f * IsoProjection.PixelsPerUnit * ZoomLevels[i]);
                float halfW = halfH * Screen.width / Mathf.Max(1f, Screen.height);
                if (halfH <= reach && halfW <= reach * 2f) return i;
            }
            return ZoomLevels.Length - 1;
        }

        /// <summary>World point under the mouse on the camera plane (before height-aware picking).</summary>
        public Vector2 MouseWorld => _camera.ScreenToWorldPoint(GameInput.MousePosition);
    }
}
