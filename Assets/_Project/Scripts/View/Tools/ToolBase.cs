using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.View.CameraControl;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>Everything a tool may read or use this frame. Built by ToolController.</summary>
    public sealed class ToolContext
    {
        public SimulationRunner Runner;
        public ViewState View;
        public PreviewRenderer Preview;
        public IsoCameraController Camera;

        public GameSimulation Sim => Runner.Sim;

        public bool HasHover;
        public Int2 HoverTile;
        public Int2 HoverVertex;
        public Vector2 MouseWorld;
        public bool PointerOverUi;

        /// <summary>One line shown in the HUD status bar: what the tool will do, its cost, or why it can't.</summary>
        public string Status;
        public bool StatusIsError;

        public void SetStatus(string text, bool error = false)
        {
            Status = text;
            StatusIsError = error;
        }

        public void SetStatus(CommandResult quote, string okText)
        {
            if (quote.Ok) SetStatus(okText + (quote.Cost > 0 ? $"  —  ${quote.Cost:N0}" : ""));
            else SetStatus(quote.Error + (quote.Cost > 0 ? $"  (${quote.Cost:N0})" : ""), true);
        }
    }

    /// <summary>
    /// A player tool (inspect, build lift, paint run...). Tools translate mouse input into commands and
    /// draw their own previews; they never modify simulation state directly.
    /// To add a tool: subclass this, then register it in ToolController.CreateTools.
    /// </summary>
    public abstract class ToolBase
    {
        public abstract string Name { get; }
        public virtual string Help => "";
        /// <summary>Hotkey shown in tooltips, e.g. "2".</summary>
        public string Hotkey = "";

        public virtual void OnActivated(ToolContext ctx) { }
        public virtual void OnDeactivated(ToolContext ctx) { }

        /// <summary>Called every frame while active (after hover picking). Handle input, draw previews, set status.</summary>
        public abstract void Tick(ToolContext ctx);

        /// <summary>Toolbar icon (see PixelArtData.IconNames).</summary>
        public virtual string Icon => "inspect";

        /// <summary>True if the tool has options, shown in a dropdown window under its toolbar button.</summary>
        public virtual bool HasOptions => false;

        /// <summary>Draws the tool's options into its dropdown window (OpenTTD-style widgets).</summary>
        public virtual void OnOptionsUi(UiContext ui, ToolContext ctx) { }

        /// <summary>Esc / right-click. Return true if the tool consumed it (e.g. cancelled a half-placed lift).</summary>
        public virtual bool Cancel(ToolContext ctx) => false;
    }
}
