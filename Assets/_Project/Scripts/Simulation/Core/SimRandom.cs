using System;

namespace IceCoast.Simulation.Core
{
    /// <summary>
    /// Deterministic, serializable PRNG (xorshift64*). All randomness in the simulation must come
    /// from here (never System.Random or UnityEngine.Random) so that a save + seed replays exactly.
    /// </summary>
    public sealed class SimRandom
    {
        public ulong State;

        public SimRandom(ulong seed)
        {
            // SplitMix64 scramble so small seeds (1, 2, 3...) still produce good streams.
            State = SplitMix(seed == 0 ? 0x9E3779B97F4A7C15UL : seed);
            if (State == 0) State = 1;
        }

        public static ulong SplitMix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public ulong NextULong()
        {
            State ^= State >> 12;
            State ^= State << 25;
            State ^= State >> 27;
            return State * 0x2545F4914F6CDD1DUL;
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Picks an index from a weight table. Weights need not sum to 1.</summary>
        public int PickWeighted(float[] weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Math.Max(0f, weights[i]);
            float r = NextFloat() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                r -= Math.Max(0f, weights[i]);
                if (r < 0f) return i;
            }
            return weights.Length - 1;
        }

        /// <summary>
        /// Stateless hash for procedural detail that must not consume the shared stream
        /// (e.g. tree placement jitter, render-only variation).
        /// </summary>
        public static uint Hash(int x, int y, int salt = 0)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)salt * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, int salt = 0) => (Hash(x, y, salt) >> 8) * (1f / 16777216f);
    }
}
