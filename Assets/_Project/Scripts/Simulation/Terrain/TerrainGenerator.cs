using System;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Terrain
{
    /// <summary>
    /// Builds a new world: an untouched, densely forested Vermont mountain with several peaks
    /// (see <see cref="MountainShape"/>), the summit in the top-left of the map and the resort base
    /// in the bottom-right, plus a ring of scenery that continues the range past the map edge.
    ///
    /// Heights are generated over the extended area (map + scenery padding) in one pass, so the map
    /// edge and the scenery always match, then the max-step-per-edge rule is enforced over all of it.
    ///
    /// The base gets a flat parking lot and an access road graded in from beyond the map edge, laid out by
    /// <see cref="BaseLayout"/> (the parking system builds the lot's spaces from the same layout).
    /// </summary>
    public static class TerrainGenerator
    {
        /// <summary>Size of the cleared pad for the base village, and of the starter beginner clearing.</summary>
        private const float BaseInnerRadius = 12f, BaseOuterRadius = 20f;
        private const float StarterClearingLength = 55f, StarterClearingHalfWidth = 7f;

        public static Int2 EntranceFor(int mapWidth, int mapHeight) => new Int2(mapWidth - 28, mapHeight - 28);

        public static void Generate(GameSimulation sim)
        {
            var cfg = sim.Config;
            var world = sim.World;
            var map = sim.Map;
            int seed = cfg.Map.Seed;
            int pad = Math.Max(0, cfg.Map.SceneryPadding);

            world.MapSeed = seed;
            world.ResortEntrance = EntranceFor(map.Width, map.Height);
            var layout = sim.ParkingSystem.DefaultLayout();

            var heights = BuildExtendedHeights(cfg, map.Width, map.Height, pad, seed, world.ResortEntrance, layout);
            int evw = map.Width + 2 * pad + 1;

            for (int vy = 0; vy <= map.Height; vy++)
                for (int vx = 0; vx <= map.Width; vx++)
                    map.SetVertexHeight(vx, vy, heights[(vy + pad) * evw + vx + pad]);

            PaintMapTiles(sim, seed, world.ResortEntrance);
            layout.ApplySurfaces(map);
            world.Scenery = BuildScenery(sim, heights, pad, seed, layout);
        }

        /// <summary>
        /// Rebuilds only the scenery ring (used after loading a save). The playable map is untouched.
        /// </summary>
        public static void RegenerateScenery(GameSimulation sim, int seed)
        {
            var cfg = sim.Config;
            var map = sim.Map;
            int pad = Math.Max(0, cfg.Map.SceneryPadding);
            var layout = sim.ParkingSystem.DefaultLayout();
            var heights = BuildExtendedHeights(cfg, map.Width, map.Height, pad, seed, EntranceFor(map.Width, map.Height), layout);
            sim.World.Scenery = BuildScenery(sim, heights, pad, seed, layout);
        }

        // =====================================================================
        // Heights
        // =====================================================================

        private static int[] BuildExtendedHeights(SimConfig cfg, int width, int height, int pad, int seed, Int2 entrance, BaseLayout layout)
        {
            var shape = new MountainShape(seed);
            int ew = width + 2 * pad, eh = height + 2 * pad;
            int vw = ew + 1, vh = eh + 1;
            var heights = new int[vw * vh];

            float relief = cfg.Terrain.SummitElevationSteps - cfg.Terrain.BaseElevationSteps;

            for (int evy = 0; evy < vh; evy++)
            {
                float v = (evy - pad) / (float)height;
                for (int evx = 0; evx < vw; evx++)
                {
                    float u = (evx - pad) / (float)width;
                    float h01 = SoftCap(shape.Height01(u, v));
                    heights[evy * vw + evx] = cfg.Terrain.BaseElevationSteps + (int)Math.Round(relief * h01);
                }
            }

            FlattenBaseArea(heights, vw, vh, new Int2(entrance.X + pad, entrance.Y + pad), BaseInnerRadius, BaseOuterRadius);
            FlattenLot(heights, vw, vh, pad, layout, heights[(entrance.Y + pad) * vw + entrance.X + pad]);
            GradeRoad(heights, vw, vh, pad, layout, width);
            EnforceMaxSlope(heights, vw, vh, cfg.Terrain.MaxStepPerEdge);
            return heights;
        }

        /// <summary>Levels the parking lot (and a tile around it) to the base height, feathering into the slope.</summary>
        private static void FlattenLot(int[] heights, int vw, int vh, int pad, BaseLayout layout, int target)
        {
            const int feather = 3;
            for (int vy = layout.LotY - feather; vy <= layout.LotY + layout.LotHeight + feather; vy++)
                for (int vx = layout.LotX - feather; vx <= layout.LotX + layout.LotWidth + feather; vx++)
                {
                    int ex = vx + pad, ey = vy + pad;
                    if (ex < 0 || ey < 0 || ex >= vw || ey >= vh) continue;
                    int dx = Math.Max(Math.Max(layout.LotX - vx, vx - (layout.LotX + layout.LotWidth)), 0);
                    int dy = Math.Max(Math.Max(layout.LotY - vy, vy - (layout.LotY + layout.LotHeight)), 0);
                    float d = Math.Max(dx, dy);
                    float t = 1f - SimMath.SmoothStep(0f, feather + 1f, d);
                    int i = ey * vw + ex;
                    heights[i] = (int)Math.Round(SimMath.Lerp(heights[i], target, t));
                }
        }

        /// <summary>
        /// Grades the access road: a smoothed version of the ground along it, no steeper than one step per tile
        /// (10%), starting level with the lot. Its shoulders are blended toward it so the road sits in a cut
        /// rather than on a ridge.
        /// </summary>
        private static void GradeRoad(int[] heights, int vw, int vh, int pad, BaseLayout layout, int mapWidth)
        {
            int roadY = layout.RoadY;
            int x0 = layout.RoadStartX;
            int x1 = layout.Road.Count > 0 ? layout.Road[0].X + 1 : mapWidth;
            int n = x1 - x0 + 1;
            if (n <= 1 || roadY + 1 + pad >= vh || roadY - 1 + pad < 0) return;

            int H(int vx, int vy)
            {
                int ex = SimMath.Clamp(vx + pad, 0, vw - 1), ey = SimMath.Clamp(vy + pad, 0, vh - 1);
                return heights[ey * vw + ex];
            }

            var natural = new float[n];
            for (int i = 0; i < n; i++) natural[i] = (H(x0 + i, roadY) + H(x0 + i, roadY + 1)) * 0.5f;

            const int window = 6;
            var profile = new float[n];
            float start = natural[0];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = Math.Max(0, i - window); k <= Math.Min(n - 1, i + window); k++) { sum += natural[k]; count++; }
                float smooth = sum / count;
                profile[i] = SimMath.Lerp(start, smooth, SimMath.SmoothStep(0f, 6f, i));
            }
            for (int i = 1; i < n; i++) profile[i] = SimMath.Clamp(profile[i], profile[i - 1] - 1f, profile[i - 1] + 1f);

            for (int i = 0; i < n; i++)
            {
                int ex = x0 + i + pad;
                if (ex < 0 || ex >= vw) continue;
                int road = (int)Math.Round(profile[i]);
                for (int vy = roadY - 1; vy <= roadY + 2; vy++)
                {
                    int ey = vy + pad;
                    if (ey < 0 || ey >= vh) continue;
                    int idx = ey * vw + ex;
                    bool onRoad = vy == roadY || vy == roadY + 1;
                    heights[idx] = onRoad ? road : (int)Math.Round(SimMath.Lerp(heights[idx], road, 0.6f));
                }
            }
        }

        /// <summary>
        /// Rounds off anything above the summit height instead of clipping it, so the summit stays a dome
        /// (a hard clip would leave a flat mesa).
        /// </summary>
        private static float SoftCap(float h01)
        {
            const float knee = 0.94f;
            if (h01 <= knee) return h01;
            float over = h01 - knee;
            return knee + 0.06f * (1f - (float)Math.Exp(-over / 0.06f));
        }

        /// <summary>Blends terrain around the entrance toward a flat pad for the base village.</summary>
        private static void FlattenBaseArea(int[] heights, int vw, int vh, Int2 center, float inner, float outer)
        {
            int target = heights[center.Y * vw + center.X];
            int r = (int)Math.Ceiling(outer);
            for (int vy = Math.Max(0, center.Y - r); vy < Math.Min(vh, center.Y + r); vy++)
            {
                for (int vx = Math.Max(0, center.X - r); vx < Math.Min(vw, center.X + r); vx++)
                {
                    float dx = vx - center.X, dy = vy - center.Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float t = 1f - SimMath.SmoothStep(inner, outer, d);
                    int i = vy * vw + vx;
                    heights[i] = (int)Math.Round(SimMath.Lerp(heights[i], target, t));
                }
            }
        }

        /// <summary>Lowers vertices until no edge exceeds the max step. Converges because it only ever lowers.</summary>
        public static void EnforceMaxSlope(int[] heights, int vw, int vh, int maxStep)
        {
            for (int pass = 0; pass < 64; pass++)
            {
                bool changed = false;
                for (int vy = 0; vy < vh; vy++)
                {
                    for (int vx = 0; vx < vw; vx++)
                    {
                        int i = vy * vw + vx;
                        int limit = int.MaxValue;
                        if (vx > 0) limit = Math.Min(limit, heights[i - 1] + maxStep);
                        if (vx < vw - 1) limit = Math.Min(limit, heights[i + 1] + maxStep);
                        if (vy > 0) limit = Math.Min(limit, heights[i - vw] + maxStep);
                        if (vy < vh - 1) limit = Math.Min(limit, heights[i + vw] + maxStep);
                        if (heights[i] > limit)
                        {
                            heights[i] = limit;
                            changed = true;
                        }
                    }
                }
                if (!changed) return;
            }
        }

        // =====================================================================
        // Surface: terrain type, forest, snow
        // =====================================================================

        private static void PaintMapTiles(GameSimulation sim, int seed, Int2 entrance)
        {
            var cfg = sim.Config;
            var map = sim.Map;
            var scale = sim.TerrainScale;
            var forest = new ValueNoise(seed * 17 + 3);
            float relief = cfg.Terrain.SummitElevationSteps - cfg.Terrain.BaseElevationSteps;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var t = map.Locate(x, y);
                    var chunk = t.Chunk;
                    int i = t.Index;

                    float slope = TerrainQueries.SlopePercent(map, scale, x, y);
                    float h = map.TileCenterHeight(x, y);
                    float elev01 = SimMath.Clamp01((h - cfg.Terrain.BaseElevationSteps) / relief);

                    var type = TypeFor(slope);
                    int density = ForestDensity(cfg, forest, seed, x, y, h, slope, type);

                    // The base village pad and the starter beginner slope are already cleared.
                    float ex = x - entrance.X, ey = y - entrance.Y;
                    if ((float)Math.Sqrt(ex * ex + ey * ey) < 9f)
                    {
                        density = 0;
                        type = TerrainType.Dirt;
                        chunk.Flags[i] |= (byte)TileFlags.BaseArea;
                    }
                    float along = -(ex + ey) * 0.7071f;   // uphill, toward the summit
                    float across = (ex - ey) * 0.7071f;
                    if (along > 0f && along < StarterClearingLength && Math.Abs(across) < StarterClearingHalfWidth) density = 0;

                    chunk.TerrainType[i] = (byte)type;
                    chunk.TreeDensity[i] = (byte)density;

                    // Early-December snowpack: deeper up high, a little patchy, thin on ledges.
                    float depth = SimMath.Lerp(cfg.Snow.InitialBaseDepthCm, cfg.Snow.InitialSummitDepthCm, elev01)
                                  * (0.85f + 0.3f * SimRandom.Hash01(x, y, seed));
                    if (type == TerrainType.Rock) depth *= 0.3f;
                    chunk.SnowDepth[i] = (ushort)depth;
                    chunk.SnowQuality[i] = (byte)(depth < cfg.Snow.BareDepthCm ? SnowQuality.Bare
                                                : elev01 > 0.45f ? SnowQuality.Powder : SnowQuality.Packed);
                }
            }

            foreach (var chunk in map.Chunks) chunk.MarkShapeChanged();
        }

        private static Scenery BuildScenery(GameSimulation sim, int[] heights, int pad, int seed, BaseLayout layout)
        {
            var cfg = sim.Config;
            var map = sim.Map;
            var scenery = new Scenery(map.Width, map.Height, pad);
            Array.Copy(Array.ConvertAll(heights, h => (short)h), scenery.VertexHeight, heights.Length);

            var forest = new ValueNoise(seed * 17 + 3);
            float metersPerStep = cfg.Terrain.MetersPerHeightStep, tile = cfg.Terrain.TileSizeMeters;
            int vw = scenery.Width + 1;
            int roadEnd = layout.Road.Count > 0 ? layout.Road[0].X + 1 : 0;

            for (int y = scenery.MinY; y < scenery.MaxY; y++)
            {
                for (int x = scenery.MinX; x < scenery.MaxX; x++)
                {
                    if (!scenery.IsSceneryTile(x, y)) continue;
                    int i0 = (y + pad) * vw + (x + pad);
                    int h00 = heights[i0], h10 = heights[i0 + 1], h01 = heights[i0 + vw], h11 = heights[i0 + vw + 1];
                    float gx = ((h10 + h11) - (h00 + h01)) * 0.5f, gy = ((h01 + h11) - (h00 + h10)) * 0.5f;
                    float slope = (float)Math.Sqrt(gx * gx + gy * gy) * metersPerStep / tile * 100f;
                    float h = (h00 + h10 + h01 + h11) * 0.25f;

                    var type = TypeFor(slope);
                    int ti = scenery.TileIndex(x, y);
                    scenery.TerrainTypes[ti] = (byte)type;
                    scenery.TreeDensity[ti] = (byte)ForestDensity(cfg, forest, seed, x, y, h, slope, type);

                    // The access road continues out through the scenery.
                    if (layout.OnRoad(x, y, roadEnd)) scenery.Surface[ti] = (byte)SurfaceType.Road;
                    if (layout.OnRoadShoulder(x, y, roadEnd)) scenery.TreeDensity[ti] = 0;
                }
            }
            return scenery;
        }

        private static TerrainType TypeFor(float slopePercent) => slopePercent > 70f ? TerrainType.Rock : TerrainType.Grass;

        /// <summary>
        /// Vermont forest: nearly everything is wooded, right up to the summit. Openings are rare (wetlands,
        /// ledges, old blowdowns). Above <c>StuntedForestSteps</c> the spruce-fir gets shorter and a bit thinner,
        /// and steep ledges carry only scattered trees.
        /// </summary>
        public static int ForestDensity(SimConfig cfg, ValueNoise forest, int seed, int x, int y, float heightSteps, float slopePercent, TerrainType type)
        {
            if (type == TerrainType.Rock) return SimRandom.Hash01(x, y, seed + 41) < 0.3f ? 1 : 0;

            float openings = forest.Fbm(x / 70f, y / 70f, 3) + forest.Sample(x / 9f, y / 9f) * 0.12f;
            float threshold = SimMath.Lerp(0.6f, -0.6f, cfg.Terrain.ForestCover);
            if (openings < threshold) return 0;
            if (openings < threshold + 0.07f) return 1 + (SimRandom.Hash01(x, y, seed + 7) < 0.5f ? 1 : 0); // forest edge

            int density = 3 + (SimRandom.Hash01(x, y, seed + 99) < 0.6f ? 1 : 0);
            if (slopePercent > 50f) density--;
            if (heightSteps > cfg.Terrain.StuntedForestSteps) density = Math.Max(2, density - 1);
            return SimMath.Clamp(density, 0, 4);
        }
    }
}
