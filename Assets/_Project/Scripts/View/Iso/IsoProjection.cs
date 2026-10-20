using IceCoast.Simulation.World;
using UnityEngine;

namespace IceCoast.View.Iso
{
    /// <summary>
    /// The one place that knows how grid space maps to Unity world space.
    ///
    /// Grid: x to the screen's lower-right, y to the lower-left, h up. Tile (0,0) is at the top of the
    /// screen (the summit corner); the resort base is at the bottom.
    ///
    /// Art scale: tiles are 64x32 px and 1 world unit = 32 px, so a tile is 2 x 1 units. One height step
    /// (1 m) is 4 px. Z carries depth: everything at a larger grid diagonal (x + y) is nearer the camera,
    /// which gives correct occlusion for a heightfield seen from this angle, using the depth buffer.
    /// </summary>
    public static class IsoProjection
    {
        public const float PixelsPerUnit = 32f;
        public const float TileWidthPx = 64f;
        public const float TileHeightPx = 32f;
        public const float HeightStepPx = 4f;

        public const float HalfWidth = TileWidthPx * 0.5f / PixelsPerUnit;   // 1.0
        public const float HalfHeight = TileHeightPx * 0.5f / PixelsPerUnit; // 0.5
        public const float StepHeight = HeightStepPx / PixelsPerUnit;        // 0.125

        /// <summary>World Z per unit of grid diagonal. Small enough to stay inside the camera clip range.</summary>
        public const float DepthPerDiagonal = 0.01f;

        public static float Depth(float gx, float gy) => -(gx + gy) * DepthPerDiagonal;

        /// <summary>Grid position (continuous, tile units) + height (steps) to world space.</summary>
        public static Vector3 ToWorld(float gx, float gy, float h)
            => new Vector3((gx - gy) * HalfWidth, -(gx + gy) * HalfHeight + h * StepHeight, Depth(gx, gy));

        /// <summary>World position at the centre of a tile, on the ground.</summary>
        public static Vector3 TileCenter(WorldMap map, int x, int y)
            => ToWorld(x + 0.5f, y + 0.5f, map.TileCenterHeight(x, y));

        /// <summary>Ground point under a continuous grid position.</summary>
        public static Vector3 GroundPoint(WorldMap map, float gx, float gy)
            => ToWorld(gx, gy, map.SampleHeight(gx, gy));

        /// <summary>Inverse projection, assuming the point lies at height h.</summary>
        public static Vector2 WorldToGrid(Vector2 world, float h)
        {
            float a = world.x / HalfWidth;                            // gx - gy
            float b = -(world.y - h * StepHeight) / HalfHeight;       // gx + gy
            return new Vector2((a + b) * 0.5f, (b - a) * 0.5f);
        }

        /// <summary>
        /// Which diagonal a tile quad is split along. Must match between the mesh builder and the picker.
        /// Splitting along the flatter diagonal avoids ugly folds on saddle-shaped tiles.
        /// </summary>
        public static bool SplitAlongMainDiagonal(int h00, int h10, int h01, int h11)
            => Mathf.Abs(h00 - h11) <= Mathf.Abs(h10 - h01);
    }
}
