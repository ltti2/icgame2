using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Terrain;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>Raise, lower or flatten corners with a square brush. Hold the mouse to keep working.</summary>
    public sealed class TerraformTool : ToolBase
    {
        public TerraformMode Mode = TerraformMode.Raise;
        public int BrushRadius;
        private float _repeatTimer;
        private const float RepeatInterval = 0.15f;

        public override string Name => "Terraform";
        public override string Icon => "terraform";
        public override string Help => "Click/hold on a corner. Shift inverts raise/lower. [ and ] change brush size.";

        public override void Tick(ToolContext ctx)
        {
            if (GameInput.Pressed(GameKey.BrushBigger)) BrushRadius = Mathf.Min(5, BrushRadius + 1);
            if (GameInput.Pressed(GameKey.BrushSmaller)) BrushRadius = Mathf.Max(0, BrushRadius - 1);
            if (!ctx.HasHover) return;

            var map = ctx.Sim.Map;
            var v = ctx.HoverVertex;
            var mode = Mode;
            if (GameInput.Held(GameKey.Modifier) && mode != TerraformMode.Flatten)
                mode = mode == TerraformMode.Raise ? TerraformMode.Lower : TerraformMode.Raise;

            var command = new TerraformCommand(v, BrushRadius, mode);
            var result = ctx.Runner.Preview(command);
            var color = result.Ok ? Palette.Valid : Palette.Invalid;
            for (int vy = v.Y - BrushRadius; vy <= v.Y + BrushRadius; vy++)
                for (int vx = v.X - BrushRadius; vx <= v.X + BrushRadius; vx++)
                    if (map.VertexInBounds(vx, vy)) ctx.Preview.VertexMarker(map, vx, vy, color);
            ctx.SetStatus(result, $"{mode} ({BrushRadius * 2 + 1}x{BrushRadius * 2 + 1} corners)");

            if (ctx.PointerOverUi) return;
            if (GameInput.LeftDown) { Apply(ctx, command, result.Ok); _repeatTimer = RepeatInterval * 2f; }
            else if (GameInput.LeftHeld)
            {
                _repeatTimer -= Time.unscaledDeltaTime;
                if (_repeatTimer <= 0f)
                {
                    Apply(ctx, command, result.Ok);
                    _repeatTimer = RepeatInterval;
                }
            }
        }

        private static void Apply(ToolContext ctx, TerraformCommand command, bool ok)
        {
            if (ok) ctx.Runner.Submit(command);
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            Mode = (TerraformMode)ui.Toggles((int)Mode, "Raise", "Lower", "Flatten");
            int size = BrushRadius * 2 + 1;
            BrushRadius = ui.Counter("Radius", BrushRadius, 0, 5, 1, $"{BrushRadius}  ({size}x{size})", "Also [ and ]");
            ui.Paragraph("Click or hold on a corner. Shift swaps raise and lower.", ui.Scheme.Text);
        }
    }
}
