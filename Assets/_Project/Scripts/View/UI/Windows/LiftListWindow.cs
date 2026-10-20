using IceCoast.Simulation.Lifts;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Every lift with its status and line. Click one to open its window and look at it.</summary>
    public sealed class LiftListWindow : UiWindow
    {
        public const string WindowKey = "lifts";
        private readonly GameUi _game;
        private readonly ScrollState _scroll = new ScrollState();

        public LiftListWindow(GameUi game) : base(WindowKey, "Lifts", 270)
        {
            _game = game;
            Scheme = UiTheme.Grey;
            Resizable = true;
            MinSize = new UnityEngine.Vector2(180f, 70f);
        }

        public override string Caption => _game.Sim != null ? $"Lifts ({_game.Sim.World.Lifts.Count})" : "Lifts";

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            if (sim.World.Lifts.Count == 0)
            {
                ui.Paragraph("No lifts yet. Use Build Lift on the toolbar.", ui.Scheme.Text);
                return;
            }

            ui.BeginScroll(_scroll, ListHeight(System.Math.Max(40f, System.Math.Min(160f, sim.World.Lifts.Count * ui.R.LineHeight + 6f))));
            foreach (var lift in sim.World.Lifts)
            {
                string right = lift.IsRunning ? $"{lift.QueueCount} in line" : LiftWindow.StatusText(lift.Status);
                var color = lift.IsRunning ? UiTheme.TextBlack : LiftWindow.StatusColor(lift.Status);
                if (ui.ListRow(lift.Name, lift.Id == _game.View.SelectedLiftId, color, right))
                {
                    _game.OpenLift(lift.Id);
                    _game.Focus(lift.BottomStation);
                }
            }
            ui.EndScroll(_scroll);
        }
    }
}
