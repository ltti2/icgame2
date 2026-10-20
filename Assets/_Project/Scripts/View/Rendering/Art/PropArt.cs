using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Pixel art for small props around the resort: cars in the lot and trail light poles. Drawn in code like
    /// the rest of the world art (<see cref="SpriteArt"/>, <see cref="LiftArt"/>). Sizes follow the world scale:
    /// a tile is 64x32 px and a metre of height is 4 px.
    /// </summary>
    public static class PropArt
    {
        public const int CarColorCount = 6;

        private static readonly Color32[] Paint =
        {
            new Color32(178, 34, 34, 255),   // red
            new Color32(40, 78, 170, 255),   // blue
            new Color32(226, 228, 230, 255), // white
            new Color32(40, 42, 46, 255),    // black
            new Color32(54, 120, 70, 255),   // green
            new Color32(214, 170, 40, 255),  // yellow
        };

        private static readonly Color32 Glass = new Color32(52, 70, 92, 255);
        private static readonly Color32 GlassLit = new Color32(120, 150, 180, 255);
        private static readonly Color32 Tire = new Color32(22, 22, 24, 255);
        private static readonly Color32 Headlight = new Color32(255, 246, 200, 255);
        private static readonly Color32 Taillight = new Color32(200, 30, 30, 255);
        private static readonly Color32 SnowRoof = new Color32(240, 244, 250, 255);

        private static Color32 Shade(Color32 c, float f)
            => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);

        /// <summary>Canvas pixel of a point in car space: gx along grid x, gy along grid y (tiles), h in metres.</summary>
        private static void Project(float gx, float gy, float h, float cx, float cy, out float px, out float py)
        {
            px = cx + (gx - gy) * 32f;
            py = cy + (gx + gy) * 16f - h * 4f;
        }

        /// <summary>
        /// An axis-aligned box seen from the game's camera: the top, the +x face (lower right) and the +y face
        /// (lower left) are visible.
        /// </summary>
        private static void Box(PixelCanvas c, float cx, float cy, float x0, float x1, float y0, float y1, float h0, float h1,
                                Color32 top, Color32 faceX, Color32 faceY)
        {
            void Quad(float ax, float ay, float ah, float bx, float by, float bh, float qx, float qy, float qh, float dx, float dy, float dh, Color32 col)
            {
                Project(ax, ay, ah, cx, cy, out float p0x, out float p0y);
                Project(bx, by, bh, cx, cy, out float p1x, out float p1y);
                Project(qx, qy, qh, cx, cy, out float p2x, out float p2y);
                Project(dx, dy, dh, cx, cy, out float p3x, out float p3y);
                c.FillPolygon(new[] { p0x, p1x, p2x, p3x }, new[] { p0y, p1y, p2y, p3y }, col);
            }
            Quad(x1, y0, h0, x1, y1, h0, x1, y1, h1, x1, y0, h1, faceX); // +x face
            Quad(x0, y1, h0, x1, y1, h0, x1, y1, h1, x0, y1, h1, faceY); // +y face
            Quad(x0, y0, h1, x1, y0, h1, x1, y1, h1, x0, y1, h1, top);   // roof / bonnet
        }

        /// <summary>
        /// A car pointing along grid x. <paramref name="noseTowardCamera"/>: the bonnet is at +x (lower right on
        /// screen, headlights showing); otherwise the boot is (tail lights showing). Mirror the sprite for cars
        /// pointing along grid y.
        /// </summary>
        public static PixelCanvas Car(int color, bool noseTowardCamera)
        {
            var c = new PixelCanvas(26, 20);
            float cx = 13f, cy = 13f;
            var paint = Paint[color % Paint.Length];
            var top = Shade(paint, 1.1f);
            var sideX = Shade(paint, 0.82f);
            var sideY = Shade(paint, 0.64f);

            const float halfL = 0.21f, halfW = 0.09f;
            // Wheels peeking out under the body.
            Box(c, cx, cy, -halfL + 0.05f, -halfL + 0.11f, -halfW - 0.005f, halfW + 0.005f, 0f, 0.55f, Tire, Tire, Tire);
            Box(c, cx, cy, halfL - 0.11f, halfL - 0.05f, -halfW - 0.005f, halfW + 0.005f, 0f, 0.55f, Tire, Tire, Tire);
            // Lower body.
            Box(c, cx, cy, -halfL, halfL, -halfW, halfW, 0.25f, 0.9f, top, sideX, sideY);
            // Cabin, set back from the bonnet.
            float cab0 = noseTowardCamera ? -halfL + 0.03f : -halfL + 0.12f;
            float cab1 = noseTowardCamera ? halfL - 0.12f : halfL - 0.03f;
            Box(c, cx, cy, cab0, cab1, -halfW + 0.015f, halfW - 0.015f, 0.9f, 1.45f, top, noseTowardCamera ? GlassLit : Glass, Glass);

            // Lights on the visible +x end.
            Project(halfL, -halfW + 0.03f, 0.6f, cx, cy, out float lx0, out float ly0);
            Project(halfL, halfW - 0.03f, 0.6f, cx, cy, out float lx1, out float ly1);
            var lamp = noseTowardCamera ? Headlight : Taillight;
            c.Set(Mathf.RoundToInt(lx0), Mathf.RoundToInt(ly0), lamp);
            c.Set(Mathf.RoundToInt(lx1), Mathf.RoundToInt(ly1), lamp);

            // Pivot: the car's centre on the ground.
            c.PivotX = (int)cx;
            c.PivotY = (int)cy;
            return c;
        }

        // =====================================================================
        // Trail lights
        // =====================================================================

        private static readonly Color32 PoleDark = new Color32(58, 60, 66, 255);
        private static readonly Color32 PoleLight = new Color32(96, 100, 108, 255);
        private static readonly Color32 Housing = new Color32(44, 46, 52, 255);
        private static readonly Color32 Lens = new Color32(255, 240, 190, 255);
        private static readonly Color32 LensEdge = new Color32(230, 200, 140, 255);

        private static int PoleHeightPx(LightDefinition def) => Mathf.RoundToInt(def.PoleHeightMeters * 4f);

        private static int LampCount(LightSize size) => size == LightSize.Small ? 1 : size == LightSize.Medium ? 2 : 4;

        private static int CanvasWidth(LightSize size) => size == LightSize.Large ? 19 : 13;

        /// <summary>The pole and crossbar (no lamp lenses, see <see cref="LampHeads"/>). Pivot at the foot.</summary>
        public static PixelCanvas Pole(LightDefinition def)
        {
            int h = PoleHeightPx(def) + 6, w = CanvasWidth(def.Size);
            var c = new PixelCanvas(w, h);
            int mid = w / 2, foot = h - 1, topY = 4;
            bool thick = def.Size != LightSize.Small;

            // Concrete footing and the pole.
            c.Rect(mid - 1, foot - 1, 3, 2, new Color32(150, 150, 146, 255));
            for (int y = topY; y < foot - 1; y++)
            {
                c.Set(mid, y, PoleLight);
                if (thick) c.Set(mid + 1, y, PoleDark);
            }

            // Crossbar with the lamp housings hanging off it.
            int lamps = LampCount(def.Size);
            if (lamps == 1)
            {
                c.HLine(mid, mid + 4, topY, PoleDark);
                c.Rect(mid + 3, topY + 1, 3, 2, Housing);
            }
            else
            {
                int span = lamps == 2 ? 4 : 8;
                c.HLine(mid - span, mid + span + (thick ? 1 : 0), topY, PoleDark);
                for (int k = 0; k < lamps; k++)
                {
                    int lx = mid - span + k * (2 * span / (lamps - 1));
                    c.Rect(lx - 1, topY + 1, 3, 2, Housing);
                }
            }
            c.PivotX = mid;
            c.PivotY = foot;
            return c;
        }

        /// <summary>
        /// Just the glowing lenses, on a canvas the same size as the pole so both share a pivot. Drawn as a
        /// separate sprite so the renderer can make them glow at night.
        /// </summary>
        public static PixelCanvas LampHeads(LightDefinition def)
        {
            int h = PoleHeightPx(def) + 6, w = CanvasWidth(def.Size);
            var c = new PixelCanvas(w, h);
            int mid = w / 2, topY = 4;
            int lamps = LampCount(def.Size);
            if (lamps == 1)
            {
                c.HLine(mid + 3, mid + 5, topY + 3, Lens);
            }
            else
            {
                int span = lamps == 2 ? 4 : 8;
                for (int k = 0; k < lamps; k++)
                {
                    int lx = mid - span + k * (2 * span / (lamps - 1));
                    c.Set(lx - 1, topY + 3, LensEdge);
                    c.Set(lx, topY + 3, Lens);
                    c.Set(lx + 1, topY + 3, LensEdge);
                }
            }
            c.PivotX = mid;
            c.PivotY = h - 1;
            return c;
        }
    }
}
