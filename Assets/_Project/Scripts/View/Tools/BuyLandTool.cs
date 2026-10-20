using IceCoast.Simulation.Commands;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>Hover a parcel to see its price and requirements; click to buy. Switches on the ownership overlay.</summary>
    public sealed class BuyLandTool : ToolBase
    {
        private OverlayMode _previousOverlay;

        public override string Name => "Buy Land";
        public override string Icon => "buyland";
        public override string Help => "Click a parcel next to your land to buy it.";

        public override void OnActivated(ToolContext ctx)
        {
            _previousOverlay = ctx.View.Overlay;
            ctx.View.Overlay = OverlayMode.Ownership;
        }

        public override void OnDeactivated(ToolContext ctx)
        {
            if (ctx.View.Overlay == OverlayMode.Ownership) ctx.View.Overlay = _previousOverlay;
        }

        public override void Tick(ToolContext ctx)
        {
            if (!ctx.HasHover)
            {
                ctx.SetStatus("Hover a parcel.");
                return;
            }

            var map = ctx.Sim.Map;
            var parcel = ctx.Sim.ParcelSystem.GetParcelAt(ctx.HoverTile.X, ctx.HoverTile.Y);
            if (parcel == null) return;

            var command = new BuyParcelCommand(parcel.Id);
            var result = ctx.Runner.Preview(command);
            ctx.Preview.RectOutline(map, parcel.X, parcel.Y, parcel.Width, parcel.Height,
                parcel.Owned ? Palette.Selection : result.Ok ? Palette.Valid : Palette.Invalid, 0.15f);

            if (parcel.Owned) ctx.SetStatus($"{parcel.Name}: yours.");
            else ctx.SetStatus(result, $"{parcel.Name} (needs reputation {parcel.RequiredReputation:0}). Click to buy");

            if (GameInput.LeftDown && !ctx.PointerOverUi && result.Ok) ctx.Runner.Submit(command);
        }
    }
}
