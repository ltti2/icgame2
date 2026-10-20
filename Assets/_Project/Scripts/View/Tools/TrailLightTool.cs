using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lighting;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// Puts up trail lights (small, medium or large) beside trails, or takes them down. While the tool is
    /// active the map shows light coverage (lit trail tiles yellow, dark ones red), and hovering previews the
    /// light a new pole would cast. A trail needs most of its length lit before its lift can run at night.
    /// </summary>
    public sealed class TrailLightTool : ToolBase
    {
        private static readonly string[] SizeLabels = { "Small", "Medium", "Large" };

        public LightSize Size = LightSize.Medium;
        public bool RemoveMode;
        private OverlayMode _previousOverlay;

        public override string Name => "Trail Lights";
        public override string Icon => "light";
        public override string Help => "Click beside a trail to put up a light. Lit trails stay open for night skiing. Shift-click removes a light.";

        public override void OnActivated(ToolContext ctx)
        {
            _previousOverlay = ctx.View.Overlay;
            ctx.View.Overlay = OverlayMode.Lighting;
        }

        public override void OnDeactivated(ToolContext ctx)
        {
            if (ctx.View.Overlay == OverlayMode.Lighting) ctx.View.Overlay = _previousOverlay;
        }

        public override void Tick(ToolContext ctx)
        {
            if (!ctx.HasHover) return;
            var sim = ctx.Sim;
            var map = sim.Map;
            var tile = ctx.HoverTile;
            bool removing = RemoveMode ^ GameInput.Held(GameKey.Modifier);

            if (removing)
            {
                var light = sim.LightingSystem.LightAt(tile.X, tile.Y);
                ctx.Preview.Tile(map, tile.X, tile.Y, light != null ? Palette.WithAlpha(Palette.Erase, 180) : Palette.Hover);
                if (light == null)
                {
                    ctx.SetStatus("Click a light pole to take it down (no refund).");
                    return;
                }
                DrawReach(ctx, light.Tile, sim.Config.GetLight(light.Size).RadiusTiles, Palette.WithAlpha(Palette.Erase, 50));
                ctx.SetStatus($"Take down this {sim.Config.GetLight(light.Size).DisplayName.ToLowerInvariant()} (no refund).");
                if (GameInput.LeftDown && !ctx.PointerOverUi) ctx.Runner.Submit(new RemoveLightCommand(light.Id));
                return;
            }

            var def = sim.Config.GetLight(Size);
            var command = new PlaceLightCommand(Size, tile);
            var quote = ctx.Runner.Preview(command);
            DrawReach(ctx, tile, def.RadiusTiles, new Color32(255, 230, 120, 90));
            ctx.Preview.Tile(map, tile.X, tile.Y, quote.Ok ? Palette.WithAlpha(Palette.Valid, 200) : Palette.WithAlpha(Palette.Invalid, 200));
            ctx.SetStatus(quote, $"{def.DisplayName}: lights about {def.RadiusTiles * 0.8f:0} tiles around, ${def.OperatingCostPerHour}/hr when on");
            if (GameInput.LeftDown && !ctx.PointerOverUi && quote.Ok) ctx.Runner.Submit(command);
        }

        /// <summary>Tiles a light at <paramref name="center"/> would light on its own.</summary>
        private static void DrawReach(ToolContext ctx, Int2 center, float radius, Color32 color)
        {
            var map = ctx.Sim.Map;
            int threshold = ctx.Sim.Config.Lighting.LitThreshold;
            int r = Mathf.CeilToInt(radius);
            for (int y = center.Y - r; y <= center.Y + r; y++)
                for (int x = center.X - r; x <= center.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    float d = Mathf.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
                    if (LightingSystem.Contribution(d, radius) >= threshold) ctx.Preview.Tile(map, x, y, color);
                }
        }

        public override bool HasOptions => true;

        public override void OnOptionsUi(UiContext ui, ToolContext ctx)
        {
            var sim = ctx.Sim;
            RemoveMode = ui.Toggles(RemoveMode ? 1 : 0, "Put up", "Take down") == 1;
            ui.Space(2);
            Size = (LightSize)ui.Toggles((int)Size, SizeLabels);
            var def = sim.Config.GetLight(Size);
            ui.Field("Price", $"${def.Cost:N0}");
            ui.Field("Reach", sim.Units.Length(def.RadiusTiles * 0.8f * sim.TerrainScale.TileSizeMeters));
            ui.Field("Power when on", $"${def.OperatingCostPerHour}/hr");
            ui.Separator();

            long hourly = 0;
            foreach (var l in sim.World.Lights) hourly += sim.Config.GetLight(l.Size).OperatingCostPerHour;
            ui.Field("Lights", $"{sim.World.Lights.Count} (${hourly}/hr when on)");
            ui.Field("Sunset today", ClockText(sim.TimeSystem.SunsetMinute));
            ui.Field("Night skiing", sim.World.NightSkiing ? $"On, until {ClockText(sim.Config.Time.NightCloseMinute)}" : "Off");
            if (ui.Button(sim.World.NightSkiing ? "Turn night skiing off" : "Turn night skiing on"))
                ctx.Runner.Submit(new SetNightSkiingCommand(!sim.World.NightSkiing));
            ui.Paragraph($"A trail counts as lit when {sim.Config.Lighting.RunLitFraction * 100:0}% of it is under lights. " +
                         "After the day close only lifts with a lit trail off the top keep running, and after dark guests avoid unlit trails.",
                         ui.Scheme.Text);
        }

        private static string ClockText(int minuteOfDay)
        {
            int h = minuteOfDay / 60, m = minuteOfDay % 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h >= 12 ? "PM" : "AM")}";
        }
    }
}
