using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Runs
{
    /// <summary>
    /// Turns slopes into difficulty. Two outputs:
    ///  - a discrete <see cref="Difficulty"/> from the design-doc table (what goes on the sign),
    ///  - a continuous score (0 = gentlest green .. ~5 = sheer chute) that drives injuries and guest comfort.
    /// </summary>
    public static class RunRater
    {
        // Max-slope breakpoints (percent) mapped to scores 0,1,2,3,4,5.
        private static readonly float[] ScoreBreakpoints = { 0f, 18f, 35f, 55f, 70f, 100f };

        public static Difficulty Rate(SimConfig.RunSettings cfg, float avgSlope, float maxSlope)
        {
            int avgIndex = IndexFor(cfg.AvgSlopeLimits, avgSlope);
            int maxIndex = IndexFor(cfg.MaxSlopeLimits, maxSlope);
            return (Difficulty)System.Math.Max(avgIndex, maxIndex);
        }

        private static int IndexFor(float[] limits, float value)
        {
            for (int i = 0; i < limits.Length; i++)
                if (value <= limits[i]) return i;
            return limits.Length;
        }

        /// <summary>Continuous difficulty for a slope in percent.</summary>
        public static float SlopeScore(float slopePercent)
        {
            if (slopePercent <= 0f) return 0f;
            for (int i = 1; i < ScoreBreakpoints.Length; i++)
            {
                if (slopePercent <= ScoreBreakpoints[i])
                {
                    float t = (slopePercent - ScoreBreakpoints[i - 1]) / (ScoreBreakpoints[i] - ScoreBreakpoints[i - 1]);
                    return (i - 1) + t;
                }
            }
            return 5f + (slopePercent - 100f) / 30f;
        }

        /// <summary>How hard one tile is to ski right now: slope, plus surface and obstacles.</summary>
        public static float TileScore(WorldMap map, TerrainSettingsView scale, int x, int y)
        {
            float score = SlopeScore(TerrainQueries.SlopePercent(map, scale, x, y));
            var t = map.Locate(x, y);
            switch ((SnowQuality)t.Chunk.SnowQuality[t.Index])
            {
                case SnowQuality.Ice: score += 0.8f; break;
                case SnowQuality.Bare: score += 1.2f; break; // rocks and grass
                case SnowQuality.Crud: score += 0.3f; break;
                case SnowQuality.Slush: score += 0.2f; break;
            }
            int trees = t.Chunk.TreeDensity[t.Index];
            if (trees > 0) score += 0.4f * trees; // glades
            return score;
        }

        /// <summary>Comfort ceiling per skill level, on the same scale as <see cref="SlopeScore"/>.</summary>
        public static float SkillComfort(Guests.SkillLevel skill)
        {
            switch (skill)
            {
                case Guests.SkillLevel.FirstTimer: return 0.5f;
                case Guests.SkillLevel.Beginner: return 1.0f;
                case Guests.SkillLevel.Intermediate: return 2.0f;
                case Guests.SkillLevel.Advanced: return 3.0f;
                case Guests.SkillLevel.Expert: return 4.0f;
                default: return 5.0f;
            }
        }

        public static string Label(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.DoubleBlack: return "Double Black";
                case Difficulty.TripleBlack: return "Triple Black";
                default: return d.ToString();
            }
        }
    }
}
