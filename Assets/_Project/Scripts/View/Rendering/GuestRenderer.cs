using IceCoast.Simulation;
using IceCoast.Simulation.Guests;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws all guests as pixel-art sprites into one dynamic mesh each frame (a few thousand quads is trivial
    /// for the GPU and avoids thousands of GameObjects). Positions are interpolated between ticks so movement
    /// is smooth at any simulation speed. Off-screen guests are skipped; at far zoom guests are hidden entirely
    /// (LOD). Guests riding a lift are drawn by the LiftRenderer, on their chair.
    /// </summary>
    public sealed class GuestRenderer : MonoBehaviour
    {
        [Tooltip("Hide guests when the camera is zoomed out further than this orthographic size.")]
        [SerializeField] private float hideAboveOrthoSize = 70f;

        private SimulationRunner _runner;
        private LiftRenderer _lifts;
        private Camera _camera;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private readonly MeshBuffer _buffer = new MeshBuffer();

        public int DrawnCount { get; private set; }

        public void Initialize(SimulationRunner runner, LiftRenderer lifts, Camera cam)
        {
            _runner = runner;
            _lifts = lifts;
            _camera = cam;
            _mesh = MeshBuffer.CreateDynamicMesh("Guests");
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = RenderMaterials.Opaque;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            var sim = _runner?.Sim;
            if (sim == null) return;

            _buffer.Clear();
            DrawnCount = 0;
            if (_camera.orthographicSize <= hideAboveOrthoSize) DrawGuests(sim);
            _buffer.ApplyTo(_mesh);
        }

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private void DrawGuests(GameSimulation sim)
        {
            var pool = sim.World.Guests;
            var map = sim.Map;
            float alpha = _runner.InterpolationAlpha;
            float time = Time.unscaledTime;
            int bareDepth = sim.Config.Snow.BareDepthCm;
            bool lightsOn = sim.LightingSystem.LightsOn;

            // Visible world rectangle, padded.
            float halfH = _camera.orthographicSize + 2f, halfW = halfH * _camera.aspect + 2f;
            Vector3 cp = _camera.transform.position;
            float minX = cp.x - halfW, maxX = cp.x + halfW, minY = cp.y - halfH, maxY = cp.y + halfH;

            for (int i = 0; i < pool.HighWater; i++)
            {
                if (!pool.Alive[i]) continue;
                var state = pool.State[i];
                if (state == GuestState.Riding) continue; // drawn on their chair by the LiftRenderer

                float gx = Mathf.Lerp(pool.PrevX[i], pool.PosX[i], alpha);
                float gy = Mathf.Lerp(pool.PrevY[i], pool.PosY[i], alpha);
                Vector3 foot = IsoProjection.ToWorld(gx, gy, map.SampleHeight(gx, gy));
                if (foot.x < minX || foot.x > maxX || foot.y < minY || foot.y > maxY) continue;
                foot.z -= IsoProjection.DepthPerDiagonal * 0.8f;

                int tx = Mathf.Clamp((int)gx, 0, map.Width - 1), ty = Mathf.Clamp((int)gy, 0, map.Height - 1);
                // Skis off on walking areas and in the lot: they walk.
                bool onSnow = map.GetSnowDepth(tx, ty) >= bareDepth && map.GetSurface(tx, ty) == Simulation.World.SurfaceType.Natural;
                var pose = PoseFor(pool, i, state, onSnow, time, out bool flip);
                var sprite = WorldAtlas.Skier((int)pool.Skill[i], i * 7 % WorldAtlas.SkierLooks, pose);
                var tint = White;
                if (lightsOn) tint.a = ChunkMeshBuilder.LampAlpha(Mathf.Clamp01(map.GetLightLevel(tx, ty) / 200f)); // lit by trail lights
                _buffer.AddSprite(foot, sprite, flip, tint);
                DrawnCount++;
            }
        }

        /// <summary>
        /// Picks a sprite from what the guest is doing and which way they are moving on screen: skiing across
        /// the hill (side view, mirrored for left), straight down at the camera, side-stepping uphill (back
        /// view), standing in a lift line facing the lift (with an occasional shuffle), or, only on bare ground,
        /// walking with skis on the shoulder (two frames). On snow, guests ski to the lift and back to the car.
        /// </summary>
        private static SkierPose PoseFor(GuestPool pool, int i, GuestState state, bool onSnow, float time, out bool flip)
        {
            flip = false;
            // Velocity over the last tick, in screen terms: x right, y up.
            float vx = pool.PosX[i] - pool.PrevX[i], vy = pool.PosY[i] - pool.PrevY[i];
            float sx = vx - vy, sy = -(vx + vy) * 0.5f;
            bool moving = vx * vx + vy * vy > 1e-6f;

            switch (state)
            {
                case GuestState.Queueing:
                case GuestState.WaitingForFood:
                {
                    // Mostly still, now and then shifting weight.
                    int beat = (int)(time * 0.8f + i * 0.37f);
                    return (beat + i) % 5 == 0 ? SkierPose.BackIdle : SkierPose.Back;
                }
                case GuestState.Skiing:
                    if ((pool.Flags[i] & GuestFlags.Fallen) != 0) return SkierPose.Fallen;
                    return SkiPose(moving, sx, sy, out flip);
                case GuestState.WalkingToLift:
                case GuestState.Leaving:
                case GuestState.Arriving:
                case GuestState.Resting: // early birds wandering the base
                case GuestState.WalkingToFood:
                case GuestState.Eating: // strolling over to a spot to eat, then standing there
                    if (onSnow) return SkiPose(moving, sx, sy, out flip);
                    if (!moving) return SkierPose.SkiFront;
                    flip = sx < 0f;
                    return ((int)(time * 4f) + i) % 2 == 0 ? SkierPose.Walk1 : SkierPose.Walk2;
                default:
                    return SkierPose.SkiFront;
            }
        }

        private static SkierPose SkiPose(bool moving, float sx, float sy, out bool flip)
        {
            flip = false;
            if (!moving) return SkierPose.SkiFront;
            if (sy > 0f && sy > 0.2f * Mathf.Abs(sx)) return SkierPose.Back;       // side-stepping / skating uphill
            if (Mathf.Abs(sx) < Mathf.Abs(sy) * 0.6f) return SkierPose.SkiFront; // straight down the fall line
            flip = sx < 0f;
            return SkierPose.SkiSide;
        }
    }
}
