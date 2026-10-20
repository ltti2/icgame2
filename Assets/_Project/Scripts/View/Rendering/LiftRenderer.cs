using System.Collections.Generic;
using IceCoast.Simulation;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Lifts;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws lifts. Static parts (stations, towers, cables, bullwheels, the carpet's belt) are rebuilt only
    /// when lifts change; carriers and their riders are rebuilt every frame.
    ///
    /// Everything is pixel art from the <see cref="WorldAtlas"/> (see <see cref="LiftArt"/>): a wooden shed and
    /// posts for a rope tow, steel housings and a rubber belt for the magic carpet, a green-roofed hut and
    /// poles for a T-bar, log shacks and grey towers with red triples for the fixed-grip, blue (or orange,
    /// for bubbles) terminals on legs for the detachables, and a glass hall, dark terminal and lattice towers
    /// for the gondola. The up and down lines sit <see cref="LiftArt.LineOffset"/> either side of the centre
    /// line, far enough apart that passing carriers never overlap.
    ///
    /// Carriers follow the simulation exactly: carrier k is on the line at (LineClock - k x interval) / RideTicks,
    /// the same clock LiftSystem boards riders with, so every rider is drawn sitting on the chair they boarded
    /// (seat by seat), and a lift on wind hold freezes with its riders hanging in the air.
    /// </summary>
    public sealed class LiftRenderer : MonoBehaviour
    {
        [Tooltip("Hide riders when the camera is zoomed out further than this orthographic size.")]
        [SerializeField] private float hideRidersAboveOrthoSize = 70f;
        [Tooltip("Most carriers drawn per lift (a long lift at high capacity can have hundreds).")]
        [SerializeField] private int maxCarriersPerLift = 1200;

        /// <summary>Cable heights along a lift, as (t along the line, height in steps).</summary>
        private sealed class LiftPath
        {
            public Lift Lift;
            public LiftDefinition Def;
            public readonly List<float> T = new List<float>();
            public readonly List<float> H = new List<float>();

            public float HeightAt(float t)
            {
                if (t <= T[0]) return H[0];
                for (int i = 1; i < T.Count; i++)
                    if (t <= T[i]) return Mathf.Lerp(H[i - 1], H[i], Mathf.InverseLerp(T[i - 1], T[i], t));
                return H[H.Count - 1];
            }
        }

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private SimulationRunner _runner;
        private ViewState _view;
        private Camera _camera;
        private GameSimulation _sim;
        private Mesh _staticMesh, _dynamicMesh;
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private readonly Dictionary<int, LiftPath> _paths = new Dictionary<int, LiftPath>();
        private readonly HashSet<long> _occupied = new HashSet<long>();
        private int _builtVersion = -1;
        private int _builtSelection;

        public void Initialize(SimulationRunner runner, ViewState view, Camera cam = null)
        {
            _runner = runner;
            _view = view;
            _camera = cam;
            _staticMesh = MeshBuffer.CreateDynamicMesh("Lift Structures");
            _dynamicMesh = MeshBuffer.CreateDynamicMesh("Lift Carriers");
            CreateChild("Structures", _staticMesh);
            CreateChild("Carriers", _dynamicMesh);
            _runner.SimulationChanged += sim => { _sim = sim; _builtVersion = -1; };
            _sim = _runner.Sim;
        }

        private void CreateChild(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_sim == null) return;
            if (_builtVersion != _sim.World.LiftsVersion || _builtSelection != _view.SelectedLiftId) RebuildStatic();
            RebuildCarriers();
        }

        // ---------- Geometry queries ----------

        /// <summary>Cable height (in steps) at a grid position along a lift.</summary>
        public bool TryGetCableHeight(int liftId, float gx, float gy, out float heightSteps)
        {
            heightSteps = 0f;
            if (!_paths.TryGetValue(liftId, out var path)) return false;
            heightSteps = path.HeightAt(ParamAlong(path.Lift, gx, gy));
            return true;
        }

        private static float ParamAlong(Lift lift, float gx, float gy)
        {
            float bx = lift.BottomStation.X + 0.5f, by = lift.BottomStation.Y + 0.5f;
            float dx = lift.TopStation.X + 0.5f - bx, dy = lift.TopStation.Y + 0.5f - by;
            float len2 = dx * dx + dy * dy;
            return len2 < 1e-6f ? 0f : Mathf.Clamp01(((gx - bx) * dx + (gy - by) * dy) / len2);
        }

        private static bool IsSurfaceLift(LiftType t) => t == LiftType.RopeTow || t == LiftType.MagicCarpet || t == LiftType.TBar;

        // =====================================================================
        // Static parts
        // =====================================================================

        private void RebuildStatic()
        {
            _builtVersion = _sim.World.LiftsVersion;
            _builtSelection = _view.SelectedLiftId;
            _buffer.Clear();

            var keep = new HashSet<int>();
            foreach (var lift in _sim.World.Lifts)
            {
                keep.Add(lift.Id);
                if (!_paths.TryGetValue(lift.Id, out var path))
                {
                    path = new LiftPath();
                    _paths[lift.Id] = path;
                }
                path.Lift = lift;
                path.Def = _sim.Config.GetLift(lift.Type);
                BuildPath(path);

                bool selected = lift.Id == _view.SelectedLiftId;
                DrawStation(path, lift.BottomStation, true, selected);
                DrawStation(path, lift.TopStation, false, selected);

                if (lift.Type == LiftType.MagicCarpet)
                {
                    DrawBelt(path, selected);
                    continue;
                }
                foreach (var tower in lift.Towers) DrawTower(path, tower, selected);
                DrawCable(path, +1, selected);
                DrawCable(path, -1, selected);
            }

            var stale = new List<int>();
            foreach (var id in _paths.Keys) if (!keep.Contains(id)) stale.Add(id);
            foreach (var id in stale) _paths.Remove(id);

            _buffer.ApplyTo(_staticMesh);
        }

        private void BuildPath(LiftPath path)
        {
            var map = _sim.Map;
            var lift = path.Lift;
            float clearance = path.Def.CableHeightMeters / _sim.Config.Terrain.MetersPerHeightStep;
            path.T.Clear();
            path.H.Clear();

            void AddPoint(float gx, float gy)
            {
                path.T.Add(ParamAlong(lift, gx, gy));
                path.H.Add(map.SampleHeight(gx, gy) + clearance);
            }

            AddPoint(lift.BottomStation.X + 0.5f, lift.BottomStation.Y + 0.5f);
            foreach (var t in lift.Towers) AddPoint(t.X + 0.5f, t.Y + 0.5f);
            AddPoint(lift.TopStation.X + 0.5f, lift.TopStation.Y + 0.5f);
        }

        private void DrawCable(LiftPath path, int side, bool selected)
        {
            var lift = path.Lift;
            OffsetLine(lift, side * LiftArt.LineOffset(lift.Type), out float ox, out float oy);
            var color = selected ? Palette.Selection : Palette.Cable;
            float width = selected ? 0.06f : lift.Type == LiftType.Gondola ? 0.045f : 0.035f;
            for (int i = 1; i < path.T.Count; i++)
            {
                lift.PointAlong(path.T[i - 1], out float ax, out float ay);
                lift.PointAlong(path.T[i], out float bx, out float by);
                Vector3 a = IsoProjection.ToWorld(ax + ox, ay + oy, path.H[i - 1]);
                Vector3 b = IsoProjection.ToWorld(bx + ox, by + oy, path.H[i]);
                a.z -= 0.006f; b.z -= 0.006f;
                _buffer.AddLine(a, b, width, color);
            }
        }

        private static void OffsetLine(Lift lift, float amount, out float ox, out float oy)
        {
            float dx = lift.TopStation.X - lift.BottomStation.X, dy = lift.TopStation.Y - lift.BottomStation.Y;
            float len = Mathf.Max(0.001f, Mathf.Sqrt(dx * dx + dy * dy));
            ox = -dy / len * amount;
            oy = dx / len * amount;
        }

        // ---------- Towers ----------

        private static readonly Color32 SelectedTint = new Color32(255, 226, 120, 255);

        /// <summary>
        /// A line tower: the type's pixel-art pole stretched to meet the cable, a crossarm out to both lines,
        /// and a sheave train under each end.
        /// </summary>
        private void DrawTower(LiftPath path, Simulation.Core.Int2 tower, bool selected)
        {
            var map = _sim.Map;
            var lift = path.Lift;
            float gx = tower.X + 0.5f, gy = tower.Y + 0.5f;
            float ground = map.SampleHeight(gx, gy);
            float cable = path.HeightAt(ParamAlong(lift, gx, gy));
            Vector3 foot = IsoProjection.ToWorld(gx, gy, ground);
            foot.z -= 0.004f;
            var tint = selected ? SelectedTint : White;
            DrawPole(lift.Type, foot, (cable - ground) * LiftArt.PixelsPerStep + 4f, tint);

            float offset = LiftArt.LineOffset(lift.Type);
            OffsetLine(lift, offset, out float ox, out float oy);
            Vector3 armA = IsoProjection.ToWorld(gx + ox, gy + oy, cable + 0.6f), armB = IsoProjection.ToWorld(gx - ox, gy - oy, cable + 0.6f);
            armA.z = armB.z = foot.z - 0.0005f;
            _buffer.AddLine(armA, armB, lift.Type == LiftType.RopeTow ? 0.06f : 0.09f, ArmColor(lift.Type, selected));
            if (lift.Type != LiftType.RopeTow)
            {
                var sheaveA = IsoProjection.ToWorld(gx + ox, gy + oy, cable + 0.25f);
                var sheaveB = IsoProjection.ToWorld(gx - ox, gy - oy, cable + 0.25f);
                sheaveA.z = sheaveB.z = foot.z - 0.001f;
                _buffer.AddSprite(sheaveA, WorldAtlas.Sheaves, false, tint);
                _buffer.AddSprite(sheaveB, WorldAtlas.Sheaves, false, tint);
            }
        }

        /// <summary>The type's tower sprite on <paramref name="foot"/>, stretched to <paramref name="heightPx"/> pixels tall.</summary>
        private void DrawPole(LiftType type, Vector3 foot, float heightPx, Color32 tint)
        {
            var pole = WorldAtlas.Tower(type);
            if (!pole.IsValid) return;
            float sy = Mathf.Max(0.25f, heightPx / pole.Height);
            _buffer.AddSprite(foot, pole, false, tint, 1f, sy);
        }

        private static Color32 ArmColor(LiftType type, bool selected)
        {
            if (selected) return Palette.Selection;
            switch (type)
            {
                case LiftType.RopeTow: return new Color32(118, 86, 56, 255);
                case LiftType.TBar: return new Color32(70, 106, 78, 255);
                case LiftType.Gondola: return new Color32(132, 138, 148, 255);
                default: return new Color32(110, 116, 126, 255);
            }
        }

        // ---------- Stations ----------

        /// <summary>
        /// A station: the type's hut or hall on the ground, and either a drive post carrying the bullwheel
        /// (surface lifts, fixed-grip chairs) or an enclosed terminal on legs at cable height (detachables and
        /// gondolas), all pixel art.
        /// </summary>
        private void DrawStation(LiftPath path, Simulation.Core.Int2 tile, bool bottom, bool selected)
        {
            var lift = path.Lift;
            var type = lift.Type;
            var map = _sim.Map;
            int x = tile.X, y = tile.Y;
            float h = map.TileCenterHeight(x, y);
            float clearance = path.Def.CableHeightMeters / _sim.Config.Terrain.MetersPerHeightStep;
            float cable = h + clearance;
            float z = StationDepth(x, y);
            var tint = selected ? SelectedTint : White;

            var centre = IsoProjection.ToWorld(x + 0.5f, y + 0.5f, h);
            centre.z = z;
            var hut = WorldAtlas.Hut(type, bottom);
            if (hut.IsValid) _buffer.AddSprite(centre, hut, false, tint);
            if (type == LiftType.MagicCarpet) return;

            if (LiftArt.HasTerminal(type))
            {
                // Legs from the ground up to the underside of the terminal (9 px below the cable).
                float legTop = clearance * LiftArt.PixelsPerStep - 9f;
                var column = WorldAtlas.Column;
                foreach (var (lx, ly) in TerminalLegs)
                {
                    var foot = IsoProjection.ToWorld(x + lx, y + ly, map.SampleHeight(x + lx, y + ly));
                    foot.z = z - 0.0005f;
                    float px = legTop + (h - map.SampleHeight(x + lx, y + ly)) * LiftArt.PixelsPerStep;
                    if (px > 1f) _buffer.AddSprite(foot, column, false, tint, 1f, px / column.Height);
                }
                var top = IsoProjection.ToWorld(x + 0.5f, y + 0.5f, cable);
                top.z = z - 0.002f;
                _buffer.AddSprite(top, WorldAtlas.Terminal(type), false, tint);
                return;
            }

            // Drive (or return) post up through the bullwheel.
            var post = IsoProjection.ToWorld(x + 0.5f, y + 0.5f, h);
            post.z = z - 0.001f;
            DrawPole(type, post, clearance * LiftArt.PixelsPerStep + 4f, tint);
            var wheel = IsoProjection.ToWorld(x + 0.5f, y + 0.5f, cable);
            wheel.z = z - 0.0015f;
            _buffer.AddSprite(wheel, WorldAtlas.Bullwheel(type), false, tint);
        }

        private static readonly (float, float)[] TerminalLegs = { (0.2f, 0.8f), (0.8f, 0.2f), (0.8f, 0.8f) };

        private float StationDepth(int x, int y) => IsoProjection.Depth(x + 0.5f, y + 0.5f) - IsoProjection.DepthPerDiagonal * 0.9f;

        /// <summary>The magic carpet: a dark rubber belt on the snow between the housings, with side rails.</summary>
        private void DrawBelt(LiftPath path, bool selected)
        {
            var lift = path.Lift;
            var map = _sim.Map;
            OffsetLine(lift, 0.22f, out float ox, out float oy);
            var belt = selected ? Palette.Selection : new Color32(46, 48, 54, 255);
            var rail = new Color32(170, 176, 186, 255);
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
                lift.PointAlong(t0, out float ax, out float ay);
                lift.PointAlong(t1, out float bx, out float by);
                Vector3 P(float gx, float gy)
                {
                    var v = IsoProjection.ToWorld(gx, gy, map.SampleHeight(gx, gy) + 0.3f);
                    v.z -= IsoProjection.DepthPerDiagonal * 0.5f;
                    return v;
                }
                _buffer.AddQuad(P(ax - ox, ay - oy), P(bx - ox, by - oy), P(bx + ox, by + oy), P(ax + ox, ay + oy), belt);
                _buffer.AddLine(P(ax - ox, ay - oy), P(bx - ox, by - oy), 0.04f, rail);
                _buffer.AddLine(P(ax + ox, ay + oy), P(bx + ox, by + oy), 0.04f, rail);
            }
        }

        // =====================================================================
        // Carriers and riders
        // =====================================================================

        private void RebuildCarriers()
        {
            _buffer.Clear();
            float alpha = _runner.InterpolationAlpha;
            bool showRiders = _camera == null || _camera.orthographicSize <= hideRidersAboveOrthoSize;

            foreach (var path in _paths.Values)
            {
                var lift = path.Lift;
                if (lift.RideTicks <= 0) continue;
                double clock = lift.LineClock + (LiftSystem.LineMoving(lift) && !_runner.Paused ? alpha : 0f);
                double interval = _sim.LiftSystem.CarrierInterval(lift);
                if (interval <= 0 || double.IsInfinity(interval) || interval > 1e6) continue;

                // Which carriers have someone aboard (gondolas show faces in the windows).
                _occupied.Clear();
                foreach (var r in lift.Riders) _occupied.Add((long)System.Math.Round(r.BoardClock / interval));

                DrawCarriers(path, clock, interval);
                if (showRiders && lift.Type != LiftType.Gondola) DrawRiders(path, clock);
            }
            _buffer.ApplyTo(_dynamicMesh);
        }

        /// <summary>
        /// Every carrier on the loop: up the line for t in 0..1 after loading, then back down empty. Carrier k
        /// loaded at k x interval on the line clock (negative k are the ones already on the loop at the start).
        /// </summary>
        private void DrawCarriers(LiftPath path, double clock, double interval)
        {
            var lift = path.Lift;
            double ride = lift.RideTicks;
            long newest = (long)System.Math.Floor(clock / interval);
            long oldest = (long)System.Math.Ceiling((clock - 2.0 * ride) / interval);
            if (newest - oldest > maxCarriersPerLift) oldest = newest - maxCarriersPerLift;

            for (long k = newest; k >= oldest; k--)
            {
                double travelled = (clock - k * interval) / ride;
                bool up = travelled <= 1.0;
                float t = up ? (float)travelled : (float)(2.0 - travelled);
                if (t < 0f || t > 1f) continue;
                DrawCarrier(path, t, up, up && _occupied.Contains(k));
            }
        }

        /// <summary>Where a carrier hangs (the grip on the cable) at progress t on the up or down line.</summary>
        private Vector3 GripAt(LiftPath path, float t, bool up, out float gx, out float gy)
        {
            var lift = path.Lift;
            OffsetLine(lift, (up ? 1f : -1f) * LiftArt.LineOffset(lift.Type), out float ox, out float oy);
            lift.PointAlong(t, out gx, out gy);
            gx += ox; gy += oy;
            return IsoProjection.ToWorld(gx, gy, path.HeightAt(t));
        }

        private void DrawCarrier(LiftPath path, float t, bool up, bool occupied)
        {
            var lift = path.Lift;
            var grip = GripAt(path, t, up, out float gx, out float gy);
            grip.z -= 0.007f;

            switch (lift.Type)
            {
                case LiftType.MagicCarpet:
                {
                    if (!up) return;
                    // Belt seams sliding up the carpet.
                    OffsetLine(lift, 0.2f, out float ox, out float oy);
                    lift.PointAlong(t, out float cx, out float cy);
                    var map = _sim.Map;
                    var a = IsoProjection.ToWorld(cx - ox, cy - oy, map.SampleHeight(cx, cy) + 0.32f);
                    var b = IsoProjection.ToWorld(cx + ox, cy + oy, map.SampleHeight(cx, cy) + 0.32f);
                    a.z = b.z = IsoProjection.Depth(cx, cy) - IsoProjection.DepthPerDiagonal * 0.55f;
                    _buffer.AddLine(a, b, 0.025f, new Color32(84, 88, 96, 255));
                    return;
                }
                case LiftType.RopeTow:
                    _buffer.AddSprite(grip, WorldAtlas.RopeHandle, false, White);
                    return;
                case LiftType.TBar:
                {
                    // Spring-box cable from the haul rope down to the bar at hip height.
                    float bar = _sim.Map.SampleHeight(gx, gy) + 1.3f;
                    var low = IsoProjection.ToWorld(gx, gy, Mathf.Min(bar, path.HeightAt(t)));
                    low.z = grip.z;
                    _buffer.AddLine(grip, low, 0.025f, new Color32(60, 62, 70, 255));
                    _buffer.AddSprite(low, WorldAtlas.TBar, false, White);
                    return;
                }
                default:
                    _buffer.AddSprite(grip, WorldAtlas.Carrier(lift.Type, occupied), false, White);
                    return;
            }
        }

        private void DrawRiders(LiftPath path, double clock)
        {
            var lift = path.Lift;
            var pool = _sim.World.Guests;
            int seats = _sim.LiftSystem.Seats(lift);
            bool surface = IsSurfaceLift(lift.Type);

            foreach (var r in lift.Riders)
            {
                float t = LiftSystem.Progress(lift, r.BoardClock, clock);
                int g = r.GuestIndex;
                if (g < 0 || g >= pool.HighWater || !pool.Alive[g]) continue;
                int skill = (int)pool.Skill[g];
                int look = g * 7 % WorldAtlas.SkierLooks;

                if (surface)
                {
                    // Standing on their skis, towed (or carried on the belt) up the hill: back to the camera.
                    float lineOffset = LiftArt.LineOffset(lift.Type);
                    OffsetLine(lift, lineOffset, out float ox, out float oy);
                    if (lift.Type == LiftType.MagicCarpet) { ox = oy = 0f; }
                    lift.PointAlong(t, out float gx, out float gy);
                    // Two to a T-bar, one each side of the bar.
                    if (lift.Type == LiftType.TBar)
                    {
                        float side = (r.Seat == 0 ? -1f : 1f) * 0.1f;
                        OffsetLine(lift, lineOffset + side, out ox, out oy);
                    }
                    gx += ox; gy += oy;
                    float lift01 = lift.Type == LiftType.MagicCarpet ? 0.3f : 0f;
                    var foot = IsoProjection.ToWorld(gx, gy, _sim.Map.SampleHeight(gx, gy) + lift01);
                    foot.z -= IsoProjection.DepthPerDiagonal * 0.9f;
                    _buffer.AddSprite(foot, WorldAtlas.Skier(skill, look, SkierPose.Back), false, White);
                    continue;
                }

                // Seated on the chair: lap on the bench, at their seat across the chair.
                var grip = GripAt(path, t, true, out _, out _);
                float u = WorldAtlas.UnitsPerTexel;
                var lap = grip + new Vector3(SpriteArt.SeatOffset(r.Seat, seats) * u, -SpriteArt.SeatRow * u, -0.0075f - r.Seat * 0.00001f);
                _buffer.AddSprite(lap, WorldAtlas.Skier(skill, look, SkierPose.Seated), false, White);
            }
        }
    }
}
