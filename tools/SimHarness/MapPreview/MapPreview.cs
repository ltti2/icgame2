using System;
using System.IO;
using System.Linq;
using IceCoast.Simulation;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using IceCoast.Tests;

/// <summary>
/// Software iso renderer: painter's algorithm over tiles back-to-front, flat-shaded like the Unity
/// prototype, including the scenery ring around the map. Pixel scale = zoom x the in-game art
/// (tile 64x32 px, 4 px per metre).
/// Args: seed, output.ppm, zoom (e.g. 0.125), then optional flags: "resort" builds the starter lift and run,
/// "slope" colours the map by difficulty, "contours" draws 100 ft contour lines, "lit=HOUR" applies that hour's sunlight,
/// "base" renders only the area around the resort base, "minutes=N" runs the game N minutes first (cars, guests),
/// "lights" puts a few trail lights beside the starter run.
/// </summary>
public static class MapPreview
{
    /// <summary>"slope" argument: colour map tiles by difficulty (green/blue/black/purple/red) instead of snow.</summary>
    private static bool SlopeMode;
    /// <summary>"contours": 100 ft lines, bolder every 500 ft (same as the Unity shader).</summary>
    private static bool Contours;
    /// <summary>"lit=HOUR" (e.g. lit=9.5): sunlight for that time of day, same model as DayLightController.</summary>
    private static Lighting Light;

    /// <summary>Port of the shader + DayLightController lighting, for previews.</summary>
    private sealed class Lighting
    {
        private readonly float[] _sun = new float[3];
        private readonly float _intensity, _ambient;
        private readonly (float r, float g, float b) _sunColor;

        public Lighting(float hour)
        {
            float t = (hour - 7.25f) / (16.33f - 7.25f);
            float az = (120f + (240f - 120f) * t) * MathF.PI / 180f;
            float el = MathF.Max(2f, 35f * MathF.Sin(Math.Clamp(t, 0f, 1f) * MathF.PI)) * MathF.PI / 180f;
            float r2 = 0.70710678f;
            // north = (1,-1), east = (1,1) in grid space
            float hx = r2 * MathF.Cos(az) + r2 * MathF.Sin(az), hy = -r2 * MathF.Cos(az) + r2 * MathF.Sin(az);
            _sun[0] = hx * MathF.Cos(el); _sun[1] = hy * MathF.Cos(el); _sun[2] = MathF.Sin(el);
            float day = SmoothStep(-0.06f, 0.06f, t) * SmoothStep(1.06f, 0.94f, t);
            _intensity = day;
            _ambient = 0.17f + (0.45f - 0.17f) * day;
            float warmth = Math.Clamp((20f - el * 180f / MathF.PI) / 17f, 0f, 1f);
            _sunColor = (0.6f, 0.6f * (0.97f - 0.31f * warmth), 0.6f * (0.92f - 0.5f * warmth));
        }

        private static float SmoothStep(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }

        public (float, float, float) Apply((float r, float g, float b) c, float gx, float gy)
        {
            float s = 0.1f * 2.5f;
            float nx = -gx * s, ny = -gy * s, nz = 1f;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= len; ny /= len; nz /= len;
            float flat = MathF.Max(_sun[2], 0.25f);
            float direct = Math.Clamp(nx * _sun[0] + ny * _sun[1] + nz * _sun[2], 0f, 1f) / flat;
            direct = MathF.Min(direct, 1.6f);
            float sky = 1f + (MathF.Pow(nz, 2.5f) - 1f) * 0.5f;
            float lr = (_ambient + _sunColor.r * _intensity * direct) * sky;
            float lg = (_ambient + _sunColor.g * _intensity * direct) * sky;
            float lb = (_ambient * 1.2f + _sunColor.b * _intensity * direct) * sky;
            return (c.r * lr, c.g * lg, c.b * lb);
        }
    }

