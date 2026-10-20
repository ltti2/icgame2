using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.UI;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// The trail editor. Two brush modes (Shift flips between them while held):
    ///  - <b>Paint</b>: with "New trail" picked, paint tiles for a new run and press Enter (or Create) to make it;
    ///    with an existing trail picked, every stroke adds tiles to that trail as soon as the mouse is released.
    ///  - <b>Erase</b>: every stroke takes the tiles under it off whatever trails they belong to (and off the
    ///    new trail being painted). The trails keep their names and settings; nothing needs clearing.
    /// A stroke that would cut a trail in two, or leave it too short, is shown in red and not applied.
    ///
    /// Uphill travel is shown in yellow, both on the run being painted and on existing runs, so it's
    /// obvious where skiers would have to side-step or skate. The painted run's direction is worked out
    /// exactly as the game will: a flow field from its lowest tile.
    /// </summary>
    public sealed class PaintRunTool : ToolBase
    {
        /// <summary>A step counts as uphill when the next tile is this many metres higher.</summary>
        private const float UphillThresholdMeters = 0.5f;

        public readonly TileBrush Brush = new TileBrush();
        /// <summary>Trail being edited: 0 = painting a new one.</summary>
        public int EditRunId { get; private set; }

        private readonly HashSet<Int2> _pending = new HashSet<Int2>();
        private string _name = "";

        // Stroke preview for edits of existing trails.
        private readonly Dictionary<int, List<Int2>> _eraseByRun = new Dictionary<int, List<Int2>>();
        private readonly List<Int2> _addToRun = new List<Int2>();
        private CommandResult _strokeResult = CommandResult.Success();
        private int _strokeSize = -1;

        // Uphill analysis caches.
        private readonly HashSet<Int2> _pendingUphill = new HashSet<Int2>();
        private int _pendingVersion, _analysedPendingVersion = -1;
        private readonly List<Int2> _existingUphill = new List<Int2>();
        private int _analysedRunsVersion = -1;
        private readonly MinHeap<Int2> _heap = new MinHeap<Int2>();
        private readonly Dictionary<Int2, float> _dist = new Dictionary<Int2, float>();

        private ToolContext _ctx;

        public override string Name => "Trail Editor";
        public override string Icon => "run";
        public override string Help => "Paint a new trail or add to the picked one; Erase (or Shift-drag) removes tiles from any trail. Enter creates.";

        public override void OnActivated(ToolContext ctx)
        {
            _ctx = ctx;
            _analysedRunsVersion = -1;
            if (EditRunId != 0) ctx.View.SelectedRunId = EditRunId;
        }

        public override void OnDeactivated(ToolContext ctx)
        {
            ClearPending();
            Brush.CancelStroke();
        }

        /// <summary>Starts editing an existing trail's tiles (from its window's "Edit tiles" button).</summary>
        public void BeginEdit(int runId)
        {
            EditRunId = runId;
            ClearPending();
            if (_ctx != null) _ctx.View.SelectedRunId = runId;
        }

        public void BeginNewTrail()
        {
            if (_ctx != null && _ctx.View.SelectedRunId == EditRunId) _ctx.View.SelectedRunId = 0;
            EditRunId = 0;
        }

        public override void Tick(ToolContext ctx)
        {
            _ctx = ctx;
            var sim = ctx.Sim;
            var map = sim.Map;
            if (EditRunId != 0 && sim.World.GetRun(EditRunId) == null) BeginNewTrail();

            Brush.HandleKeys();
            bool erasing = Brush.Erasing;
            bool newTrail = EditRunId == 0;

            // Painting a new trail is live (it only exists once you press Enter). Everything else is per stroke.
            if (newTrail && !erasing && Brush.StrokeActive == false && GameInput.LeftHeld && ctx.HasHover && !ctx.PointerOverUi)
            {
                Brush.ForEach(ctx.HoverTile, map, t =>
                {
                    if (CanPaintNew(map, t) && _pending.Add(t)) _pendingVersion++;
                });
            }
            else
            {
                bool ended = Brush.UpdateStroke(ctx, t => erasing ? map.GetRunId(t.X, t.Y) != 0 || _pending.Contains(t)
                                                                  : CanPaintNew(map, t));
                if (Brush.StrokeActive || ended) PlanStroke(ctx);
                if (ended) ApplyStroke(ctx);
            }

            AnalyseExistingRuns(ctx);
            AnalysePending(ctx);
            DrawPreview(ctx, erasing);
            SetStatus(ctx, erasing);

            if (newTrail && _pending.Count > 0 && GameInput.Pressed(GameKey.Confirm)) Commit(ctx);
        }

        private static bool CanPaintNew(WorldMap map, Int2 t)
            => map.IsOwned(t) && map.GetRunId(t.X, t.Y) == 0 && map.GetSurface(t) == SurfaceType.Natural;

        // ---------- Strokes on existing trails ----------

        /// <summary>Splits the stroke into the edits it means and validates them (for the preview and status).</summary>
        private void PlanStroke(ToolContext ctx)
        {
            var stroke = Brush.Stroke;
            if (stroke.Count == _strokeSize && Brush.StrokeActive) return;
            _strokeSize = stroke.Count;
            var map = ctx.Sim.Map;

            _eraseByRun.Clear();
            _addToRun.Clear();
            if (Brush.StrokeErasing)
            {
                foreach (var t in stroke)
                {
                    int runId = map.GetRunId(t.X, t.Y);
                    if (runId == 0) continue;
                    if (!_eraseByRun.TryGetValue(runId, out var list)) _eraseByRun[runId] = list = new List<Int2>();
                    list.Add(t);
                }
                _strokeResult = CommandResult.Success();
                foreach (var kv in _eraseByRun)
                {
                    var r = ctx.Runner.Preview(new EditRunTilesCommand(kv.Key, null, kv.Value));
                    if (!r.Ok) { _strokeResult = r; break; }
                }
            }
            else if (EditRunId != 0)
            {
                foreach (var t in stroke) _addToRun.Add(t);
                _strokeResult = _addToRun.Count > 0
                    ? ctx.Runner.Preview(new EditRunTilesCommand(EditRunId, _addToRun, null))
                    : CommandResult.Fail("Nothing to add.");
            }
        }

        private void ApplyStroke(ToolContext ctx)
        {
            if (Brush.StrokeErasing)
            {
                // Erasing also trims the new trail being painted.
                foreach (var t in Brush.Stroke) if (_pending.Remove(t)) _pendingVersion++;
                if (_strokeResult.Ok)
                    foreach (var kv in _eraseByRun) ctx.Runner.Submit(new EditRunTilesCommand(kv.Key, null, kv.Value));
            }
            else if (EditRunId != 0 && _addToRun.Count > 0)
            {
                ctx.Runner.Submit(new EditRunTilesCommand(EditRunId, _addToRun, null));
            }
            Brush.ClearStroke();
            _eraseByRun.Clear();
            _addToRun.Clear();
            _strokeSize = -1;
        }

        // ---------- Preview ----------

        private void DrawPreview(ToolContext ctx, bool erasing)
        {
            var map = ctx.Sim.Map;
            foreach (var t in _existingUphill) ctx.Preview.Tile(map, t.X, t.Y, Palette.Uphill);
            foreach (var t in _pending)
                ctx.Preview.Tile(map, t.X, t.Y, _pendingUphill.Contains(t) ? Palette.Uphill : Palette.WithAlpha(Palette.Valid, 110));

            if (Brush.StrokeActive)
            {
                var color = erasing
                    ? (_strokeResult.Ok ? Palette.WithAlpha(Palette.Erase, 150) : Palette.WithAlpha(Palette.Invalid, 200))
                    : (_strokeResult.Ok ? Palette.WithAlpha(Palette.Valid, 140) : Palette.WithAlpha(Palette.Invalid, 140));
                foreach (var t in Brush.Stroke) ctx.Preview.Tile(map, t.X, t.Y, color);
            }

            if (!ctx.HasHover) return;
            Brush.ForEach(ctx.HoverTile, map, t =>
            {
                Color32 c;
                if (erasing) c = map.GetRunId(t.X, t.Y) != 0 || _pending.Contains(t) ? Palette.WithAlpha(Palette.Erase, 110) : Palette.Hover;
                else c = CanPaintNew(map, t) || map.GetRunId(t.X, t.Y) == EditRunId && EditRunId != 0 ? Palette.Hover : Palette.WithAlpha(Palette.Invalid, 60);
                ctx.Preview.Tile(map, t.X, t.Y, c);
            });
        }

        private void SetStatus(ToolContext ctx, bool erasing)
        {
            var run = EditRunId != 0 ? ctx.Sim.World.GetRun(EditRunId) : null;
            if (Brush.StrokeActive)
            {
                if (erasing)
                {
                    int tiles = 0;
                    foreach (var kv in _eraseByRun) tiles += kv.Value.Count;
                    ctx.SetStatus(_strokeResult, tiles > 0 ? $"Erase {tiles} trail tiles. Release to apply." : "Drag over trail tiles to erase them.");
                }
                else ctx.SetStatus(_strokeResult, $"Add {_addToRun.Count} tiles to \"{run?.Name}\". Release to apply.");
                return;
            }

            if (erasing)
            {
                ctx.SetStatus($"Erase brush {Brush.Size}x{Brush.Size}: drag over any trail to remove those tiles.");
                return;
            }
            if (run != null)
            {
                ctx.SetStatus($"Adding to \"{run.Name}\" ({run.Tiles.Count} tiles). Drag to extend it; Shift-drag erases.");
                return;
            }
            if (_pending.Count == 0)
            {
                ctx.SetStatus($"Brush {Brush.Size}x{Brush.Size}. Paint downhill from a lift's top station. Yellow = uphill.");
                return;
            }

            var result = ctx.Runner.Preview(new CreateRunCommand(_pending, _name));
            string uphill = _pendingUphill.Count > 0 ? $"  {_pendingUphill.Count} tiles go uphill (yellow)." : "";
            ctx.SetStatus(result, $"{_pending.Count} tiles, looks like a {EstimateRating(ctx)} run.{uphill} Enter to create.");
        }

        // ---------- Uphill analysis ----------

        /// <summary>
        /// Flow field over the painted tiles from the lowest one (same idea as RunSystem): each tile's next
        /// step is the neighbour closest to the end. Steps that climb are uphill travel.
        /// </summary>
        private void AnalysePending(ToolContext ctx)
        {
            if (_analysedPendingVersion == _pendingVersion) return;
            _analysedPendingVersion = _pendingVersion;
            _pendingUphill.Clear();
            if (_pending.Count < 2) return;

            var map = ctx.Sim.Map;
            float metersPerStep = ctx.Sim.TerrainScale.MetersPerStep;
            float penalty = ctx.Sim.Config.Runs.UphillPenalty;

            Int2 end = default;
            float lowest = float.MaxValue;
            foreach (var t in _pending)
            {
                float h = map.TileCenterHeight(t.X, t.Y);
                if (h < lowest) { lowest = h; end = t; }
            }

            _dist.Clear();
            _heap.Clear();
            _dist[end] = 0f;
            _heap.Push(end, 0f);
            while (_heap.Count > 0)
            {
                var u = _heap.Pop(out float d);
                if (d > _dist[u]) continue;
                float hu = map.TileCenterHeight(u.X, u.Y);
                for (int k = 0; k < Int2.Neighbours8.Length; k++)
                {
                    var v = u + Int2.Neighbours8[k];
                    if (!_pending.Contains(v)) continue;
                    float climb = Mathf.Max(0f, hu - map.TileCenterHeight(v.X, v.Y)); // skier goes v -> u
                    float nd = d + (k < 4 ? 1f : 1.41421f) + climb * penalty;
                    if (!_dist.TryGetValue(v, out float old) || nd < old)
                    {
                        _dist[v] = nd;
                        _heap.Push(v, nd);
                    }
                }
            }

            foreach (var t in _pending)
            {
                if (!_dist.TryGetValue(t, out float here)) continue; // disconnected: validation will complain
                Int2 next = t;
                float best = here;
                foreach (var o in Int2.Neighbours8)
                {
                    var n = t + o;
                    if (_dist.TryGetValue(n, out float dn) && dn < best) { best = dn; next = n; }
                }
                if (next == t) continue;
                float rise = (map.TileCenterHeight(next.X, next.Y) - map.TileCenterHeight(t.X, t.Y)) * metersPerStep;
                if (rise > UphillThresholdMeters) _pendingUphill.Add(t);
            }
        }

        /// <summary>Uphill steps on runs that already exist, along their real flow fields.</summary>
        private void AnalyseExistingRuns(ToolContext ctx)
        {
            var sim = ctx.Sim;
            if (_analysedRunsVersion == sim.World.RunsVersion) return;
            _analysedRunsVersion = sim.World.RunsVersion;
            _existingUphill.Clear();

            var map = sim.Map;
            float metersPerStep = sim.TerrainScale.MetersPerStep;
            foreach (var run in sim.World.Runs)
            {
                foreach (var t in run.Tiles)
                {
                    if (!sim.RunSystem.TryNextTile(run, t, out var next)) continue;
                    float rise = (map.TileCenterHeight(next.X, next.Y) - map.TileCenterHeight(t.X, t.Y)) * metersPerStep;
                    if (rise > UphillThresholdMeters) _existingUphill.Add(t);
                }
            }
        }

        // ---------- Actions ----------

        private void Commit(ToolContext ctx)
        {
            if (_pending.Count == 0) return;
            var result = ctx.Runner.Submit(new CreateRunCommand(_pending, string.IsNullOrWhiteSpace(_name) ? null : _name));
            if (result.Ok)
            {
                ClearPending();
                _name = "";
            }
        }

        private void ClearPending()
        {
            _pending.Clear();
            _pendingVersion++;
        }

        private string EstimateRating(ToolContext ctx)
        {
            var map = ctx.Sim.Map;
            float sum = 0f, max = 0f;
            foreach (var t in _pending)
            {
                float s = TerrainQueries.SlopePercent(map, ctx.Sim.TerrainScale, t.X, t.Y);
                sum += s;
                if (s > max) max = s;
            }
            // Painted areas include edges, so use a softened max as a rough "sustained" value.
            var rating = RunRater.Rate(ctx.Sim.Config.Runs, sum / _pending.Count, max * 0.9f);
            return RunRater.Label(rating);
        }

        public override bool Cancel(ToolContext ctx)
        {
            if (Brush.CancelStroke()) return true;
            if (_pending.Count > 0) { ClearPending(); return true; }
            if (EditRunId != 0) { BeginNewTrail(); return true; }
            return false;
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            var sim = ctx.Sim;
            var runs = sim.World.Runs;

            // Which trail: new, or an existing one (arrows cycle through them).
            var run = EditRunId != 0 ? sim.World.GetRun(EditRunId) : null;
            var row = ui.Row(UiContext.ButtonHeight);
            int index = run != null ? runs.IndexOf(run) : -1;
            if (ui.ArrowButton(UiContext.SplitLeft(row, 14), "◄", runs.Count > 0)) Pick(ctx, index <= 0 ? runs.Count - 1 : index - 1, runs.Count);
            if (ui.ArrowButton(UiContext.SplitRight(row, 14), "►", runs.Count > 0)) Pick(ctx, index + 1 >= runs.Count ? -1 : index + 1, runs.Count);
            var label = new Rect(row.x + 16, row.y, row.width - 32, row.height);
            ui.Label(label, run != null ? "Editing: " + run.Name : "New trail", run != null ? GameUi.DifficultyText(run.DisplayedRating) : ui.Scheme.Text);

            int selected = ctx.View.SelectedRunId;
            if (run == null && selected != 0 && sim.World.GetRun(selected) != null)
            {
                if (ui.Button($"Edit {sim.World.GetRun(selected).Name}")) BeginEdit(selected);
            }
            else if (run != null && ui.Button("New trail instead")) BeginNewTrail();

            ui.Space(2);
            Brush.OptionsUi(ui, run != null ? "Add tiles" : "Paint", "Erase");
            ui.Space(2);

            if (run == null)
            {
                ui.Label("Name (optional)");
                ui.TextField(ref _name, 32);
                ui.Space(2);
                ui.Field("Tiles painted", _pending.Count.ToString());
                if (_pendingUphill.Count > 0) ui.Field("Uphill (yellow)", _pendingUphill.Count.ToString(), UiTheme.TextYellow);
                var buttons = ui.Row(UiContext.ButtonHeight);
                if (ui.Button(UiContext.Cell(buttons, 0, 2), "Create (Enter)", _pending.Count > 0)) Commit(ctx);
                if (ui.Button(UiContext.Cell(buttons, 1, 2), "Clear", _pending.Count > 0)) ClearPending();
                ui.Paragraph("Paint downhill from a lift top, then Create. Erase or Shift-drag removes tiles from any trail.", ui.Scheme.Text);
            }
            else
            {
                ui.Field("Tiles", run.Tiles.Count.ToString());
                ui.Paragraph("Each stroke changes the trail when you let go. It keeps its name and sign. Esc goes back to a new trail.", ui.Scheme.Text);
            }
        }

        private void Pick(ToolContext ctx, int index, int count)
        {
            if (index < 0 || index >= count) BeginNewTrail();
            else BeginEdit(ctx.Sim.World.Runs[index].Id);
        }
    }
}
