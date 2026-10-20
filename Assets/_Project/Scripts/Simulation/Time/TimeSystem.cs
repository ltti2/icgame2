using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;

namespace IceCoast.Simulation.Time
{
    /// <summary>
    /// Advances the game clock. A game minute passes every <c>TicksPerGameMinute</c> ticks; the
    /// sub-minute phase is derived from World.Tick so there is nothing extra to save.
    ///
    /// Also answers "is the resort open?". The day session runs from LiftsOpenMinute to LiftsCloseMinute.
    /// With night skiing on and at least one lift serving a lit trail, a night session follows until
    /// NightCloseMinute; only lifts with lit terrain off the top run in it (LiftSystem decides per lift).
    /// </summary>
    public sealed class TimeSystem : ISimSystem
    {
        private readonly GameSimulation _sim;
        private int _sunDay = -1;
        private int _sunrise, _sunset;

        public TimeSystem(GameSimulation sim) { _sim = sim; }

        public int TicksPerMinute => _sim.Config.Time.TicksPerGameMinute;
        public int TicksPerHour => TicksPerMinute * GameTime.MinutesPerHour;

        public void Tick()
        {
            var signals = new ClockSignals();
            if (_sim.World.Tick % TicksPerMinute == 0)
            {
                var time = _sim.World.Time;
                int dayBefore = time.Day;
                time.TotalMinutes++;
                signals.MinuteAdvanced = true;
                signals.HourAdvanced = time.Minute == 0;
                signals.DayAdvanced = time.Day != dayBefore;

                if (signals.DayAdvanced)
                    _sim.Events.Publish(SimEventKind.DayStarted, EventSeverity.Info, $"Good morning! It's {time.FormatDate()}.");
            }
            _sim.Clock = signals;
        }

        // ---------- Sun ----------

        private void EnsureSunTimes()
        {
            int day = _sim.World.Time.Day;
            if (day == _sunDay) return;
            _sunDay = day;
            Daylight.SunTimes(_sim.Config.Time, day, _sim.World.Time.StartWeekday, out _sunrise, out _sunset);
        }

        /// <summary>Today's sunrise, minutes after midnight.</summary>
        public int SunriseMinute { get { EnsureSunTimes(); return _sunrise; } }
        /// <summary>Today's sunset, minutes after midnight.</summary>
        public int SunsetMinute { get { EnsureSunTimes(); return _sunset; } }

        /// <summary>Dark enough that unlit trails are skied only by guests who have no other way down.</summary>
        public bool IsDark => Daylight.IsDark(_sim.World.Time.MinuteOfDay, SunriseMinute, SunsetMinute, _sim.Config.Time.TwilightMinutes);

        // ---------- Operating hours ----------

        /// <summary>Within the day session.</summary>
        public bool DayOperating
        {
            get
            {
                int m = _sim.World.Time.MinuteOfDay;
                var t = _sim.Config.Time;
                return m >= t.LiftsOpenMinute && m < t.LiftsCloseMinute;
            }
        }

        /// <summary>Night skiing is on and at least one lift has a lit trail off its top.</summary>
        public bool NightSessionAvailable => _sim.World.NightSkiing && _sim.LightingSystem.NightTerrainAvailable;

        /// <summary>Within the night session (only lifts serving lit trails run).</summary>
        public bool NightOperating
        {
            get
            {
                if (!NightSessionAvailable) return false;
                int m = _sim.World.Time.MinuteOfDay;
                var t = _sim.Config.Time;
                return m >= t.LiftsCloseMinute && m < t.NightCloseMinute;
            }
        }

        /// <summary>Some lifts may be running (day session, or night session for lit lifts).</summary>
        public bool LiftsOperating => DayOperating || NightOperating;

        /// <summary>When the last lift stops today: the night close if there is a night session, else the day close.</summary>
        public int ClosingMinute => NightSessionAvailable ? _sim.Config.Time.NightCloseMinute : _sim.Config.Time.LiftsCloseMinute;
    }
}
