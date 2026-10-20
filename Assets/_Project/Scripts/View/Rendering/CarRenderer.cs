using IceCoast.Simulation;
using IceCoast.Simulation.Parking;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws the cars: parked in the lot, and driving in and out along the road. Positions come straight
    /// from the simulation (<see cref="ParkingSystem.TryGetCarPose"/>), interpolated between ticks. Cars on
    /// the move keep to their side of the road. Rebuilt every frame (a few hundred quads at most).
    /// </summary>
    public sealed class CarRenderer : MonoBehaviour
    {
        [Tooltip("Hide cars when the camera is zoomed out further than this orthographic size.")]
        [SerializeField] private float hideAboveOrthoSize = 90f;
        [Tooltip("How far (tiles) moving cars keep right of the road's centre line.")]
        [SerializeField] private float laneOffset = 0.22f;

        private SimulationRunner _runner;
        private Camera _camera;
        private Mesh _mesh;
        private readonly MeshBuffer _buffer = new MeshBuffer();

        public void Initialize(SimulationRunner runner, Camera cam)
        {
            _runner = runner;
            _camera = cam;
            _mesh = MeshBuffer.CreateDynamicMesh("Cars");
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            var sim = _runner?.Sim;
            if (sim == null) return;
            _buffer.Clear();
            _buffer.IncludeNormals = false;
            if (_camera.orthographicSize <= hideAboveOrthoSize) DrawCars(sim);
            _buffer.ApplyTo(_mesh);
        }

        private void DrawCars(GameSimulation sim)
        {
            var map = sim.Map;
            var scenery = sim.World.Scenery;
            var parking = sim.ParkingSystem;
            float alpha = _runner.InterpolationAlpha;
            bool lightsOn = sim.LightingSystem.LightsOn;

            float halfH = _camera.orthographicSize + 2f, halfW = halfH * _camera.aspect + 2f;
            Vector3 cp = _camera.transform.position;

            foreach (var lot in sim.World.ParkingLots)
            {
                for (int i = 0; i < lot.Spaces.Length; i++)
                {
                    if (!parking.TryGetCarPose(lot, i, alpha, out float x, out float y, out float dx, out float dy)) continue;
                    var state = lot.Spaces[i].State;
                    if (state != CarState.Parked)
                    {
                        // Keep right: offset perpendicular to the direction of travel.
                        x += -dy * laneOffset;
                        y += dx * laneOffset;
                    }

                    float h = scenery != null ? scenery.SampleHeight(map, x, y) : map.SampleHeight(x, y);
                    Vector3 foot = IsoProjection.ToWorld(x, y, h);
                    if (foot.x < cp.x - halfW || foot.x > cp.x + halfW || foot.y < cp.y - halfH || foot.y > cp.y + halfH) continue;
                    foot.z -= IsoProjection.DepthPerDiagonal * 0.7f;

                    bool alongX = Mathf.Abs(dx) >= Mathf.Abs(dy);
                    bool noseToCamera = alongX ? dx > 0f : dy > 0f;
                    var sprite = WorldAtlas.Car(lot.Spaces[i].Color, noseToCamera);

                    var tint = new Color32(255, 255, 255, 255);
                    if (lightsOn && map.InBounds((int)x, (int)y))
                        tint.a = ChunkMeshBuilder.LampAlpha(Mathf.Clamp01(map.GetLightLevel((int)x, (int)y) / 200f));
                    _buffer.AddSprite(foot, sprite, !alongX, tint);
                }
            }
        }
    }
}
