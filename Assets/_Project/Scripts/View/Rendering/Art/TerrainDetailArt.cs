using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Procedural ground textures. Each is a square that tiles seamlessly and holds per-texel colour
    /// multipliers around 1 (linear), which the shader applies over the tile's own colour. So the same snow
    /// texture works for bright powder at noon, blue shade at dusk and dimmed unowned land.
    /// Texel (x, y) lies along grid x and grid y, so "along x" patterns run toward the screen's lower right.
    /// </summary>
    public static class TerrainDetailArt
    {
        public static Color[] Draw(SurfaceDetail detail, int n)
        {
            var m = new Color[n * n];
            for (int i = 0; i < m.Length; i++) m[i] = Color.white;
            int seed = 7919 * ((int)detail + 1);
            switch (detail)
            {
                case SurfaceDetail.Powder: Powder(m, n, seed); break;
                case SurfaceDetail.Packed: Packed(m, n, seed); break;
                case SurfaceDetail.GroomedX: Corduroy(m, n, seed, 0); break;
                case SurfaceDetail.GroomedY: Corduroy(m, n, seed, 1); break;
                case SurfaceDetail.GroomedDown: Corduroy(m, n, seed, 2); break;
                case SurfaceDetail.GroomedAcross: Corduroy(m, n, seed, 3); break;
                case SurfaceDetail.Slush: Slush(m, n, seed); break;
                case SurfaceDetail.Ice: Ice(m, n, seed); break;
                case SurfaceDetail.Crud: Crud(m, n, seed); break;
                case SurfaceDetail.SnowyRock: SnowyRock(m, n, seed); break;
                case SurfaceDetail.Grass: Grass(m, n, seed); break;
                case SurfaceDetail.Dirt: Dirt(m, n, seed); break;
                case SurfaceDetail.Rock: Rock(m, n, seed); break;
                case SurfaceDetail.Scree: Scree(m, n, seed); break;
                case SurfaceDetail.Water: Water(m, n, seed); break;
                case SurfaceDetail.Walkway: Walkway(m, n, seed); break;
                case SurfaceDetail.RoadX: Road(m, n, seed, alongX: true); break;
                case SurfaceDetail.RoadY: Road(m, n, seed, alongX: false); break;
                case SurfaceDetail.ParkingStalls: Parking(m, n, seed, stalls: true); break;
                case SurfaceDetail.ParkingAisle: Parking(m, n, seed, stalls: false); break;
            }
            return m;
        }

        // ---------- Snow ----------

        private static void Powder(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Soft drifts plus wind ripples (sastrugi) running across the slope.
                    float drift = Fbm(x, y, n, 32, seed, 3) - 0.5f;
                    float warp = Fbm(x, y, n, 16, seed + 5, 2) * 6f;
                    float ripple = Mathf.Sin((x + 2 * y + warp) * (2f * Mathf.PI / 16f));
                    float v = 1f + drift * 0.06f + (ripple > 0.82f ? -0.035f : ripple < -0.9f ? 0.02f : 0f);
                    // Sparkles: the odd bright crystal, with a faint blue shadow beside it.
                    float h = Hash(x, y, seed);
                    if (h > 0.9935f) v = 1.12f;
                    else if (Hash(x - 1, y, seed) > 0.9935f) v -= 0.03f;
                    Put(m, n, x, y, v, v, v * 1.01f);
                }
        }

        private static void Packed(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.995f + (Fbm(x, y, n, 16, seed, 3) - 0.5f) * 0.07f + (Hash(x, y, seed) - 0.5f) * 0.02f;
                    Put(m, n, x, y, v, v, v);
                }
            // Ski tracks: pairs of carved arcs, mostly down the fall line (+x+y), wrapping around the tile.
            for (int k = 0; k < 6; k++)
            {
                float sx = Hash(k, 1, seed) * n, sy = Hash(k, 2, seed) * n;
                float amp = 5f + Hash(k, 3, seed) * 7f, period = 60f + Hash(k, 4, seed) * 50f;
                float phase = Hash(k, 5, seed) * 6.28f;
                for (int s = 0; s < n * 2; s++)
                {
                    float t = s * 0.5f;
                    float side = Mathf.Sin(t / period * 6.28f + phase) * amp;
                    // Along (+1,+1)/sqrt2 with a sideways swing along (+1,-1)/sqrt2.
                    float px = sx + t * 0.707f + side * 0.707f, py = sy + t * 0.707f - side * 0.707f;
                    Mul(m, n, Mathf.RoundToInt(px), Mathf.RoundToInt(py), 0.965f);
                    Mul(m, n, Mathf.RoundToInt(px + 1.4f), Mathf.RoundToInt(py - 1.4f), 0.975f);
                }
            }
        }

        /// <param name="dir">0 = along x, 1 = along y, 2 = along +x+y, 3 = along +x-y.</param>
        private static void Corduroy(Color[] m, int n, int seed, int dir)
        {
            // Ridge profile across the corduroy, every 4 texels, and a darker groomer-pass seam every 32.
            float[] profile = { 1.035f, 1.01f, 0.955f, 0.985f };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int across;
                    switch (dir)
                    {
                        case 0: across = y; break;
                        case 1: across = x; break;
                        case 2: across = x - y + n; break;
                        default: across = x + y; break;
                    }
                    float v = profile[across & 3];
                    if ((across & 31) == 0) v = 0.94f;
                    v += (Fbm(x, y, n, 32, seed, 2) - 0.5f) * 0.03f + (Hash(x, y, seed) - 0.5f) * 0.012f;
                    Put(m, n, x, y, v, v, v * 1.01f);
                }
        }

        private static void Slush(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wet = Fbm(x, y, n, 16, seed, 3);
                    float r = 0.97f, g = 0.97f, b = 0.98f;
                    if (wet > 0.56f) { r = 0.86f; g = 0.88f; b = 0.93f; }       // grey, water-logged patches
                    else if (wet > 0.52f) { r = 0.92f; g = 0.93f; b = 0.96f; }
                    float h = Hash(x, y, seed);
                    if (h > 0.985f) { r *= 0.86f; g *= 0.8f; b *= 0.72f; }       // dirt flecks
                    float lump = (Fbm(x, y, n, 8, seed + 3, 2) - 0.5f) * 0.06f;
                    Put(m, n, x, y, r + lump, g + lump, b + lump);
                }
        }

        private static void Ice(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Glassy streaks scraped down the fall line, with a few dull patches and cracks.
                    float warp = Fbm(x, y, n, 32, seed, 2) * 10f;
                    float streak = Mathf.Sin((x - y + warp) * (2f * Mathf.PI / 21.333f));
                    float r = 0.98f, g = 1f, b = 1.02f;
                    if (streak > 0.86f) { r = 1.08f; g = 1.1f; b = 1.12f; }
                    else if (streak < -0.7f) { r = 0.93f; g = 0.96f; b = 1.01f; }
                    float c = Fbm(x, y, n, 8, seed + 9, 3);
                    if (Mathf.Abs(c - 0.5f) < 0.012f) { r = 0.84f; g = 0.88f; b = 0.94f; }
                    Put(m, n, x, y, r, g, b);
                }
        }

        private static void Crud(Color[] m, int n, int seed)
        {
            // Lumpy, chopped-up snow: an embossed noise field lit from the upper left of the screen.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Fbm(x - 1, y - 1, n, 8, seed, 3), b = Fbm(x + 1, y + 1, n, 8, seed, 3);
                    float v = 0.99f + (a - b) * 1.1f + (Hash(x, y, seed) - 0.5f) * 0.03f;
                    v = Mathf.Clamp(v, 0.82f, 1.09f);
                    Put(m, n, x, y, v, v, v * 1.01f);
                }
        }

        private static void SnowyRock(Color[] m, int n, int seed)
        {
            Powder(m, n, seed);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Fbm(x, y, n, 16, seed + 11, 3);
                    if (r < 0.6f) continue;
                    // Dark rock poking through, lit edge on its upper side.
                    bool edge = Fbm(x - 1, y - 1, n, 16, seed + 11, 3) < 0.6f;
                    float v = edge ? 0.62f : 0.42f + (Hash(x, y, seed) - 0.5f) * 0.08f;
                    Put(m, n, x, y, v, v, v * 1.04f);
                }
        }

        // ---------- Bare ground ----------

        private static void Grass(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float patch = Fbm(x, y, n, 16, seed, 3);
                    float h = Hash(x, y, seed);
                    float v = 0.92f + (patch - 0.5f) * 0.25f + (h - 0.5f) * 0.16f;
                    float r = v, g = v, b = v;
                    if (patch > 0.6f) { r *= 1.12f; g *= 1.06f; b *= 0.8f; }  // dry, yellowed winter grass
                    if (h > 0.93f) { r *= 1.15f; g *= 1.15f; b *= 1.05f; }   // blade tips catching light
                    else if (h < 0.06f) { r *= 0.8f; g *= 0.82f; b *= 0.8f; }
                    Put(m, n, x, y, r, g, b);
                }
        }

        private static void Dirt(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.95f + (Fbm(x, y, n, 16, seed, 3) - 0.5f) * 0.2f + (Hash(x, y, seed) - 0.5f) * 0.12f;
                    if (Hash(x, y, seed + 1) > 0.975f) v = 1.25f;                  // pebble
                    else if (Hash(x - 1, y - 1, seed + 1) > 0.975f) v = 0.78f;     // its shadow
                    Put(m, n, x, y, v, v * 0.98f, v * 0.95f);
                }
        }

        private static void Rock(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Fbm(x - 1, y - 1, n, 16, seed, 3), b = Fbm(x + 1, y + 1, n, 16, seed, 3);
                    float v = 1f + (a - b) * 1.6f + (Hash(x, y, seed) - 0.5f) * 0.06f;
                    float crack = Fbm(x, y, n, 32, seed + 4, 2);
                    if (Mathf.Abs(crack - 0.5f) < 0.01f) v = 0.62f;
                    v = Mathf.Clamp(v, 0.6f, 1.25f);
                    Put(m, n, x, y, v, v, v * 1.02f);
                }
        }

        private static void Scree(Color[] m, int n, int seed)
        {
            // Loose stones: each 4x4 block is a stone with its own shade, darker at the bottom-right edge.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int bx = x >> 2, by = y >> 2;
                    float shade = 0.82f + Hash(bx, by, seed) * 0.36f;
                    int lx = x & 3, ly = y & 3;
                    if (lx == 3 || ly == 3) shade *= 0.78f;
                    else if (lx == 0 && ly == 0) shade *= 1.1f;
                    Put(m, n, x, y, shade, shade, shade);
                }
        }

        private static void Water(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float warp = Fbm(x, y, n, 32, seed, 2) * 8f;
                    float w = Mathf.Sin((x + y + warp) * (2f * Mathf.PI / 8f));
                    float v = 1f + (w > 0.9f ? 0.18f : w < -0.6f ? -0.06f : 0f);
                    Put(m, n, x, y, v, v, v);
                }
        }

        // ---------- Walkways, roads and parking ----------

        /// <summary>Trampled snow: lumpy, a little grey, scattered with pairs of boot prints going every which way.</summary>
        private static void Walkway(Color[] m, int n, int seed)
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.97f + (Fbm(x, y, n, 8, seed, 3) - 0.5f) * 0.09f + (Hash(x, y, seed) - 0.5f) * 0.03f;
                    Put(m, n, x, y, v, v * 0.995f, v * 0.985f);
                }
            for (int k = 0; k < 70; k++)
            {
                int px = (int)(Hash(k, 1, seed) * n), py = (int)(Hash(k, 2, seed) * n);
                bool across = Hash(k, 3, seed) > 0.5f;
                for (int f = 0; f < 2; f++)
                {
                    int fx = px + (across ? 0 : f * 3), fy = py + (across ? f * 3 : 0);
                    Mul(m, n, fx, fy, 0.9f); Mul(m, n, fx + (across ? 1 : 0), fy + (across ? 0 : 1), 0.9f);
                }
            }
        }

        // Paved surfaces are drawn with a light "paint" vertex colour: the texture darkens everything except the
        // painted lines down to asphalt, so lines stay crisp white or yellow.
        private const float Asphalt = 0.085f;

        private static float AsphaltAt(int x, int y, int n, int seed)
            => Asphalt * (1f + (Fbm(x, y, n, 8, seed, 2) - 0.5f) * 0.25f + (Hash(x, y, seed) - 0.5f) * 0.18f);

        /// <summary>A two-lane road one tile wide: white edge lines and a dashed yellow centre line.</summary>
        private static void Road(Color[] m, int n, int seed, bool alongX)
        {
            const int tile = WorldAtlasConstants.TexelsPerTile;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int across = (alongX ? y : x) % tile, along = alongX ? x : y;
                    float a = AsphaltAt(x, y, n, seed);
                    if (across == 2 || across == tile - 3) Put(m, n, x, y, 0.8f, 0.8f, 0.8f);                 // edge lines
                    else if ((across == tile / 2 - 1 || across == tile / 2) && along % 16 < 9) Put(m, n, x, y, 1f, 0.8f, 0.22f); // centre
                    else Put(m, n, x, y, a, a, a * 1.04f);
                }
        }

        /// <summary>Parking lot asphalt. Stall tiles get white dividers every half tile (two cars per tile across).</summary>
        private static void Parking(Color[] m, int n, int seed, bool stalls)
        {
            const int tile = WorldAtlasConstants.TexelsPerTile;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = AsphaltAt(x, y, n, seed) * (stalls ? 1f : 1.08f);
                    int lx = x % (tile / 2);
                    if (stalls && lx == 0) Put(m, n, x, y, 0.85f, 0.85f, 0.85f);
                    else Put(m, n, x, y, a, a, a * 1.04f);
                }
            // A few oil stains.
            for (int k = 0; k < 10; k++)
            {
                int cx = (int)(Hash(k, 7, seed) * n), cy = (int)(Hash(k, 8, seed) * n);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                        if (dx * dx + dy * dy * 2 <= 8 && m[Wrap(cy + dy, n) * n + Wrap(cx + dx, n)].r < 0.5f) Mul(m, n, cx + dx, cy + dy, 0.8f);
            }
        }

        // ---------- Helpers ----------

        private static void Put(Color[] m, int n, int x, int y, float r, float g, float b)
            => m[Wrap(y, n) * n + Wrap(x, n)] = new Color(r, g, b, 1f);

        private static void Mul(Color[] m, int n, int x, int y, float f)
        {
            int i = Wrap(y, n) * n + Wrap(x, n);
            var c = m[i];
            m[i] = new Color(c.r * f, c.g * f, c.b * f, 1f);
        }

        private static int Wrap(int v, int n) => ((v % n) + n) % n;

        /// <summary>Stateless hash to 0..1 (same as the simulation's tree jitter, but view-only).</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Value noise that tiles every <paramref name="n"/> texels, with features <paramref name="cell"/> texels apart.</summary>
        private static float Noise(int x, int y, int n, int cell, int seed)
        {
            int period = Mathf.Max(1, n / cell);
            float fx = Wrap(x, n) / (float)cell, fy = Wrap(y, n) / (float)cell;
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            x0 %= period; y0 %= period;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static float Fbm(int x, int y, int n, int cell, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves && cell >= 1; o++)
            {
                sum += Noise(x, y, n, cell, seed + o * 101) * amp;
                norm += amp;
                amp *= 0.5f;
                cell /= 2;
            }
            return norm > 0f ? sum / norm : 0.5f;
        }
    }
}
