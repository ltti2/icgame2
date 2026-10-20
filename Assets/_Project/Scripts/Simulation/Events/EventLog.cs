using System.Collections.Generic;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Events
{
    public enum EventSeverity : byte { Info, Good, Warning, Danger }

    public enum SimEventKind : byte
    {
        General,
        CommandRejected,
        LiftBuilt,
        LiftClosed,
        LiftOpened,
        RunCreated,
        ParcelBought,
        Injury,
        GuestComplaint,
        WeatherChanged,
        DayStarted,
        DayEnded,
        CashWarning,
        Bankrupt,
    }

    /// <summary>
    /// Something the player may want to know about. Carries an optional map location so the UI can
    /// offer "jump to". Also the seed of the causal log ("why did satisfaction drop?").
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public EventSeverity Severity;
        public string Message;
        public Int2 Location;
        public int EntityId;
        public long Tick;

        public bool HasLocation => Location.IsValid;
    }

    /// <summary>
    /// Collects events raised during ticks. The view drains <see cref="Pending"/> each frame;
    /// <see cref="History"/> keeps the most recent ones for a log panel.
    /// </summary>
    public sealed class EventLog
    {
        public const int HistoryLimit = 200;

        private readonly List<SimEvent> _pending = new List<SimEvent>();
        private readonly Queue<SimEvent> _history = new Queue<SimEvent>();
        private long _currentTick;

        public IEnumerable<SimEvent> History => _history;

        internal void SetTick(long tick) => _currentTick = tick;

        public void Publish(SimEventKind kind, EventSeverity severity, string message, Int2? location = null, int entityId = 0)
        {
            var e = new SimEvent
            {
                Kind = kind,
                Severity = severity,
                Message = message,
                Location = location ?? Int2.Invalid,
                EntityId = entityId,
                Tick = _currentTick,
            };
            _pending.Add(e);
            _history.Enqueue(e);
            while (_history.Count > HistoryLimit) _history.Dequeue();
        }

        /// <summary>Moves all pending events into <paramref name="into"/> and clears the pending list.</summary>
        public void Drain(List<SimEvent> into)
        {
            into.AddRange(_pending);
            _pending.Clear();
        }
    }
}
