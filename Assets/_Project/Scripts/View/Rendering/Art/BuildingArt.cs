using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Pixel art for buildings on walking areas, drawn in code like the lift huts (<see cref="LiftArt"/>, whose
    /// isometric helpers it shares). Each is the default for its sprite in the "buildings" sheet; a PNG in
    /// StreamingAssets/Art replaces it (see <see cref="ArtSheets"/>). Pivot: the footprint's centre on the ground.
    /// </summary>
    public static class BuildingArt
    {
        private static readonly Color32 RoofLit = new Color32(168, 58, 44, 255);
        private static readonly Color32 RoofShade = new Color32(124, 40, 34, 255);
        private static readonly Color32 Interior = new Color32(58, 40, 30, 255);
        private static readonly Color32 Glow = new Color32(255, 196, 110, 255);
        private static readonly Color32 Counter = new Color32(196, 156, 104, 255);
        private static readonly Color32 AwningRed = new Color32(196, 44, 40, 255);
        private static readonly Color32 AwningCream = new Color32(246, 232, 200, 255);
        private static readonly Color32 Waffle = new Color32(226, 170, 72, 255);
        private static readonly Color32 WaffleDark = new Color32(168, 112, 40, 255);

        public static PixelCanvas For(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.WaffleCabin: return WaffleCabin();
                default: return null;
            }
        }

        /// <summary>
        /// A little log cabin with a red gable roof. The serving window, under a striped awning, faces +x (screen
        /// lower right), where the line forms; a golden waffle hangs on the gable above it; the door is round the side.
        /// </summary>
        public static PixelCanvas WaffleCabin()
        {
            var iso = LiftArt.BuildingCanvas(36, out var c);
            float x0 = 0.2f, x1 = 0.74f, y0 = 0.24f, y1 = 0.8f, h = 15f;

            // Walls: logs, lit on the window side.
            iso.RightWall(x1, y0, y1, 0, h, LiftArt.Planks(LiftArt.WoodLit));
            iso.LeftWall(y1, x0, x1, 0, h, LiftArt.Planks(LiftArt.WoodMid));

            // Serving window: warm light inside, a counter along the bottom.
            float ya = y0 + 0.11f, yb = y1 - 0.11f;
            iso.RightWall(x1, ya, yb, 5f, 12f, (x, y) => LiftArt.Outline);
            iso.RightWall(x1, ya + 0.02f, yb - 0.02f, 6f, 11f, (x, y) => y % 4 == 0 ? Glow : Interior);
            iso.RightWall(x1, ya - 0.02f, yb + 0.02f, 5f, 6f, (x, y) => Counter);

            // Striped awning over the window, sloping out toward the line.
            iso.Quad(new[] { x1, ya - 0.03f, 14.5f, x1, yb + 0.03f, 14.5f, x1 + 0.05f, yb + 0.03f, 12.5f, x1 + 0.05f, ya - 0.03f, 12.5f },
                     (x, y) => (x / 2) % 2 == 0 ? AwningRed : AwningCream);

            // Door round the side.
            LiftArt.Door(iso, y1, x0 + 0.1f, x0 + 0.24f, 11f, LiftArt.WoodDark);
            LiftArt.WindowLeft(iso, y1, x0 + 0.32f, x1 - 0.08f, 7f, 12f);

            // Gable roof, ridge along x, so the gable end sits over the serving window.
            iso.GableX(x0, y0, x1, y1, h, h + 11f, RoofLit, RoofShade, LiftArt.WoodLit);

            // A golden waffle sign on the gable.
            float ym = (y0 + y1) * 0.5f;
            int cx = Mathf.RoundToInt(iso.X(x1, ym) - 0.5f), cy = Mathf.RoundToInt(iso.Y(x1, ym, h + 4f) - 0.5f);
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    bool edge = Mathf.Abs(dx) == 3 || Mathf.Abs(dy) == 3;
                    bool grid = (dx + 3) % 2 == 0 || (dy + 3) % 2 == 0;
                    c.Set(cx + dx, cy + dy, edge ? WaffleDark : grid ? Waffle : WaffleDark);
                }

            // Stovepipe with a wisp of steam.
            int px = Mathf.RoundToInt(iso.X(x0 + 0.14f, y0 + 0.16f)), py = Mathf.RoundToInt(iso.Y(x0 + 0.14f, y0 + 0.16f, h + 6f));
            c.Rect(px, py - 6, 2, 7, new Color32(60, 60, 66, 255));
            c.Set(px, py - 8, new Color32(230, 234, 240, 255));
            c.Set(px + 1, py - 10, new Color32(220, 226, 234, 255));
            c.Set(px, py - 12, new Color32(210, 218, 228, 255));
            return c;
        }
    }
}
