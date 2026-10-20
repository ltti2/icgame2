using System;

namespace IceCoast.Simulation.Core
{
    public enum UnitSystem : byte
    {
        /// <summary>Feet, inches, miles per hour, Fahrenheit. The default (US resort).</summary>
        Imperial,
        /// <summary>Metres, centimetres, km/h, Celsius.</summary>
        Metric,
    }

    /// <summary>
    /// Turns internal values into display strings. The simulation always computes in SI units
    /// (metres, centimetres of snow, km/h wind, degrees Celsius) because the physics constants are SI;
    /// only text shown to the player is converted. Both the simulation's event messages and the HUD
    /// format through the same instance, so switching units changes everything at once.
    /// </summary>
    public sealed class UnitFormatter
    {
        public const float FeetPerMeter = 3.28084f;
        public const float InchesPerCm = 0.393701f;
        public const float MphPerKmh = 0.621371f;

        public UnitSystem System;

        public UnitFormatter(UnitSystem system) { System = system; }

        public bool Imperial => System == UnitSystem.Imperial;

        /// <summary>Distances and lengths: "1,234 ft" / "376 m".</summary>
        public string Length(float meters)
            => Imperial ? $"{meters * FeetPerMeter:N0} ft" : $"{meters:N0} m";

        /// <summary>Elevation above sea level. Same units as length; named separately for clarity at call sites.</summary>
        public string Elevation(float meters) => Length(meters);

        /// <summary>Snow depth: "14 in" / "36 cm".</summary>
        public string SnowDepth(float cm)
            => Imperial ? $"{cm * InchesPerCm:0} in" : $"{cm:0} cm";

        /// <summary>Snowfall rates and other small amounts: "0.8 in/hr" / "2.0 cm/hr".</summary>
        public string SnowRate(float cmPerHour)
            => Imperial ? $"{cmPerHour * InchesPerCm:0.0} in/hr" : $"{cmPerHour:0.0} cm/hr";

        public float TemperatureValue(float celsius) => Imperial ? celsius * 9f / 5f + 32f : celsius;

        /// <summary>"23°F" / "-5°C".</summary>
        public string Temperature(float celsius)
            => Imperial ? $"{TemperatureValue(celsius):0}°F" : $"{celsius:0}°C";

        /// <summary>Wind and other speeds: "18 mph" / "29 km/h".</summary>
        public string Speed(float kmh)
            => Imperial ? $"{kmh * MphPerKmh:0} mph" : $"{kmh:0} km/h";

        public string Toggle() => Imperial ? "°F / ft" : "°C / m";

        public override string ToString() => System.ToString();

        public static UnitSystem Next(UnitSystem s) => (UnitSystem)(((int)s + 1) % Enum.GetValues(typeof(UnitSystem)).Length);
    }
}
