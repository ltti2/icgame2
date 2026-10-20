using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Pixel art for lift structures, in the same style as the trees, skiers and chairs: towers, drive posts,
    /// station huts, the elevated terminals of detachable lifts and gondolas, bullwheels and sheaves. Buildings
    /// are drawn in true 2:1 isometric (a map tile is 64x32 pixels; 4 pixels per metre of height).
    ///
    /// Towers and posts are authored at the lift type's default cable height and stretched vertically by the
    /// renderer to meet the actual cable, so a pole stays a clean pixel column whatever the terrain does.
    /// </summary>
    public static class LiftArt
    {
        public const float PixelsPerStep = 4f;

        /// <summary>
        /// Sideways distance (tiles) of the up and down lines from the lift's centre line. Wide enough that
        /// chairs going up never brush the ones coming down; also the bullwheel's radius.
        /// </summary>
        public static float LineOffset(LiftType type)
        {
            switch (type)
            {
                case LiftType.RopeTow: return 0.3f;
                case LiftType.MagicCarpet: return 0.22f;
                case LiftType.TBar: return 0.35f;
                case LiftType.FixedGripChair: return 0.45f;
                case LiftType.DetachableChair: return 0.5f;
                default: return 0.55f; // six-pack bubbles and gondola cabins are the widest
            }
        }

        /// <summary>Default cable height in pixels for a lift type (what towers are authored at).</summary>
        public static int DefaultCablePixels(LiftType type)
        {
            foreach (var def in SimConfig.DefaultLifts())
                if (def.Type == type) return Mathf.Max(4, Mathf.RoundToInt(def.CableHeightMeters * PixelsPerStep));
            return 36;
        }

        /// <summary>Lifts whose stations have an enclosed terminal at cable height.</summary>
        public static bool HasTerminal(LiftType t) => t == LiftType.DetachableChair || t == LiftType.DetachableBubble || t == LiftType.Gondola;

        // ---------- Palette ----------

        private static readonly Color32 Outline = new Color32(34, 36, 42, 255);
        private static readonly Color32 SteelLit = new Color32(176, 182, 192, 255);
        private static readonly Color32 SteelMid = new Color32(132, 138, 148, 255);
        private static readonly Color32 SteelDark = new Color32(78, 82, 92, 255);
        private static readonly Color32 Concrete = new Color32(170, 168, 160, 255);
        private static readonly Color32 ConcreteDark = new Color32(128, 126, 120, 255);
        private static readonly Color32 WoodLit = new Color32(160, 116, 74, 255);
        private static readonly Color32 WoodMid = new Color32(128, 90, 56, 255);
        private static readonly Color32 WoodDark = new Color32(92, 64, 40, 255);
        private static readonly Color32 Glass = new Color32(150, 196, 226, 255);
        private static readonly Color32 GlassDark = new Color32(98, 138, 172, 255);
        private static readonly Color32 GlassGlint = new Color32(226, 240, 250, 255);

        private static Color32 Shade(Color32 c, float f)
            => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);

        private static Color32 PaintFor(LiftType t)
        {
            switch (t)
            {
                case LiftType.TBar: return new Color32(84, 128, 92, 255);
                case LiftType.FixedGripChair: return new Color32(196, 52, 40, 255);
                case LiftType.DetachableChair: return new Color32(44, 96, 196, 255);
                case LiftType.DetachableBubble: return new Color32(232, 116, 36, 255);
                case LiftType.Gondola: return new Color32(206, 44, 44, 255);
                default: return WoodMid;
            }
        }

        // =====================================================================
        // Towers (also used as station drive posts)
        // =====================================================================

        /// <summary>A line tower standing on its foot, reaching 4 px above the default cable height.</summary>
        public static PixelCanvas Tower(LiftType type)
        {
            int h = DefaultCablePixels(type) + 4;
            switch (type)
            {
                case LiftType.RopeTow: return Post(h, 3, WoodLit, WoodMid, WoodDark, false, null);
                case LiftType.MagicCarpet: return Post(6, 3, SteelLit, SteelMid, SteelDark, false, null);
                case LiftType.TBar: return Post(h, 3, Shade(PaintFor(type), 1.25f), PaintFor(type), Shade(PaintFor(type), 0.7f), true, null);
                case LiftType.Gondola: return Lattice(h);
                case LiftType.FixedGripChair: return Post(h, 4, SteelLit, SteelMid, SteelDark, true, null);
                default: return Post(h, 5, SteelLit, SteelMid, SteelDark, true, PaintFor(type));
            }
        }

        /// <summary>
        /// A round pole: lit left column, mid body, dark right edge, on a concrete footing, with ladder rungs
        /// (steel towers) and an optional painted band near the top.
        /// </summary>
        private static PixelCanvas Post(int h, int w, Color32 lit, Color32 mid, Color32 dark, bool steel, Color32? band)
        {
            int cw = w + 4;
            var c = new PixelCanvas(cw, h);
            int x0 = 2;
            for (int y = 0; y < h; y++)
            {
                c.Set(x0, y, lit);
                for (int x = x0 + 1; x < x0 + w - 1; x++) c.Set(x, y, mid);
                c.Set(x0 + w - 1, y, dark);
            }
            if (!steel)
            {
                // Wood grain: a few darker knots.
                for (int y = 2; y < h; y += 5) c.Set(x0 + 1, y, dark);
            }
            else
            {
                for (int y = 4; y < h - 4; y += 4) c.Set(x0 + w, y, dark); // ladder rungs on the shady side
                c.HLine(x0, x0 + w - 1, 0, Outline);                       // cap
            }
            if (band.HasValue)
                for (int y = 3; y < 6 && y < h; y++)
                    for (int x = x0; x < x0 + w; x++) c.Set(x, y, x == x0 ? Shade(band.Value, 1.2f) : x == x0 + w - 1 ? Shade(band.Value, 0.75f) : band.Value);
            // Footing.
            c.HLine(0, cw - 1, h - 1, ConcreteDark);
            c.HLine(1, cw - 2, h - 2, Concrete);
            c.PivotX = x0 + w / 2;
            c.PivotY = h - 1;
            return c;
        }

        /// <summary>A gondola lattice tower: splayed legs with X bracing, narrowing to the head frame.</summary>
        private static PixelCanvas Lattice(int h)
        {
            int w = 17;
            var c = new PixelCanvas(w, h);
            int cx = w / 2;
            float bottomHalf = 7f, topHalf = 2f;
            int head = 4;
            int prevL = -1, prevR = -1, prevY = -1;
            for (int y = head; y < h; y++)
            {
                float f = (y - head) / (float)(h - 1 - head);
                int half = Mathf.RoundToInt(Mathf.Lerp(topHalf, bottomHalf, f));
                c.Set(cx - half, y, SteelLit);
                c.Set(cx + half, y, SteelDark);
                // Bracing every 8 rows: a crossbar and an X down to the next one.
                if ((y - head) % 8 == 0)
                {
                    c.HLine(cx - half, cx + half, y, SteelMid);
                    if (prevY >= 0)
                    {
                        c.Line(prevL, prevY, cx + half, y, SteelMid);
                        c.Line(prevR, prevY, cx - half, y, SteelMid);
                    }
                    prevL = cx - half; prevR = cx + half; prevY = y;
                }
            }
            // Head frame.
            c.Rect(cx - 3, 0, 7, head, SteelMid);
            c.HLine(cx - 3, cx + 3, 0, Outline);
            c.VLine(cx + 3, 1, head - 1, SteelDark);
            // Footings.
            c.HLine(0, 3, h - 1, ConcreteDark);
            c.HLine(w - 4, w - 1, h - 1, ConcreteDark);
            c.PivotX = cx;
            c.PivotY = h - 1;
            return c;
        }

        /// <summary>Sheave train at the end of a tower crossarm: the little wheels the rope runs over.</summary>
        public static PixelCanvas Sheaves()
        {
            var c = new PixelCanvas(9, 4);
            c.HLine(0, 8, 0, SteelMid);
            for (int i = 0; i < 4; i++)
            {
                int x = 1 + i * 2;
                c.Set(x, 1, Outline); c.Set(x + 1, 1, SteelDark);
                c.Set(x, 2, SteelDark); c.Set(x + 1, 2, Outline);
            }
            c.HLine(1, 7, 3, Outline);
            c.PivotX = 4;
            c.PivotY = 0;
            return c;
        }

        // =====================================================================
        // Bullwheels
        // =====================================================================

        /// <summary>The horizontal wheel the haul rope turns around, seen in isometric (a 2:1 ellipse).</summary>
        public static PixelCanvas Bullwheel(LiftType type)
        {
            float r = LineOffset(type);
            float a = r * 1.41421f * 32f, b = a * 0.5f; // grid circle -> screen ellipse half-axes
            int w = Mathf.CeilToInt(a) * 2 + 3, h = Mathf.CeilToInt(b) * 2 + 4;
            var c = new PixelCanvas(w, h);
            float cx = (w - 1) * 0.5f, cy = (h - 2) * 0.5f;
            var rim = type == LiftType.RopeTow ? SteelMid : SteelDark;
            // Rim (2 px thick, with a lit upper edge) and its shadowed side band.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - cx) / a, dy = (y - cy) / b;
                    float d = dx * dx + dy * dy;
                    if (d <= 1f && d >= 0.72f) c.Set(x, y, dy < 0 ? SteelLit : rim);
                    float d2 = dx * dx + ((y - 1 - cy) / b) * ((y - 1 - cy) / b);
                    if (!c.IsSet(x, y) && d2 <= 1f && d2 >= 0.72f && dy > 0) c.Set(x, y, Outline); // rim thickness below
                }
            // Spokes and hub.
            int icx = Mathf.RoundToInt(cx), icy = Mathf.RoundToInt(cy);
            c.Line(icx - Mathf.RoundToInt(a * 0.8f), icy, icx + Mathf.RoundToInt(a * 0.8f), icy, SteelMid);
            c.Line(icx, icy - Mathf.RoundToInt(b * 0.75f), icx, icy + Mathf.RoundToInt(b * 0.75f), SteelMid);
            c.Rect(icx - 1, icy - 1, 3, 2, Outline);
            c.PivotX = icx;
            c.PivotY = icy;
            return c;
        }

        // =====================================================================
        // Buildings
        // =====================================================================

        /// <summary>
        /// Draws isometric shapes onto a canvas. Grid coordinates are fractions of the station's tile (0..1 on
        /// each axis), heights are pixels above the tile centre's ground. The tile's top corner sits at (ox, oy).
        /// </summary>
        private sealed class Iso
        {
            public readonly PixelCanvas C;
            private readonly float _ox, _oy;
            public Iso(PixelCanvas c, float ox, float oy) { C = c; _ox = ox; _oy = oy; }

            public float X(float gx, float gy) => _ox + (gx - gy) * 32f;
            public float Y(float gx, float gy, float h) => _oy + (gx + gy) * 16f - h;

            public void Quad(float[] g, System.Func<int, int, Color32> shade)
            {
                // g: (gx, gy, h) x 4
                var xs = new float[4]; var ys = new float[4];
                for (int i = 0; i < 4; i++) { xs[i] = X(g[i * 3], g[i * 3 + 1]); ys[i] = Y(g[i * 3], g[i * 3 + 1], g[i * 3 + 2]); }
                C.FillPolygon(xs, ys, shade);
            }

            public void Tri(float[] g, Color32 col)
            {
                var xs = new float[3]; var ys = new float[3];
                for (int i = 0; i < 3; i++) { xs[i] = X(g[i * 3], g[i * 3 + 1]); ys[i] = Y(g[i * 3], g[i * 3 + 1], g[i * 3 + 2]); }
                C.FillPolygon(xs, ys, col);
            }

            /// <summary>The +x wall (screen lower right) of a box.</summary>
            public void RightWall(float x1, float y0, float y1, float h0, float h1, System.Func<int, int, Color32> shade)
                => Quad(new[] { x1, y0, h0, x1, y1, h0, x1, y1, h1, x1, y0, h1 }, shade);

            /// <summary>The +y wall (screen lower left) of a box.</summary>
            public void LeftWall(float y1, float x0, float x1, float h0, float h1, System.Func<int, int, Color32> shade)
                => Quad(new[] { x0, y1, h0, x1, y1, h0, x1, y1, h1, x0, y1, h1 }, shade);

            public void Top(float x0, float y0, float x1, float y1, float h, Color32 col)
                => Quad(new[] { x0, y0, h, x1, y0, h, x1, y1, h, x0, y1, h }, (x, y) => col);

            /// <summary>Edge line between two grid points, for outlines.</summary>
            public void Edge(float gx0, float gy0, float h0, float gx1, float gy1, float h1, Color32 col)
                => C.Line(Mathf.RoundToInt(X(gx0, gy0) - 0.5f), Mathf.RoundToInt(Y(gx0, gy0, h0) - 0.5f),
                          Mathf.RoundToInt(X(gx1, gy1) - 0.5f), Mathf.RoundToInt(Y(gx1, gy1, h1) - 0.5f), col);

            /// <summary>A plain box: right wall, left wall, flat top, with dark edges.</summary>
            public void Box(float x0, float y0, float x1, float y1, float h0, float h1,
                            System.Func<int, int, Color32> right, System.Func<int, int, Color32> left, Color32 top)
            {
                RightWall(x1, y0, y1, h0, h1, right);
                LeftWall(y1, x0, x1, h0, h1, left);
                Top(x0, y0, x1, y1, h1, top);
                Edge(x1, y1, h0, x1, y1, h1, Outline);
                Edge(x0, y1, h1, x1, y1, h1, Shade(top, 0.6f));
                Edge(x1, y0, h1, x1, y1, h1, Shade(top, 0.6f));
            }

            /// <summary>A gable roof with its ridge along x, overhanging the walls a little.</summary>
            public void GableX(float x0, float y0, float x1, float y1, float h, float ridge, Color32 lit, Color32 shade, Color32 gableWall)
            {
                const float o = 0.04f;
                float ym = (y0 + y1) * 0.5f;
                Tri(new[] { x1, y0, h, x1, y1, h, x1, ym, ridge }, gableWall);
                Quad(new[] { x0 - o, y0 - o, h - 1, x1 + o, y0 - o, h - 1, x1 + o, ym, ridge, x0 - o, ym, ridge }, (x, y) => lit);
                Quad(new[] { x0 - o, ym, ridge, x1 + o, ym, ridge, x1 + o, y1 + o, h - 1, x0 - o, y1 + o, h - 1 },
                     (x, y) => (y % 3 == 0) ? Shade(shade, 0.85f) : shade); // shingle rows
                Edge(x0 - o, ym, ridge, x1 + o, ym, ridge, Shade(lit, 1.15f));
                Edge(x0 - o, y1 + o, h - 1, x1 + o, y1 + o, h - 1, Outline);
            }
        }

        private const int BuildingTopRoom = 46;

        /// <summary>Canvas for a building on one tile, pivot at the tile centre on the ground.</summary>
        private static Iso BuildingCanvas(int topRoom, out PixelCanvas c)
        {
            c = new PixelCanvas(68, 34 + topRoom);
            var iso = new Iso(c, 34f, topRoom);
            c.PivotX = 34;
            c.PivotY = topRoom + 16;
            return iso;
        }

        private static System.Func<int, int, Color32> Planks(Color32 baseCol)
            => (x, y) => y % 3 == 0 ? Shade(baseCol, 0.82f) : baseCol;

        private static System.Func<int, int, Color32> Flat(Color32 col) => (x, y) => col;

        /// <summary>
        /// The ground-level building at a station: a lift shack, ticket booth or station hall depending on the
        /// type. Null where the station is just a post (e.g. the top of a rope tow).
        /// </summary>
        public static PixelCanvas Hut(LiftType type, bool bottom)
        {
            PixelCanvas c;
            Iso iso;
            switch (type)
            {
                case LiftType.RopeTow:
                {
                    if (!bottom) return null;
                    iso = BuildingCanvas(BuildingTopRoom, out c);
                    float x0 = 0.12f, x1 = 0.5f, y0 = 0.52f, y1 = 0.9f, h = 12f;
                    iso.RightWall(x1, y0, y1, 0, h, Planks(WoodLit));
                    iso.LeftWall(y1, x0, x1, 0, h, Planks(WoodMid));
                    Door(iso, y1, x0 + 0.12f, x0 + 0.24f, 9f, WoodDark);
                    iso.GableX(x0, y0, x1, y1, h, h + 8f, new Color32(96, 92, 90, 255), new Color32(70, 66, 66, 255), WoodLit);
                    return c;
                }
                case LiftType.MagicCarpet:
                {
                    iso = BuildingCanvas(16, out c);
                    float x0 = 0.28f, x1 = 0.72f, y0 = 0.28f, y1 = 0.72f, h = 8f;
                    iso.Box(x0, y0, x1, y1, 0, h, Flat(SteelLit), Flat(SteelMid), new Color32(92, 96, 104, 255));
                    // Yellow and black safety stripe along the top of the housing.
                    var yellow = new Color32(236, 196, 40, 255);
                    iso.RightWall(x1, y0, y1, h - 3f, h - 1f, (x, y) => ((x + y) / 2) % 2 == 0 ? yellow : Outline);
                    iso.LeftWall(y1, x0, x1, h - 3f, h - 1f, (x, y) => ((x - y) / 2 & 1) == 0 ? Shade(yellow, 0.85f) : Outline);
                    return c;
                }
                case LiftType.TBar:
                {
                    if (!bottom) return null;
                    iso = BuildingCanvas(BuildingTopRoom, out c);
                    var wall = new Color32(214, 204, 176, 255);
                    var roof = PaintFor(type);
                    float x0 = 0.1f, x1 = 0.48f, y0 = 0.52f, y1 = 0.9f, h = 15f;
                    iso.RightWall(x1, y0, y1, 0, h, Flat(wall));
                    iso.LeftWall(y1, x0, x1, 0, h, Flat(Shade(wall, 0.82f)));
                    WindowRight(iso, x1, y0 + 0.1f, y1 - 0.1f, 6f, 12f);
                    Door(iso, y1, x0 + 0.1f, x0 + 0.22f, 11f, new Color32(110, 80, 60, 255));
                    iso.GableX(x0, y0, x1, y1, h, h + 9f, Shade(roof, 1.15f), roof, wall);
                    return c;
                }
                case LiftType.FixedGripChair:
                {
                    // Log lift shack with a red roof; the top one has no door on our side.
                    iso = BuildingCanvas(BuildingTopRoom, out c);
                    var roof = PaintFor(type);
                    float x0 = 0.06f, x1 = 0.42f, y0 = 0.56f, y1 = 0.94f, h = 16f;
                    iso.RightWall(x1, y0, y1, 0, h, Planks(WoodLit));
                    iso.LeftWall(y1, x0, x1, 0, h, Planks(WoodMid));
                    WindowRight(iso, x1, y0 + 0.08f, y1 - 0.08f, 7f, 13f);
                    if (bottom) Door(iso, y1, x0 + 0.08f, x0 + 0.2f, 12f, WoodDark);
                    else WindowLeft(iso, y1, x0 + 0.08f, x1 - 0.08f, 7f, 13f);
                    iso.GableX(x0, y0, x1, y1, h, h + 9f, Shade(roof, 1.15f), roof, WoodLit);
                    return c;
                }
                case LiftType.DetachableChair:
                case LiftType.DetachableBubble:
                {
                    // Glass operator booth with a flat roof in the lift's colour.
                    iso = BuildingCanvas(30, out c);
                    var wall = new Color32(232, 234, 238, 255);
                    float x0 = 0.6f, x1 = 0.95f, y0 = 0.6f, y1 = 0.95f, h = 14f;
                    iso.Box(x0, y0, x1, y1, 0, h, Flat(wall), Flat(Shade(wall, 0.84f)), PaintFor(type));
                    WindowRight(iso, x1, y0 + 0.05f, y1 - 0.05f, 5f, 12f);
                    WindowLeft(iso, y1, x0 + 0.05f, x1 - 0.05f, 5f, 12f);
                    return c;
                }
                case LiftType.Gondola:
                {
                    // Station hall: glass curtain walls with mullions, a dark roof slab.
                    iso = BuildingCanvas(40, out c);
                    float x0 = 0.08f, x1 = 0.92f, y0 = 0.08f, y1 = 0.92f, h = 26f;
                    iso.RightWall(x1, y0, y1, 0, h, CurtainWall(Glass, 0));
                    iso.LeftWall(y1, x0, x1, 0, h, CurtainWall(GlassDark, 1));
                    iso.Top(x0 - 0.03f, y0 - 0.03f, x1 + 0.03f, y1 + 0.03f, h, new Color32(64, 68, 78, 255));
                    iso.Edge(x0 - 0.03f, y1 + 0.03f, h, x1 + 0.03f, y1 + 0.03f, h, Outline);
                    iso.Edge(x1 + 0.03f, y0 - 0.03f, h, x1 + 0.03f, y1 + 0.03f, h, Outline);
                    // Plinth.
                    iso.RightWall(x1, y0, y1, 0, 2, Flat(Concrete));
                    iso.LeftWall(y1, x0, x1, 0, 2, Flat(ConcreteDark));
                    Door(iso, y1, 0.42f, 0.58f, 14f, new Color32(60, 64, 72, 255));
                    return c;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// The enclosed terminal of a detachable chair or gondola, around the bullwheel at cable height, where
        /// carriers slow down to load. Pivot at the tile centre at cable height.
        /// </summary>
        public static PixelCanvas Terminal(LiftType type)
        {
            int topRoom = 26;
            var c = new PixelCanvas(68, 34 + topRoom + 12);
            var iso = new Iso(c, 34f, topRoom);
            c.PivotX = 34;
            c.PivotY = topRoom + 16;
            bool gondola = type == LiftType.Gondola;
            var wall = gondola ? new Color32(78, 82, 92, 255) : new Color32(236, 238, 242, 255);
            var roof = gondola ? new Color32(54, 58, 66, 255) : PaintFor(type);
            float x0 = 0.04f, x1 = 0.96f, y0 = 0.04f, y1 = 0.96f;
            float bottomH = -9f, topH = gondola ? 14f : 11f;
            // Walls, with the dark slot the carriers run through and a livery stripe.
            System.Func<int, int, Color32> Wall(Color32 w) => (x, y) => w;
            iso.RightWall(x1, y0, y1, bottomH, topH, Wall(wall));
            iso.LeftWall(y1, x0, x1, bottomH, topH, Wall(Shade(wall, 0.84f)));
            iso.RightWall(x1, y0, y1, -6f, 1f, Wall(new Color32(30, 32, 38, 255)));
            iso.LeftWall(y1, x0, x1, -6f, 1f, Wall(new Color32(22, 24, 28, 255)));
            var stripe = gondola ? PaintFor(type) : Shade(roof, 1f);
            iso.RightWall(x1, y0, y1, 4f, 6f, Wall(stripe));
            iso.LeftWall(y1, x0, x1, 4f, 6f, Wall(Shade(stripe, 0.85f)));
            // Roof: a lower lip and a raised, lighter crown so it reads as curved.
            iso.Box(x0 - 0.03f, y0 - 0.03f, x1 + 0.03f, y1 + 0.03f, topH, topH + 3f, Wall(Shade(roof, 0.85f)), Wall(Shade(roof, 0.7f)), roof);
            iso.Top(x0 + 0.18f, y0 + 0.18f, x1 - 0.18f, y1 - 0.18f, topH + 4f, Shade(roof, 1.18f));
            iso.Edge(x1, y1, bottomH, x1, y1, topH, Outline);
            return c;
        }

        /// <summary>Thin steel leg holding up a terminal (stretched to reach the ground).</summary>
        public static PixelCanvas Column()
        {
            var c = new PixelCanvas(3, 16);
            for (int y = 0; y < 16; y++) { c.Set(0, y, SteelLit); c.Set(1, y, SteelMid); c.Set(2, y, SteelDark); }
            c.PivotX = 1;
            c.PivotY = 15;
            return c;
        }

        // ---------- Building details ----------

        private static System.Func<int, int, Color32> CurtainWall(Color32 glass, int phase)
            => (x, y) => ((x + phase) % 6 == 0 || y % 9 == 0) ? new Color32(70, 80, 92, 255) : (((x + y) % 11 == 0) ? GlassGlint : glass);

        private static void WindowRight(Iso iso, float x1, float ya, float yb, float h0, float h1)
        {
            iso.RightWall(x1, ya, yb, h0, h1, (x, y) => Outline);
            iso.RightWall(x1, ya + 0.02f, yb - 0.02f, h0 + 1, h1 - 1, (x, y) => (x + y) % 5 == 0 ? GlassGlint : Glass);
        }

        private static void WindowLeft(Iso iso, float y1, float xa, float xb, float h0, float h1)
        {
            iso.LeftWall(y1, xa, xb, h0, h1, (x, y) => Outline);
            iso.LeftWall(y1, xa + 0.02f, xb - 0.02f, h0 + 1, h1 - 1, (x, y) => (x + y) % 5 == 0 ? Glass : GlassDark);
        }

        private static void Door(Iso iso, float y1, float xa, float xb, float h, Color32 col)
        {
            iso.LeftWall(y1, xa, xb, 0, h, (x, y) => col);
            iso.Edge(xa, y1, h, xb, y1, h, Outline);
        }
    }
}
