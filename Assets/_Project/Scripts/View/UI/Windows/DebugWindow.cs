using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Frame rate, simulation cost and other numbers for development.</summary>
    public sealed class DebugWindow : UiWindow
    {
        public const string WindowKey = "debug";
        private readonly GameUi _game;
        private float _fps = 60f;

        public DebugWindow(GameUi game) : base(WindowKey, "Performance", 220)
        {
            _game = game;
            Scheme = UiTheme.Grey;
        }

        public override bool StillValid() => _game.Settings.ShowDebug;
        public override void OnClosed() { _game.Settings.ShowDebug = false; _game.Settings.Save(); }

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            ui.Field("Frame rate", $"{_fps:0} fps");
            ui.Field("Simulation", $"{_game.Runner.LastTickCostMs:0.00} ms/frame");
            if (sim == null) return;
            ui.Field("Tick", sim.World.Tick.ToString("N0"));
            ui.Field("Guests (drawn)", $"{sim.World.Guests.Count} ({_game.Guests.DrawnCount})");
            ui.Field("Hover tile", _game.View.HoverTile.ToString());
            ui.Field("Zoom", $"{_game.Camera.Zoom}x");
        }
    }
}
