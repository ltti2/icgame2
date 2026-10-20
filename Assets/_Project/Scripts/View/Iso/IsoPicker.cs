using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using UnityEngine;

namespace IceCoast.View.Iso
{
    /// <summary>
    /// Finds the tile under a world-space point on a terrain with heights.
    ///
    /// With heights, many tiles can project onto the same pixel; the visible one is the nearest
    /// (largest grid diagonal). So we walk candidate tiles front-to-back in the narrow screen column
    /// around the point and return the first whose projected quad contains it.
    /// </summary>
    public static class IsoPicker
    {
        public static bool PickTile(WorldMap map, Vector2 world, int maxHeightSteps, out Int2 tile)
        {
            tile = Int2.Invalid;
            float k = world.x / IsoProjection.HalfWidth; // x - y
            int kMin = Mathf.FloorToInt(k) - 1, kMax = Mathf.CeilToInt(k) + 1;

            // Bound the diagonal range using the min/max possible heights.
            float bLow = -world.y / IsoProjection.HalfHeight;
            float bHigh = -(world.y - maxHeightSteps * IsoProjection.StepHeight) / IsoProjection.HalfHeight;
            int dMax = Mathf.Min(map.Width + map.Height - 2, Mathf.CeilToInt(bHigh) + 2);
            int dMin = Mathf.Max(0, Mathf.FloorToInt(bLow) - 3);

            for (int d = dMax; d >= dMin; d--)
            {
                for (int kk = kMin; kk <= kMax; kk++)
                {
                    if (((d + kk) & 1) != 0) continue; // x and y must be integers
                    int x = (d + kk) / 2, y = (d - kk) / 2;
                    if (!map.InBounds(x, y)) continue;
                    if (TileContains(map, x, y, world))
                    {
                        tile = new Int2(x, y);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Closest grid corner of the picked tile, measured on screen. Used by terraforming.</summary>
        public static Int2 NearestVertex(WorldMap map, Int2 tile, Vector2 world)
        {
            Int2 best = tile;
            float bestD = float.MaxValue;
            for (int oy = 0; oy <= 1; oy++)
            {
                for (int ox = 0; ox <= 1; ox++)
                {
                    int vx = tile.X + ox, vy = tile.Y + oy;
                    Vector2 p = IsoProjection.ToWorld(vx, vy, map.GetVertexHeight(vx, vy));
                    float d = (p - world).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = new Int2(vx, vy);
                    }
                }
            }
            return best;
        }

        public static bool TileContains(WorldMap map, int x, int y, Vector2 p)
        {
            map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
            Vector2 a = IsoProjection.ToWorld(x, y, h00);
            Vector2 b = IsoProjection.ToWorld(x + 1, y, h10);
            Vector2 c = IsoProjection.ToWorld(x + 1, y + 1, h11);
            Vector2 d = IsoProjection.ToWorld(x, y + 1, h01);

            if (IsoProjection.SplitAlongMainDiagonal(h00, h10, h01, h11))
                return InTriangle(p, a, b, c) || InTriangle(p, a, c, d);
            return InTriangle(p, a, b, d) || InTriangle(p, b, c, d);
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
