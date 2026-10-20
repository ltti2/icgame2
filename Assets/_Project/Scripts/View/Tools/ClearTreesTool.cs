using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>Drag to cut trees. Costs money, returns a little lumber. Cleared ground loses wind shelter.</summary>
    public sealed class ClearTreesTool : ToolBase
    {
        public int BrushRadius = 1;
        private readonly List<Int2> _brush = new List<Int2>();

        public override string Name => "Clear Trees";
        public override string Icon => "trees";
        public override string Help => "Drag over trees to cut them. [ and ] change brush size.";

        public override void Tick(ToolContext ctx)
        {
            if (GameInput.Pressed(GameKey.BrushBigger)) BrushRadius = Mathf.Min(6, BrushRadius + 1);
            if (GameInput.Pressed(GameKey.BrushSmaller)) BrushRadius = Mathf.Max(0, BrushRadius - 1);
            if (!ctx.HasHover) return;

            var map = ctx.Sim.Map;
            _brush.Clear();
            for (int y = ctx.HoverTile.Y - BrushRadius; y <= ctx.HoverTile.Y + BrushRadius; y++)
                for (int x = ctx.HoverTile.X - BrushRadius; x <= ctx.HoverTile.X + BrushRadius; x++)
                    if (map.InBounds(x, y) && map.GetTreeDensity(x, y) > 0) _brush.Add(new Int2(x, y));

            var command = new ClearTreesCommand(_brush);
            var result = _brush.Count > 0 ? ctx.Runner.Preview(command) : CommandResult.Fail("No trees under the brush.");
            for (int y = ctx.HoverTile.Y - BrushRadius; y <= ctx.HoverTile.Y + BrushRadius; y++)
                for (int x = ctx.HoverTile.X - BrushRadius; x <= ctx.HoverTile.X + BrushRadius; x++)
                    ctx.Preview.Tile(map, x, y, result.Ok ? Palette.WithAlpha(Palette.Valid, 90) : Palette.WithAlpha(Palette.Invalid, 60));
            ctx.SetStatus(result, $"Clear {_brush.Count} wooded tiles");

            if (GameInput.LeftHeld && !ctx.PointerOverUi && result.Ok) ctx.Runner.Submit(command);
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            int size = BrushRadius * 2 + 1;
            BrushRadius = ui.Counter("Radius", BrushRadius, 0, 6, 1, $"{BrushRadius}  ({size}x{size})", "Also [ and ]");
            ui.Paragraph("Trees shelter runs from wind and shade the snow. Glades (sparse trees) are fun for experts. Clear with care.", ui.Scheme.Text);
        }
    }
}
