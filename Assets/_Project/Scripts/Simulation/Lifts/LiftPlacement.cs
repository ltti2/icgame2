using System;
using System.Collections.Generic;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Lifts
{
    /// <summary>Everything the UI needs to preview a lift before building it.</summary>
    public sealed class LiftQuote
    {
        public bool Valid;
        public string Error;
        public long Cost;
        public long TreeClearingCost;
        public float LengthTiles;
        public int RiseSteps;
        public float GradePercent;
        public readonly List<Int2> LineTiles = new List<Int2>();
        public readonly List<Int2> Towers = new List<Int2>();
    }

    /// <summary>
    /// Pure validation and quoting for lift placement. Shared by the build command (authoritative check)
    /// and the build tool (live preview), so the preview can never disagree with the result.
    /// </summary>
    public static class LiftPlacement
    {
        public static LiftQuote Quote(GameSimulation sim, LiftType type, Int2 bottom, Int2 top)
        {
            var q = new LiftQuote();
            var map = sim.Map;
            var def = sim.Config.GetLift(type);
            var economy = sim.World.Economy;

            if (!map.InBounds(bottom) || !map.InBounds(top)) return Fail(q, "Outside the map.");
            if (bottom == top) return Fail(q, "Pick a different top station.");

            RasterizeLine(bottom, top, q.LineTiles);
            q.LengthTiles = bottom.Distance(top);
            q.RiseSteps = (int)Math.Round(map.TileCenterHeight(top.X, top.Y) - map.TileCenterHeight(bottom.X, bottom.Y));
            float riseMeters = q.RiseSteps * sim.TerrainScale.MetersPerStep;
            q.GradePercent = riseMeters / (q.LengthTiles * sim.TerrainScale.TileSizeMeters) * 100f;
            ComputeTowers(def, q);

            // Cost first, so even a rejected quote shows a price.
            long treeDensity = 0;
            foreach (var t in q.LineTiles) treeDensity += map.GetTreeDensity(t.X, t.Y);
            q.TreeClearingCost = treeDensity * sim.Config.Terrain.TreeClearCostPerDensity;
            q.Cost = def.BaseCost + (long)(def.CostPerTile * q.LengthTiles) + q.TreeClearingCost;

            if (!sim.Config.SandboxMode && economy.Reputation < def.RequiredReputation)
                return Fail(q, $"{def.DisplayName} unlocks at reputation {def.RequiredReputation:0}.");
            float tileMeters = sim.TerrainScale.TileSizeMeters;
            if (q.LengthTiles < def.MinLengthTiles) return Fail(q, $"Too short (min {sim.Units.Length(def.MinLengthTiles * tileMeters)}).");
            if (q.LengthTiles > def.MaxLengthTiles) return Fail(q, $"Too long (max {sim.Units.Length(def.MaxLengthTiles * tileMeters)}).");
            if (q.RiseSteps < 1) return Fail(q, "The top station must be higher than the bottom.");
            if (q.GradePercent > def.MaxGradePercent) return Fail(q, $"Too steep for a {def.DisplayName} ({q.GradePercent:0}% > {def.MaxGradePercent:0}%).");

            foreach (var t in q.LineTiles)
            {
                if (!map.IsOwned(t)) return Fail(q, "The whole lift line must be on land you own.");
                if (map.HasFlag(t.X, t.Y, TileFlags.Blocked)) return Fail(q, "The line crosses blocked terrain.");
            }

            if (map.IsPaved(bottom.X, bottom.Y) || map.IsPaved(top.X, top.Y)) return Fail(q, "Stations can't go on the road or in the parking lot.");
            foreach (var tower in q.Towers)
                if (map.IsPaved(tower.X, tower.Y)) return Fail(q, "A tower would stand on the road or in the parking lot.");
            if (map.GetOccupant(bottom.X, bottom.Y) != 0) return Fail(q, "Bottom station tile is occupied.");
            if (map.GetOccupant(top.X, top.Y) != 0) return Fail(q, "Top station tile is occupied.");
            foreach (var tower in q.Towers)
                if (map.GetOccupant(tower.X, tower.Y) != 0) return Fail(q, $"Tower site {tower} is occupied.");

            if (!economy.CanAfford(q.Cost)) return Fail(q, $"Not enough cash (need ${q.Cost:N0}).");

            q.Valid = true;
            return q;
        }

        private static LiftQuote Fail(LiftQuote q, string error)
        {
            q.Valid = false;
            q.Error = error;
            return q;
        }

        private static void ComputeTowers(LiftDefinition def, LiftQuote q)
        {
            if (def.TowerSpacingTiles <= 0 || q.LineTiles.Count < 3) return;
            // Skip the station tiles at both ends.
            for (int i = def.TowerSpacingTiles; i < q.LineTiles.Count - 1; i += def.TowerSpacingTiles)
                q.Towers.Add(q.LineTiles[i]);
        }

        /// <summary>Bresenham line, inclusive of both endpoints.</summary>
        public static void RasterizeLine(Int2 a, Int2 b, List<Int2> into)
        {
            into.Clear();
            int x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                into.Add(new Int2(x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
