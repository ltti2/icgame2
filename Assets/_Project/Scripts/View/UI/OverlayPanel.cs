using IceCoast.Simulation.Runs;
using IceCoast.View.Rendering;
using IceCoast.View.Runtime;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.UI
{
    /// <summary>
    /// Bottom-right map panel in the style of Crusader Kings II's map modes: a row of overlay buttons, a
    /// legend for the active overlay, and a minimap showing where the camera is. Click or drag on the
    /// minimap to move the camera. The ▼ button folds the minimap away.
    /// </summary>
    public sealed class OverlayPanel
    {
        public const int LayerId = 3;
        private const float InnerWidth = 220f;
        private const float Pad = 3f;
        private const float TitleHeight = 13f;
        private const float ButtonHeight = 20f;
        private const float LegendHeight = 11f;

        private static readonly OverlayMode[] Modes =
        {
            OverlayMode.None, OverlayMode.Scenic, OverlayMode.Difficulty, OverlayMode.SnowDepth, OverlayMode.SnowQuality,
            OverlayMode.Wear, OverlayMode.Temperature, OverlayMode.Lighting, OverlayMode.Ownership,
        };

        private readonly GameUi _game;
        private readonly Minimap _minimap = new Minimap();
        private bool _draggingMap;

        public Rect Bounds { get; private set; }
        public bool IsInteracting => _draggingMap;

        public OverlayPanel(GameUi game)
        {
            _game = game;
            game.Runner.SimulationChanged += sim => _minimap.SetSimulation(sim, game.View);
        }

        /// <summary>Works out where the panel goes this frame (call before hit testing).</summary>
        public void Layout(UiContext ui, float bottom)
        {
            float h = Pad + TitleHeight + 2 + ButtonHeight + Pad;
            if (_game.Settings.MinimapVisible) h += LegendHeight + 2 + MapHeight() + Pad;
            float w = InnerWidth + Pad * 2;
            Bounds = new Rect(Mathf.Floor(ui.Width - w - 2), Mathf.Floor(bottom - h - 2), w, h);
        }

        private float MapHeight()
        {
            var tex = _minimap.Texture;
            if (tex == null) return 110f;
            return Mathf.Clamp(Mathf.Round(InnerWidth * tex.height / tex.width), 70f, 150f);
        }

        public void Draw(UiContext ui)
        {
            _minimap.Update();
            ui.BeginLayer(LayerId, UiTheme.Grey);
            var R = ui.R;
            var b = Bounds;
            R.Outline(new Rect(b.x - 1, b.y - 1, b.width + 2, b.height + 2), UiTheme.Outline);
            R.Bevel(b, UiTheme.Grey, false);

            float x = b.x + Pad, y = b.y + Pad;
            var view = _game.View;

            // Title: current map mode + fold button
            var fold = new Rect(b.xMax - Pad - 11, y, 11, 11);
            R.Text(x, y, OverlayName(view.Overlay), UiTheme.Grey.Heading);
            if (ui.Button(fold, _game.Settings.MinimapVisible ? "\u25bc" : "\u25b2"))
            {
                _game.Settings.MinimapVisible = !_game.Settings.MinimapVisible;
                _game.Settings.Save();
            }
            ui.SetTooltip(fold, _game.Settings.MinimapVisible ? "Hide the minimap" : "Show the minimap");
            y += TitleHeight + 2;

            // Map-mode buttons (overlays + contour lines)
            var row = new Rect(x, y, InnerWidth, ButtonHeight);
            int count = Modes.Length + 1;
            for (int i = 0; i < Modes.Length; i++)
            {
                var mode = Modes[i];
                if (ui.IconButton(UiContext.Cell(row, i, count), IconFor(mode), view.Overlay == mode, OverlayName(mode) + " (O cycles)"))
                    view.Overlay = mode;
            }
            if (ui.IconButton(UiContext.Cell(row, Modes.Length, count), "contours", view.ShowContours, "Contour lines (C)"))
                view.ShowContours = !view.ShowContours;
            y += ButtonHeight + Pad;

            if (!_game.Settings.MinimapVisible) return;

            DrawLegend(ui, new Rect(x, y, InnerWidth, LegendHeight));
            y += LegendHeight + 2;

            // Minimap
            var mapRect = new Rect(x, y, InnerWidth, MapHeight());
            R.Bevel(new Rect(mapRect.x - 1, mapRect.y - 1, mapRect.width + 2, mapRect.height + 2), UiTheme.Grey, true);
            if (_minimap.Ready) R.Image(_minimap.Texture, mapRect, FitUv(mapRect), new Color32(255, 255, 255, 255));
            else R.Fill(mapRect, new Color32(40, 48, 64, 255));

            // Camera view rectangle
            R.PushClip(mapRect);
            var vr = _game.Camera.ViewWorldRect;
            var uv = FitUv(mapRect);
            var a = _minimap.WorldToUv(new Vector2(vr.xMin, vr.yMax));
            var c = _minimap.WorldToUv(new Vector2(vr.xMax, vr.yMin));
            float rx0 = mapRect.x + (a.x - uv.xMin) / uv.width * mapRect.width;
            float rx1 = mapRect.x + (c.x - uv.xMin) / uv.width * mapRect.width;
            float ry0 = mapRect.y + (uv.yMax - a.y) / uv.height * mapRect.height;
            float ry1 = mapRect.y + (uv.yMax - c.y) / uv.height * mapRect.height;
            var viewBox = Rect.MinMaxRect(Mathf.Round(rx0), Mathf.Round(ry0), Mathf.Round(rx1), Mathf.Round(ry1));
            R.Outline(viewBox, new Color32(255, 255, 255, 255));
            R.Outline(new Rect(viewBox.x + 1, viewBox.y + 1, viewBox.width - 2, viewBox.height - 2), new Color32(0, 0, 0, 120));
            R.PopClip();

            // Click / drag to move the camera
            if (ui.Clicked(mapRect)) _draggingMap = true;
            if (_draggingMap)
            {
                if (ui.LeftHeld)
                {
                    float u = uv.xMin + Mathf.Clamp01((ui.Mouse.x - mapRect.x) / mapRect.width) * uv.width;
                    float v = uv.yMax - Mathf.Clamp01((ui.Mouse.y - mapRect.y) / mapRect.height) * uv.height;
                    _game.Camera.CenterOn(_minimap.UvToWorld(new Vector2(u, v)));
                }
                else _draggingMap = false;
            }
            ui.SetTooltip(mapRect, "Click or drag to move the view");
        }

        /// <summary>The part of the minimap texture shown in <paramref name="r"/>, cropped to keep its aspect ratio.</summary>
        private Rect FitUv(Rect r)
        {
            var tex = _minimap.Texture;
            if (tex == null) return new Rect(0, 0, 1, 1);
            float texAspect = tex.width / (float)tex.height;
            float boxAspect = r.width / r.height;
            if (texAspect > boxAspect)
            {
                float w = boxAspect / texAspect;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            float h = texAspect / boxAspect;
            return new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }

        private void DrawLegend(UiContext ui, Rect r)
        {
            var R = ui.R;
            var text = UiTheme.TextBlack;
            switch (_game.View.Overlay)
            {
                case OverlayMode.Difficulty:
                    Swatches(R, r, new[] { "Grn", "Blu", "Blk", "2Blk", "3Blk" }, new[]
                    {
                        Palette.ForDifficulty(Difficulty.Green), Palette.ForDifficulty(Difficulty.Blue), new Color32(60, 60, 64, 255),
                        Palette.ForDifficulty(Difficulty.DoubleBlack), Palette.ForDifficulty(Difficulty.TripleBlack),
                    });
                    break;
                case OverlayMode.SnowDepth: Gradient(R, r, "Thin", "Deep", true); break;
                case OverlayMode.Wear: Gradient(R, r, "Fresh", "Worn", false); break;
                case OverlayMode.Temperature:
                    var units = _game.Sim.Units;
                    Gradient(R, r, units.Temperature(-20f), units.Temperature(5f), false);
                    break;
                case OverlayMode.SnowQuality:
                    Swatches(R, r, new[] { "Pow", "Grm", "Pck", "Crd", "Slh", "Ice", "Bare" }, new[]
                    {
                        new Color32(255, 255, 255, 255), new Color32(120, 220, 240, 255), new Color32(170, 180, 190, 255),
                        new Color32(200, 170, 120, 255), new Color32(210, 200, 60, 255), new Color32(40, 90, 220, 255),
                        new Color32(200, 60, 50, 255),
                    });
                    break;
                case OverlayMode.Ownership:
                    Swatches(R, r, new[] { "Yours", "Buy", "Far", "Rep" }, new[]
                    {
                        new Color32(110, 190, 120, 255), new Color32(230, 200, 80, 255), new Color32(150, 150, 160, 255), new Color32(110, 90, 120, 255),
                    });
                    break;
                case OverlayMode.Lighting:
                    Swatches(R, r, new[] { "Lit", "Dark", "Glow", "Night" }, new[]
                    {
                        new Color32(255, 220, 70, 255), new Color32(200, 60, 50, 255), new Color32(220, 200, 140, 255), new Color32(36, 46, 82, 255),
                    });
                    break;
                case OverlayMode.Scenic:
                    R.Text(r.x, r.y, "The mountain without trail markings.", text);
                    break;
                default:
                    R.Text(r.x, r.y, "Trails are tinted by their sign colour.", text);
                    break;
            }
        }

        private static void Swatches(UiRenderer R, Rect r, string[] labels, Color32[] colors)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                var cell = UiContext.Cell(r, i, labels.Length, 2);
                var sw = new Rect(cell.x, cell.y + 2, 7, 7);
                R.Fill(sw, colors[i]);
                R.Outline(sw, UiTheme.Black);
                R.Text(sw.xMax + 2, cell.y, labels[i], UiTheme.TextBlack);
            }
        }

        private static void Gradient(UiRenderer R, Rect r, string left, string right, bool reversed)
        {
            float lw = R.Measure(left), rw = R.Measure(right);
            var bar = new Rect(r.x + lw + 4, r.y + 2, r.width - lw - rw - 8, 7);
            R.Text(r.x, r.y, left, UiTheme.TextBlack);
            R.Text(r.xMax - rw, r.y, right, UiTheme.TextBlack);
            int steps = Mathf.Max(1, (int)bar.width);
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1 == 0 ? 1 : steps - 1);
                R.Fill(new Rect(bar.x + i, bar.y, 1, bar.height), Palette.Heat(reversed ? 1f - t : t));
            }
            R.Outline(bar, UiTheme.Black);
        }

        public static string OverlayName(OverlayMode m)
        {
            switch (m)
            {
                case OverlayMode.Difficulty: return "Slope difficulty";
                case OverlayMode.SnowDepth: return "Snow depth";
                case OverlayMode.SnowQuality: return "Snow quality";
                case OverlayMode.Wear: return "Wear from skier traffic";
                case OverlayMode.Temperature: return "Temperature";
                case OverlayMode.Ownership: return "Land ownership";
                case OverlayMode.Lighting: return "Trail lights (night)";
                case OverlayMode.Scenic: return "Natural view, no trail markings";
                default: return "Natural view";
            }
        }

        private static string IconFor(OverlayMode m)
        {
            switch (m)
            {
                case OverlayMode.Difficulty: return "difficulty";
                case OverlayMode.SnowDepth: return "depth";
                case OverlayMode.SnowQuality: return "quality";
                case OverlayMode.Wear: return "wear";
                case OverlayMode.Temperature: return "temperature";
                case OverlayMode.Ownership: return "ownership";
                case OverlayMode.Lighting: return "lighting";
                case OverlayMode.Scenic: return "scenic";
                default: return "none";
            }
        }
    }
}
