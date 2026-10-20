using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.World;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// Puts up buildings (Waffle Cabins) on walking areas. Hovering shows the walking area nearby and whether the
    /// tile under the mouse can take a building; clicking builds. Clicking an existing building opens its window,
    /// where its price is set.
    /// </summary>
    public sealed class BuildBuildingTool : ToolBase
    {
        public BuildingType Type = BuildingType.WaffleCabin;

        private static readonly Color32 WalkTint = new Color32(255, 236, 170, 70);

        public override string Name => "Waffle Cabins";
        public override string Icon => "waffle";
        public override string Help => "Click a flat walking area to build a Waffle Cabin. Click a cabin to set its waffle price.";

        public override void Tick(ToolContext ctx)
        {
            if (!ctx.HasHover) return;
            var sim = ctx.Sim;
            var map = sim.Map;
            var tile = ctx.HoverTile;
            var def = sim.Config.GetBuilding(Type);

            // Where buildings can go: the walking area around the mouse.
            const int reach = 8;
            for (int y = tile.Y - reach; y <= tile.Y + reach; y++)
                for (int x = tile.X - reach; x <= tile.X + reach; x++)
                    if (map.InBounds(x, y) && map.GetSurface(x, y) == SurfaceType.WalkingArea && map.GetOccupant(x, y) == 0)
                        ctx.Preview.Tile(map, x, y, WalkTint);

            var existing = sim.BuildingSystem.BuildingAt(tile.X, tile.Y);
            if (existing != null)
            {
                ctx.Preview.Tile(map, tile.X, tile.Y, Palette.Selection);
                ctx.SetStatus($"{existing.Name}: waffles ${existing.Price}, {existing.SoldToday} sold today. Click to open its window.");
                if (GameInput.LeftDown && !ctx.PointerOverUi)
                    ctx.View.Select(tile, 0, 0, GameInput.MousePosition, existing.Id);
                return;
            }

            var command = new PlaceBuildingCommand(Type, tile);
            var quote = ctx.Runner.Preview(command);
            for (int y = tile.Y; y < tile.Y + def.FootprintHeight; y++)
                for (int x = tile.X; x < tile.X + def.FootprintWidth; x++)
                    if (map.InBounds(x, y))
                        ctx.Preview.Tile(map, x, y, quote.Ok ? Palette.WithAlpha(Palette.Valid, 200) : Palette.WithAlpha(Palette.Invalid, 200));
            ctx.SetStatus(quote, $"{def.DisplayName}: ${def.OperatingCostPerHour}/hr while open, ${def.IngredientCostPerSale} a waffle");
            if (GameInput.LeftDown && !ctx.PointerOverUi && quote.Ok) ctx.Runner.Submit(command);
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            var sim = ctx.Sim;
            var def = sim.Config.GetBuilding(Type);
            ui.Heading(def.DisplayName);
            ui.Field("Price to build", $"${def.BuildCost:N0}");
            ui.Field("Running cost", $"${def.OperatingCostPerHour}/hr while open");
            ui.Field("Ingredients", $"${def.IngredientCostPerSale} a waffle");
            ui.Field("Serves", $"{60f / Mathf.Max(0.01f, def.ServiceMinutes):0} waffles/hr");
            ui.Field("Fair waffle price", $"${def.FairPrice}");
            ui.Separator();

            int count = 0, sold = 0;
            long revenue = 0;
            foreach (var b in sim.World.Buildings)
            {
                if (b.Type != Type) continue;
                count++;
                sold += b.SoldToday;
                revenue += b.RevenueToday;
            }
            ui.Field("Your cabins", count == 0 ? "None yet" : $"{count} ({sold:N0} sold today, ${revenue:N0})");
            ui.Paragraph("Cabins go on flat walking areas (paint one with the Walking Areas tool, 7). Hungry guests " +
                         "walk over from the base or from anywhere on the same walking area, line up at the window, " +
                         "and come away happier. Set each cabin's waffle price in its window.", ui.Scheme.Text);
        }
    }
}
