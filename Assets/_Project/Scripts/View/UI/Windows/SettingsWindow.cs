using IceCoast.Simulation.Core;
using IceCoast.View.CameraControl;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Game options. Changes apply immediately and are remembered between sessions.</summary>
    public sealed class SettingsWindow : UiWindow
    {
        public const string WindowKey = "settings";
        private readonly GameUi _game;

        public SettingsWindow(GameUi game) : base(WindowKey, "Settings", 270)
        {
            _game = game;
            Scheme = UiTheme.Grey;
        }

        public override void DrawContent(UiContext ui)
        {
            var s = _game.Settings;
            bool changed = false;

            ui.Heading("Interface");
            int auto = GameSettings.AutoScale;
            var steps = GameSettings.UiScaleSteps;
            int current = 0;
            for (int i = 0; i < steps.Length; i++) if (Mathf.Approximately(steps[i], s.UiScale)) current = i;
            int picked = ui.Counter("Interface size", current, 0, steps.Length - 1, 1,
                                    current == 0 ? $"Auto ({auto}x)" : GameSettings.ScaleLabel(steps[current]),
                                    "Pixel scale of all windows and text");
            if (picked != current) { s.UiScale = steps[picked]; changed = true; }

            int secs = ui.Counter("Message display time", s.MessageSeconds, 5, 120, 5, $"{s.MessageSeconds} s",
                                  "How long new messages stay highlighted in the status bar");
            if (secs != s.MessageSeconds) { s.MessageSeconds = secs; changed = true; }

            bool tips = ui.Checkbox("Show tool cost/info next to the cursor", s.CursorTooltips);
            if (tips != s.CursorTooltips) { s.CursorTooltips = tips; changed = true; }

            bool mini = ui.Checkbox("Show minimap", s.MinimapVisible);
            if (mini != s.MinimapVisible) { s.MinimapVisible = mini; changed = true; }

            ui.Separator();
            ui.Heading("Units");
            int units = ui.Toggles(s.Metric ? 1 : 0, "Imperial (°F, ft)", "Metric (°C, m)");
            if ((units == 1) != s.Metric)
            {
                s.Metric = units == 1;
                if (_game.Sim != null) _game.Sim.Units.System = s.Metric ? UnitSystem.Metric : UnitSystem.Imperial;
                changed = true;
            }

            ui.Separator();
            ui.Heading("Camera");
            int pan = ui.Counter("Scroll speed", s.PanSpeedPercent, 25, 300, 25, $"{s.PanSpeedPercent}%", "Keyboard and screen-edge scrolling speed");
            if (pan != s.PanSpeedPercent)
            {
                s.PanSpeedPercent = pan;
                _game.Camera.PanSpeed = IsoCameraController.DefaultPanSpeed * pan / 100f;
                changed = true;
            }
            bool edge = ui.Checkbox("Scroll at the screen edges", s.EdgeScrolling);
            if (edge != s.EdgeScrolling)
            {
                s.EdgeScrolling = edge;
                _game.Camera.EdgeScrolling = edge;
                changed = true;
            }
            _game.View.ShowContours = ui.Checkbox("Contour lines (C)", _game.View.ShowContours);

            ui.Separator();
            bool debug = ui.Checkbox("Show performance info", s.ShowDebug);
            if (debug != s.ShowDebug) { s.ShowDebug = debug; changed = true; }

            if (ui.Button("Close all windows (Delete)")) _game.Windows.CloseAllUnpinned();

            if (changed) s.Save();
        }
    }
}
