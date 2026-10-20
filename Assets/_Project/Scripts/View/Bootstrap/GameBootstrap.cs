using IceCoast.Simulation.Config;
using IceCoast.View.CameraControl;
using IceCoast.View.Config;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.Tools;
using IceCoast.View.UI;
using UnityEngine;

namespace IceCoast.View.Bootstrap
{
    /// <summary>
    /// Composition root for the Unity side. The only MonoBehaviour that needs to be placed in the scene:
    /// it creates every other runtime object as a child and wires dependencies explicitly
    /// (no singletons, no FindObjectOfType at runtime).
    ///
    /// Scene setup: Ice Coast > Set Up Game Scene (editor menu) does all of this for you.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Tuning asset. If empty, built-in defaults are used.")]
        [SerializeField] private SimConfigAsset config;

        [Header("Rendering")]
        [Tooltip("IceCoast/UnlitVertexColor. Referenced here so it is included in builds.")]
        [SerializeField] private Shader unlitShader;
        [SerializeField] private Camera gameCamera;

        [Header("Frame rate")]
        [Tooltip("Like OpenTTD and RollerCoaster Tycoon, the game draws at a fixed, modest frame rate. " +
                 "The simulation ticks independently of it and positions are interpolated between ticks.")]
        [SerializeField] private int targetFrameRate = 30;

        [Header("New game overrides")]
        [SerializeField] private bool sandboxMode;
        [SerializeField] private bool overrideSeed;
        [SerializeField] private int seed = 12345;

        public SimulationRunner Runner { get; private set; }
        public ViewState View { get; private set; }

        private void Awake()
        {
            // Lock the frame rate: vSync would override targetFrameRate, so turn it off.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            RenderMaterials.Initialize(unlitShader);
            View = new ViewState();

            var cam = SetUpCamera();
            Runner = CreateChild<SimulationRunner>("Simulation");

            var terrain = CreateChild<TerrainRenderer>("Terrain");
            terrain.Initialize(Runner, View, cam);

            var scenery = CreateChild<SceneryRenderer>("Scenery");
            scenery.Initialize(Runner);

            var daylight = CreateChild<DayLightController>("Day Light");
            daylight.Initialize(Runner, View, cam);

            var lifts = CreateChild<LiftRenderer>("Lifts");
            lifts.Initialize(Runner, View, cam);

            var trailLights = CreateChild<TrailLightRenderer>("Trail Lights");
            trailLights.Initialize(Runner);

            var buildings = CreateChild<BuildingRenderer>("Buildings");
            buildings.Initialize(Runner);

            var cars = CreateChild<CarRenderer>("Cars");
            cars.Initialize(Runner, cam);

            var guests = CreateChild<GuestRenderer>("Guests");
            guests.Initialize(Runner, lifts, cam);

            var preview = CreateChild<PreviewRenderer>("Tool Preview");
            preview.Initialize();

            var camController = cam.GetComponent<IsoCameraController>();
            if (camController == null) camController = cam.gameObject.AddComponent<IsoCameraController>();
            camController.Initialize(Runner, View);

            var contours = CreateChild<ContourRenderer>("Contours");
            contours.Initialize(Runner, View, camController);

            var tools = CreateChild<ToolController>("Tools");
            tools.Initialize(Runner, View, preview, camController);

            var hud = CreateChild<GameHud>("HUD");
            hud.Initialize(Runner, View, tools, camController, guests, NewGame);

            // Edit the PNGs in StreamingAssets/Art while playing and the game redraws them.
            CreateChild<ArtHotReload>("Art Hot Reload");
        }

        private void Start() => NewGame(false);

        /// <param name="sandbox">Unlimited money (also forced on by the inspector's Sandbox Mode).</param>
        public void NewGame(bool sandbox)
        {
            SimConfig cfg = config != null ? config.CreateRuntimeCopy() : SimConfig.CreateDefault();
            if (sandboxMode || sandbox) cfg.SandboxMode = true;
            if (overrideSeed) cfg.Map.Seed = seed;
            Runner.StartNewGame(cfg);
        }

        private Camera SetUpCamera()
        {
            var cam = gameCamera != null ? gameCamera : Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Sky;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -100f), Quaternion.identity);
            return cam;
        }

        private T CreateChild<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
