using IceCoast.Simulation.Core;

namespace IceCoast.View.Runtime
{
    public enum OverlayMode
    {
        /// <summary>Natural view: textures and lighting, trails tinted by their sign colour.</summary>
        None,
        /// <summary>Natural view without trail markings: the mountain as a guest would see it.</summary>
        Scenic,
        Difficulty,
        SnowDepth,
        SnowQuality,
        Wear,
        Temperature,
        Ownership,
        /// <summary>Trail light coverage: which tiles and trails are lit at night.</summary>
        Lighting,
    }

    /// <summary>
    /// Presentation-only state shared by renderers, tools and the HUD: what is selected, which overlay is on.
    /// Lives in the view layer because the simulation should not care what the player is looking at.
    /// </summary>
    public sealed class ViewState
    {
        private OverlayMode _overlay = OverlayMode.None;
        private int _selectedRunId;

        public OverlayMode Overlay
        {
            get => _overlay;
            set { if (_overlay != value) { _overlay = value; ColorEpoch++; } }
        }

        /// <summary>Selected run is drawn highlighted on the terrain.</summary>
        public int SelectedRunId
        {
            get => _selectedRunId;
            set { if (_selectedRunId != value) { _selectedRunId = value; ColorEpoch++; } }
        }

        /// <summary>Elevation contour lines on the terrain (toggle with C or the HUD).</summary>
        public bool ShowContours;

        public int SelectedLiftId;
        public Int2 SelectedTile = Int2.Invalid;

        public Int2 HoverTile = Int2.Invalid;
        public Int2 HoverVertex = Int2.Invalid;
        public bool PointerOverUi;

        /// <summary>Bumped whenever terrain colours must be recomputed for reasons outside the simulation.</summary>
        public int ColorEpoch { get; private set; }
        public void InvalidateColors() => ColorEpoch++;

        public void ClearSelection()
        {
            SelectedLiftId = 0;
            SelectedRunId = 0;
            SelectedTile = Int2.Invalid;
        }

        /// <summary>Bumped every time the player clicks something with the Inspect tool. The HUD opens a window for it.</summary>
        public int ClickVersion { get; private set; }
        /// <summary>Screen position (bottom-left origin) of the last inspect click, so windows open nearby.</summary>
        public UnityEngine.Vector2 ClickScreenPosition;

        /// <summary>The player clicked something: a lift, a run (with the tile under it) or bare ground.</summary>
        public void Select(Int2 tile, int liftId, int runId, UnityEngine.Vector2 screenPosition)
        {
            ClearSelection();
            SelectedTile = tile;
            SelectedLiftId = liftId;
            SelectedRunId = runId;
            ClickScreenPosition = screenPosition;
            ClickVersion++;
        }

        /// <summary>Natural looking modes (textures, sunlight) as opposed to flat-coloured data overlays.</summary>
        public static bool IsNaturalLook(OverlayMode m) => m == OverlayMode.None || m == OverlayMode.Scenic;
        public bool NaturalLook => IsNaturalLook(_overlay);

        public void CycleOverlay()
        {
            int n = System.Enum.GetValues(typeof(OverlayMode)).Length;
            Overlay = (OverlayMode)(((int)Overlay + 1) % n);
        }
    }
}
