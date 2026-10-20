using IceCoast.Simulation;
using IceCoast.Simulation.World;
using IceCoast.View.Iso;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// A small isometric picture of the whole map for the overlay panel, coloured by the current overlay
    /// (so switching overlays recolours the minimap too, as in Crusader Kings II).
    ///
    /// Built in the same projection as the main view, so the camera's view is simply a rectangle on it and
    /// clicking a point maps straight back to world space. Tiles are painted back to front (painter's
    /// algorithm, so nearer ridges hide what is behind them) a slice at a time across several frames into a
    /// back buffer, then uploaded in one go: no frame hitch even on a 512 x 512 map.
    /// </summary>
    public sealed class Minimap
    {
        private const int TextureWidth = 320;
        private const int TilesPerFrame = 25000;
        private const float MinRebuildInterval = 0.75f;
        private const float TemperatureRefreshInterval = 4f;

        private static readonly Color32 Background = new Color32(40, 48, 64, 255);
        private static readonly Color32 LiftColor = new Color32(20, 20, 24, 255);

        public Texture2D Texture { get; private set; }
        /// <summary>World-space rectangle the texture covers.</summary>
        public Rect WorldBounds { get; private set; }
        public bool Ready { get; private set; }

        private GameSimulation _sim;
        private ViewState _view;
        private TerrainColorizer _colorizer;
        private Color32[] _back;
        private int _w, _h;
        private float _unitsPerPixel;

        // Progressive build state
        private bool _building;
        private int _diag, _x;
        private float _lastBuildStart = -999f;
        private int _builtSignature;
        private OverlayMode _builtOverlay;
        private bool _dirty = true;

        public void SetSimulation(GameSimulation sim, ViewState view)
        {
            _sim = sim;
            _view = view;
            _colorizer = new TerrainColorizer(sim, view);
            _building = false;
            _dirty = true;
            Ready = false;
            SetUpBounds();
        }

        private void SetUpBounds()
        {
            var map = _sim.Map;
            int minH = int.MaxValue, maxH = int.MinValue;
            for (int vy = 0; vy <= map.Height; vy += 2)
                for (int vx = 0; vx <= map.Width; vx += 2)
                {
                    int h = map.GetVertexHeight(vx, vy);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            maxH += 200; // room for terraforming up

            float minX = -map.Height * IsoProjection.HalfWidth;
            float maxX = map.Width * IsoProjection.HalfWidth;
            float maxY = maxH * IsoProjection.StepHeight;
            float minY = -(map.Width + map.Height) * IsoProjection.HalfHeight + minH * IsoProjection.StepHeight;
            WorldBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);

            _w = TextureWidth;
            _unitsPerPixel = (maxX - minX) / _w;
            _h = Mathf.Clamp(Mathf.CeilToInt((maxY - minY) / _unitsPerPixel), 16, 1024);
            if (Texture == null || Texture.width != _w || Texture.height != _h)
            {
                if (Texture != null) Object.Destroy(Texture);
                Texture = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
                {
                    name = "IceCoast Minimap",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }
            _back = new Color32[_w * _h];
        }

        /// <summary>World point to minimap UV (0..1, bottom-left origin).</summary>
        public Vector2 WorldToUv(Vector2 world)
            => new Vector2((world.x - WorldBounds.xMin) / WorldBounds.width, (world.y - WorldBounds.yMin) / WorldBounds.height);

        public Vector2 UvToWorld(Vector2 uv)
            => new Vector2(WorldBounds.xMin + uv.x * WorldBounds.width, WorldBounds.yMin + uv.y * WorldBounds.height);

        /// <summary>Call once per frame. Continues an in-progress build or starts one when the map changed.</summary>
        public void Update()
        {
            if (_sim == null) return;
            int signature = Signature();
            if (signature != _builtSignature || _view.Overlay != _builtOverlay) _dirty = true;
            if (_view.Overlay == OverlayMode.Temperature && Time.unscaledTime - _lastBuildStart > TemperatureRefreshInterval) _dirty = true;

            if (!_building && _dirty && Time.unscaledTime - _lastBuildStart >= MinRebuildInterval)
            {
                _building = true;
                _dirty = false;
                _diag = 0;
                _x = 0;
                _lastBuildStart = Time.unscaledTime;
                _builtSignature = signature;
                _builtOverlay = _view.Overlay;
                for (int i = 0; i < _back.Length; i++) _back[i] = Background;
                _colorizer.BeginPass();
            }
            if (_building) Continue();
        }

        /// <summary>Changes whenever anything visible on the minimap changes.</summary>
        private int Signature()
        {
            var world = _sim.World;
            int s = world.LiftsVersion * 31 + world.RunsVersion * 17 + world.ParcelsVersion * 13 + _view.ColorEpoch * 7;
            var chunks = world.Map.Chunks;
            for (int i = 0; i < chunks.Length; i++) s = s * 3 + chunks[i].SurfaceVersion;
            return s;
        }

        private void Continue()
        {
            var map = _sim.Map;
            int maxDiag = map.Width + map.Height - 2;
            int budget = TilesPerFrame;
            while (budget > 0 && _diag <= maxDiag)
            {
                int xMin = Mathf.Max(0, _diag - (map.Height - 1));
                int xMax = Mathf.Min(map.Width - 1, _diag);
                if (_x < xMin) _x = xMin;
                for (; _x <= xMax && budget > 0; _x++, budget--)
                    PaintTile(map, _x, _diag - _x);
                if (_x > xMax)
                {
                    _diag++;
                    _x = 0;
                }
            }

            if (_diag > maxDiag)
            {
                foreach (var lift in _sim.World.Lifts) PaintLift(map, lift.BottomStation.X, lift.BottomStation.Y, lift.TopStation.X, lift.TopStation.Y);
                Texture.SetPixels32(_back);
                Texture.Apply(false);
                _building = false;
                Ready = true;
            }
        }

        private void PaintTile(WorldMap map, int x, int y)
        {
            var t = map.Locate(x, y);
            Color32 c = _colorizer.TileColor(x, y);

            // Forest on the natural view.
            int trees = t.Chunk.TreeDensity[t.Index];
            if (trees > 0 && _view.NaturalLook)
                c = Color32.Lerp(c, t.Chunk.SnowDepth[t.Index] > 20 ? Palette.TreeLight : Palette.TreeDark, Mathf.Min(0.75f, trees * 0.2f));

            // Simple relief shading: lit from the upper left.
            float hHere = map.TileCenterHeight(x, y);
            float hLeft = x > 0 ? map.TileCenterHeight(x - 1, y) : hHere;
            float hUp = y > 0 ? map.TileCenterHeight(x, y - 1) : hHere;
            float shade = Mathf.Clamp(1f + ((hLeft - hHere) * 0.6f + (hUp - hHere) * 0.25f) * 0.035f, 0.72f, 1.12f);
            c = Palette.Scale(c, shade);
            c.a = 255;

            // Project the tile centre and fill its footprint, plus the drop to the next row for steep faces.
            var p = IsoProjection.ToWorld(x + 0.5f, y + 0.5f, hHere);
            int px = Mathf.FloorToInt((p.x - WorldBounds.xMin) / _unitsPerPixel);
            int py = Mathf.FloorToInt((WorldBounds.yMax - p.y) / _unitsPerPixel); // top-down rows
            int fw = Mathf.Max(1, Mathf.CeilToInt(2f * IsoProjection.HalfWidth / _unitsPerPixel));
            float below = Mathf.Min(x + 1 < map.Width ? map.TileCenterHeight(x + 1, y) : hHere, y + 1 < map.Height ? map.TileCenterHeight(x, y + 1) : hHere);
            float drop = Mathf.Max(0f, hHere - below) * IsoProjection.StepHeight;
            int fh = Mathf.Max(1, Mathf.CeilToInt((2f * IsoProjection.HalfHeight + drop) / _unitsPerPixel));
            int x0 = px - fw / 2;
            for (int yy = py; yy < py + fh; yy++)
            {
                if (yy < 0 || yy >= _h) continue;
                int row = (_h - 1 - yy) * _w; // texture rows run bottom-up
                for (int xx = x0; xx < x0 + fw; xx++)
                    if (xx >= 0 && xx < _w) _back[row + xx] = c;
            }
        }

        private void PaintLift(WorldMap map, int bx, int by, int tx, int ty)
        {
            var a = IsoProjection.TileCenter(map, bx, by);
            var b = IsoProjection.TileCenter(map, tx, ty);
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / _unitsPerPixel));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                int px = Mathf.FloorToInt((p.x - WorldBounds.xMin) / _unitsPerPixel);
                int py = Mathf.FloorToInt((WorldBounds.yMax - p.y) / _unitsPerPixel);
                if (px < 0 || px >= _w || py < 0 || py >= _h) continue;
                _back[(_h - 1 - py) * _w + px] = LiftColor;
            }
        }
    }
}
