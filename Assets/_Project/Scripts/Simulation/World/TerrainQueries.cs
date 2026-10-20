using System;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.World
{
    /// <summary>
    /// Derived terrain values (slope, aspect, fall line). Computed on demand from corner heights,
    /// never stored, so terraforming can never leave them stale.
    /// </summary>
    public static class TerrainQueries
    {
        /// <summary>
        /// Height gradient of a tile in steps per tile. Positive gx means the ground rises toward +x.
        /// </summary>
        public static void Gradient(WorldMap map, int x, int y, out float gx, out float gy)
        {
            map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
            gx = ((h10 + h11) - (h00 + h01)) * 0.5f;
            gy = ((h01 + h11) - (h00 + h10)) * 0.5f;
        }

        /// <summary>Grade of a tile in percent (rise / run * 100). 100% = 45 degrees.</summary>
        public static float SlopePercent(WorldMap map, TerrainSettingsView terrain, int x, int y)
        {
            Gradient(map, x, y, out float gx, out float gy);
            float stepsPerTile = (float)Math.Sqrt(gx * gx + gy * gy);
            return stepsPerTile * terrain.MetersPerStep / terrain.TileSizeMeters * 100f;
        }

        /// <summary>Signed grade along a direction (dx, dy) of unit-ish length. Negative = downhill.</summary>
        public static float DirectionalGradePercent(WorldMap map, TerrainSettingsView terrain, int x, int y, float dx, float dy)
        {
            Gradient(map, x, y, out float gx, out float gy);
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-5f) return 0f;
            float along = (gx * dx + gy * dy) / len;
            return along * terrain.MetersPerStep / terrain.TileSizeMeters * 100f;
        }

        /// <summary>Downhill unit direction (the fall line). Returns false on flat ground.</summary>
        public static bool FallLine(WorldMap map, int x, int y, out float dx, out float dy)
        {
            Gradient(map, x, y, out float gx, out float gy);
            float len = (float)Math.Sqrt(gx * gx + gy * gy);
            if (len < 0.01f)
            {
                dx = dy = 0f;
                return false;
            }
            dx = -gx / len;
            dy = -gy / len;
            return true;
        }

        /// <summary>
        /// How much a tile faces the (southern, winter) sun. The mountain faces east toward the base, so in grid
        /// space east is (+1,+1) and south is (-1,+1). Returns -1 (north-facing) .. +1 (south-facing), 0 for flat.
        /// </summary>
        public static float SunExposure(WorldMap map, int x, int y)
        {
            if (!FallLine(map, x, y, out float dx, out float dy)) return 0f;
            return (-dx + dy) * 0.70710678f; // downhill direction projected onto south
        }

        /// <summary>Tile-centre elevation in metres.</summary>
        public static float ElevationMeters(WorldMap map, TerrainSettingsView terrain, int x, int y)
            => map.TileCenterHeight(x, y) * terrain.MetersPerStep;
    }

    /// <summary>
    /// Thin value view over the terrain scale constants so hot-path queries don't chase config references.
    /// </summary>
    public readonly struct TerrainSettingsView
    {
        public readonly float TileSizeMeters;
        public readonly float MetersPerStep;

        public TerrainSettingsView(SimConfig.TerrainSettings s)
        {
            TileSizeMeters = s.TileSizeMeters;
            MetersPerStep = s.MetersPerHeightStep;
        }
    }
}
