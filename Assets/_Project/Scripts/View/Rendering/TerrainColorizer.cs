using IceCoast.Simulation;
using IceCoast.Simulation.Land;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;
using IceCoast.View.Runtime;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Decides the colour of one terrain tile for the current overlay. This is where "readable systems"
    /// (design pillar 3) happens for the map: every overlay is just a different colour function.
    /// </summary>
    public sealed class TerrainColorizer
    {
        private readonly GameSimulation _sim;
        private readonly ViewState _view;
        private readonly System.Collections.Generic.Dictionary<int, Color32> _parcelColors = new System.Collections.Generic.Dictionary<int, Color32>();

        public TerrainColorizer(GameSimulation sim, ViewState view)
        {
            _sim = sim;
            _view = view;
        }

        /// <summary>Call once per frame before colouring tiles; clears per-frame caches.</summary>
        public void BeginPass() => _parcelColors.Clear();

        public Color32 TileColor(int x, int y)
        {
            var map = _sim.Map;
            var t = map.Locate(x, y);
            var chunk = t.Chunk;
            int i = t.Index;
            bool owned = (chunk.Flags[i] & (byte)TileFlags.Owned) != 0;

            Color32 c;
            switch (_view.Overlay)
            {
                case OverlayMode.Difficulty: c = DifficultyColor(x, y); break;
                case OverlayMode.SnowDepth: c = Palette.Heat(1f - chunk.SnowDepth[i] / 150f); break;
                case OverlayMode.SnowQuality: c = QualityOverlayColor((SnowQuality)chunk.SnowQuality[i]); break;
                case OverlayMode.Wear: c = Palette.Heat(chunk.Compaction[i] / 255f); break;
                case OverlayMode.Temperature: c = Palette.Heat(Mathf.InverseLerp(-20f, 5f, _sim.WeatherSystem.TemperatureAt(x, y))); break;
                case OverlayMode.Ownership: c = OwnershipColor(x, y, owned); break;
                case OverlayMode.Lighting: c = LightingColor(chunk, i); break;
                case OverlayMode.Scenic: c = NaturalColor(x, y, chunk, i, showTrails: false); break;
                default: c = NaturalColor(x, y, chunk, i, showTrails: true); break;
            }

            // Sunlight and slope shading are applied in the shader (DayLightController), not baked in here.
            if (((x + y) & 1) == 0) c = Palette.Scale(c, 0.985f); // faint checker so flat ground reads as tiles

            if (!owned && _view.Overlay != OverlayMode.Ownership)
                c = Palette.DimUnowned(c);
            return c;
        }

        /// <summary>
        /// Which ground texture a tile gets (None while a map overlay is showing, so overlays stay flat colour).
        /// Corduroy is laid down the fall line, so its direction follows the tile's slope.
        /// </summary>
        public SurfaceDetail TileDetail(int x, int y, int h00, int h10, int h01, int h11)
        {
            if (!_view.NaturalLook) return SurfaceDetail.None;
            var t = _sim.Map.Locate(x, y);
            switch ((SurfaceType)t.Chunk.Surface[t.Index])
            {
                case SurfaceType.Road: return RoadDetail(x, y);
                case SurfaceType.Parking:
                    var lot = _sim.ParkingSystem.LotAt(x, y);
                    return lot != null && y == lot.AisleY ? SurfaceDetail.ParkingAisle : SurfaceDetail.ParkingStalls;
                case SurfaceType.WalkingArea:
                    if (t.Chunk.SnowDepth[t.Index] >= _sim.Config.Snow.BareDepthCm) return SurfaceDetail.Walkway;
                    break;
            }
            return NaturalDetail(t.Chunk, t.Index, _sim.Config.Snow.BareDepthCm, h00, h10, h01, h11);
        }

        /// <summary>Centre line along the road's direction: along x if the road or lot continues in x, else along y.</summary>
        private SurfaceDetail RoadDetail(int x, int y)
        {
            var map = _sim.Map;
            return map.IsPaved(x + 1, y) || map.IsPaved(x - 1, y) ? SurfaceDetail.RoadX : SurfaceDetail.RoadY;
        }

        /// <summary>
        /// Light from trail lights falling on a grid corner (0..1): the average of the four tiles around it, so
        /// the glow is smooth. Zero while the lights are off (daytime) or a data overlay is on.
        /// </summary>
        public float CornerLamp(int vx, int vy)
        {
            if (!_sim.LightingSystem.LightsOn || !_view.NaturalLook) return 0f;
            var map = _sim.Map;
            int sum = 0;
            for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    int x = vx + dx, y = vy + dy;
                    if (map.InBounds(x, y)) sum += map.GetLightLevel(x, y);
                }
            return Mathf.Clamp01(sum / (4f * 200f));
        }

        public static SurfaceDetail NaturalDetail(Chunk chunk, int i, int bareDepthCm, int h00, int h10, int h01, int h11)
        {
            var type = (TerrainType)chunk.TerrainType[i];
            bool snowy = chunk.SnowDepth[i] >= bareDepthCm;
            if (!snowy) return GroundDetail(type);
            if (type == TerrainType.Rock) return SurfaceDetail.SnowyRock;
            switch ((SnowQuality)chunk.SnowQuality[i])
            {
                case SnowQuality.Powder: return SurfaceDetail.Powder;
                case SnowQuality.Groomed: return GroomedDetail(h00, h10, h01, h11);
                case SnowQuality.Slush: return SurfaceDetail.Slush;
                case SnowQuality.Ice: return SurfaceDetail.Ice;
                case SnowQuality.Crud: return SurfaceDetail.Crud;
                case SnowQuality.Bare: return GroundDetail(type);
                default: return SurfaceDetail.Packed;
            }
        }

        public static SurfaceDetail GroundDetail(TerrainType type)
        {
            switch (type)
            {
                case TerrainType.Dirt: return SurfaceDetail.Dirt;
                case TerrainType.Rock: return SurfaceDetail.Rock;
                case TerrainType.Scree: return SurfaceDetail.Scree;
                case TerrainType.Water: return SurfaceDetail.Water;
                default: return SurfaceDetail.Grass;
            }
        }

        /// <summary>Corduroy along the steepest direction of the tile (x, y, or one of the screen diagonals).</summary>
        public static SurfaceDetail GroomedDetail(int h00, int h10, int h01, int h11)
        {
            float gx = (h10 + h11) - (h00 + h01);
            float gy = (h01 + h11) - (h00 + h10);
            float ax = Mathf.Abs(gx), ay = Mathf.Abs(gy);
            if (ax < 0.01f && ay < 0.01f) return SurfaceDetail.GroomedDown;
            if (ax > ay * 2.4f) return SurfaceDetail.GroomedX;
            if (ay > ax * 2.4f) return SurfaceDetail.GroomedY;
            return gx * gy > 0f ? SurfaceDetail.GroomedDown : SurfaceDetail.GroomedAcross;
        }

        private Color32 NaturalColor(int x, int y, Chunk chunk, int i, bool showTrails)
        {
            var map = _sim.Map;
            var type = (TerrainType)chunk.TerrainType[i];
            bool snowy = chunk.SnowDepth[i] >= _sim.Config.Snow.BareDepthCm;
            var surface = (SurfaceType)chunk.Surface[i];
            if (surface == SurfaceType.Road || surface == SurfaceType.Parking) return Palette.Paint; // texture darkens to asphalt

            Color32 c = snowy ? Palette.Snow((SnowQuality)chunk.SnowQuality[i]) : Palette.Ground(type);
            if (snowy && type == TerrainType.Rock) c = Color32.Lerp(c, Palette.Rock, 0.45f);
            if (surface == SurfaceType.WalkingArea) c = snowy ? Color32.Lerp(Palette.Packed, Palette.Walkway, 0.55f) : Palette.Dirt;
            else if (snowy && (chunk.Flags[i] & (byte)TileFlags.BaseArea) != 0) c = Color32.Lerp(c, Palette.Dirt, 0.15f);

            int runId = chunk.RunId[i];
            if (runId != 0 && showTrails)
            {
                var run = _sim.World.GetRun(runId);
                if (run != null)
                {
                    float amount = runId == _view.SelectedRunId ? 0.55f : 0.28f;
                    if (!run.Open) amount *= 0.4f;
                    c = Color32.Lerp(c, Palette.ForDifficulty(run.DisplayedRating), amount);
                }
            }
            return c;
        }

        /// <summary>
        /// Trail light coverage: night blue where it's dark, warming to lamp yellow where lights reach. Trail tiles
        /// are drawn stronger (lit = bright yellow, unlit = red) so dark stretches of trail stand out.
        /// </summary>
        private Color32 LightingColor(Chunk chunk, int i)
        {
            int level = chunk.LightLevel[i];
            int threshold = _sim.Config.Lighting.LitThreshold;
            bool trail = chunk.RunId[i] != 0;
            var surface = (SurfaceType)chunk.Surface[i];
            if (trail) return level >= threshold ? new Color32(255, 220, 70, 255) : new Color32(200, 60, 50, 255);
            var dark = surface == SurfaceType.Natural ? new Color32(36, 46, 82, 255) : new Color32(70, 74, 92, 255);
            return Color32.Lerp(dark, new Color32(250, 230, 150, 255), Mathf.Clamp01(level / (float)threshold) * 0.85f);
        }

        private Color32 DifficultyColor(int x, int y)
        {
            float slope = TerrainQueries.SlopePercent(_sim.Map, _sim.TerrainScale, x, y);
            if (slope < _sim.Config.Runs.FlatGrade) return new Color32(200, 200, 200, 255);
            var rating = RunRater.Rate(_sim.Config.Runs, slope, slope);
            var c = Palette.ForDifficulty(rating);
            return rating == Difficulty.Black ? new Color32(60, 60, 64, 255) : c; // keep black readable
        }

        private static Color32 QualityOverlayColor(SnowQuality q)
        {
            switch (q)
            {
                case SnowQuality.Powder: return new Color32(255, 255, 255, 255);
                case SnowQuality.Groomed: return new Color32(120, 220, 240, 255);
                case SnowQuality.Packed: return new Color32(170, 180, 190, 255);
                case SnowQuality.Crud: return new Color32(200, 170, 120, 255);
                case SnowQuality.Slush: return new Color32(210, 200, 60, 255);
                case SnowQuality.Ice: return new Color32(40, 90, 220, 255);
                default: return new Color32(200, 60, 50, 255);
            }
        }

        private Color32 OwnershipColor(int x, int y, bool owned)
        {
            if (owned) return new Color32(110, 190, 120, 255);
            Parcel p = _sim.ParcelSystem.GetParcelAt(x, y);
            if (p == null) return Palette.Unowned;
            if (_parcelColors.TryGetValue(p.Id, out var cached)) return cached;
            var color = ParcelColor(p);
            _parcelColors[p.Id] = color;
            return color;
        }

        private Color32 ParcelColor(Parcel p)
        {
            bool buyable = _sim.ParcelSystem.ValidatePurchase(p).Ok;
            bool repOk = _sim.Config.SandboxMode || _sim.World.Economy.Reputation >= p.RequiredReputation;
            if (buyable) return new Color32(230, 200, 80, 255);
            return repOk ? new Color32(150, 150, 160, 255) : new Color32(110, 90, 120, 255);
        }
    }
}
