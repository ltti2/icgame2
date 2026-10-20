using System;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Time
{
    /// <summary>
    /// Sunrise and sunset for a game day, from the standard solar-position approximation (declination plus
    /// the equation of time) at the resort's latitude and longitude, on a US Eastern clock with daylight saving
    /// from the second Sunday in March. Killington on Dec 1: sunrise about 7:05, sunset about 4:15 PM.
    ///
    /// Pure functions of the day number and config, so there is nothing to save; the simulation and the
    /// view's sky both read it, which keeps "dark" in the rules and dark on screen in step.
    /// </summary>
    public static class Daylight
    {
        /// <summary>Day of the year (1..365) of season day 0 (Dec 1).</summary>
        private const int SeasonStartDayOfYear = 335;
        /// <summary>Season day of March 1 (Dec 31 + Jan 31 + Feb 28).</summary>
        private const int March1 = 90;

        /// <summary>Sunrise and sunset as minutes after midnight on the local clock.</summary>
        public static void SunTimes(SimConfig.TimeSettings cfg, int seasonDay, Weekday startWeekday, out int sunriseMinute, out int sunsetMinute)
        {
            int n = (SeasonStartDayOfYear + seasonDay - 1) % 365 + 1;
            double rad = Math.PI / 180.0;

            double decl = -23.44 * Math.Cos(2.0 * Math.PI / 365.0 * (n + 10));
            double lat = cfg.LatitudeDegrees;
            double cosH = (Math.Sin(-0.833 * rad) - Math.Sin(lat * rad) * Math.Sin(decl * rad))
                        / (Math.Cos(lat * rad) * Math.Cos(decl * rad));
            cosH = Math.Max(-1.0, Math.Min(1.0, cosH));
            double halfDayMinutes = Math.Acos(cosH) / rad * 4.0; // 4 minutes per degree of hour angle

            double b = 2.0 * Math.PI * (n - 81) / 364.0;
            double equationOfTime = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b);
            double solarNoon = 12 * 60 - equationOfTime - (cfg.LongitudeDegrees - cfg.TimeZoneMeridianDegrees) * 4.0;
            if (IsDaylightSaving(seasonDay, startWeekday)) solarNoon += 60;

            sunriseMinute = (int)Math.Round(solarNoon - halfDayMinutes);
            sunsetMinute = (int)Math.Round(solarNoon + halfDayMinutes);
        }

        /// <summary>US daylight saving starts on the second Sunday in March.</summary>
        public static bool IsDaylightSaving(int seasonDay, Weekday startWeekday)
        {
            int firstSunday = March1;
            while ((Weekday)(((int)startWeekday + firstSunday) % 7) != Weekday.Sunday) firstSunday++;
            return seasonDay >= firstSunday + 7;
        }

        /// <summary>Dark: more than the twilight allowance after sunset or before sunrise.</summary>
        public static bool IsDark(int minuteOfDay, int sunriseMinute, int sunsetMinute, int twilightMinutes)
            => minuteOfDay >= sunsetMinute + twilightMinutes || minuteOfDay < sunriseMinute - twilightMinutes;
    }
}
