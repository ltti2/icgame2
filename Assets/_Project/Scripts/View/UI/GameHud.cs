using System;
using IceCoast.Simulation.Core;
using IceCoast.View.CameraControl;
using IceCoast.View.Input;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.Tools;
using IceCoast.View.UI.Toolkit;
using IceCoast.View.UI.Windows;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// The in-game interface, modelled on OpenTTD: a toolbar of buttons along the top (with dropdown
    /// windows for tools and menus), a status bar along the bottom (date, time, latest message, money),
    /// movable windows for everything you click, and a Crusader Kings II style map-mode panel with a
    /// minimap in the bottom-right corner.
    ///
    /// Runs before the tools each frame so it can claim the mouse first: whatever is under a window or
    /// button never reaches the map. Everything is drawn with <see cref="UiRenderer"/> in OnGUI.
    /// All game changes still go through commands (via the runner), so the simulation is untouched.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameHud : MonoBehaviour
    {
        private GameUi _game;
        private UiContext _ui;
        private WindowManager _windows;
        private MainToolbar _toolbar;
        private StatusBar _status;
        private OverlayPanel _overlay;

        private ToolBase _lastTool;
        private int _lastClickVersion;
        private bool _pointerOverUi;

        public void Initialize(SimulationRunner runner, ViewState view, ToolController tools, IsoCameraController cam,
                               GuestRenderer guests, Action<bool> newGame)
        {
            var settings = GameSettings.Load();
            _ui = new UiContext(new UiRenderer(new UiAtlas()));
            _windows = new WindowManager();
            _game = new GameUi
            {
                Runner = runner,
                View = view,
                Tools = tools,
                Camera = cam,
                Guests = guests,
                Windows = _windows,
                Messages = new NotificationFeed(runner),
                Settings = settings,
                NewGame = newGame,
            };
            _toolbar = new MainToolbar(_game);
            _status = new StatusBar(_game);
            _overlay = new OverlayPanel(_game);

            cam.EdgeScrolling = settings.EdgeScrolling;
            cam.PanSpeed = IsoCameraController.DefaultPanSpeed * settings.PanSpeedPercent / 100f;

            runner.SimulationChanged += sim =>
            {
                sim.Units.System = _game.Settings.Metric ? UnitSystem.Metric : UnitSystem.Imperial;
                // Windows about things in the old game would point at the wrong lifts and runs.
                foreach (var w in new System.Collections.Generic.List<UiWindow>(_windows.Windows))
                    if (w is LiftWindow || w is RunWindow || w is TileWindow || w is ConfirmWindow || w is BankruptWindow)
                        _windows.Close(w);
                _lastClickVersion = view.ClickVersion;
            };
        }

        private void Update()
        {
            if (_game == null || _game.Sim == null) return;
            var view = _game.View;

            _ui.BeginFrame(_game.Settings.EffectiveScale, GameInput.MousePosition, GameInput.LeftDown, GameInput.LeftHeld,
                           GameInput.LeftUp, GameInput.RightDown, GameInput.ScrollDelta);

            // Layout first, so hit testing uses this frame's positions.
            _toolbar.Layout(_ui);
            _status.Layout(_ui);
            _overlay.Layout(_ui, _status.Bounds.y - 1);
            _windows.Bounds = Rect.MinMaxRect(0, _toolbar.Bounds.yMax + 1, _ui.Width, _status.Bounds.y - 1);
            _windows.Avoid.Clear();
            _windows.Avoid.Add(_overlay.Bounds);

            _ui.HotLayer = HotLayer();
            _pointerOverUi = _ui.HotLayer != 0 || _windows.IsInteracting || _overlay.IsInteracting || _ui.ActiveId != 0;
            view.PointerOverUi = _pointerOverUi;
            GameInput.KeyboardSuppressed = _ui.HasTextFocus;

            HandleKeys();
            SyncToolWindow();
            OpenWindowsForClicks();
            OpenStateWindows();

            // Bottom to top: map panel, floating windows, then the bars (always on top).
            _overlay.Draw(_ui);
            _windows.Draw(_ui);
            _toolbar.Draw(_ui);
            _status.Draw(_ui);
            DrawCursorTooltip();
            _ui.EndFrame();
        }

        private void OnGUI()
        {
            if (_ui == null) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && _ui.HasTextFocus)
            {
                _ui.QueueKey(e.keyCode, e.character);
                e.Use();
                return;
            }
            if (e.type == EventType.Repaint)
                _ui.R.Render(Screen.width / (float)_ui.Scale, Screen.height / (float)_ui.Scale);
        }

        /// <summary>Which layer (bar, window or panel) the mouse is over; 0 = the map.</summary>
        private int HotLayer()
        {
            var m = _ui.Mouse;
            if (_toolbar.Bounds.Contains(m)) return MainToolbar.LayerId;
            if (_status.Bounds.Contains(m)) return StatusBar.LayerId;
            var w = _windows.TopmostAt(m);
            if (w != null) return w.LayerId;
            if (_overlay.Bounds.Contains(m)) return OverlayPanel.LayerId;
            return 0;
        }

        private void HandleKeys()
        {
            if (GameInput.Pressed(GameKey.Delete)) _windows.CloseAllUnpinned();
            if (GameInput.Pressed(GameKey.QuickSave)) _game.QuickSave();
            if (GameInput.Pressed(GameKey.QuickLoad)) _game.QuickLoad();
        }

        /// <summary>
        /// A construction tool was picked (toolbar or hotkey): drop its options window under its button.
        /// Windows of tools that are no longer active close themselves.
        /// </summary>
        private void SyncToolWindow()
        {
            var active = _game.Tools.Active;
            if (active == _lastTool) return;
            _lastTool = active;
            if (active == null || !active.HasOptions) return;
            var w = _windows.Open(new ToolOptionsWindow(_game, active));
            _windows.PlaceBelow(w, _toolbar.RectFor(active));
        }

        /// <summary>The Inspect tool clicked something: open (or update) its window next to the click.</summary>
        private void OpenWindowsForClicks()
        {
            var view = _game.View;
            if (view.ClickVersion == _lastClickVersion) return;
            _lastClickVersion = view.ClickVersion;

            var s = view.ClickScreenPosition;
            var near = new Vector2(s.x, Screen.height - s.y) / _ui.Scale;
            if (view.SelectedLiftId != 0) _game.OpenLift(view.SelectedLiftId, near);
            else if (view.SelectedRunId != 0)
            {
                _game.OpenRun(view.SelectedRunId, near);
                _game.ShowTile(view.SelectedTile, near, openIfClosed: false);
            }
            else if (view.SelectedTile.IsValid) _game.ShowTile(view.SelectedTile, near);
        }

        private void OpenStateWindows()
        {
            if (_game.Sim.World.Economy.Bankrupt && !_windows.IsOpen(BankruptWindow.WindowKey))
                _windows.PlaceCentered(_windows.Open(new BankruptWindow(_game)));
            if (_game.Settings.ShowDebug && !_windows.IsOpen(DebugWindow.WindowKey))
                _game.OpenSingleton(new DebugWindow(_game));
        }

        /// <summary>What the active tool will do and what it costs, next to the cursor (red when it can't).</summary>
        private void DrawCursorTooltip()
        {
            var ctx = _game.Tools.Context;
            if (!_game.Settings.CursorTooltips || _pointerOverUi || ctx == null || !ctx.HasHover) return;
            if (_game.Tools.Active == _game.Tools.Inspect || string.IsNullOrEmpty(ctx.Status)) return;
            _ui.DrawTooltip(ctx.Status, _ui.Mouse.x + 14, _ui.Mouse.y + 16, ctx.StatusIsError ? new Color32(176, 0, 0, 255) : (Color32?)null);
        }
    }
}
