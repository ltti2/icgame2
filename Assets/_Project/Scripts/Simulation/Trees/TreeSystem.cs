using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Trees
{
    /// <summary>
    /// Tree clearing. Trees are per-tile density, not entities. Clearing costs money but yields some lumber.
    /// Their gameplay effects (shelter, shade, glades) are read where they matter: weather, snow and skiing.
    /// </summary>
    public sealed class TreeSystem
    {
        private readonly GameSimulation _sim;

        public TreeSystem(GameSimulation sim) { _sim = sim; }

        public CommandResult QuoteClear(IReadOnlyList<Int2> tiles, out long lumber)
        {
            var map = _sim.Map;
            var terrain = _sim.Config.Terrain;
            long density = 0;
            lumber = 0;
            foreach (var t in tiles)
            {
                if (!map.InBounds(t)) continue;
                if (!map.IsOwned(t)) return CommandResult.Fail("You can only cut trees on land you own.");
                if (map.HasFlag(t.X, t.Y, TileFlags.Protected)) return CommandResult.Fail("These trees are protected.");
                density += map.GetTreeDensity(t.X, t.Y);
            }
            if (density == 0) return CommandResult.Fail("No trees here.");

            long cost = density * terrain.TreeClearCostPerDensity;
            lumber = density * terrain.LumberIncomePerDensity;
            if (!_sim.World.Economy.CanAfford(cost)) return CommandResult.Fail($"Not enough cash (need ${cost:N0}).", cost);
            return CommandResult.Success(cost);
        }

        public void Clear(IReadOnlyList<Int2> tiles, long cost, long lumber)
        {
            var map = _sim.Map;
            foreach (var t in tiles)
                if (map.InBounds(t) && map.GetTreeDensity(t.X, t.Y) > 0) map.SetTreeDensity(t.X, t.Y, 0);

            var economy = _sim.World.Economy;
            economy.AddExpense(LedgerCategory.TreeClearing, cost);
            economy.AddIncome(LedgerCategory.Lumber, lumber);
        }
    }
}
