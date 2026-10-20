using IceCoast.Simulation.Weather;
using IceCoast.View.UI.Toolkit;

namespace IceCoast.View.UI.Windows
{
    /// <summary>Current weather at the base and the forecast.</summary>
    public sealed class WeatherWindow : UiWindow
    {
        public const string WindowKey = "weather";
        private readonly GameUi _game;

        public WeatherWindow(GameUi game) : base(WindowKey, "Weather", 230)
        {
            _game = game;
            Scheme = UiTheme.Blue;
        }

        public override void DrawContent(UiContext ui)
        {
            var sim = _game.Sim;
            if (sim == null) return;
            var wx = sim.World.Weather;
            var units = sim.Units;

            var row = ui.Row(18);
            ui.R.Icon(IconFor(wx.Type), row.x, row.y + 1, new UnityEngine.Color32(255, 255, 255, 255));
            ui.R.Text(row.x + 20, row.y + 4, WeatherSystem.Describe(wx.Type), UiTheme.TextYellow);
            ui.Field("Temperature (base)", units.Temperature(wx.BaseTemperature));
            ui.Field("Wind", units.Speed(wx.WindSpeed));
            if (wx.IsSnowing && wx.Precipitation > 0f) ui.Field("Snowfall", units.SnowRate(wx.Precipitation));

            ui.Separator();
            ui.Heading("Forecast");
            if (wx.Forecast.Count == 0) ui.Label("No forecast.", ui.Scheme.Text);
            foreach (var f in wx.Forecast)
                ui.Field($"{WeatherSystem.Describe(f.Type)}, ~{f.DurationHours}h", $"{f.Confidence * 100:0}% sure");
        }

        /// <summary>Toolbar/weather icon for a weather type.</summary>
        public static string IconFor(WeatherType t)
        {
            switch (t)
            {
                case WeatherType.LightSnow:
                case WeatherType.HeavySnow:
                case WeatherType.Blizzard: return "snow";
                case WeatherType.Rain: return "rain";
                case WeatherType.Cloudy:
                case WeatherType.Fog: return "cloud";
                default: return "sun";
            }
        }
    }
}
