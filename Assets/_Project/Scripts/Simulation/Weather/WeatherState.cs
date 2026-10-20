using System.Collections.Generic;

namespace IceCoast.Simulation.Weather
{
    public enum WeatherType : byte
    {
        Clear,
        Cloudy,
        LightSnow,
        HeavySnow,
        Blizzard,
        Rain,
        WarmSpell,
        ColdSnap,
        Fog,
    }

    public struct ForecastEntry
    {
        public WeatherType Type;
        public int DurationHours;
        /// <summary>0..1 confidence; later entries are less certain.</summary>
        public float Confidence;
    }

    public sealed class WeatherState
    {
        public WeatherType Type = WeatherType.Clear;
        /// <summary>Temperature at base elevation, degrees C, including the daily cycle.</summary>
        public float BaseTemperature = -4f;
        public float WindSpeed = 10f;          // km/h
        public float WindDirection;            // radians, 0 = blowing toward +x
        public float Precipitation;            // cm of snow per game hour (water-equivalent * 10 for rain)
        public float Visibility = 1f;          // 0..1
        public int MinutesRemaining = 600;

        /// <summary>Mean (no daily swing) temperature of the current state.</summary>
        public float MeanTemperature = -4f;

        public readonly List<ForecastEntry> Forecast = new List<ForecastEntry>();

        public bool IsSnowing => Type == WeatherType.LightSnow || Type == WeatherType.HeavySnow || Type == WeatherType.Blizzard;
    }
}
