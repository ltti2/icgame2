using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Resort overview: guests, reputation, today's numbers and how yesterday went.</summary>
    public sealed class ResortWindow : UiWindow
    {
        private static string Clock(int minuteOfDay)
        {
            int h = minuteOfDay / 60, m = minuteOfDay % 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }

        public const string WindowKey = "resort";
        private readonly GameUi _game;

        public ResortWindow(GameUi game) : base(WindowKey, "Resort", 236)
        {
            _game = game;
            Scheme = UiTheme.Brown;
        }

        public override string Caption => _game.Sim != null && _game.Sim.Config.SandboxMode ? "Resort (sandbox)" : "Resort";

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            var world = sim.World;
            var econ = world.Economy;
            var today = econ.Today;

            ui.Field("Guests on the mountain", world.Guests.Count.ToString("N0"));
            ui.Field("Reputation", $"{econ.Reputation:0} / 100");
            ui.Field("Lifts / runs", $"{world.Lifts.Count} / {world.Runs.Count}");
            ui.Field("Expected arrivals", $"{sim.ArrivalSystem.CurrentRatePerHour:0}/hr");
            ui.Field("Fair ticket price (guests)", $"~${sim.ArrivalSystem.LastFairPrice:0}");

            ui.Separator();
            ui.Heading("Parking and night skiing");
            int cap = sim.ParkingSystem.TotalCapacity, used = sim.ParkingSystem.TotalOccupied;
            ui.Field("Cars in the lot", $"{used} / {cap}", used >= cap ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger) : ui.Scheme.Text);
            if (today.GuestsTurnedAway > 0)
                ui.Field("Turned away (lot full)", $"{today.GuestsTurnedAway} guests", GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Warning));
            ui.Field("Sunset", Clock(sim.TimeSystem.SunsetMinute));
            ui.Field("Trail lights", sim.LightingSystem.LightsOn ? $"{world.Lights.Count}, on" : $"{world.Lights.Count}, off");
            bool nightSkiing = ui.Checkbox("Night skiing", world.NightSkiing, "Lifts with a lit trail off the top run until " + Clock(sim.Config.Time.NightCloseMinute));
            if (nightSkiing != world.NightSkiing) _game.Runner.Submit(new Simulation.Commands.SetNightSkiingCommand(nightSkiing));
            if (world.NightSkiing && !sim.LightingSystem.NightTerrainAvailable)
                ui.Paragraph("No lift has a lit trail off its top yet. Put up trail lights.", ui.Scheme.Text);

            ui.Separator();
            ui.Heading("Today");
            ui.Field("Arrived / peak", $"{today.GuestsArrived:N0} / {today.PeakGuests:N0}");
            ui.Field("Lift rides", today.LiftRides.ToString("N0"));
            ui.Field("Runs skied", today.RunsCompleted.ToString("N0"));
            ui.Field("Injuries", today.Injuries.ToString(), today.Injuries > 0 ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger) : ui.Scheme.Text);
            if (today.GuestsDeparted > 0) ui.Field("Happiness (leaving)", $"{today.AverageHappiness:0}%");
            ui.Field("Profit so far", _game.Money(today.Profit), today.Profit >= 0 ? ui.Scheme.Text : GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger));

            if (econ.History.Count > 0)
            {
                var y = econ.History[econ.History.Count - 1];
                ui.Separator();
                ui.Heading("Yesterday");
                ui.Field("Guests", y.GuestsArrived.ToString("N0"));
                ui.Field("Profit", _game.Money(y.Profit), y.Profit >= 0 ? ui.Scheme.Text : GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger));
                if (y.GuestsDeparted > 0) ui.Field("Happiness", $"{y.AverageHappiness:0}%");
            }
        }
    }
}
