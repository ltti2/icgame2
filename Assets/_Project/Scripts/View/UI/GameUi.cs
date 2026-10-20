using System;
using System.IO;
using IceCoast.Simulation;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Runs;
using IceCoast.View.CameraControl;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.Tools;
using IceCoast.View.UI.Toolkit;
using IceCoast.View.UI.Windows;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// Everything the HUD and its windows need, in one place: the game, the camera, tools, settings,
    /// messages and the window manager, plus the actions windows share (open the window for a lift,
    /// save, load, start a new game...).
    /// </summary>
    public sealed class GameUi
    {
        public SimulationRunner Runner;
        public ViewState View;
        public ToolController Tools;
        public IsoCameraController Camera;
        public GuestRenderer Guests;
        public WindowManager Windows;
        public NotificationFeed Messages;
        public GameSettings Settings;
        /// <summary>Starts a new game; the flag asks for sandbox mode (unlimited money).</summary>
        public Action<bool> NewGame;

        public GameSimulation Sim => Runner.Sim;

        // =====================================================================
        // Object windows
        // =====================================================================

        public void OpenLift(int liftId, Vector2? near = null)
        {
            var w = Windows.Find(LiftWindow.KeyFor(liftId));
            if (w != null) { Windows.BringToFront(w); return; }
            var lw = Windows.Open(new LiftWindow(this, liftId));
            Place(lw, near);
            View.SelectedLiftId = liftId;
        }

        public void OpenBuilding(int buildingId, Vector2? near = null)
        {
            var w = Windows.Find(BuildingWindow.KeyFor(buildingId));
            if (w != null) { Windows.BringToFront(w); View.SelectedBuildingId = buildingId; return; }
            var bw = Windows.Open(new BuildingWindow(this, buildingId));
            Place(bw, near);
            View.SelectedBuildingId = buildingId;
        }

        public void OpenRun(int runId, Vector2? near = null)
        {
            var w = Windows.Find(RunWindow.KeyFor(runId));
            if (w != null) { Windows.BringToFront(w); View.SelectedRunId = runId; return; }
            var rw = Windows.Open(new RunWindow(this, runId));
            Place(rw, near);
            View.SelectedRunId = runId;
        }

        /// <summary>
        /// The tile window is a singleton: clicking another patch of snow updates it instead of opening a
        /// second window.
        /// </summary>
        public void ShowTile(Int2 tile, Vector2? near = null, bool openIfClosed = true)
        {
            if (Windows.Find(TileWindow.WindowKey) is TileWindow existing)
            {
                existing.Tile = tile;
                Windows.BringToFront(existing);
                return;
            }
            if (!openIfClosed) return;
            var tw = Windows.Open(new TileWindow(this, tile));
            Place(tw, near);
        }

        public T OpenSingleton<T>(T window) where T : UiWindow
        {
            bool wasOpen = Windows.IsOpen(window.Key);
            var w = Windows.Open(window);
            if (!wasOpen) Windows.PlaceInFreeSpace(w);
            return w;
        }

        /// <summary>Opens the window, or closes it if it is already open (toolbar buttons toggle).</summary>
        public void ToggleSingleton(UiWindow window)
        {
            var existing = Windows.Find(window.Key);
            if (existing != null) Windows.Close(existing);
            else OpenSingleton(window);
        }

        private void Place(UiWindow w, Vector2? near)
        {
            if (near.HasValue) Windows.PlaceNear(w, near.Value);
            else Windows.PlaceInFreeSpace(w);
        }

        public void Focus(Int2 tile)
        {
            if (Sim == null || !tile.IsValid) return;
            Camera.FocusOnTile(Sim.Map, tile.X, tile.Y);
        }

        public void Confirm(string title, string message, string yes, Action onYes)
        {
            var w = Windows.Open(new ConfirmWindow(title, message, yes, onYes));
            Windows.PlaceCentered(w);
        }

        // =====================================================================
        // Save / load
        // =====================================================================

        public static string SlotPath(int slot) => Path.Combine(SimulationRunner.SaveDirectory, $"slot{slot}.icsave");

        public void SaveTo(string path, string label)
        {
            if (Runner.Save(path, out var error)) Messages.Post($"Game saved ({label}).", EventSeverity.Good);
            else Messages.Post("Save failed: " + error, EventSeverity.Danger);
        }

        public void LoadFrom(string path, string label)
        {
            if (Runner.Load(path, out var error)) Messages.Post($"Game loaded ({label}).", EventSeverity.Good);
            else Messages.Post("Load failed: " + error, EventSeverity.Danger);
        }

        public void QuickSave() => SaveTo(SimulationRunner.QuickSavePath, "quick save");
        public void QuickLoad() => LoadFrom(SimulationRunner.QuickSavePath, "quick save");

        /// <summary>"Wed Dec 2, 10:41 AM" style timestamp of a save file, or null if there is none.</summary>
        public static string SaveStamp(string path)
        {
            if (!File.Exists(path)) return null;
            return File.GetLastWriteTime(path).ToString("MMM d, h:mm tt");
        }

        // =====================================================================
        // Formatting
        // =====================================================================

        public string Money(long amount) => amount < 0 ? $"-${-amount:N0}" : $"${amount:N0}";

        public string CashText => Sim.Config.SandboxMode ? "Unlimited" : Money(Sim.World.Economy.Cash);

        /// <summary>Severity colour readable on the light grey and brown windows.</summary>
        public static Color32 SeverityOnLight(EventSeverity s)
        {
            switch (s)
            {
                case EventSeverity.Good: return new Color32(0, 92, 0, 255);
                case EventSeverity.Warning: return new Color32(132, 64, 0, 255);
                case EventSeverity.Danger: return new Color32(156, 0, 0, 255);
                default: return UiTheme.TextBlack;
            }
        }

        /// <summary>Severity colour readable on black (the status bar ticker).</summary>
        public static Color32 SeverityOnDark(EventSeverity s)
        {
            switch (s)
            {
                case EventSeverity.Good: return UiTheme.TextGreen;
                case EventSeverity.Warning: return UiTheme.TextOrange;
                case EventSeverity.Danger: return UiTheme.TextRed;
                default: return UiTheme.TextWhite;
            }
        }

        /// <summary>Trail-sign colour readable as text on light windows.</summary>
        public static Color32 DifficultyText(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Green: return new Color32(0, 104, 24, 255);
                case Difficulty.Blue: return new Color32(16, 52, 184, 255);
                case Difficulty.Black: return UiTheme.TextBlack;
                case Difficulty.DoubleBlack: return new Color32(72, 0, 92, 255);
                default: return new Color32(156, 0, 0, 255);
            }
        }
    }
}
