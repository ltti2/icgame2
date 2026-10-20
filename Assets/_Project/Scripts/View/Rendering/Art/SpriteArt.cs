using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Pixel-art sprites for the world, authored in code: trees (snow-laden or bare), skiers in a handful of
    /// poses, and the carriers for each lift type. Sizes are in texels; at zoom 1 one texel is one screen pixel.
    /// </summary>
    public static class SpriteArt
    {
        // =====================================================================
        // Trees
        // =====================================================================

        private static readonly Color32 NeedleDark = new Color32(22, 48, 34, 255);
        private static readonly Color32 NeedleMid = new Color32(32, 68, 44, 255);
        private static readonly Color32 NeedleLight = new Color32(50, 92, 58, 255);
        private static readonly Color32 NeedleTip = new Color32(74, 116, 70, 255);
        private static readonly Color32 SnowLit = new Color32(242, 246, 252, 255);
        private static readonly Color32 SnowShade = new Color32(200, 214, 232, 255);
        private static readonly Color32 Bark = new Color32(84, 62, 44, 255);
        private static readonly Color32 BarkDark = new Color32(58, 42, 30, 255);
        private static readonly Color32 Twig = new Color32(112, 98, 90, 255);
        private static readonly Color32 TwigDark = new Color32(84, 72, 68, 255);
        private static readonly Color32 BirchBark = new Color32(214, 210, 200, 255);

        public static PixelCanvas Tree(TreeKind kind, int variant, bool snowy)
        {
            switch (kind)
            {
                case TreeKind.Spruce: return Conifer(11, 22, 5, variant, snowy, 101);
                case TreeKind.Fir: return Conifer(13, 18, 4, variant, snowy, 202);
                default: return Hardwood(variant, snowy);
            }
        }

        /// <summary>
        /// A small stand of three trees of one kind in a single sprite (two behind, one in front), so the forest
        /// looks thick without drawing three times as many trees.
        /// </summary>
        public static PixelCanvas Cluster(TreeKind kind, int variant, bool snowy)
        {
            var front = Tree(kind, variant, snowy);
            var backL = Tree(kind, 1 - variant, snowy);
            var backR = Tree(kind == TreeKind.Hardwood ? TreeKind.Spruce : kind, variant, snowy);
            int w = front.Width + 12, h = front.Height + 5;
            var c = new PixelCanvas(w, h);
            Stamp(c, backL, 1, h - 4 - backL.Height);
            Stamp(c, backR, w - backR.Width - 1, h - 5 - backR.Height);
            Stamp(c, front, 6, h - front.Height);
            c.PivotX = 6 + front.PivotX;
            c.PivotY = h - 1;
            return c;
        }

        private static void Stamp(PixelCanvas dst, PixelCanvas src, int ox, int oy)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                    if (src.IsSet(x, y)) dst.Set(ox + x, oy + y, src.Get(x, y));
        }

        /// <summary>
        /// A conifer: stacked tiers of drooping branches, lit from the upper left. Snowy trees carry snow on the
        /// top of each tier and a white tip; bare ones show green tier tops instead.
        /// </summary>
        private static PixelCanvas Conifer(int w, int h, int tiers, int variant, bool snowy, int salt)
        {
            var c = new PixelCanvas(w, h);
            int seed = salt + variant * 31;
            int trunkRows = 3;
            int crownH = h - trunkRows + 1;
            float cx = (w - 1) * 0.5f;
            float maxHalf = w * 0.5f;
            var surface = new bool[w * h];

            var tierOf = new int[h];
            var withinOf = new float[h];
            for (int y = 0; y < crownH; y++)
            {
                // Tiers overlap: each starts narrow and flares out to drooping tips at its base, so the
                // silhouette is a sawtooth rather than a cone.
                float pos = (y + 0.5f) / crownH * tiers;
                int tier = Mathf.Min(tiers - 1, (int)pos);
                float within = pos - tier;
                tierOf[y] = tier;
                withinOf[y] = within;
                float tierHalf = maxHalf * (0.3f + 0.7f * (tier + 1) / tiers);
                float half = tierHalf * (0.3f + 0.7f * within * within) + (Hash(y, variant, seed) - 0.5f) * 0.5f;
                if (y == 0) half = 0.4f;
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    if (Mathf.Abs(dx) > half + 0.25f) continue;
                    float side = dx / Mathf.Max(0.5f, half); // -1 left (lit) .. +1 right (shade)
                    Color32 col = side < -0.3f ? NeedleLight : side > 0.35f ? NeedleDark : NeedleMid;
                    if (within > 0.8f) col = NeedleDark;                       // shadow under each tier
                    else if (Hash(x, y, seed) > 0.85f) col = side < 0 ? NeedleMid : NeedleDark;
                    c.Set(x, y, col);
                    if (within < 0.45f) surface[y * w + x] = true;              // upper part of a tier
                }
            }

            // Trunk under the lowest branches.
            int tx = Mathf.RoundToInt(cx);
            for (int y = crownH - 1; y < h; y++)
            {
                c.Set(tx, y, Bark);
                if (w >= 12) c.Set(tx + 1, y, BarkDark);
            }

            // Snow (or a lighter green) on the upper surface of each tier: texels with open air above, and the
            // top rows of each tier, heavier on the lit (left) side.
            var snowAt = new bool[w * h];
            for (int y = 0; y < crownH; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!c.IsSet(x, y)) continue;
                    bool openAbove = !c.IsSet(x, y - 1);
                    bool right = x > cx + 0.5f;
                    bool tierTop = surface[y * w + x] && Hash(x, y, seed + 7) > (right ? 0.55f : 0.25f);
                    if (openAbove || tierTop) snowAt[y * w + x] = true;
                }
            for (int y = 0; y < crownH; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!snowAt[y * w + x]) continue;
                    bool right = x > cx + 0.5f;
                    if (snowy) c.Set(x, y, right ? SnowShade : SnowLit);
                    else if (!c.IsSet(x, y - 1) && !right) c.Set(x, y, NeedleTip);
                }
            if (snowy) c.Set(tx, 0, SnowLit);

            c.PivotX = tx;
            c.PivotY = h - 1;
            return c;
        }

        /// <summary>A leafless hardwood (maple or birch, it's winter in Vermont), branches dusted when it has snowed.</summary>
        private static PixelCanvas Hardwood(int variant, bool snowy)
        {
            int w = 13, h = 17;
            var c = new PixelCanvas(w, h);
            int seed = 303 + variant * 17;
            bool birch = variant == 1;
            Color32 trunk = birch ? BirchBark : Bark;
            Color32 trunkDark = birch ? new Color32(150, 146, 138, 255) : BarkDark;
            int tx = w / 2;

            for (int y = 6; y < h; y++)
            {
                c.Set(tx, y, trunk);
                if (birch && Hash(tx, y, seed) > 0.7f) c.Set(tx, y, new Color32(40, 38, 36, 255)); // birch marks
                if (y > 10) c.Set(tx + 1, y, trunkDark);
            }
            Branch(c, tx, 7, -1, 6, seed, 0);
            Branch(c, tx, 6, 1, 6, seed + 1, 0);
            Branch(c, tx, 9, -1, 4, seed + 2, 0);
            Branch(c, tx, 8, 1, 4, seed + 3, 0);
            c.Line(tx, 7, tx, 0, Twig);

            if (snowy)
                for (int y = 1; y < h - 4; y++)
                    for (int x = 0; x < w; x++)
                        if (c.IsSet(x, y) && !c.IsSet(x, y - 1) && !IsSnow(c.Get(x, y)) && Hash(x, y, seed + 9) > 0.2f) c.Set(x, y - 1, SnowLit);

            c.PivotX = tx;
            c.PivotY = h - 1;
            return c;
        }

        private static void Branch(PixelCanvas c, int x, int y, int dir, int length, int seed, int depth)
        {
            int ex = x + dir * length / 2 + (Hash(x, y, seed) > 0.5f ? dir : 0);
            int ey = y - length;
            c.Line(x, y, ex, ey, depth == 0 ? Twig : TwigDark);
            if (depth < 2 && length > 3)
            {
                Branch(c, ex, ey + 1, dir, length / 2, seed + 5, depth + 1);
                Branch(c, (x + ex) / 2, (y + ey) / 2, -dir, length / 2, seed + 9, depth + 1);
            }
        }

        // =====================================================================
        // Skiers
        // =====================================================================

        private static readonly Color32 Skin = new Color32(238, 196, 164, 255);
        private static readonly Color32 Goggle = new Color32(40, 44, 70, 255);
        private static readonly Color32 Boot = new Color32(36, 36, 40, 255);
        private static readonly Color32 Pole = new Color32(70, 72, 78, 255);
        private static readonly Color32 Glove = new Color32(30, 30, 34, 255);

        // Outfit colours by skill: first-timer hi-vis yellow, beginner orange, intermediate sky blue,
        // advanced red, expert purple, pro black (matches Palette.Skill).
        private static readonly Color32[] Jackets =
        {
            new Color32(255, 214, 0, 255),
            new Color32(255, 140, 0, 255),
            new Color32(30, 160, 230, 255),
            new Color32(200, 30, 60, 255),
            new Color32(130, 40, 200, 255),
            new Color32(36, 36, 40, 255),
        };

        // Looks: helmet or hat, trousers, skis.
        private static readonly Color32[] Helmets =
        {
            new Color32(236, 236, 240, 255), new Color32(30, 30, 34, 255), new Color32(190, 40, 40, 255), new Color32(140, 190, 230, 255),
        };
        private static readonly Color32[] Trousers =
        {
            new Color32(40, 48, 84, 255), new Color32(56, 56, 62, 255), new Color32(26, 26, 30, 255), new Color32(98, 102, 112, 255),
        };
        private static readonly Color32[] Skis =
        {
            new Color32(210, 50, 40, 255), new Color32(240, 200, 30, 255), new Color32(40, 110, 220, 255), new Color32(40, 170, 90, 255),
        };

        // H helmet, G goggles, S skin, J jacket, j jacket shade, L glove, P trousers, p trouser shade,
        // B boots, K skis, O pole.
        private static readonly string[] SkiSideArt =
        {
            "....HH...",
            "...HHHH..",
            "...HSGG..",
            "....SS...",
            "...jJJJ..",
            "..jjJJJL.",
            "..jJJJ.O.",
            "...JJJ.O.",
            "...pPP.O.",
            "...ppPP..",
            "...pp.PP.",
            "..BBB.BB.",
            "KKKKKKKKK",
        };
        private static readonly string[] SkiFrontArt =
        {
            "...HHH...",
            "..HHHHH..",
            "..HGGGH..",
            "...SSS...",
            "..jJJJj..",
            ".LjJJJjL.",
            ".O.JJJ.O.",
            ".O.pPp.O.",
            ".O.p.p.O.",
            "..pp.pp..",
            "..BB.BB..",
            "..KK.KK..",
            "..KK.KK..",
        };
        private static readonly string[] BackArt =
        {
            "...HHH...",
            "..HHHHH..",
            "..HHHHH..",
            "...jJj...",
            "..jJJJj..",
            ".LjJJJjL.",
            ".O.JJJ.O.",
            ".O.pPp.O.",
            ".O.p.p.O.",
            "...p.p...",
            "..BB.BB..",
            "..KK.KK..",
            "..KK.KK..",
        };
        private static readonly string[] BackIdleArt =
        {
            "...HHH...",
            "..HHHHH..",
            "..HHHHH..",
            "...jJj...",
            "..jJJJj..",
            ".LJJJJJL.",
            "O..JJJ..O",
            "O..pPp..O",
            "O..p.p..O",
            "...p.p...",
            "..BB.BB..",
            "..KK.KK..",
            "..KK.KK..",
        };
        private static readonly string[] Walk1Art =
        {
            "....HH...",
            "...HHHH..",
            "...HSGG.K",
            "....SS.K.",
            "..jJJJK..",
            "..jJJJL..",
            ".KjJJJ...",
            "K..JJJ...",
            "...pPP...",
            "...p..P..",
            "..p...P..",
            ".BB...BB.",
            ".........",
        };
        private static readonly string[] Walk2Art =
        {
            "....HH...",
            "...HHHH..",
            "...HSGG.K",
            "....SS.K.",
            "..jJJJK..",
            "..jJJJL..",
            ".KjJJJ...",
            "K..JJJ...",
            "...pPP...",
            "...pP....",
            "...pP....",
            "...BBB...",
            ".........",
        };
        /// <summary>Front view on a chair. The lap (row 6) is the pivot: it sits on the bench.</summary>
        private static readonly string[] SeatedArt =
        {
            "..HHH..",
            ".HHHHH.",
            ".HGGGH.",
            "..SSS..",
            ".jJJJj.",
            "LjJJJjL",
            ".pPPPp.",
            ".p...p.",
            ".B...B.",
            ".K...K.",
            ".K...K.",
        };
        private static readonly string[] FallenArt =
        {
            "...........",
            ".K.......HH",
            ".KjJJJJJHSG",
            "ppPPJJJL...",
            "BB.KKKKKKK.",
        };

        public static PixelCanvas Skier(int skill, int look, SkierPose pose)
        {
            string[] art;
            switch (pose)
            {
                case SkierPose.SkiSide: art = SkiSideArt; break;
                case SkierPose.SkiFront: art = SkiFrontArt; break;
                case SkierPose.Back: art = BackArt; break;
                case SkierPose.BackIdle: art = BackIdleArt; break;
                case SkierPose.Walk1: art = Walk1Art; break;
                case SkierPose.Walk2: art = Walk2Art; break;
                case SkierPose.Seated: art = SeatedArt; break;
                default: art = FallenArt; break;
            }

            var c = new PixelCanvas(art[0].Length, art.Length);
            var jacket = Jackets[Mathf.Clamp(skill, 0, Jackets.Length - 1)];
            var helmet = Helmets[look % Helmets.Length];
            var trousers = Trousers[look % Trousers.Length];
            c.Paint(0, 0, art,
                ('H', helmet), ('G', Goggle), ('S', Skin), ('J', jacket), ('j', Shade(jacket, 0.72f)), ('L', Glove),
                ('P', trousers), ('p', Shade(trousers, 0.75f)), ('B', Boot), ('K', Skis[look % Skis.Length]), ('O', Pole));

            // A red knit hat gets a bobble instead of goggles on top.
            if (look == 2 && pose != SkierPose.Fallen) c.Set(c.Width / 2, 0, new Color32(240, 240, 240, 255));

            c.PivotX = c.Width / 2;
            c.PivotY = pose == SkierPose.Seated ? 6 : c.Height - 1;
            return c;
        }

        // =====================================================================
        // Lift carriers
        // =====================================================================

        private static readonly Color32 Steel = new Color32(86, 90, 98, 255);
        private static readonly Color32 SteelDark = new Color32(52, 54, 60, 255);
        private static readonly Color32 SteelLight = new Color32(140, 146, 156, 255);

        /// <summary>Texels between seat centres on a chair.</summary>
        public const int SeatPitch = 5;
        /// <summary>Row (from the top, i.e. below the grip) where a seated rider's lap sits.</summary>
        public const int SeatRow = 11;

        /// <summary>Horizontal offset (texels from the grip) of seat <paramref name="seat"/> on a chair with <paramref name="seats"/> seats.</summary>
        public static float SeatOffset(int seat, int seats) => (seat - (seats - 1) * 0.5f) * SeatPitch;

        /// <summary>The thing on the cable for each lift type (null where there is none to draw, e.g. a carpet).</summary>
        public static PixelCanvas Carrier(LiftType type, bool occupied)
        {
            switch (type)
            {
                case LiftType.FixedGripChair: return Chair(3, new Color32(196, 52, 40, 255), false);
                case LiftType.DetachableChair: return Chair(4, new Color32(40, 92, 196, 255), false);
                case LiftType.DetachableBubble: return Chair(6, new Color32(64, 66, 74, 255), true);
                case LiftType.Gondola: return Cabin(occupied);
                default: return null;
            }
        }

        /// <summary>
        /// A chair seen from the front: grip and hanger, backrest, bench (where riders' laps go), armrests and a
        /// footrest. Riders are drawn over it, one per seat.
        /// </summary>
        private static PixelCanvas Chair(int seats, Color32 paint, bool bubble)
        {
            int w = seats * SeatPitch + 3, h = 16;
            var c = new PixelCanvas(w, h);
            int cx = w / 2;
            var paintDark = Shade(paint, 0.7f);

            if (bubble)
            {
                // Raised weather bubble: an orange dome behind the riders, darker rim.
                var dome = new Color32(236, 116, 34, 255);
                float rx = (w - 3) * 0.5f, ry = 7f;
                for (int y = 0; y <= 7; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        float dx = (x - cx) / rx, dy = (7 - y) / ry;
                        float d = dx * dx + dy * dy;
                        if (d <= 1f) c.Set(x, y, d > 0.72f ? Shade(dome, 0.75f) : ((x + y) & 1) == 0 ? dome : Shade(dome, 0.9f));
                    }
            }

            // Grip and hanger.
            c.Rect(cx - 1, 0, 3, 2, SteelDark);
            c.VLine(cx, 2, 7, Steel);
            // Top rail, backrest, bench.
            c.HLine(1, w - 2, 7, SteelLight);
            for (int y = 8; y <= 10; y++)
                for (int x = 1; x < w - 1; x++)
                    c.Set(x, y, (x - 1) % SeatPitch == 0 ? paintDark : paint);
            c.HLine(0, w - 1, SeatRow, paintDark);
            // Armrests.
            c.VLine(0, 7, SeatRow, Steel);
            c.VLine(w - 1, 7, SeatRow, Steel);
            // Footrest.
            c.VLine(1, SeatRow + 1, h - 1, Steel);
            c.VLine(w - 2, SeatRow + 1, h - 1, Steel);
            c.HLine(1, w - 2, h - 1, SteelLight);

            c.PivotX = cx;
            c.PivotY = 0;
            return c;
        }

        /// <summary>A gondola cabin. Occupied cabins show heads in the windows; riders aren't drawn separately.</summary>
        private static PixelCanvas Cabin(bool occupied)
        {
            int w = 13, h = 20;
            var c = new PixelCanvas(w, h);
            int cx = w / 2;
            var body = new Color32(206, 44, 44, 255);
            var bodyDark = Shade(body, 0.72f);
            var glass = new Color32(150, 196, 226, 255);
            var glassDark = new Color32(104, 146, 182, 255);

            c.Rect(cx - 1, 0, 3, 2, SteelDark);
            c.VLine(cx, 2, 5, Steel);
            c.HLine(2, w - 3, 5, SteelDark);
            c.Rect(1, 6, w - 2, 2, new Color32(66, 70, 78, 255));  // roof
            c.Rect(0, 8, w, 11, body);                             // body
            c.VLine(w - 1, 8, 18, bodyDark);
            c.VLine(w - 2, 8, 18, bodyDark);
            c.Rect(1, 9, w - 2, 5, glass);                         // window band
            c.HLine(1, w - 2, 13, glassDark);
            c.VLine(cx, 9, 13, bodyDark);                          // mullion
            c.HLine(0, w - 1, 15, new Color32(240, 240, 240, 255)); // livery stripe
            c.HLine(0, w - 1, 19, SteelDark);                      // floor

            if (occupied)
            {
                int[] heads = { 2, 4, 8, 10 };
                for (int i = 0; i < heads.Length; i++)
                {
                    var hat = Helmets[i % Helmets.Length];
                    c.Set(heads[i], 10, hat);
                    c.Set(heads[i] + 1, 10, hat);
                    c.Set(heads[i], 11, Skin);
                    c.Set(heads[i] + 1, 11, Skin);
                    c.Set(heads[i], 12, Jackets[(i * 2 + 1) % Jackets.Length]);
                    c.Set(heads[i] + 1, 12, Jackets[(i * 2 + 1) % Jackets.Length]);
                }
            }

            c.PivotX = cx;
            c.PivotY = 0;
            return c;
        }

        /// <summary>The bar of a T-bar: riders lean against it, one each side. Pivot at the spring box.</summary>
        public static PixelCanvas TBarCross()
        {
            var c = new PixelCanvas(11, 4);
            c.Rect(4, 0, 3, 2, SteelDark);  // spring box bottom
            c.VLine(5, 0, 2, Steel);
            c.HLine(0, 10, 2, SteelLight);
            c.HLine(1, 9, 3, Steel);
            c.PivotX = 5;
            c.PivotY = 0;
            return c;
        }

        /// <summary>A rope-tow handle.</summary>
        public static PixelCanvas RopeHandle()
        {
            var c = new PixelCanvas(3, 4);
            c.VLine(1, 0, 2, SteelDark);
            c.HLine(0, 2, 3, new Color32(220, 180, 40, 255));
            c.PivotX = 1;
            c.PivotY = 0;
            return c;
        }

        // =====================================================================

        private static bool IsSnow(Color32 c) => c.r == SnowLit.r && c.g == SnowLit.g && c.b == SnowLit.b;

        private static Color32 Shade(Color32 c, float f)
            => new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);

        private static float Hash(int x, int y, int seed) => TerrainDetailArt.Hash(x, y, seed);
    }
}