    public static int Main(string[] args)
    {
        int seed = args.Length > 0 ? int.Parse(args[0]) : 12345;
        string output = args.Length > 1 ? args[1] : "map.ppm";
        float zoom = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.125f;
        bool resort = args.Contains("resort");
        SlopeMode = args.Contains("slope");
        Contours = args.Contains("contours");
        var litArg = args.FirstOrDefault(a => a.StartsWith("lit="));
        if (litArg != null) Light = new Lighting(float.Parse(litArg.Substring(4), System.Globalization.CultureInfo.InvariantCulture));

        var cfg = SimConfig.CreateDefault();
        cfg.Map.Seed = seed;
        var sim = SimulationFactory.CreateNewGame(cfg);
        if (resort) TestGames.BuildStarterResort(sim);
        if (args.Contains("lights")) TestGames.LightStarterRun(sim);
        var minutesArg = args.FirstOrDefault(a => a.StartsWith("minutes="));
        if (minutesArg != null) TestGames.RunMinutes(sim, int.Parse(minutesArg.Substring(8)));
        var map = sim.Map;
        var scenery = sim.World.Scenery;
        TerrainStats.Print(sim);

        int minX = scenery.MinX, minY = scenery.MinY, maxX = scenery.MaxX, maxY = scenery.MaxY;
        if (args.Contains("base"))
        {
            var e = sim.World.ResortEntrance;
            minX = Math.Max(minX, e.X - 36); maxX = Math.Min(maxX, e.X + 64);
            minY = Math.Max(minY, e.Y - 36); maxY = Math.Min(maxY, e.Y + 30);
        }
        float halfW = 32 * zoom, halfH = 16 * zoom, step = 4 * zoom;
        int maxH = cfg.Terrain.SummitElevationSteps + 50;
        int spanW = maxX - minX, spanH = maxY - minY;
        int width = (int)((spanW + spanH) * halfW) + 4;
        int height = (int)((spanW + spanH) * halfH + maxH * step) + 4;
        float ox = (spanH) * halfW + 2, oy = maxH * step + 2;
        var img = new byte[width * height * 3];
        for (int i = 0; i < img.Length; i += 3) { img[i] = 118; img[i + 1] = 156; img[i + 2] = 196; }

        (float, float) P(float gx, float gy, float h)
            => (ox + ((gx - minX) - (gy - minY)) * halfW, oy + ((gx - minX) + (gy - minY)) * halfH - h * step);

        for (int d = 0; d <= spanW + spanH - 2; d++)
        {
            for (int lx = Math.Max(0, d - spanH + 1); lx <= Math.Min(d, spanW - 1); lx++)
            {
                int x = minX + lx, y = minY + (d - lx);
                scenery.GetCorners(map, x, y, out int h00, out int h10, out int h01, out int h11);
                bool inMap = map.InBounds(x, y);
                var c = inMap ? TileColor(sim, x, y) : SceneryColor(sim, x, y, h00, h10, h01, h11);
                var a = P(x, y, h00); var b = P(x + 1, y, h10); var cc = P(x + 1, y + 1, h11); var dd = P(x, y + 1, h01);
                FillTri(img, width, height, a, b, cc, c, (h00, h10, h11));
                FillTri(img, width, height, a, cc, dd, c, (h00, h11, h01));

                int trees = inMap ? map.GetTreeDensity(x, y) : scenery.GetTreeDensity(x, y);
                if (SlopeMode && inMap) trees = 0;
                if (zoom < 0.1f) trees = Math.Min(trees, 1);
                for (int k = 0; k < trees; k++)
                {
                    float jx = SimRandom.Hash01(x, y, seed + k * 3) * 0.8f + 0.1f;
                    float jy = SimRandom.Hash01(x, y, seed + k * 3 + 1) * 0.8f + 0.1f;
                    var foot = P(x + jx, y + jy, scenery.SampleHeight(map, x + jx, y + jy));
                    float th = 20 * zoom, tw = 6.4f * zoom;
                    bool dim = inMap && !map.IsOwned(x, y);
                    var col = dim ? ((byte)36, (byte)62, (byte)50) : ((byte)34, (byte)66, (byte)44);
                    FillTri(img, width, height, (foot.Item1 - tw, foot.Item2), (foot.Item1 + tw, foot.Item2), (foot.Item1, foot.Item2 - th), col);
                }
                if (Contours && inMap) DrawContours(img, width, height, P, x, y, h00, h10, h11, h01, zoom);
            }
        }

        DrawProps(sim, img, width, height, P, zoom);

        using (var fs = File.Create(output))
        {
            var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            fs.Write(header, 0, header.Length);
            fs.Write(img, 0, img.Length);
        }
        Console.WriteLine($"Wrote {output} ({width}x{height})");
        return 0;
    }

