using System;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Terrain
{
    /// <summary>
    /// The height function for an old, worn-down New England mountain, loosely modelled on Killington.
    ///
    /// height = apron + softmax(peak and ridge prominences) - drainages + rolling + detail
    ///
    ///  - The <b>apron</b> is one broad dome that carries the whole mountain down to the valley.
    ///  - <b>Peaks</b> and <b>ridgelines</b> are defined by their <i>prominence</i> above the apron, not their
    ///    absolute height. That keeps flanks at believable east-coast grades: steepness comes from how far a
    ///    peak stands out, the way real worn-down ranges work. Ridges connect peaks and sag into saddles.
    ///    Peaks follow Killington's layout in spirit: the summit with a second high peak beside it, then
    ///    sub-peaks like Snowdon, Rams Head, Bear Mountain and Sunrise, and more mountains beyond the map.
    ///  - <b>Drainages</b> are broad, shallow hollows (brooks), not alpine gullies.
    ///
    /// Coordinates are normalised map coordinates: (0,0) is the map's top-left corner, (1,1) the bottom-right.
    /// Values outside 0..1 are valid and produce the surrounding scenery.
    /// Returns 0 at the resort base elevation and about 1 at the summit (slightly below 0 off the map).
    /// </summary>
    public sealed class MountainShape
    {
        private struct Peak
        {
            public float U, V, Prominence, Radius;
            public Peak(float u, float v, float prominence, float radius) { U = u; V = v; Prominence = prominence; Radius = radius; }
        }

        private struct Ridge
        {
            public int A, B;
            public float Width, Sag;
            public Ridge(int a, int b, float width, float sag) { A = a; B = b; Width = width; Sag = sag; }
        }

        /// <summary>The main summit. Always the highest point, in the top-left of the map.</summary>
        public const float SummitU = 0.13f, SummitV = 0.14f;

        private const float ApronHeight = 0.70f, ApronRadius = 0.62f;
        private const float RidgeSoftness = 4f; // exponent of the soft maximum

        private const int Summit = 0, Skye = 1, SouthRidge = 2, Snowdon = 3, RamsHead = 4, Bear = 5, Sunrise = 6, Knoll = 7,
                          WestMountain = 8, NorthRange = 9, NorthWest = 10, EastHills = 11, SouthHills = 12;

        // Prominence is in units of total relief (summit - base). The summit's is computed so it tops out at 1.0.
        private static readonly Peak[] PeakLayout =
        {
            new Peak(SummitU, SummitV, 0f, 0.15f),
            new Peak(0.29f, 0.08f, 0.24f, 0.10f),    // second high peak beside the summit (Skye-like)
            new Peak(0.09f, 0.38f, 0.18f, 0.10f),    // south ridge high point
            new Peak(0.44f, 0.33f, 0.23f, 0.10f),    // mid-mountain peak (Snowdon-like)
            new Peak(0.28f, 0.57f, 0.22f, 0.10f),    // western sub-peak (Rams Head-like)
            new Peak(0.66f, 0.16f, 0.21f, 0.10f),    // northern sub-peak (Bear Mountain-like)
            new Peak(0.76f, 0.47f, 0.14f, 0.09f),    // eastern sub-peak (Sunrise-like)
            new Peak(0.80f, 0.79f, 0.04f, 0.08f),    // knoll above the base area: bunny terrain
            new Peak(-0.24f, 0.52f, 0.62f, 0.26f),   // neighbouring mountain to the west (Pico-like)
            new Peak(0.48f, -0.27f, 0.50f, 0.26f),   // the range continuing north
            new Peak(-0.24f, -0.20f, 0.60f, 0.28f),  // north-west massif
            new Peak(1.26f, 0.22f, 0.30f, 0.22f),    // eastern hills
            new Peak(0.24f, 1.28f, 0.26f, 0.22f),    // southern hills
        };

        private static readonly Ridge[] RidgeLayout =
        {
            new Ridge(Summit, Skye, 0.10f, 0.04f),
            new Ridge(Summit, SouthRidge, 0.10f, 0.05f),
            new Ridge(Summit, Snowdon, 0.11f, 0.08f),
            new Ridge(Snowdon, RamsHead, 0.09f, 0.08f),
            new Ridge(SouthRidge, RamsHead, 0.09f, 0.07f),
            new Ridge(Skye, Bear, 0.09f, 0.09f),
            new Ridge(Snowdon, Bear, 0.08f, 0.10f),
            new Ridge(Snowdon, Sunrise, 0.08f, 0.08f),
            new Ridge(Summit, NorthWest, 0.13f, 0.08f),
            new Ridge(Skye, NorthRange, 0.12f, 0.10f),
            new Ridge(SouthRidge, WestMountain, 0.12f, 0.25f), // deep gap between the two mountains
            new Ridge(Bear, EastHills, 0.10f, 0.08f),
            new Ridge(RamsHead, SouthHills, 0.10f, 0.08f),
        };

        private readonly Peak[] _peaks;
        private readonly ValueNoise _warp, _rolling, _drainage, _detail, _crest;

        public MountainShape(int seed)
        {
            var rng = new SimRandom((ulong)(uint)seed * 2654435761UL + 17);
            _peaks = new Peak[PeakLayout.Length];
            for (int i = 0; i < PeakLayout.Length; i++)
            {
                var p = PeakLayout[i];
                if (i != Summit)
                {
                    // Every seed gets a slightly different range; the summit stays put.
                    p.U += rng.Range(-0.025f, 0.025f);
                    p.V += rng.Range(-0.025f, 0.025f);
                    p.Prominence *= rng.Range(0.85f, 1.15f);
                    p.Radius *= rng.Range(0.9f, 1.15f);
                }
                _peaks[i] = p;
            }
            // The summit tops out at exactly 1.0 (before noise).
            _peaks[Summit].Prominence = 1f - Apron(SummitU, SummitV);

            _warp = new ValueNoise(seed * 71 + 5);
            _rolling = new ValueNoise(seed * 53 + 11);
            _drainage = new ValueNoise(seed * 31 + 7);
            _detail = new ValueNoise(seed);
            _crest = new ValueNoise(seed * 13 + 29);
        }

        public float Height01(float u, float v)
        {
            // Domain warp: ridges wander and nothing looks compass-drawn.
            float wu = u + _warp.Fbm(u * 2.5f, v * 2.5f, 3) * 0.045f;
            float wv = v + _warp.Fbm(u * 2.5f + 17f, v * 2.5f + 9f, 3) * 0.045f;

            float apron = Apron(wu, wv);

            // Soft maximum of prominences: landforms merge where they meet, with saddles between peaks.
            float sum = 0f;
            for (int i = 0; i < _peaks.Length; i++)
            {
                var p = _peaks[i];
                sum += Pow4(p.Prominence * Dome(Dist2(wu, wv, p.U, p.V) / (p.Radius * p.Radius)));
            }
            for (int i = 0; i < RidgeLayout.Length; i++)
            {
                var r = RidgeLayout[i];
                var a = _peaks[r.A];
                var b = _peaks[r.B];
                SegmentDistance(wu, wv, a.U, a.V, b.U, b.V, out float t, out float d2);
                // The crest runs between the two peaks' prominences, sags into a saddle and undulates a little.
                float crest = SimMath.Lerp(a.Prominence, b.Prominence, t) * 0.85f - r.Sag * 4f * t * (1f - t);
                crest += _crest.Sample(t * 6f + i * 13f, i * 7f) * 0.02f;
                if (crest > 0f) sum += Pow4(crest * Dome(d2 / (r.Width * r.Width)));
            }
            float relief = (float)Math.Pow(sum, 1.0 / RidgeSoftness);
            float massif = apron + relief;

            // Drainages: broad, shallow, smooth hollows (brooks and their valleys).
            float n = _drainage.Fbm(wu * 4.5f, wv * 4.5f, 3);
            float hollow = 1f - Math.Abs(n);
            hollow = hollow * hollow * hollow;
            float slopeZone = SimMath.SmoothStep(0.04f, 0.25f, massif) * SimMath.SmoothStep(1.05f, 0.75f, massif);
            float drainage = hollow * 0.035f * slopeZone;

            // Rolling shoulders, knobs and benches.
            float rolling = _rolling.Fbm(wu * 7f, wv * 7f, 3) * 0.025f * (0.4f + massif);

            // Fine texture so even flat ground isn't perfectly flat.
            float detail = _detail.Fbm(u * 30f, v * 30f, 3) * 0.006f;

            // The valley floor keeps dropping gently past the base.
            float valley = -0.05f * SimMath.SmoothStep(0.95f, 1.35f, (u + v) * 0.5f);

            return massif - drainage + rolling + detail + valley;
        }

        private static float Apron(float u, float v)
            => ApronHeight * Dome(Dist2(u, v, SummitU, SummitV) / (ApronRadius * ApronRadius));

        private static float Dist2(float u, float v, float pu, float pv)
        {
            float du = u - pu, dv = v - pv;
            return du * du + dv * dv;
        }

        private static void SegmentDistance(float u, float v, float au, float av, float bu, float bv, out float t, out float d2)
        {
            float abu = bu - au, abv = bv - av;
            float len2 = abu * abu + abv * abv;
            t = len2 < 1e-8f ? 0f : SimMath.Clamp01(((u - au) * abu + (v - av) * abv) / len2);
            d2 = Dist2(u, v, au + abu * t, av + abv * t);
        }

        private static float Pow4(float x)
        {
            float x2 = x * x;
            return x2 * x2;
        }

        /// <summary>Rounded profile: a broad worn top with long gentle skirts. r2 is squared distance in radii.</summary>
        private static float Dome(float r2)
        {
            float core = (float)Math.Exp(-r2);
            float skirt = 1f / (1f + r2 * 0.8f);
            return core * 0.7f + skirt * 0.3f;
        }
    }
}
