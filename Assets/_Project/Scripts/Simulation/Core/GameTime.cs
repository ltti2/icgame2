namespace IceCoast.Simulation.Core
{
    public enum Weekday : byte { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

    public enum SeasonPhase : byte { EarlySeason, Holidays, MidWinter, LateSeason, Spring }

    /// <summary>
    /// In-game calendar. Stored as total elapsed game minutes since the season start
    /// (Dec 1, 00:00). Everything else is derived so there is a single source of truth.
    /// </summary>
    public sealed class GameTime
    {
        public const int MinutesPerHour = 60;
        public const int MinutesPerDay = 24 * 60;

        public long TotalMinutes;

        /// <summary>Weekday of day 0. Dec 1 2026 is a Tuesday.</summary>
        public Weekday StartWeekday = Weekday.Tuesday;

        private static readonly string[] MonthNames = { "Dec", "Jan", "Feb", "Mar", "Apr", "May" };
        private static readonly int[] MonthLengths = { 31, 31, 28, 31, 30, 31 };

        public int Day => (int)(TotalMinutes / MinutesPerDay);
        public int MinuteOfDay => (int)(TotalMinutes % MinutesPerDay);
        public int Hour => MinuteOfDay / MinutesPerHour;
        public int Minute => MinuteOfDay % MinutesPerHour;
        public Weekday Weekday => (Weekday)(((int)StartWeekday + Day) % 7);
        public bool IsWeekend => Weekday == Weekday.Saturday || Weekday == Weekday.Sunday;

        /// <summary>Christmas through New Year's Day: peak demand.</summary>
        public bool IsHolidayPeriod => Day >= 23 && Day <= 31;

        public SeasonPhase Phase
        {
            get
            {
                int d = Day;
                if (d < 20) return SeasonPhase.EarlySeason;
                if (d <= 32) return SeasonPhase.Holidays;
                if (d < 90) return SeasonPhase.MidWinter;
                if (d < 110) return SeasonPhase.LateSeason;
                return SeasonPhase.Spring;
            }
        }

        public void GetCalendarDate(out int monthIndex, out int dayOfMonth)
        {
            int d = Day;
            monthIndex = 0;
            while (monthIndex < MonthLengths.Length - 1 && d >= MonthLengths[monthIndex])
            {
                d -= MonthLengths[monthIndex];
                monthIndex++;
            }
            dayOfMonth = d + 1;
        }

        public string FormatDate()
        {
            GetCalendarDate(out int m, out int dom);
            return $"{Weekday.ToString().Substring(0, 3)} {MonthNames[m]} {dom}";
        }

        public string FormatClock()
        {
            int h = Hour;
            string suffix = h >= 12 ? "PM" : "AM";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{h12}:{Minute:00} {suffix}";
        }

        public override string ToString() => $"{FormatDate()}, {FormatClock()}";
    }
}