    /// <summary>Trail lights, cars and guests, roughly (back to front is not handled; fine for checking layouts).</summary>
    private static void DrawProps(GameSimulation sim, byte[] img, int w, int h, Func<float, float, float, (float, float)> P, float zoom)
    {
        var map = sim.Map;
        var scenery = sim.World.Scenery;
        foreach (var light in sim.World.Lights)
        {
            float gx = light.Tile.X + 0.5f, gy = light.Tile.Y + 0.5f, gh = map.SampleHeight(gx, gy);
            var foot = P(gx, gy, gh);
            float pole = sim.Config.GetLight(light.Size).PoleHeightMeters * 4f * zoom;
            Line(img, w, h, foot, (foot.Item1, foot.Item2 - pole), Math.Max(1f, 1.5f * zoom), (60, 60, 64));
            Line(img, w, h, (foot.Item1 - 4 * zoom, foot.Item2 - pole), (foot.Item1 + 4 * zoom, foot.Item2 - pole), Math.Max(1f, 3f * zoom), (255, 230, 120));
        }
        (byte, byte, byte)[] carColors = { (200, 40, 40), (40, 80, 200), (230, 230, 230), (30, 30, 30), (60, 150, 70), (220, 180, 40) };
        foreach (var lot in sim.World.ParkingLots)
            for (int i = 0; i < lot.Spaces.Length; i++)
            {
                if (!sim.ParkingSystem.TryGetCarPose(lot, i, 0f, out float cx, out float cy, out float dx, out float dy)) continue;
                float ch = scenery.SampleHeight(map, cx, cy);
                var a = P(cx - dx * 0.2f, cy - dy * 0.2f, ch);
                var b = P(cx + dx * 0.2f, cy + dy * 0.2f, ch);
                Line(img, w, h, a, b, 7f * zoom, carColors[lot.Spaces[i].Color % carColors.Length]);
            }
        var pool = sim.World.Guests;
        for (int g = 0; g < pool.HighWater; g++)
        {
            if (!pool.Alive[g]) continue;
            var p = P(pool.PosX[g], pool.PosY[g], map.SampleHeight(pool.PosX[g], pool.PosY[g]));
            Line(img, w, h, p, (p.Item1, p.Item2 - 6 * zoom), Math.Max(1f, 2.5f * zoom), (230, 120, 20));
        }
    }

    private static float Shade(float gx, float gy)
    {
        float s = 0.38f; // matches TerrainColorizer relief exaggeration
        float nx = -gx * s, ny = -gy * s, nz = 1f;
        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        float lit = (nx * -0.62f + ny * -0.25f + nz * 0.74f) / len;
        return Math.Clamp(0.55f + 0.6f * lit, 0.6f, 1.15f);
    }

    private static (byte, byte, byte) SceneryColor(GameSimulation sim, int x, int y, int h00, int h10, int h01, int h11)
    {
        var s = sim.World.Scenery;
        (float r, float g, float b) c = (250f, 252f, 255f);
        if (s.GetTerrainType(x, y) == TerrainType.Rock) c = Mix(c, (122, 122, 128), 0.45f);
        if (s.GetSurface(x, y) == SurfaceType.Road) c = (64, 66, 70);
        float gx = ((h10 + h11) - (h00 + h01)) * 0.5f, gy = ((h01 + h11) - (h00 + h10)) * 0.5f;
        if (Light != null) c = Light.Apply(c, gx, gy);
        else { float shade = Shade(gx, gy); c = (c.r * shade, c.g * shade, c.b * shade); }
        c = Mix(c, (118, 156, 196), 0.2f * s.DistanceFromMap01(x, y)); // atmospheric haze
        return (B(c.r), B(c.g), B(c.b));
    }

