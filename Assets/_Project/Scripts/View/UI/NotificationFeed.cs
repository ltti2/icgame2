using System.Collections.Generic;
using IceCoast.Simulation.Events;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// Collects messages for the player: simulation events plus UI messages (saved, load failed...).
    /// The newest one is shown in the status bar; the history feeds the message log window.
    /// Plain C# (not a MonoBehaviour) so any UI implementation can reuse it.
    /// </summary>
    public sealed class NotificationFeed
    {
        public struct Entry
        {
            public SimEvent Event;
            public float ShownAt;
            /// <summary>Game date and time when it happened, for the log.</summary>
            public string When;
        }

        public const int HistorySize = 200;

        private readonly SimulationRunner _runner;
        private readonly List<Entry> _history = new List<Entry>();
        public IReadOnlyList<Entry> History => _history;

        /// <summary>The message shown in the status bar (morning greetings are log-only).</summary>
        public Entry? Latest { get; private set; }
        /// <summary>Bumped whenever a message is added.</summary>
        public int Version { get; private set; }

        public NotificationFeed(SimulationRunner runner)
        {
            _runner = runner;
            runner.EventRaised += OnEvent;
            runner.SimulationChanged += _ =>
            {
                _history.Clear();
                Latest = null;
                Version++;
            };
        }

        private void OnEvent(SimEvent e)
        {
            var entry = new Entry
            {
                Event = e,
                ShownAt = Time.unscaledTime,
                When = Stamp(e),
            };
            _history.Add(entry);
            if (_history.Count > HistorySize) _history.RemoveAt(0);
            if (e.Kind != SimEventKind.DayStarted) Latest = entry;
            Version++;
        }

        /// <summary>Game date and time the event happened (events are drained a frame later, possibly many ticks).</summary>
        private string Stamp(SimEvent e)
        {
            var sim = _runner.Sim;
            if (sim == null) return "";
            var now = sim.World.Time;
            long ticksAgo = System.Math.Max(0L, sim.CurrentTick - e.Tick);
            var then = new Simulation.Core.GameTime
            {
                TotalMinutes = System.Math.Max(0L, now.TotalMinutes - ticksAgo / System.Math.Max(1, sim.Config.Time.TicksPerGameMinute)),
                StartWeekday = now.StartWeekday,
            };
            return $"{then.FormatDate()} {then.FormatClock()}";
        }

        public void Post(string message, EventSeverity severity = EventSeverity.Info)
            => OnEvent(new SimEvent { Message = message, Severity = severity, Location = Simulation.Core.Int2.Invalid, Tick = _runner.Sim?.CurrentTick ?? 0 });
    }
}
