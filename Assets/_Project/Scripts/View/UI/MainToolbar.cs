using System;
using System.Collections.Generic;
using IceCoast.View.Runtime;
using IceCoast.View.Tools;
using IceCoast.View.UI.Toolkit;
using IceCoast.View.UI.Windows;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// The row of buttons along the top of the screen, OpenTTD style: game speed, save/load, settings,
    /// construction tools, and information windows. Buttons with options open a small window right under
    /// themselves (lift types, terraform modes, speeds, save slots...). That window can be dragged away.
    /// </summary>
    public sealed class MainToolbar
    {
        public const int LayerId = 1;
        public const float Height = 24f;
        private const float ButtonSize = 22f;
        private const float GroupGap = 6f;

        private struct Button
        {
            public Func<string> Icon;
            public string Tooltip;
            public Func<bool> Down;
            public Action<Rect> OnClick;
            public ToolBase Tool;
            public bool GroupStart;
        }

        private readonly GameUi _game;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly Dictionary<ToolBase, Rect> _toolRects = new Dictionary<ToolBase, Rect>();

        public Rect Bounds { get; private set; }

        public MainToolbar(GameUi game)
        {
            _game = game;
            var runner = game.Runner;
            var tools = game.Tools;

            // Game speed
            Add("pause", "Pause (Space)", () => runner.Paused, _ => runner.TogglePause(), group: true);
            Add("play", "Play at normal speed", () => runner.SpeedIndex == 1, _ => runner.SetSpeedIndex(1));
            Add("fastforward", "Fast forward: pick a speed (. and , also change it)", () => runner.SpeedIndex > 1, SpeedMenu);

            // Game
            Add("save", "Save game (F5 quick saves)", () => IsMenuOpen("save"), SaveMenu, group: true);
            Add("load", "Load game (F9 quick loads)", () => IsMenuOpen("load"), LoadMenu);
            Add("newgame", "New game / quit", () => IsMenuOpen("game"), GameMenu);
            Add("settings", "Settings", () => _game.Windows.IsOpen(SettingsWindow.WindowKey), _ => _game.ToggleSingleton(new SettingsWindow(_game)));

            // Construction tools
            bool first = true;
            foreach (var tool in tools.Tools)
            {
                var t = tool;
                _buttons.Add(new Button
                {
                    Icon = () => t.Icon,
                    Tooltip = $"{t.Name} ({t.Hotkey})",
                    Down = () => tools.Active == t,
                    OnClick = _ => tools.Activate(tools.Active == t && t != tools.Inspect ? tools.Inspect : t),
                    Tool = t,
                    GroupStart = first,
                });
                first = false;
            }

            // Information
            Add("resort", "Resort overview", () => _game.Windows.IsOpen(ResortWindow.WindowKey), _ => _game.ToggleSingleton(new ResortWindow(_game)), group: true);
            Add("finance", "Finances and ticket price", () => _game.Windows.IsOpen(FinanceWindow.WindowKey), _ => _game.ToggleSingleton(new FinanceWindow(_game)));
            Add("liftlist", "List of lifts", () => _game.Windows.IsOpen(LiftListWindow.WindowKey), _ => _game.ToggleSingleton(new LiftListWindow(_game)));
            Add("runlist", "List of runs", () => _game.Windows.IsOpen(RunListWindow.WindowKey), _ => _game.ToggleSingleton(new RunListWindow(_game)));
            _buttons.Add(new Button
            {
                Icon = () => _game.Sim != null ? WeatherWindow.IconFor(_game.Sim.World.Weather.Type) : "sun",
                Tooltip = "Weather and forecast",
                Down = () => _game.Windows.IsOpen(WeatherWindow.WindowKey),
                OnClick = _ => _game.ToggleSingleton(new WeatherWindow(_game)),
            });
            Add("messages", "Message log", () => _game.Windows.IsOpen(MessageLogWindow.WindowKey), _ => _game.ToggleSingleton(new MessageLogWindow(_game)));
        }

        private void Add(string icon, string tooltip, Func<bool> down, Action<Rect> onClick, bool group = false)
            => _buttons.Add(new Button { Icon = () => icon, Tooltip = tooltip, Down = down, OnClick = onClick, GroupStart = group });

        /// <summary>Where a tool's button is, so its options window can drop down under it.</summary>
        public Rect RectFor(ToolBase tool) => _toolRects.TryGetValue(tool, out var r) ? r : new Rect(Bounds.center.x, Bounds.y, ButtonSize, ButtonSize);

        public void Layout(UiContext ui)
        {
            Bounds = new Rect(0, 0, ui.Width, Height);
        }

        public void Draw(UiContext ui)
        {
            ui.BeginLayer(LayerId, UiTheme.Grey);
            var R = ui.R;
            R.Bevel(Bounds, UiTheme.Grey, false);
            R.Fill(new Rect(0, Bounds.yMax - 1, Bounds.width, 1), UiTheme.Outline);

            int groups = 0;
            foreach (var b in _buttons) if (b.GroupStart) groups++;
            float total = _buttons.Count * (ButtonSize + 1) + Mathf.Max(0, groups - 1) * GroupGap;
            float x = Mathf.Floor(Mathf.Max(2f, (ui.Width - total) * 0.5f));
            bool firstGroup = true;
            _toolRects.Clear();

            foreach (var b in _buttons)
            {
                if (b.GroupStart && !firstGroup) x += GroupGap;
                firstGroup = false;
                var r = new Rect(x, 1, ButtonSize, ButtonSize);
                if (b.Tool != null) _toolRects[b.Tool] = r;
                if (ui.IconButton(r, b.Icon(), b.Down(), b.Tooltip)) b.OnClick(r);
                x += ButtonSize + 1;
            }
        }

        // =====================================================================
        // Dropdown menus
        // =====================================================================

        private bool IsMenuOpen(string key) => _game.Windows.IsOpen("menu:" + key);

        private void ShowMenu(MenuWindow menu, Rect anchor)
        {
            var existing = _game.Windows.Find(menu.Key);
            if (existing != null)
            {
                _game.Windows.Close(existing);
                return;
            }
            menu.Dismiss = m => _game.Windows.Close(m);
            _game.Windows.Open(menu);
            _game.Windows.PlaceBelow(menu, anchor);
        }

        private void SpeedMenu(Rect anchor)
        {
            var runner = _game.Runner;
            var menu = new MenuWindow("speed", "Game speed", 150);
            string[] names = { "Paused", "Normal (1x)", "Fast (2x)", "Faster (4x)", "Fastest (8x)", "Ludicrous (16x)" };
            for (int i = 1; i < SimulationRunner.SpeedSteps.Length && i < names.Length; i++)
            {
                int index = i;
                menu.Add(names[i], () => runner.SetSpeedIndex(index), null, null, () => runner.SpeedIndex == index);
            }
            ShowMenu(menu, anchor);
        }

        private void SaveMenu(Rect anchor)
        {
            var menu = new MenuWindow("save", "Save game", 200);
            menu.Add("Quick save", _game.QuickSave, "F5");
            menu.AddSeparator();
            for (int slot = 1; slot <= 3; slot++)
            {
                int s = slot;
                string path = GameUi.SlotPath(s);
                menu.Add($"Save to slot {s}", () => _game.SaveTo(path, $"slot {s}"), GameUi.SaveStamp(path) ?? "empty");
            }
            ShowMenu(menu, anchor);
        }

        private void LoadMenu(Rect anchor)
        {
            var menu = new MenuWindow("load", "Load game", 200);
            string quick = SimulationRunner.QuickSavePath;
            menu.Add("Quick load", _game.QuickLoad, GameUi.SaveStamp(quick) ?? "F9", () => System.IO.File.Exists(quick));
            menu.AddSeparator();
            for (int slot = 1; slot <= 3; slot++)
            {
                int s = slot;
                string path = GameUi.SlotPath(s);
                menu.Add($"Load slot {s}", () => _game.LoadFrom(path, $"slot {s}"), GameUi.SaveStamp(path) ?? "empty", () => System.IO.File.Exists(path));
            }
            ShowMenu(menu, anchor);
        }

        private void GameMenu(Rect anchor)
        {
            var menu = new MenuWindow("game", "Game", 180);
            menu.Add("New game...", () => _game.Confirm("New game", "Start a new game? Anything you haven't saved will be lost.", "Start", () => _game.NewGame?.Invoke(false)));
            menu.Add("New sandbox game...", () => _game.Confirm("New sandbox game", "Start a sandbox game with unlimited money? Anything you haven't saved will be lost.", "Start", () => _game.NewGame?.Invoke(true)));
            menu.AddSeparator();
            menu.Add("Quit", () => _game.Confirm("Quit", "Quit Ice Coast? Anything you haven't saved will be lost.", "Quit", Application.Quit));
            ShowMenu(menu, anchor);
        }
    }
}
