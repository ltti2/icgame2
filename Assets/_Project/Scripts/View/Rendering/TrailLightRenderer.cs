using IceCoast.Simulation;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws trail light poles. One static mesh, rebuilt only when lights are added, removed or switched on or
    /// off (<c>WorldState.LightsVersion</c>). While the lights are on, the lamp heads are drawn with full lamp
    /// light in their vertex alpha so they shine at night (see IceCoastUnlit.shader); the glow on the snow
    /// around them comes from the terrain (ChunkMeshBuilder).
    /// </summary>
    public sealed class TrailLightRenderer : MonoBehaviour
    {
        private SimulationRunner _runner;
        private GameSimulation _sim;
        private Mesh _mesh;
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private int _builtVersion = -1;

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        public void Initialize(SimulationRunner runner)
        {
            _runner = runner;
            _mesh = MeshBuffer.CreateDynamicMesh("Trail Lights");
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _runner.SimulationChanged += sim => { _sim = sim; _builtVersion = -1; };
            _sim = _runner.Sim;
        }

        private void LateUpdate()
        {
            if (_sim == null || _builtVersion == _sim.World.LightsVersion) return;
            _builtVersion = _sim.World.LightsVersion;
            Rebuild();
        }

        private void Rebuild()
        {
            _buffer.Clear();
            _buffer.IncludeNormals = false;
            var map = _sim.Map;
            var head = _sim.LightingSystem.LightsOn
                ? new Color32(255, 255, 255, ChunkMeshBuilder.LampAlpha(1f))
                : White;
            foreach (var light in _sim.World.Lights)
            {
                float gx = light.Tile.X + 0.5f, gy = light.Tile.Y + 0.5f;
                Vector3 foot = IsoProjection.ToWorld(gx, gy, map.SampleHeight(gx, gy));
                foot.z -= IsoProjection.DepthPerDiagonal * 0.6f;
                _buffer.AddSprite(foot, WorldAtlas.LightPole(light.Size), false, White);
                foot.z -= IsoProjection.DepthPerDiagonal * 0.01f;
                _buffer.AddSprite(foot, WorldAtlas.LampHead(light.Size), false, head);
            }
            _buffer.ApplyTo(_mesh);
        }
    }
}
