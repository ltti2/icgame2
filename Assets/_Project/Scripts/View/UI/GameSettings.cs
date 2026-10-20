using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// Player preferences shown in the Settings window. Stored in PlayerPrefs (per machine, not in saves).
    /// </summary>
    public sealed class GameSettings
    {
        private const string Prefix = "IceCoast.";

        /// <summary>Interface sizes offered in Settings. 0 = pick from the screen height.</summary>
        public static readonly float[] UiScaleSteps = { 0f, 1f, 1.5f, 2f, 3f, 4f };

        /// <summary>Interface scale (1, 1.5, 2, 3 or 4); 0 = pick from the screen height.</summary>
        public float UiScale;
        public bool Metric;
        public bool EdgeScrolling;
        /// <summary>Keyboard / edge panning speed, percent of default.</summary>
        public int PanSpeedPercent = 100;
        /// <summary>How long a message stays bright in the status bar, in seconds.</summary>
        public int MessageSeconds = 20;
        /// <summary>Show what the active tool will do (and what it costs) next to the cursor.</summary>
        public bool CursorTooltips = true;
        public bool MinimapVisible = true;
        public bool ShowDebug;

        public float EffectiveScale => UiScale > 0f ? UiScale : AutoScale;

        /// <summary>"1.5x", "2x" ...</summary>
        public static string ScaleLabel(float scale) => scale.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "x";
        public static int AutoScale => Mathf.Clamp(Mathf.RoundToInt(Screen.height / 720f), 1, 4);

        public static GameSettings Load()
        {
            var s = new GameSettings();
            // "UiScale" held whole steps only; "UiScaleF" adds 1.5x. Read the old key for older installs.
            s.UiScale = PlayerPrefs.HasKey(Prefix + "UiScaleF")
                ? PlayerPrefs.GetFloat(Prefix + "UiScaleF", 0f)
                : PlayerPrefs.GetInt(Prefix + "UiScale", 0);
            s.Metric = PlayerPrefs.GetInt(Prefix + "Metric", 0) != 0;
            s.EdgeScrolling = PlayerPrefs.GetInt(Prefix + "EdgeScrolling", 0) != 0;
            s.PanSpeedPercent = PlayerPrefs.GetInt(Prefix + "PanSpeed", 100);
            s.MessageSeconds = PlayerPrefs.GetInt(Prefix + "MessageSeconds", 20);
            s.CursorTooltips = PlayerPrefs.GetInt(Prefix + "CursorTooltips", 1) != 0;
            s.MinimapVisible = PlayerPrefs.GetInt(Prefix + "MinimapVisible", 1) != 0;
            s.ShowDebug = PlayerPrefs.GetInt(Prefix + "ShowDebug", 0) != 0;
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "UiScaleF", UiScale);
            PlayerPrefs.SetInt(Prefix + "UiScale", Mathf.RoundToInt(UiScale));
            PlayerPrefs.SetInt(Prefix + "Metric", Metric ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "EdgeScrolling", EdgeScrolling ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "PanSpeed", PanSpeedPercent);
            PlayerPrefs.SetInt(Prefix + "MessageSeconds", MessageSeconds);
            PlayerPrefs.SetInt(Prefix + "CursorTooltips", CursorTooltips ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "MinimapVisible", MinimapVisible ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ShowDebug", ShowDebug ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
