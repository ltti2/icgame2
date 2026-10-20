using IceCoast.Simulation.World;
using IceCoast.View.Input;
using IceCoast.View.Rendering;

namespace IceCoast.View.Tools
{
    /// <summary>Click a lift, run or tile; the HUD opens a window for it (one per lift/run, one shared tile window).</summary>
    public sealed class InspectTool : ToolBase
    {
        public override string Name => "Inspect";
        public override string Icon => "inspect";
        public override string Help => "Click a lift, run or patch of snow to open its window.";

        public override void Tick(ToolContext ctx)
        {
            var map = ctx.Sim.Map;
            var view = ctx.View;

            if (view.SelectedTile.IsValid) ctx.Preview.Tile(map, view.SelectedTile.X, view.SelectedTile.Y, Palette.Selection);
            if (!ctx.HasHover) return;
            ctx.Preview.Tile(map, ctx.HoverTile.X, ctx.HoverTile.Y, Palette.Hover);

            if (!GameInput.LeftDown || ctx.PointerOverUi) return;

            var t = ctx.HoverTile;
            int liftId = 0, runId = 0;
            ushort occupant = map.GetOccupant(t.X, t.Y);
            var kind = Occupant.KindOf(occupant);
            if (kind == OccupantKind.LiftStation || kind == OccupantKind.LiftTower) liftId = Occupant.IdOf(occupant);
            else runId = map.GetRunId(t.X, t.Y);
            view.Select(t, liftId, runId, GameInput.MousePosition);
        }

        public override bool Cancel(ToolContext ctx)
        {
            if (!ctx.View.SelectedTile.IsValid && ctx.View.SelectedLiftId == 0 && ctx.View.SelectedRunId == 0) return false;
            ctx.View.ClearSelection();
            return true;
        }
    }
}
