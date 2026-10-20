using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Every run, coloured by its sign, with today's skiers. Click one to open it.</summary>
    public sealed class RunListWindow : UiWindow
    {
        public const string WindowKey = "runs";
        private readonly GameUi _game;
        private readonly ScrollState _scroll = new ScrollState();

        public RunListWindow(GameUi game) : base(WindowKey, "Runs", 270)
        {
            _game = game;
            Scheme = UiTheme.Grey;
            Resizable = true;
            MinSize = new UnityEngine.Vector2(180f, 70f);
        }

        public override string Caption => _game.Sim != null ? $"Runs ({_game.Sim.World.Runs.Count})" : "Runs";

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            if (sim.World.Runs.Count == 0)
            {
                ui.Paragraph("No runs yet. Use Paint Run on the toolbar.", ui.Scheme.Text);
                return;
            }

            ui.BeginScroll(_scroll, ListHeight(System.Math.Max(40f, System.Math.Min(180f, sim.World.Runs.Count * ui.R.LineHeight + 6f))));
            foreach (var run in sim.World.Runs)
            {
                string right = run.Open ? $"{run.SkiersToday} skiers" : "Closed";
                if (ui.ListRow(run.Name, run.Id == _game.View.SelectedRunId, GameUi.DifficultyText(run.DisplayedRating), right))
                {
                    _game.OpenRun(run.Id);
                    _game.Focus(run.Start);
                }
            }
            ui.EndScroll(_scroll);
        }
    }
}
