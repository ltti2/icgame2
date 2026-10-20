using IceCoast.Simulation;
using IceCoast.Simulation.Weather;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Drives sunlight and sky colour through global shader properties every frame.
    /// Because lighting happens in the shader, the sun moves smoothly through the day without rebuilding
    /// any meshes.
    ///
    /// The sun follows the simulation's sunrise and sunset (TimeSystem): in December it rises in the south-east
    /// around 7:05, crosses the southern sky and sets in the south-west around 4:15. Real midwinter sun only climbs ~23 degrees there; the game
    /// uses a higher peak (Max Sun Elevation) so midday stays readable. Clouds and snow soften it.
    /// </summary>
    public sealed class DayLightController : MonoBehaviour
    {
        [Header("Sun path (Vermont)")]
        [SerializeField] private float sunriseAzimuth = 120f;
        [SerializeField] private float sunsetAzimuth = 240f;
        [Tooltip("Peak sun height at noon, degrees. Real Vermont midwinter is ~23; higher reads better.")]
        [SerializeField] private float maxSunElevation = 35f;

        [Header("Light")]
        [SerializeField] private float sunStrength = 0.6f;
        [SerializeField] private Color dayAmbient = new Color(0.45f, 0.48f, 0.56f);
        [SerializeField] private Color nightAmbient = new Color(0.17f, 0.19f, 0.30f);
        [Tooltip("How much steep slopes darken, independent of the sun. Makes downhill read darker.")]
        [Range(0f, 1f)]
        [SerializeField] private float skyShade = 0.5f;
        [Tooltip("Lighting strength while an overlay is on, so overlay colours stay readable.")]
        [Range(0f, 1f)]
        [SerializeField] private float lightingWithOverlay = 0.3f;

        [Tooltip("Brightness of the glow under trail lights at night.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float lampStrength = 0.9f;

        [Header("Sky")]
        [SerializeField] private Color nightSky = new Color(0.08f, 0.10f, 0.20f);
        [SerializeField] private Color sunsetSky = new Color(0.85f, 0.55f, 0.45f);

        private static readonly int SunDirId = Shader.PropertyToID("_IC_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_IC_SunColor");
        private static readonly int AmbientId = Shader.PropertyToID("_IC_AmbientColor");
        private static readonly int SkyShadeId = Shader.PropertyToID("_IC_SkyShade");
        private static readonly int LightAmountId = Shader.PropertyToID("_IC_LightAmount");
        private static readonly int LampColorId = Shader.PropertyToID("_IC_LampColor");

        private SimulationRunner _runner;
        private ViewState _view;
        private Camera _camera;

        /// <summary>For the HUD: 0 = night, 1 = full sun.</summary>
        public float SunIntensity { get; private set; }

        public void Initialize(SimulationRunner runner, ViewState view, Camera cam)
        {
            _runner = runner;
            _view = view;
            _camera = cam;
        }

        private void LateUpdate()
        {
            var sim = _runner != null ? _runner.Sim : null;
            if (sim == null)
            {
                Shader.SetGlobalFloat(LightAmountId, 0f); // flat colours until a game exists
                return;
            }

            // Smooth clock: whole minutes plus progress toward the next one.
            float tpm = sim.Config.Time.TicksPerGameMinute;
            float minuteFraction = ((sim.World.Tick % (long)tpm) + _runner.InterpolationAlpha) / tpm;
            float hour = (sim.World.Time.MinuteOfDay + minuteFraction) / 60f;

            // Sunrise and sunset from the simulation, so it looks dark exactly when guests treat it as dark.
            float sunriseHour = sim.TimeSystem.SunriseMinute / 60f, sunsetHour = sim.TimeSystem.SunsetMinute / 60f;

            float t = (hour - sunriseHour) / (sunsetHour - sunriseHour); // 0 at sunrise, 1 at sunset
            float azimuth = Mathf.Lerp(sunriseAzimuth, sunsetAzimuth, t);
            float elevation = Mathf.Max(2f, maxSunElevation * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
            // Twilight fades in and out over roughly half an hour either side.
            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.06f, t))
                           * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.06f, 0.94f, t));

            float weather = WeatherSunFactor(sim.World.Weather.Type);
            SunIntensity = daylight * weather;

            Vector3 sunDir = TerrainShading.SunDirection(azimuth, elevation);
            float warmth = Mathf.InverseLerp(20f, 3f, elevation); // low sun = golden
            Color sunColor = Color.Lerp(new Color(1f, 0.97f, 0.92f), new Color(1f, 0.66f, 0.42f), warmth) * sunStrength;
            Color ambient = Color.Lerp(nightAmbient, dayAmbient, daylight);
            ambient = Color.Lerp(ambient, ambient * 1.1f, 1f - weather); // overcast: flatter, softer light

            Shader.SetGlobalVector(SunDirId, new Vector4(sunDir.x, sunDir.y, sunDir.z, SunIntensity));
            Shader.SetGlobalVector(SunColorId, Linear(sunColor));
            Shader.SetGlobalVector(AmbientId, Linear(ambient));
            Shader.SetGlobalFloat(SkyShadeId, skyShade);
            Shader.SetGlobalVector(LampColorId, Linear((Color)Palette.LampLight * lampStrength));
            Shader.SetGlobalFloat(LightAmountId, _view.NaturalLook ? 1f : lightingWithOverlay);

            if (_camera != null)
            {
                Color day = Palette.Sky;
                Color sky = Color.Lerp(nightSky, day, daylight);
                sky = Color.Lerp(sky, sunsetSky, warmth * daylight * 0.45f);
                _camera.backgroundColor = Color.Lerp(sky, Color.Lerp(sky, Color.grey, 0.5f), 1f - weather);
            }
        }

        private static float WeatherSunFactor(WeatherType type)
        {
            switch (type)
            {
                case WeatherType.Clear:
                case WeatherType.ColdSnap: return 1f;
                case WeatherType.WarmSpell: return 0.9f;
                case WeatherType.Cloudy: return 0.45f;
                case WeatherType.LightSnow: return 0.35f;
                case WeatherType.Rain: return 0.3f;
                case WeatherType.HeavySnow: return 0.2f;
                case WeatherType.Fog: return 0.2f;
                default: return 0.1f; // blizzard
            }
        }

        private static Vector4 Linear(Color c)
        {
            var l = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
            return new Vector4(l.r, l.g, l.b, 1f);
        }
    }
}
