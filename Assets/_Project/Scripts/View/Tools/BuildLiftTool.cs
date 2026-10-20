using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lifts;
using IceCoast.View.Input;
using IceCoast.View.Iso;
using IceCoast.View.Rendering;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// Two clicks: bottom station, then top station. The line, towers, cost and any problem are previewed
    /// live using the same LiftPlacement.Quote the build command uses, so preview and result always agree.
    /// </summary>
    public sealed class BuildLiftTool : ToolBase
    {
        public LiftType SelectedType = LiftType.RopeTow;
        private Int2 _bottom = Int2.Invalid;

        public override string Name => "Build Lift";
        public override string Icon => "lift";
        public override string Help => "Click the bottom station, then the top station. Right-click or Esc to cancel.";

        public override void OnDeactivated(ToolContext ctx) => _bottom = Int2.Invalid;

        public override void Tick(ToolContext ctx)
        {
            var map = ctx.Sim.Map;
            if (!ctx.HasHover)
            {
                ctx.SetStatus(_bottom.IsValid ? "Pick the top station." : "Pick the bottom station.");
                return;
            }

            if (!_bottom.IsValid)
            {
                bool ok = map.IsOwned(ctx.HoverTile) && map.GetOccupant(ctx.HoverTile.X, ctx.HoverTile.Y) == 0;
                ctx.Preview.Tile(map, ctx.HoverTile.X, ctx.HoverTile.Y, ok ? Palette.Valid : Palette.Invalid);
                var def = ctx.Sim.Config.GetLift(SelectedType);
                ctx.SetStatus($"{def.DisplayName}: click to place the bottom station.");
                if (GameInput.LeftDown && !ctx.PointerOverUi && ok) _bottom = ctx.HoverTile;
                return;
            }

            var command = new BuildLiftCommand(SelectedType, _bottom, ctx.HoverTile);
            var result = ctx.Runner.Preview(command);
            DrawQuote(ctx, command.LastQuote, result.Ok);

            var q = command.LastQuote;
            var units = ctx.Sim.Units;
            string summary = q != null
                ? $"{units.Length(q.LengthTiles * ctx.Sim.TerrainScale.TileSizeMeters)} long, {units.Length(q.RiseSteps * ctx.Sim.TerrainScale.MetersPerStep)} vertical, {q.GradePercent:0}% grade"
                : "";
            ctx.SetStatus(result, summary);

            if (GameInput.LeftDown && !ctx.PointerOverUi && result.Ok)
            {
                if (ctx.Runner.Submit(command).Ok) _bottom = Int2.Invalid;
            }
        }

        private static void DrawQuote(ToolContext ctx, LiftQuote q, bool ok)
        {
            if (q == null) return;
            var map = ctx.Sim.Map;
            var color = ok ? Palette.Valid : Palette.Invalid;
            foreach (var t in q.LineTiles) ctx.Preview.Tile(map, t.X, t.Y, Palette.WithAlpha(color, 70));
            foreach (var t in q.Towers) ctx.Preview.Tile(map, t.X, t.Y, color);
            if (q.LineTiles.Count > 0)
            {
                var a = q.LineTiles[0];
                var b = q.LineTiles[q.LineTiles.Count - 1];
                ctx.Preview.Tile(map, a.X, a.Y, color);
                ctx.Preview.Tile(map, b.X, b.Y, color);
                ctx.Preview.Line(IsoProjection.TileCenter(map, a.X, a.Y), IsoProjection.TileCenter(map, b.X, b.Y), color, 0.06f);
            }
        }

        public override bool Cancel(ToolContext ctx)
        {
            if (!_bottom.IsValid) return false;
            _bottom = Int2.Invalid;
            return true;
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            var sim = ctx.Sim;
            var economy = sim.World.Economy;
            var units = sim.Units;
            float tileM = sim.TerrainScale.TileSizeMeters;

            foreach (var def in sim.Config.Lifts)
            {
                bool locked = !sim.Config.SandboxMode && economy.Reputation < def.RequiredReputation;
                string right = locked ? $"needs rep {def.RequiredReputation:0}" : $"${def.BaseCost / 1000:N0}k+";
                var color = locked ? UiTheme.TextDisabled : ui.Scheme.Text;
                if (ui.ListRow(def.DisplayName, SelectedType == def.Type, color, right) && !locked && SelectedType != def.Type)
                {
                    SelectedType = def.Type;
                    _bottom = Int2.Invalid;
                }
            }

            var sel = sim.Config.GetLift(SelectedType);
            ui.Separator();
            ui.Field("Capacity", $"{sel.CapacityPerHour:N0} riders/hr");
            ui.Field("Length", $"{units.Length(sel.MinLengthTiles * tileM)} to {units.Length(sel.MaxLengthTiles * tileM)}");
            ui.Field("Steepest grade", $"{sel.MaxGradePercent:0}%");
            ui.Field("Cost", $"${sel.BaseCost:N0} + ${sel.CostPerTile:N0}/tile");
            ui.Field("Running cost", $"${sel.OperatingCostPerHour:N0}/hr");
            if (sel.WeatherProtected) ui.Label("Sheltered from the cold.", ui.Scheme.Heading);
            ui.Separator();
            ui.Paragraph(_bottom.IsValid ? "Now click where the top station goes." : "Click the bottom station, then the top.", ui.Scheme.Text);
        }
    }
}
