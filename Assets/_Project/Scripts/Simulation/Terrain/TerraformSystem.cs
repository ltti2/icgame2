using System;
using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Terrain
{
    public enum TerraformMode : byte { Raise, Lower, Flatten }

    /// <summary>
    /// Raise / lower / flatten vertices with a square brush. Validation enforces ownership,
    /// occupied tiles and the max-step-per-edge rule that keeps the isometric art valid.
    /// </summary>
    public sealed class TerraformSystem
    {
        private readonly GameSimulation _sim;
        private readonly Dictionary<int, int> _planned = new Dictionary<int, int>(); // vertex key -> new height

        public TerraformSystem(GameSimulation sim) { _sim = sim; }

        private int VertexKey(int vx, int vy) => vy * (_sim.Map.Width + 1) + vx;

        /// <summary>Works out the new heights. Pure: only fills the internal plan.</summary>
        public CommandResult Plan(Int2 vertex, int radius, TerraformMode mode, int steps)
        {
            var map = _sim.Map;
            var terrain = _sim.Config.Terrain;
            _planned.Clear();
            if (!map.VertexInBounds(vertex.X, vertex.Y)) return CommandResult.Fail("Outside the map.");

            int centerHeight = map.GetVertexHeight(vertex.X, vertex.Y);
            long cost = 0;

            for (int vy = vertex.Y - radius; vy <= vertex.Y + radius; vy++)
            {
                for (int vx = vertex.X - radius; vx <= vertex.X + radius; vx++)
                {
                    if (!map.VertexInBounds(vx, vy)) continue;
                    int h = map.GetVertexHeight(vx, vy);
                    int target = mode == TerraformMode.Raise ? h + steps
                               : mode == TerraformMode.Lower ? h - steps
                               : centerHeight;
                    if (target == h) continue;

                    var check = CheckVertexEditable(vx, vy);
                    if (!check.Ok) return check;

                    _planned[VertexKey(vx, vy)] = target;
                    long perStep = TouchesRock(vx, vy) ? terrain.TerraformCostPerStepRock : terrain.TerraformCostPerStepDirt;
                    cost += Math.Abs(target - h) * perStep;
                }
            }

            if (_planned.Count == 0) return CommandResult.Fail("Nothing to change.");

            // Every edge touching a changed vertex must stay within the max step.
            foreach (var kv in _planned)
            {
                int vx = kv.Key % (map.Width + 1), vy = kv.Key / (map.Width + 1);
                if (!EdgeOk(vx, vy, kv.Value, 1, 0) || !EdgeOk(vx, vy, kv.Value, -1, 0) ||
                    !EdgeOk(vx, vy, kv.Value, 0, 1) || !EdgeOk(vx, vy, kv.Value, 0, -1))
                    return CommandResult.Fail(
                        $"Too steep: max {_sim.Units.Length(terrain.MaxStepPerEdge * terrain.MetersPerHeightStep)} of rise per tile. Use a bigger brush.", cost);
            }

            if (!_sim.World.Economy.CanAfford(cost)) return CommandResult.Fail($"Not enough cash (need ${cost:N0}).", cost);
            return CommandResult.Success(cost);
        }

        private bool EdgeOk(int vx, int vy, int newHeight, int ox, int oy)
        {
            var map = _sim.Map;
            int nx = vx + ox, ny = vy + oy;
            if (!map.VertexInBounds(nx, ny)) return true;
            int other = _planned.TryGetValue(VertexKey(nx, ny), out int planned) ? planned : map.GetVertexHeight(nx, ny);
            return Math.Abs(newHeight - other) <= _sim.Config.Terrain.MaxStepPerEdge;
        }

        private CommandResult CheckVertexEditable(int vx, int vy)
        {
            var map = _sim.Map;
            for (int ty = vy - 1; ty <= vy; ty++)
            {
                for (int tx = vx - 1; tx <= vx; tx++)
                {
                    if (!map.InBounds(tx, ty)) continue;
                    if (!map.IsOwned(tx, ty)) return CommandResult.Fail("You can only reshape land you own.");
                    if (map.GetOccupant(tx, ty) != 0) return CommandResult.Fail("Something is built here. Remove it first.");
                    if (map.IsPaved(tx, ty)) return CommandResult.Fail("Roads and parking lots can't be reshaped.");
                    if (map.HasFlag(tx, ty, TileFlags.Protected)) return CommandResult.Fail("This land is protected.");
                }
            }
            return CommandResult.Success();
        }

        private bool TouchesRock(int vx, int vy)
        {
            var map = _sim.Map;
            for (int ty = vy - 1; ty <= vy; ty++)
                for (int tx = vx - 1; tx <= vx; tx++)
                    if (map.InBounds(tx, ty) && map.GetTerrainType(tx, ty) == TerrainType.Rock) return true;
            return false;
        }

        /// <summary>Applies the last successful plan.</summary>
        public void ApplyPlan(long cost)
        {
            var map = _sim.Map;
            var touchedRuns = new HashSet<int>();

            foreach (var kv in _planned)
            {
                int vx = kv.Key % (map.Width + 1), vy = kv.Key / (map.Width + 1);
                map.SetVertexHeight(vx, vy, kv.Value);

                for (int ty = vy - 1; ty <= vy; ty++)
                {
                    for (int tx = vx - 1; tx <= vx; tx++)
                    {
                        if (!map.InBounds(tx, ty)) continue;
                        var t = map.Locate(tx, ty);
                        // Fresh earth: the snow is scraped off.
                        t.Chunk.SnowDepth[t.Index] = 0;
                        t.Chunk.SnowQuality[t.Index] = (byte)SnowQuality.Bare;
                        t.Chunk.TreeDensity[t.Index] = 0;
                        int run = t.Chunk.RunId[t.Index];
                        if (run != 0) touchedRuns.Add(run);
                    }
                }
            }

            _sim.World.Economy.AddExpense(LedgerCategory.Terraforming, cost);

            foreach (int runId in touchedRuns)
            {
                var run = _sim.World.GetRun(runId);
                if (run != null) _sim.RunSystem.Rebuild(run);
            }
            if (touchedRuns.Count > 0)
            {
                _sim.RunSystem.RefreshConnections();
                _sim.World.RunsVersion++;
            }
            _planned.Clear();
        }
    }
}
