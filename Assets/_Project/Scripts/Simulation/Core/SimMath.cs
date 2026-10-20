using System;

namespace IceCoast.Simulation.Core
{
    /// <summary>Small math helpers so the simulation does not need UnityEngine.Mathf.</summary>
    public static class SimMath
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Pow(float v, float p) => (float)Math.Pow(v, p);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        public static byte ToByte(float v) => (byte)Clamp(RoundToInt(v), 0, 255);
        public static byte AddClamped(byte value, int delta) => (byte)Clamp(value + delta, 0, 255);
    }

    /// <summary>
    /// Seeded 2D value noise + fBm. Engine-independent replacement for Mathf.PerlinNoise,
    /// used only during world generation.
    /// </summary>
    public sealed class ValueNoise
    {
        private readonly int _seed;

        public ValueNoise(int seed) { _seed = seed; }

        private float Lattice(int x, int y) => SimRandom.Hash01(x, y, _seed) * 2f - 1f;

        /// <summary>Smooth noise in [-1, 1].</summary>
        public float Sample(float x, float y)
        {
            int x0 = SimMath.FloorToInt(x), y0 = SimMath.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Lattice(x0, y0), b = Lattice(x0 + 1, y0);
            float c = Lattice(x0, y0 + 1), d = Lattice(x0 + 1, y0 + 1);
            return SimMath.Lerp(SimMath.Lerp(a, b, tx), SimMath.Lerp(c, d, tx), ty);
        }

        /// <summary>Fractal sum, roughly in [-1, 1].</summary>
        public float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x, y) * amp;
                norm += amp;
                x *= lacunarity;
                y *= lacunarity;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Ridged noise in [0, 1]; sharp crests, good for ridgelines and gullies.</summary>
        public float Ridged(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Math.Abs(Sample(x, y));
                sum += n * n * amp;
                norm += amp;
                x *= 2f;
                y *= 2f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
