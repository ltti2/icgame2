using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.View.Rendering;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// Walking area editor, the same brush as the trail editor: Paint lays packed-snow walking area (clearing
    /// trees), Erase (or Shift-drag) turns it back into natural ground. Each stroke is applied when the mouse
    /// is released, after a live price and validity check.
    ///
    /// Walking areas are where guests take their skis off and walk anywhere, like the base area. Lifts that
    /// open onto the same walking area are connected on foot, trails can end on them, and buildings (coming
    /// in M4) must stand on them.
    /// </summary>
    public sealed class WalkingAreaTool : ToolBase
    {
        public readonly TileBrush Brush = new TileBrush();
        private readonly List<Int2> _scratch = new List<Int2>();
        private CommandResult _strokeResult = CommandResult.Success();
        private int _strokeSize = -1;

        public override string Name => "Walking Areas";
        public override string Icon => "walk";
        public override string Help => "Paint walking areas where guests walk on foot (plazas, decks; buildings go here). Erase or Shift-drag removes them.";

        public override void OnDeactivated(ToolContext ctx) => Brush.CancelStroke();

        public override void Tick(ToolContext ctx)
        {
            var map = ctx.Sim.Map;
            Brush.HandleKeys();
            bool erasing = Brush.Erasing;

            bool ended = Brush.UpdateStroke(ctx, t => erasing ? map.GetSurface(t) == SurfaceType.WalkingArea
                                                              : map.GetSurface(t) != SurfaceType.WalkingArea);
            if (Brush.StrokeActive || ended)
            {
                if (Brush.Stroke.Count != _strokeSize || ended)
                {
                    _strokeSize = Brush.Stroke.Count;
                    _scratch.Clear();
                    _scratch.AddRange(Brush.Stroke);
                    _strokeResult = ctx.Runner.Preview(new PaintWalkingAreaCommand(_scratch, Brush.StrokeErasing));
                }
                if (ended)
                {
                    if (_scratch.Count > 0) ctx.Runner.Submit(new PaintWalkingAreaCommand(_scratch, Brush.StrokeErasing));
                    Brush.ClearStroke();
                    _strokeSize = -1;
                }
            }

            // Existing walking areas near the cursor, outlined so you can see what you're extending.
            if (ctx.HasHover)
            {
                var c = ctx.HoverTile;
                for (int y = c.Y - 12; y <= c.Y + 12; y++)
                    for (int x = c.X - 12; x <= c.X + 12; x++)
                        if (map.InBounds(x, y) && map.GetSurface(x, y) == SurfaceType.WalkingArea)
                            ctx.Preview.Tile(map, x, y, Palette.WithAlpha(Palette.WalkPreview, 55));
            }

            if (Brush.StrokeActive)
            {
                var color = _strokeResult.Ok
                    ? (Brush.StrokeErasing ? Palette.Erase : Palette.WalkPreview)
                    : Palette.WithAlpha(Palette.Invalid, 160);
                foreach (var t in Brush.Stroke) ctx.Preview.Tile(map, t.X, t.Y, color);
                ctx.SetStatus(_strokeResult, Brush.StrokeErasing
                    ? $"Erase {Brush.Stroke.Count} tiles of walking area. Release to apply."
                    : $"Lay {Brush.Stroke.Count} tiles of walking area. Release to apply.");
                return;
            }

            if (ctx.HasHover)
                Brush.ForEach(ctx.HoverTile, map, t => ctx.Preview.Tile(map, t.X, t.Y, erasing ? Palette.WithAlpha(Palette.Erase, 90) : Palette.Hover));
            ctx.SetStatus(erasing
                ? $"Erase brush {Brush.Size}x{Brush.Size}: drag over walking areas to remove them."
                : $"Brush {Brush.Size}x{Brush.Size}: drag to lay walking area (${ctx.Sim.Config.Walking.CostPerTile}/tile plus trees).");
        }

        public override bool Cancel(ToolContext ctx) => Brush.CancelStroke();

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            Brush.OptionsUi(ui);
            ui.Space(2);
            var zone = ctx.HasHover ? ctx.Sim.WalkingAreaSystem.ZoneAt(ctx.HoverTile.X, ctx.HoverTile.Y) : null;
            if (zone != null)
            {
                ui.Field("Area under cursor", $"{zone.TileCount} tiles");
                ui.Field("Lifts boarding here", zone.LiftBottoms.Count.ToString());
                if (zone.IsBase) ui.Label("Part of the base area.");
            }
            ui.Paragraph("Guests walk anywhere on a walking area, so every lift that opens onto it is in reach. " +
                         "Trails can end on one. Walking areas can't overlap trails, roads or parking. Buildings will need them.",
                         ui.Scheme.Text);
        }
    }
}
