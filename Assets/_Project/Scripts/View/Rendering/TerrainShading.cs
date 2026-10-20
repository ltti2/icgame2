using IceCoast.Simulation;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Terrain lighting maths shared by the map, the scenery and the day-light controller.
    ///
    /// Compass: the mountain faces EAST, toward the resort base and the viewer. In grid space
    /// (x, y = grid axes, z = up) that means:
    ///   east  = (+1, +1) / sqrt2   (down the mountain, toward the bottom of the screen)
    ///   north = (+1, -1) / sqrt2   (screen right)
    ///   south = (-1, +1) / sqrt2   (screen left)
    ///   west  = (-1, -1) / sqrt2   (up the mountain, behind the summit)
    /// So the morning sun lights the face you look at, noon light comes from the left, and the afternoon
    /// sun sets behind the summit, throwing the east face into shade.
    /// </summary>
    public static class TerrainShading
    {
        /// <summary>Multiplier on real slope when building normals. Higher = more dramatic relief.</summary>
        public const float ReliefExaggeration = 2.5f;

        public static readonly Vector3 East = new Vector3(1f, 1f, 0f).normalized;
        public static readonly Vector3 North = new Vector3(1f, -1f, 0f).normalized;

        /// <summary>Grid-space surface normal of a tile from its height gradient (steps per tile).</summary>
        public static Vector3 Normal(GameSimulation sim, float gx, float gy)
        {
            float s = sim.TerrainScale.MetersPerStep / sim.TerrainScale.TileSizeMeters * ReliefExaggeration;
            return new Vector3(-gx * s, -gy * s, 1f).normalized;
        }

        public static Vector3 TileNormal(GameSimulation sim, int h00, int h10, int h01, int h11)
        {
            float gx = ((h10 + h11) - (h00 + h01)) * 0.5f;
            float gy = ((h01 + h11) - (h00 + h10)) * 0.5f;
            return Normal(sim, gx, gy);
        }

        /// <summary>
        /// Grid-space direction toward the sun, for azimuth (degrees clockwise from north) and elevation.
        /// </summary>
        public static Vector3 SunDirection(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad, el = elevationDeg * Mathf.Deg2Rad;
            Vector3 horizontal = North * Mathf.Cos(az) + East * Mathf.Sin(az);
            return (horizontal * Mathf.Cos(el) + Vector3.forward * Mathf.Sin(el)).normalized; // forward = grid "up" (z)
        }
    }
}