    private static (byte, byte, byte) TileColor(GameSimulation sim, int x, int y)
    {
        var map = sim.Map;
        if (SlopeMode)
        {
            float sl = TerrainQueries.SlopePercent(map, sim.TerrainScale, x, y);
            if (sl < 4) return (200, 200, 200);
            switch (IceCoast.Simulation.Runs.RunRater.Rate(sim.Config.Runs, sl, sl))
            {
                case IceCoast.Simulation.Runs.Difficulty.Green: return (46, 170, 80);
                case IceCoast.Simulation.Runs.Difficulty.Blue: return (40, 100, 220);
                case IceCoast.Simulation.Runs.Difficulty.Black: return (30, 30, 34);
                case IceCoast.Simulation.Runs.Difficulty.DoubleBlack: return (130, 40, 160);
                default: return (220, 30, 30);
            }
        }
        var t = map.Locate(x, y);
        int i = t.Index;
        var ch = t.Chunk;
        var type = (TerrainType)ch.TerrainType[i];
        bool snowy = ch.SnowDepth[i] >= 5;
        (float r, float g, float b) c = snowy ? (250f, 252f, 255f) : (116f, 132f, 82f);
        if ((SnowQuality)ch.SnowQuality[i] == SnowQuality.Packed && snowy) c = (222, 228, 236);
        if (type == TerrainType.Rock) c = Mix(c, (122, 122, 128), snowy ? 0.45f : 1f);
        if ((ch.Flags[i] & (byte)TileFlags.BaseArea) != 0) c = Mix(c, (132, 110, 84), 0.3f);
        if (ch.RunId[i] != 0) c = Mix(c, (46, 170, 80), 0.35f);
        switch ((SurfaceType)ch.Surface[i])
        {
            case SurfaceType.WalkingArea: c = Mix(c, (196, 184, 166), 0.55f); break;
            case SurfaceType.Road: c = (64, 66, 70); break;
            case SurfaceType.Parking: c = ((x + y) & 1) == 0 ? (78, 80, 84) : (72, 74, 78); break;
        }
        if (ch.LightLevel[i] >= sim.Config.Lighting.LitThreshold) c = Mix(c, (255, 230, 120), 0.25f);
        if (ch.OccupantId[i] != 0 && Occupant.KindOf(ch.OccupantId[i]) != OccupantKind.Light) c = (150, 60, 50);

        TerrainQueries.Gradient(map, x, y, out float gx, out float gy);
        if ((ch.Flags[i] & (byte)TileFlags.Owned) == 0) c = Mix(c, (70, 82, 108), 0.18f);
        if (Light != null) { c = Light.Apply(c, gx, gy); return (B(c.r), B(c.g), B(c.b)); }
        float shade = Shade(gx, gy);
        if ((ch.Flags[i] & (byte)TileFlags.Owned) == 0) shade *= 0.94f;
        return (B(c.r * shade), B(c.g * shade), B(c.b * shade));
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Same marching-squares contours as ContourRenderer: 100 ft lines, 500 ft index lines, over the trees.</summary>
    private static void DrawContours(byte[] img, int w, int h, Func<float, float, float, (float, float)> P,
                                     int x, int y, int h00, int h10, int h11, int h01, float zoom)
    {
        const float interval = 30.48f; // 100 ft in metres (= steps)
        int lo = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11)), hi = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
        if (lo == hi) return;
        for (int k = (int)MathF.Ceiling(lo / interval); k * interval <= hi; k++)
        {
            float level = k * interval;
            if (level <= lo) continue;
            var pts = new System.Collections.Generic.List<(float, float)>();
            void Cross(int ax, int ay, int ha, int bx, int by, int hb)
            {
                if ((ha >= level) == (hb >= level)) return;
                float t = (level - ha) / (hb - ha);
                pts.Add(P(ax + (bx - ax) * t, ay + (by - ay) * t, level));
            }
            Cross(x, y, h00, x + 1, y, h10); Cross(x + 1, y, h10, x + 1, y + 1, h11);
            Cross(x + 1, y + 1, h11, x, y + 1, h01); Cross(x, y + 1, h01, x, y, h00);
            bool major = k % 5 == 0;
            float width = (major ? 2.6f : 1.2f);
            var col = major ? ((byte)38, (byte)50, (byte)74) : ((byte)70, (byte)88, (byte)118);
            for (int i = 0; i + 1 < pts.Count; i += 2) Line(img, w, h, pts[i], pts[i + 1], width, col);
        }
    }

    private static void Line(byte[] img, int w, int h, (float x, float y) a, (float x, float y) b, float width, (byte, byte, byte) col)
    {
        float dx = b.x - a.x, dy = b.y - a.y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4f) return;
        float nx = -dy / len * width * 0.5f, ny = dx / len * width * 0.5f;
        FillTri(img, w, h, (a.x - nx, a.y - ny), (b.x - nx, b.y - ny), (b.x + nx, b.y + ny), col);
        FillTri(img, w, h, (a.x - nx, a.y - ny), (b.x + nx, b.y + ny), (a.x + nx, a.y + ny), col);
    }

    /// <summary>Partial derivative of a linearly interpolated value across a screen triangle.</summary>
    private static float Grad((float x, float y) a, (float x, float y) b, (float x, float y) c, float va, float vb, float vc, bool dx)
    {
        float det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
        if (MathF.Abs(det) < 1e-6f) return 0f;
        return dx ? ((vb - va) * (c.y - a.y) - (vc - va) * (b.y - a.y)) / det
                  : ((vc - va) * (b.x - a.x) - (vb - va) * (c.x - a.x)) / det;
    }

    private static (float, float, float) Mix((float, float, float) a, (float, float, float) b, float t)
        => (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);
    private static byte B(float v) => (byte)Math.Clamp(v, 0, 255);

    private static void FillTri(byte[] img, int w, int h, (float x, float y) a, (float x, float y) b, (float x, float y) c, (byte r, byte g, byte b) col,
                                (float ea, float eb, float ec)? elev = null)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x))));
        int maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y))));
        int maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.y, Math.Max(b.y, c.y))));
        float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Math.Abs(area) < 1e-6f) return;
        for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                float fx = px + 0.5f, fy = py + 0.5f;
                float w0 = ((b.x - fx) * (c.y - fy) - (b.y - fy) * (c.x - fx)) / area;
                float w1 = ((c.x - fx) * (a.y - fy) - (c.y - fy) * (a.x - fx)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                int o = (py * w + px) * 3;
                var outCol = col;
                img[o] = outCol.r; img[o + 1] = outCol.g; img[o + 2] = outCol.b;
            }
    }
}

/// <summary>Slope and forest distribution of the playable map, for tuning the generator.</summary>
public static class TerrainStats
{
    public static void Print(GameSimulation sim)
    {
        var map = sim.Map;
        var counts = new int[6];
        var densities = new int[5];
        int total = 0;
        float maxH = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                float s = TerrainQueries.SlopePercent(map, sim.TerrainScale, x, y);
                int idx = s < 4 ? 0 : 1 + (int)IceCoast.Simulation.Runs.RunRater.Rate(sim.Config.Runs, s, s);
                counts[idx]++;
                densities[Math.Min(4, map.GetTreeDensity(x, y))]++;
                maxH = Math.Max(maxH, map.TileCenterHeight(x, y));
                total++;
            }
        string[] names = { "flat", "green", "blue", "black", "double", "triple" };
        for (int i = 0; i < 6; i++) Console.WriteLine($"  {names[i],-7} {counts[i] * 100.0 / total,5:0.0}%");
        Console.WriteLine("  tree density 0..4: " + string.Join(", ", densities.Select(v => $"{v * 100.0 / total:0}%")));
        Console.WriteLine($"  highest point {maxH * sim.Config.Terrain.MetersPerHeightStep * 3.28084f:N0} ft");
    }
}
