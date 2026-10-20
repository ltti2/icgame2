using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Runs;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>One run: rating, slopes, snow, traffic, issues; rename, re-sign, open/close, delete.</summary>
    public sealed class RunWindow : UiWindow
    {
        private static readonly string[] SignLabels = { "Auto", "Grn", "Blu", "Blk", "2Blk", "3Blk" };

        private readonly GameUi _game;
        private readonly int _runId;
        private string _nameBuffer;
        private bool _confirmDelete;
        private readonly List<string> _connections = new List<string>();

        public static string KeyFor(int runId) => "run:" + runId;

        public RunWindow(GameUi game, int runId) : base(KeyFor(runId), "Run", 246)
        {
            _game = game;
            _runId = runId;
            Scheme = UiTheme.Brown;
        }

        private Run Run => _game.Sim?.World.GetRun(_runId);

        public override string Caption => Run?.Name ?? "Run";
        public override string CaptionAction => "inspect";
        public override string CaptionActionTooltip => "Centre the view on this run";
        public override void OnCaptionAction()
        {
            var run = Run;
            if (run != null) _game.Focus(run.Start);
        }

        public override bool StillValid() => Run != null;

        public override void OnClosed()
        {
            if (_game.View.SelectedRunId == _runId) _game.View.SelectedRunId = 0;
        }

        public override void DrawContent(UiContext ui)
        {
            var run = Run;
            if (run == null) return;
            var sim = _game.Sim;
            var units = sim.Units;
            float tileM = sim.TerrainScale.TileSizeMeters;
            if (_nameBuffer == null) _nameBuffer = run.Name;

            // Rename
            var nameRow = ui.Row(UiContext.ButtonHeight);
            var fieldRect = new UnityEngine.Rect(nameRow.x, nameRow.y, nameRow.width - 54, nameRow.height);
            ui.BeginArea(fieldRect);
            bool enter = ui.TextField(ref _nameBuffer, 32);
            ui.EndArea();
            if (ui.Button(UiContext.SplitRight(nameRow, 50), "Rename") || enter)
                _game.Runner.Submit(new RenameRunCommand(run.Id, _nameBuffer));

            ui.Field("Signed as", RunRater.Label(run.DisplayedRating), GameUi.DifficultyText(run.DisplayedRating));
            ui.Field("Really skis like", RunRater.Label(run.ComputedRating), GameUi.DifficultyText(run.ComputedRating));
            ui.Field("Average / steepest", $"{run.AvgSlope:0}% / {run.MaxSlope:0}%");
            ui.Field("Length", units.Length(run.PathLength * tileM));
            ui.Field("Average width", units.Length(run.AverageWidth * tileM));
            ui.Field("Snow quality", $"{run.AvgSnowQuality * 100:0}%");
            var lighting = sim.LightingSystem;
            bool lit = lighting.HasLights(run);
            ui.Field("Lit for night skiing", lit ? $"Yes ({run.LitFraction * 100:0}%)" : $"No ({run.LitFraction * 100:0}% of {sim.Config.Lighting.RunLitFraction * 100:0}%)",
                     lit ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Good) : ui.Scheme.Text);
            if (lighting.IsRunDark(run)) ui.Label("Dark now: only skied by guests who must.", GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Warning));
            ui.Field("Skiers today", run.SkiersToday.ToString("N0"));
            ui.Field("Injuries today", run.InjuriesToday.ToString(), run.InjuriesToday > 0 ? GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger) : ui.Scheme.Text);

            if (run.Exits.Count > 0)
            {
                _connections.Clear();
                foreach (var e in run.Exits)
                {
                    if (e.Kind == RunExitKind.Lift) { var l = sim.World.GetLift(e.TargetId); if (l != null) _connections.Add(l.Name); }
                    else { var r = sim.World.GetRun(e.TargetId); if (r != null) _connections.Add(r.Name); }
                }
                ui.Paragraph("Connects to: " + string.Join(", ", _connections), ui.Scheme.Text);
            }

            if (run.Issues != RunIssues.None)
            {
                var warn = GameUi.SeverityOnLight(Simulation.Events.EventSeverity.Danger);
                if ((run.Issues & RunIssues.NoLiftAtTop) != 0) ui.Paragraph("- No lift or connected trail feeds it.", warn);
                if ((run.Issues & RunIssues.WalkOut) != 0) ui.Paragraph("- No way down to a lift, even via other trails: guests walk out.", warn);
                if ((run.Issues & RunIssues.HasUphill) != 0) ui.Paragraph("- Has an uphill section.", warn);
                if ((run.Issues & RunIssues.Icy) != 0) ui.Paragraph("- Icy.", warn);
                if ((run.Issues & RunIssues.Mislabelled) != 0) ui.Paragraph("- Signed easier than it is. Lawyers love this.", warn);
            }

            ui.Separator();
            ui.Label("Sign the run as:");
            int current = run.RatingOverride.HasValue ? (int)run.RatingOverride.Value + 1 : 0;
            int chosen = ui.Toggles(current, SignLabels);
            if (chosen != current)
                _game.Runner.Submit(new SetRunRatingCommand(run.Id, chosen == 0 ? (Difficulty?)null : (Difficulty)(chosen - 1)));

            if (ui.Button("Edit tiles (paint / erase)", true, false, "Add tiles to this trail or erase some, keeping its name and sign"))
            {
                var tool = _game.Tools.Get<IceCoast.View.Tools.PaintRunTool>();
                tool.BeginEdit(run.Id);
                _game.Tools.Activate(tool);
            }

            var row = ui.Row(UiContext.ButtonHeight);
            if (ui.Button(UiContext.Cell(row, 0, 2), run.Open ? "Close run" : "Open run"))
                _game.Runner.Submit(new SetRunOpenCommand(run.Id, !run.Open));
            if (!_confirmDelete)
            {
                if (ui.Button(UiContext.Cell(row, 1, 2), "Delete...")) _confirmDelete = true;
            }
            else
            {
                var cell = UiContext.Cell(row, 1, 2);
                if (ui.Button(UiContext.Cell(cell, 0, 2), "Yes!", true, false, "Delete this run"))
                {
                    _game.Runner.Submit(new DeleteRunCommand(run.Id));
                    _game.View.ClearSelection();
                }
                if (ui.Button(UiContext.Cell(cell, 1, 2), "No")) _confirmDelete = false;
            }
        }
    }
}
