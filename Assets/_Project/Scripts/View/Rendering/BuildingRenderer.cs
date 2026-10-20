using IceCoast.Simulation;
using IceCoast.View.Iso;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Draws buildings (Waffle Cabins). One static mesh, rebuilt only when buildings are added, removed or changed
    /// (<c>WorldState.BuildingsVersion</c>), when the lights switch on or off (windows glow under trail lights like
    /// everything else), or when edited art changes the atlas layout.
    /// </summary>
    public sealed class BuildingRenderer : MonoBehaviour
    {
        private SimulationRunner _runner;
        private GameSimulation _sim;
        private Mesh _mesh;
        private readonly MeshBuffer _buffer = new MeshBuffer();
        private int _builtVersion = -1;
        private int _builtLights = -1;

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        public void Initialize(SimulationRunner runner)
        {
            _runner = runner;
            _mesh = MeshBuffer.CreateDynamicMesh("Buildings");
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _runner.SimulationChanged += sim => { _sim = sim; _builtVersion = -1; };
            WorldAtlas.LayoutChanged += OnAtlasLayoutChanged;
            _sim = _runner.Sim;
        }

        private void OnAtlasLayoutChanged() => _builtVersion = -1;
        private void OnDestroy() => WorldAtlas.LayoutChanged -= OnAtlasLayoutChanged;

        private void LateUpdate()
        {
            if (_sim == null) return;
            if (_builtVersion == _sim.World.BuildingsVersion && _builtLights == _sim.World.LightsVersion) return;
            _builtVersion = _sim.World.BuildingsVersion;
            _builtLights = _sim.World.LightsVersion;
            Rebuild();
        }

        private void Rebuild()
        {
            _buffer.Clear();
            _buffer.IncludeNormals = false;
            var map = _sim.Map;
            bool lightsOn = _sim.LightingSystem.LightsOn;
            foreach (var b in _sim.World.Buildings)
            {
                var sprite = WorldAtlas.Building(b.Type);
                if (!sprite.IsValid) continue;
                float gx = b.Origin.X + b.Width * 0.5f, gy = b.Origin.Y + b.Height * 0.5f;
                Vector3 foot = IsoProjection.ToWorld(gx, gy, map.SampleHeight(gx, gy));
                foot.z = IsoProjection.Depth(gx, gy) - IsoProjection.DepthPerDiagonal * 0.9f; // like a lift hut
                var tint = White;
                if (lightsOn)
                {
                    int tx = Mathf.Clamp((int)gx, 0, map.Width - 1), ty = Mathf.Clamp((int)gy, 0, map.Height - 1);
                    tint.a = ChunkMeshBuilder.LampAlpha(Mathf.Clamp01(map.GetLightLevel(tx, ty) / 200f));
                }
                _buffer.AddSprite(foot, sprite, false, tint);
            }
            _buffer.ApplyTo(_mesh);
        }
    }
}
