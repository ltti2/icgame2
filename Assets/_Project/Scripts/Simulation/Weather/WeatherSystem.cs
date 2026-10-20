using System;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Weather
{
    /// <summary>
    /// Weather as a state machine with seasonal transition weights and a forecast queue.
    /// The forecast *is* the plan, but each entry can be re-rolled when it arrives
    /// (less likely the more confident it was), so forecasts are useful but not perfect.
    /// </summary>
    public sealed class WeatherSystem : ISimSystem
    {
        private readonly GameSimulation _sim;

        private struct Profile
        {
            public float MeanTemp, TempJitter, Wind, Precip, Visibility, Attendance;
            public Profile(float meanTemp, float jitter, float wind, float precip, float vis, float attendance)
            {
                MeanTemp = meanTemp; TempJitter = jitter; Wind = wind; Precip = precip; Visibility = vis; Attendance = attendance;
            }
        }

        // Indexed by WeatherType.
        private static readonly Profile[] Profiles =
        {
            new Profile(-6f, 4f, 10f, 0f, 1.00f, 1.10f),   // Clear
            new Profile(-4f, 3f, 15f, 0f, 0.85f, 1.00f),   // Cloudy
            new Profile(-5f, 3f, 15f, 0.8f, 0.70f, 1.05f), // LightSnow
            new Profile(-6f, 3f, 25f, 2.5f, 0.45f, 0.85f), // HeavySnow
            new Profile(-11f, 4f, 60f, 3.5f, 0.15f, 0.40f),// Blizzard
            new Profile(3f, 2f, 25f, 0.6f, 0.60f, 0.45f),  // Rain
            new Profile(6f, 3f, 10f, 0f, 1.00f, 0.80f),    // WarmSpell
            new Profile(-18f, 4f, 15f, 0f, 1.00f, 0.70f),  // ColdSnap
            new Profile(-2f, 2f, 3f, 0f, 0.30f, 0.80f),    // Fog
        };

        // Rows: SeasonPhase. Columns: WeatherType.
        private static readonly float[][] SeasonWeights =
        {
            new[] { 20f, 22f, 20f, 8f, 2f, 6f, 6f, 10f, 6f },  // EarlySeason
            new[] { 20f, 20f, 20f, 10f, 4f, 3f, 3f, 14f, 6f }, // Holidays
            new[] { 20f, 20f, 20f, 10f, 4f, 3f, 3f, 14f, 6f }, // MidWinter
            new[] { 24f, 20f, 16f, 7f, 2f, 6f, 8f, 6f, 8f },   // LateSeason
            new[] { 30f, 20f, 8f, 3f, 1f, 14f, 20f, 0f, 4f },  // Spring
        };

        public WeatherSystem(GameSimulation sim) { _sim = sim; }

        private WeatherState State => _sim.World.Weather;

        /// <summary>Called once for a new game to seed the first state and forecast.</summary>
        public void Initialize()
        {
            var s = State;
            s.Forecast.Clear();
            ApplyType(WeatherType.LightSnow, 8);
            RefillForecast();
            UpdateContinuous();
        }

        public void Tick()
        {
            if (!_sim.Clock.MinuteAdvanced) return;
            var s = State;
            s.MinutesRemaining--;
            if (s.MinutesRemaining <= 0) Advance();
            UpdateContinuous();
        }

        private void Advance()
        {
            var rng = _sim.World.Rng;
            var s = State;
            var next = s.Forecast[0];
            s.Forecast.RemoveAt(0);

            WeatherType type = next.Type;
            if (rng.Chance((1f - next.Confidence) * 0.5f))
                type = PickNext(s.Type); // the forecast was wrong

            WeatherType previous = s.Type;
            ApplyType(type, next.DurationHours);
            RefillForecast();

            if (type != previous)
            {
                var severity = type == WeatherType.Blizzard || type == WeatherType.Rain || type == WeatherType.WarmSpell
                    ? EventSeverity.Warning : EventSeverity.Info;
                _sim.Events.Publish(SimEventKind.WeatherChanged, severity, $"Weather: {Describe(type)}");
            }
        }

        private void ApplyType(WeatherType type, int hours)
        {
            var rng = _sim.World.Rng;
            var s = State;
            var p = Profiles[(int)type];
            s.Type = type;
            s.MinutesRemaining = Math.Max(1, hours) * 60;
            s.MeanTemperature = p.MeanTemp + rng.Range(-p.TempJitter, p.TempJitter);
            s.WindSpeed = p.Wind * rng.Range(0.7f, 1.3f);
            s.WindDirection = rng.Range(0f, (float)(Math.PI * 2));
            s.Precipitation = p.Precip * rng.Range(0.7f, 1.3f);
            s.Visibility = p.Visibility;
        }

        private void RefillForecast()
        {
            var cfg = _sim.Config.Weather;
            var rng = _sim.World.Rng;
            var s = State;
            WeatherType last = s.Forecast.Count > 0 ? s.Forecast[s.Forecast.Count - 1].Type : s.Type;
            while (s.Forecast.Count < cfg.ForecastLength)
            {
                var type = PickNext(last);
                s.Forecast.Add(new ForecastEntry
                {
                    Type = type,
                    DurationHours = rng.Range(cfg.MinStateHours, cfg.MaxStateHours + 1),
                    Confidence = 0.9f - 0.15f * s.Forecast.Count,
                });
                last = type;
            }
            // Entries move one step closer: they become more certain.
            for (int i = 0; i < s.Forecast.Count; i++)
            {
                var e = s.Forecast[i];
                e.Confidence = 0.9f - 0.15f * i;
                s.Forecast[i] = e;
            }
        }

        private WeatherType PickNext(WeatherType current)
        {
            var phase = _sim.World.Time.Phase;
            var weights = (float[])SeasonWeights[(int)phase].Clone();
            weights[(int)current] *= 0.5f;              // discourage long repeats
            if (current == WeatherType.Blizzard) weights[(int)WeatherType.Blizzard] = 0f;
            if (current == WeatherType.Rain) weights[(int)WeatherType.ColdSnap] *= 2f; // the dreaded refreeze
            return (WeatherType)_sim.World.Rng.PickWeighted(weights);
        }

        /// <summary>Daily temperature swing: coldest ~6 AM, warmest ~2 PM.</summary>
        private void UpdateContinuous()
        {
            var s = State;
            float hour = _sim.World.Time.MinuteOfDay / 60f;
            float swing = (float)Math.Cos((hour - 14f) / 24f * Math.PI * 2) * 3.5f;
            s.BaseTemperature = s.MeanTemperature + swing;
        }

        // ---------- Queries used by other systems ----------

        public float AttendanceFactor => Profiles[(int)State.Type].Attendance;

        /// <summary>Air temperature at a tile, per the design doc formula.</summary>
        public float TemperatureAt(int x, int y)
        {
            var map = _sim.World.Map;
            float elevation = TerrainQueries.ElevationMeters(map, _sim.TerrainScale, x, y);
            float t = State.BaseTemperature - _sim.Config.Weather.LapseRateCPerMeter * (elevation - _sim.Config.Weather.BaseElevationMeters);

            bool daytime = _sim.World.Time.Hour >= 9 && _sim.World.Time.Hour < 16;
            if (daytime && State.Type == WeatherType.Clear)
                t += TerrainQueries.SunExposure(map, x, y) * 2f;
            t -= map.GetTreeDensity(x, y) * 0.3f; // shade
            return t;
        }

        /// <summary>Elevation multiplier on snowfall: more snow up high.</summary>
        public float SnowfallElevationFactor(float elevationMeters)
        {
            float baseM = _sim.Config.Weather.BaseElevationMeters;
            return 1f + Math.Max(0f, elevationMeters - baseM) / 600f;
        }

        public static string Describe(WeatherType type)
        {
            switch (type)
            {
                case WeatherType.LightSnow: return "Light snow";
                case WeatherType.HeavySnow: return "Heavy snow";
                case WeatherType.WarmSpell: return "Warm spell";
                case WeatherType.ColdSnap: return "Cold snap";
                default: return type.ToString();
            }
        }
    }
}
