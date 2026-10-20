using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Lifts;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>One lift: status, capacity, queue, wind, and open/close/demolish. One window per lift.</summary>
    public sealed class LiftWindow : UiWindow
    {
        private readonly GameUi _game;
        private readonly int _liftId;
        private bool _confirmDemolish;

        public static string KeyFor(int liftId) => "lift:" + liftId;

        public LiftWindow(GameUi game, int liftId) : base(KeyFor(liftId), "Lift", 236)
        {
            _game = game;
            _liftId = liftId;
            Scheme = UiTheme.Brown;
        }

        private Lift Lift => _game.Sim?.World.GetLift(_liftId);

        public override string Caption => Lift?.Name ?? "Lift";
        public override string CaptionAction => "inspect";
        public override string CaptionActionTooltip => "Centre the view on this lift";
        public override void OnCaptionAction()
        {
            var lift = Lift;
            if (lift != null) _game.Focus(lift.BottomStation);
        }

        public override bool StillValid() => Lift != null;

        public override void OnClosed()
        {
            if (_game.View.SelectedLiftId == _liftId) _game.View.SelectedLiftId = 0;
        }

        public override void DrawContent(UiContext ui)
        {
            var lift = Lift;
            if (lift == null) return;
            var sim = _game.Sim;
            var def = sim.Config.GetLift(lift.Type);
            var units = sim.Units;

            ui.Field(def.DisplayName, StatusText(lift.Status), StatusColor(lift.Status));
            int seats = sim.LiftSystem.Seats(lift);
            ui.Field("Capacity", $"{lift.CapacityPerHour:N0} riders/hr");
            if (seats > 1)
                ui.Field("Seats per carrier", $"{seats} (~{sim.LiftSystem.EffectiveCapacity(lift):N0}/hr with a line)");
            ui.Field("Ride time", $"{lift.RideTicks / (float)sim.Config.Time.TicksPerGameMinute:0.0} min");
            ui.Field("Length", units.Length(lift.LengthTiles * sim.TerrainScale.TileSizeMeters));
            ui.Field("Vertical rise", units.Length(lift.RiseSteps * sim.TerrainScale.MetersPerStep));
            ui.Separator();
            ui.Field("In line", $"{lift.QueueCount} (~{lift.EstimatedWaitMinutes:0} min wait)", lift.EstimatedWaitMinutes > 10 ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger) : ui.Scheme.Text);
            ui.Field("On the lift", lift.Riders.Count.ToString());
            ui.Field("Riders today", lift.RidersToday.ToString("N0"));
            ui.Field("Wind at the top", $"{units.Speed(sim.LiftSystem.EffectiveWindAtTop(lift))} (limit {units.Speed(lift.WindLimit)})");
            ui.Field("Running cost", $"${lift.OperatingCostPerHour:N0}/hr");
            if (lift.RunsFromTop.Count == 0) ui.Label("No runs start at the top!", GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger));
            else ui.Field("Runs from the top", lift.RunsFromTop.Count.ToString());
            bool night = sim.LiftSystem.RunsAtNight(lift);
            ui.Field("Night skiing", night ? $"Until {Clock(sim.Config.Time.NightCloseMinute)}" : sim.World.NightSkiing ? "No lit trail off the top" : "Switched off",
                     night ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Good) : ui.Scheme.Text);

            ui.Space(2);
            var row = ui.Row(UiContext.ButtonHeight);
            if (ui.Button(UiContext.Cell(row, 0, 2), lift.PlayerOpen ? "Close lift" : "Open lift"))
                _game.Runner.Submit(new SetLiftOpenCommand(lift.Id, !lift.PlayerOpen));
            if (!_confirmDemolish)
            {
                if (ui.Button(UiContext.Cell(row, 1, 2), "Demolish...")) _confirmDemolish = true;
            }
            else
            {
                var cell = UiContext.Cell(row, 1, 2);
                if (ui.Button(UiContext.Cell(cell, 0, 2), "Yes!", true, false, "Demolish this lift for good"))
                {
                    _game.Runner.Submit(new DemolishLiftCommand(lift.Id));
                    _game.View.ClearSelection();
                }
                if (ui.Button(UiContext.Cell(cell, 1, 2), "No")) _confirmDemolish = false;
            }
        }

        private static string Clock(int minuteOfDay)
        {
            int h = minuteOfDay / 60, m = minuteOfDay % 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }

        public static string StatusText(LiftStatus s)
        {
            switch (s)
            {
                case LiftStatus.Open: return "Running";
                case LiftStatus.Closed: return "Closed";
                case LiftStatus.AfterHours: return "After hours";
                case LiftStatus.WindHold: return "Wind hold";
                case LiftStatus.Broken: return "Broken down";
                default: return "No power";
            }
        }

        public static UnityEngine.Color32 StatusColor(LiftStatus s)
            => s == LiftStatus.Open ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Good)
             : s == LiftStatus.AfterHours ? UiTheme.TextBlack
             : GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger);
    }
}
