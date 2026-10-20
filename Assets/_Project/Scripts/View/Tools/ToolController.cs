using IceCoast.Simulation.Core;
using IceCoast.View.CameraControl;
using IceCoast.View.Input;
using IceCoast.View.Iso;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// Owns the tools, picks the hovered tile each frame, routes global hotkeys and runs the active tool.
    /// </summary>
    public sealed class ToolController : MonoBehaviour
    {
        public ToolBase[] Tools { get; private set; }
        public ToolBase Active { get; private set; }
        public ToolContext Context { get; private set; }

        private int _maxHeightForPicking;

        public void Initialize(SimulationRunner runner, ViewState view, PreviewRenderer preview, IsoCameraController cam)
        {
            Context = new ToolContext { Runner = runner, View = view, Preview = preview, Camera = cam };
            Tools = CreateTools();
            Activate(Tools[0]);
            runner.SimulationChanged += sim =>
            {
                _maxHeightForPicking = sim.Config.Terrain.SummitElevationSteps + 200;
                view.ClearSelection();
                Activate(Tools[0]);
            };
        }

        /// <summary>Order = hotkeys 1..9 and toolbar order.</summary>
        private static ToolBase[] CreateTools()
        {
            var tools = new ToolBase[]
            {
                new InspectTool(),
                new BuildLiftTool(),
                new PaintRunTool(),
                new BuyLandTool(),
                new TerraformTool(),
                new ClearTreesTool(),
                new WalkingAreaTool(),
                new TrailLightTool(),
                new BuildBuildingTool(),
            };
            for (int i = 0; i < tools.Length; i++) tools[i].Hotkey = (i + 1).ToString();
            return tools;
        }

        public ToolBase Inspect => Tools[0];

        public T Get<T>() where T : ToolBase
        {
            foreach (var t in Tools) if (t is T match) return match;
            return null;
        }

        public void Activate(ToolBase tool)
        {
            if (Active == tool) return;
            Active?.OnDeactivated(Context);
            Active = tool;
            Context.SetStatus(tool.Help);
            Active.OnActivated(Context);
        }

        private void Update()
        {
            var ctx = Context;
            if (ctx?.Runner?.Sim == null) return;

            HandleHotkeys();
            Pick(ctx);

            ctx.SetStatus(Active.Help);
            ctx.Preview.Begin();
            if (GameInput.RightDown && !ctx.PointerOverUi) Cancel();
            Active.Tick(ctx);
            ctx.Preview.End();
        }

        private void Pick(ToolContext ctx)
        {
            ctx.PointerOverUi = ctx.View.PointerOverUi;
            ctx.MouseWorld = ctx.Camera.MouseWorld;
            var map = ctx.Sim.Map;
            Int2 tile = Int2.Invalid;
            ctx.HasHover = !ctx.PointerOverUi && IsoPicker.PickTile(map, ctx.MouseWorld, _maxHeightForPicking, out tile);
            if (ctx.HasHover)
            {
                ctx.HoverTile = tile;
                ctx.HoverVertex = IsoPicker.NearestVertex(map, tile, ctx.MouseWorld);
            }
            else
            {
                ctx.HoverTile = Int2.Invalid;
                ctx.HoverVertex = Int2.Invalid;
            }
            ctx.View.HoverTile = ctx.HoverTile;
            ctx.View.HoverVertex = ctx.HoverVertex;
        }

        private void HandleHotkeys()
        {
            var runner = Context.Runner;
            if (GameInput.Pressed(GameKey.Tool1)) Activate(Tools[0]);
            if (GameInput.Pressed(GameKey.Tool2)) Activate(Tools[1]);
            if (GameInput.Pressed(GameKey.Tool3)) Activate(Tools[2]);
            if (GameInput.Pressed(GameKey.Tool4)) Activate(Tools[3]);
            if (GameInput.Pressed(GameKey.Tool5)) Activate(Tools[4]);
            if (GameInput.Pressed(GameKey.Tool6)) Activate(Tools[5]);
            if (GameInput.Pressed(GameKey.Tool7)) Activate(Tools[6]);
            if (GameInput.Pressed(GameKey.Tool8)) Activate(Tools[7]);
            if (GameInput.Pressed(GameKey.Tool9)) Activate(Tools[8]);
            if (GameInput.Pressed(GameKey.Cancel)) Cancel();
            if (GameInput.Pressed(GameKey.Pause)) runner.TogglePause();
            if (GameInput.Pressed(GameKey.SpeedUp)) runner.Faster();
            if (GameInput.Pressed(GameKey.SpeedDown)) runner.Slower();
            if (GameInput.Pressed(GameKey.CycleOverlay)) Context.View.CycleOverlay();
            if (GameInput.Pressed(GameKey.ToggleContours)) Context.View.ShowContours = !Context.View.ShowContours;
        }

        /// <summary>Esc / right-click: the tool may consume it; otherwise fall back to Inspect.</summary>
        public void Cancel()
        {
            if (Active.Cancel(Context)) return;
            if (Active != Tools[0]) Activate(Tools[0]);
            else Context.View.ClearSelection();
        }
    }
}
