using IceCoast.Simulation.Runs;

namespace IceCoast.Simulation.Guests
{
    /// <summary>
    /// Scored decision-making, kept separate from the state machine so it can be unit-tested and tuned
    /// on its own. Implements the design doc formula, extended with route planning:
    ///   score = skillMatch + snowQuality + novelty - queuePenalty - weatherPenalty - distance
    ///           + lookahead x (value of where this run leads)
    /// The "where it leads" part comes from <see cref="RouteValues"/>.
    /// </summary>
    public static class GuestChoices
    {
        public const float Prefers = 1.0f;
        public const float Unacceptable = -2.5f;

        // [skill, difficulty] preference weights. Rows: FirstTimer..Pro. Columns: Green..TripleBlack.
        private static readonly float[,] SkillMatch =
        {
            //  Green  Blue   Black  Double Triple
            {   1.0f,  0.15f, -2.5f, -2.5f, -2.5f }, // First-timer: blue only rarely
            {   1.0f,  0.4f,  -2.5f, -2.5f, -2.5f }, // Beginner
            {   0.4f,  1.0f,  0.4f,  -2.5f, -2.5f }, // Intermediate
            {   0.1f,  0.4f,  1.0f,  0.4f,  -2.5f }, // Advanced
            {   0.0f,  0.4f,  0.4f,  1.0f,  0.4f  }, // Expert
            {   0.0f,  0.4f,  0.4f,  0.4f,  1.0f  }, // Pro
        };

        public static float SkillMatchFor(SkillLevel skill, Difficulty rating) => SkillMatch[(int)skill, (int)rating];

        /// <summary>
        /// How much a guest wants to ski <paramref name="run"/> from where they are. Uses the *signposted*
        /// rating, because guests trust the sign until they have tried a run (AvoidMask then remembers bad
        /// experiences). <paramref name="remainingFraction"/> (0..1) scales the skiing part when joining partway
        /// down, and <paramref name="downstream"/> is the discounted value of where the run leads.
        /// </summary>
        public static float ScoreRun(GuestPool guests, int g, Run run, float remainingFraction, float downstream,
                                     float distanceTiles, float randomness, float noise)
        {
            if (!run.Open) return float.NegativeInfinity;

            float score = (SkillMatchFor(guests.Skill[g], run.DisplayedRating) + run.AvgSnowQuality * 0.6f) * remainingFraction;
            if (guests.LastRunId[g] != run.Id) score += 0.25f;              // novelty
            if ((guests.AvoidMask[g] & AvoidBit(run.Id)) != 0) score -= 3f; // got scared here before
            score += downstream;
            score -= distanceTiles * 0.02f;
            score += noise * randomness;
            return score;
        }

        /// <summary>How much a guest wants to ride a lift from where they stand.</summary>
        public static float ScoreLift(float routeValue, float waitMinutes, float walkTiles, float climbMeters, float randomness, float noise)
            => routeValue - waitMinutes * 0.03f - walkTiles * 0.015f - System.Math.Max(0f, climbMeters) * 0.01f + noise * randomness;

        public static ulong AvoidBit(int runId) => 1UL << (runId & 63);

        // [skill, SnowQuality] how much a skier likes the snow on a tile. Columns follow the SnowQuality enum:
        //  Bare   Powder Packed Groomed Slush  Ice    Crud
        private static readonly float[,] SnowTaste =
        {
            { -1.0f, -0.1f, 0.5f,  1.0f,  -0.3f, -1.0f, -0.4f }, // First-timer: corduroy please
            { -1.0f,  0.1f, 0.5f,  1.0f,  -0.3f, -1.0f, -0.3f }, // Beginner
            { -1.0f,  0.5f, 0.5f,  0.9f,  -0.2f, -0.8f,  0.0f }, // Intermediate
            { -1.0f,  0.8f, 0.4f,  0.7f,  -0.2f, -0.7f,  0.2f }, // Advanced
            { -1.0f,  1.0f, 0.3f,  0.5f,  -0.2f, -0.6f,  0.4f }, // Expert: hunts the stashes
            { -1.0f,  1.0f, 0.3f,  0.4f,  -0.2f, -0.5f,  0.5f }, // Pro
        };

        /// <summary>
        /// How much a skier of <paramref name="skill"/> likes snow of this quality (-1..1). Experts chase powder
        /// and leftover crud at the edges, beginners stick to fresh corduroy, and everyone avoids ice and rocks.
        /// <paramref name="recentSkiers"/> (tile traffic since the last snow update) makes tracked-out snow less
        /// attractive and spreads people out.
        /// </summary>
        public static float SnowAppeal(SkillLevel skill, World.SnowQuality quality, int recentSkiers)
        {
            float taste = SnowTaste[(int)skill, (int)quality];
            if (quality == World.SnowQuality.Powder) taste -= System.Math.Min(recentSkiers, 6) * 0.1f; // getting tracked out
            return taste - System.Math.Min(recentSkiers, 8) * 0.06f;                                   // crowding
        }

        /// <summary>How much fun a finished run was, from the guest's real experience (computed rating).</summary>
        public static float RunEnjoyment(SkillLevel skill, Run run, bool scared)
        {
            if (scared) return -8f;
            float match = SkillMatchFor(skill, run.ComputedRating);
            float fun = 2f + 4f * run.AvgSnowQuality + 3f * match;
            if (run.AverageWidth >= 3f) fun += 0.5f; // wide cruisers feel good
            return fun;
        }
    }
}
