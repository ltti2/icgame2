using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>
    /// Snow and ground at one tile. There is only ever one: clicking another tile updates it in place.
    /// </summary>
    public sealed class TileWindow : UiWindow
    {
        public const string WindowKey = "tile";
        private readonly GameUi _game;
        public Int2 Tile;

        public TileWindow(GameUi game, Int2 tile) : base(WindowKey, "Snow", 212)
        {
            _game = game;
            Tile = tile;
            Scheme = UiTheme.Brown;
        }

        public override string Caption => $"Snow at {Tile.X}, {Tile.Y}";
        public override string CaptionAction => "inspect";
        public override string CaptionActionTooltip => "Centre the view on this tile";
        public override void OnCaptionAction() => _game.Focus(Tile);

        public override bool StillValid() => _game.Sim != null && _game.Sim.Map.InBounds(Tile);

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            var map = sim.Map;
            var units = sim.Units;
            var t = map.Locate(Tile.X, Tile.Y);
            var c = t.Chunk;
            int i = t.Index;

            var quality = (SnowQuality)c.SnowQuality[i];
            ui.Field("Snow", $"{units.SnowDepth(c.SnowDepth[i])} {QualityName(quality)}", QualityColor(quality));
            ui.Field("Wear", $"{c.Compaction[i] * 100 / 255}%");
            ui.Field("Moisture", $"{c.Moisture[i] * 100 / 255}%");
            ui.Field("Air temperature", units.Temperature(sim.WeatherSystem.TemperatureAt(Tile.X, Tile.Y)));
            ui.Separator();
            ui.Field("Elevation", units.Elevation(TerrainQueries.ElevationMeters(map, sim.TerrainScale, Tile.X, Tile.Y)));
            ui.Field("Slope", $"{TerrainQueries.SlopePercent(map, sim.TerrainScale, Tile.X, Tile.Y):0}%");
            ui.Field("Ground", ((TerrainType)c.TerrainType[i]).ToString());
            int trees = c.TreeDensity[i];
            ui.Field("Trees", trees == 0 ? "None" : trees <= 1 ? "Sparse (glade)" : trees <= 2 ? "Open woods" : trees <= 3 ? "Forest" : "Dense forest");

            var surface = (SurfaceType)c.Surface[i];
            if (surface != SurfaceType.Natural)
                ui.Field("Surface", surface == SurfaceType.WalkingArea ? "Walking area" : surface == SurfaceType.Road ? "Road" : "Parking lot");
            int light = c.LightLevel[i];
            if (light > 0)
                ui.Field("Trail lights", light >= sim.Config.Lighting.LitThreshold ? $"Lit ({light * 100 / 255}%)" : $"Dim ({light * 100 / 255}%)");

            var parcel = sim.ParcelSystem.GetParcelAt(Tile.X, Tile.Y);
            if (parcel != null) ui.Field(parcel.Name, parcel.Owned ? "Yours" : $"For sale, ${parcel.Cost:N0}");

            int runId = c.RunId[i];
            var run = runId != 0 ? sim.World.GetRun(runId) : null;
            if (run != null)
            {
                ui.Separator();
                if (ui.Button($"Run: {run.Name}")) _game.OpenRun(run.Id);
            }
        }

        public static string QualityName(SnowQuality q)
        {
            switch (q)
            {
                case SnowQuality.Bare: return "bare";
                case SnowQuality.Powder: return "powder";
                case SnowQuality.Packed: return "packed";
                case SnowQuality.Groomed: return "groomed";
                case SnowQuality.Slush: return "slush";
                case SnowQuality.Ice: return "ice";
                default: return "crud";
            }
        }

        public static UnityEngine.Color32 QualityColor(SnowQuality q)
        {
            switch (q)
            {
                case SnowQuality.Ice: return new UnityEngine.Color32(16, 52, 184, 255);
                case SnowQuality.Bare:
                case SnowQuality.Slush: return new UnityEngine.Color32(156, 0, 0, 255);
                case SnowQuality.Powder:
                case SnowQuality.Groomed: return new UnityEngine.Color32(252, 252, 252, 255);
                default: return UiTheme.TextBlack;
            }
        }
    }
}
