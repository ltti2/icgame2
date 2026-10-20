using System;
using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.View.Input;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Tools
{
    /// <summary>
    /// The square paint brush shared by the area editors (trails, walking areas): a radius, a Paint / Erase
    /// mode (Shift flips it while held), and strokes. A stroke starts on mouse down, collects every tile the
    /// brush passes over, and ends on mouse up, so a whole drag can be validated and applied as one command.
    /// The mode is locked for the length of a stroke.
    /// </summary>
    public sealed class TileBrush
    {
        public const int MaxRadius = 6;

        public int Radius = 1;
        /// <summary>The mode picked in the options window. Shift held flips it.</summary>
        public bool EraseMode;

        private readonly HashSet<Int2> _stroke = new HashSet<Int2>();
        private bool _strokeErasing;

        /// <summary>Tiles covered by the stroke in progress (or just finished).</summary>
        public IReadOnlyCollection<Int2> Stroke => _stroke;
        public bool StrokeActive { get; private set; }
        /// <summary>Mode of the current (or just finished) stroke.</summary>
        public bool StrokeErasing => _strokeErasing;

        /// <summary>Whether the brush erases right now: the stroke's mode while dragging, else the current mode.</summary>
        public bool Erasing => StrokeActive ? _strokeErasing : EraseMode ^ GameInput.Held(GameKey.Modifier);

        public int Size => Radius * 2 + 1;

        /// <summary>[ and ] change the size.</summary>
        public void HandleKeys()
        {
            if (GameInput.Pressed(GameKey.BrushBigger)) Radius = Mathf.Min(MaxRadius, Radius + 1);
            if (GameInput.Pressed(GameKey.BrushSmaller)) Radius = Mathf.Max(0, Radius - 1);
        }

        public void ForEach(Int2 center, WorldMap map, Action<Int2> action)
        {
            for (int y = center.Y - Radius; y <= center.Y + Radius; y++)
                for (int x = center.X - Radius; x <= center.X + Radius; x++)
                    if (map.InBounds(x, y)) action(new Int2(x, y));
        }

        /// <summary>
        /// Advances the stroke from the mouse. Returns true on the frame the stroke ends (mouse released), when
        /// <see cref="Stroke"/> holds everything it covered. <paramref name="accept"/> filters which tiles join.
        /// </summary>
        public bool UpdateStroke(ToolContext ctx, Func<Int2, bool> accept = null)
        {
            if (!StrokeActive && GameInput.LeftDown && ctx.HasHover && !ctx.PointerOverUi)
            {
                StrokeActive = true;
                _strokeErasing = EraseMode ^ GameInput.Held(GameKey.Modifier);
                _stroke.Clear();
            }
            if (!StrokeActive) return false;

            if (GameInput.LeftHeld)
            {
                if (ctx.HasHover)
                    ForEach(ctx.HoverTile, ctx.Sim.Map, t => { if (accept == null || accept(t)) _stroke.Add(t); });
                return false;
            }

            StrokeActive = false; // released
            return true;
        }

        /// <summary>Drops the stroke in progress (Esc / right-click).</summary>
        public bool CancelStroke()
        {
            if (!StrokeActive && _stroke.Count == 0) return false;
            StrokeActive = false;
            _stroke.Clear();
            return true;
        }

        public void ClearStroke() => _stroke.Clear();

        /// <summary>The mode toggles and radius counter, for a tool's options window.</summary>
        public void OptionsUi(UiContext ui, string paintLabel = "Paint", string eraseLabel = "Erase")
        {
            int mode = ui.Toggles(EraseMode ? 1 : 0, paintLabel, eraseLabel);
            EraseMode = mode == 1;
            Radius = ui.Counter("Brush radius", Radius, 0, MaxRadius, 1, $"{Radius}  ({Size}x{Size})", "Also [ and ]");
        }
    }
}
