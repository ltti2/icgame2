using System;
using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Walking
{
    /// <summary>
    /// One connected patch of walking area (parking lots count: guests walk across them). Derived from the
    /// map by <see cref="WalkingAreaSystem"/>; never saved.
    /// </summary>
    public sealed class WalkZone
    {
        public int Id;
        public int TileCount;
        /// <summary>Includes the base village or the parking lot: guests can go home from here.</summary>
        public bool IsBase;
        /// <summary>Lifts whose bottom station opens onto this zone: guests here can walk to any of them.</summary>
        public readonly List<int> LiftBottoms = new List<int>();
        /// <summary>Lifts whose top station opens onto this zone (a mid-mountain deck).</summary>
        public readonly List<int> LiftTops = new List<int>();
    }

    /// <summary>
    /// Walking areas: packed-snow plazas where guests take their skis off and walk anywhere, like the base
    /// area. They are painted and erased with a brush (PaintWalkingAreaCommand), never overlap trails, roads
    /// or parking, and are the only place buildings may stand (<see cref="Buildings.BuildingRules"/>).
    ///
    /// What they do for guests: a connected walking area is one zone. Every lift whose bottom station opens
    /// onto a zone is in walking reach of everyone in it, however big the zone is; a trail that ends on a zone
    /// leads to all of those lifts; a lift whose top opens onto a zone connects to them too. A zone that
    /// includes the base village or the parking lot counts as the base.
    /// </summary>
    public sealed class WalkingAreaSystem
    {
        private readonly GameSimulation _sim;
        private int[] _zoneOf = Array.Empty<int>();
        private readonly List<WalkZone> _zones = new List<WalkZone>();
        private readonly Queue<int> _queue = new Queue<int>();

        public WalkingAreaSystem(GameSimulation sim) { _sim = sim; }

        /// <summary>Bumped when zones or their lift links change. Route caches key off it.</summary>
        public int Version { get; private set; }

        public IReadOnlyList<WalkZone> Zones => _zones;

        // =====================================================================
        // Queries
        // =====================================================================

        public WalkZone GetZone(int id) => id >= 1 && id <= _zones.Count ? _zones[id - 1] : null;

        /// <summary>Zone the tile belongs to, or null.</summary>
        public WalkZone ZoneAt(int x, int y)
        {
            var map = _sim.Map;
            if (!map.InBounds(x, y) || _zoneOf.Length == 0) return null;
            return GetZone(_zoneOf[map.TileKey(x, y)]);
        }

        /// <summary>Nearest zone with a tile within <paramref name="radius"/> of a tile (ties: lowest zone id), or null.</summary>
        public WalkZone ZoneNear(Int2 tile, float radius)
        {
            var map = _sim.Map;
            if (_zoneOf.Length == 0) return null;
            int r = (int)Math.Ceiling(radius);
            float best = radius * radius + 1e-3f;
            int bestId = 0;
            for (int y = tile.Y - r; y <= tile.Y + r; y++)
                for (int x = tile.X - r; x <= tile.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int id = _zoneOf[map.TileKey(x, y)];
                    if (id == 0) continue;
                    float dx = x - tile.X, dy = y - tile.Y;
                    float d = dx * dx + dy * dy;
                    if (d < best || (d == best && id < bestId)) { best = d; bestId = id; }
                }
            return GetZone(bestId);
        }

        /// <summary>Standing on (or right beside) a base zone.</summary>
        public bool IsNearBaseZone(Int2 tile)
        {
            var z = ZoneNear(tile, 1.5f);
            return z != null && z.IsBase;
        }

        // =====================================================================
        // Painting
        // =====================================================================

        public CommandResult QuotePaint(IReadOnlyList<Int2> tiles, bool erase, out int changed)
        {
            changed = 0;
            var map = _sim.Map;
            var cfg = _sim.Config.Walking;
            long cost = 0;
            long treeDensity = 0;
            foreach (var t in tiles)
            {
                if (!map.InBounds(t)) continue;
                var surface = map.GetSurface(t);
                if (erase)
                {
                    if (surface != SurfaceType.WalkingArea) continue;
                    if (Occupant.KindOf(map.GetOccupant(t.X, t.Y)) == OccupantKind.Building)
                        return CommandResult.Fail("A building stands on this walking area. Remove it first.");
                    changed++;
                    continue;
                }

                if (surface == SurfaceType.WalkingArea) continue;
                if (!map.IsOwned(t)) return CommandResult.Fail("Walking areas go on land you own.");
                if (surface != SurfaceType.Natural) return CommandResult.Fail("That's the road or the parking lot.");
                if (map.GetRunId(t.X, t.Y) != 0) return CommandResult.Fail("That's a trail. Erase the trail there first.");
                if (map.HasFlag(t.X, t.Y, TileFlags.Blocked) || map.HasFlag(t.X, t.Y, TileFlags.Protected))
                    return CommandResult.Fail("This ground can't be built on.");
                if (TerrainQueries.SlopePercent(map, _sim.TerrainScale, t.X, t.Y) > cfg.MaxSlopePercent)
                    return CommandResult.Fail($"Too steep for a walking area (max {cfg.MaxSlopePercent:0}% grade).");
                treeDensity += map.GetTreeDensity(t.X, t.Y);
                changed++;
            }

            if (changed == 0) return CommandResult.Fail(erase ? "No walking area here." : "Already a walking area.");
            if (!erase)
            {
                cost = changed * cfg.CostPerTile + treeDensity * _sim.Config.Terrain.TreeClearCostPerDensity;
                if (!_sim.World.Economy.CanAfford(cost)) return CommandResult.Fail($"Not enough cash (need ${cost:N0}).", cost);
            }
            return CommandResult.Success(cost);
        }

        public void Paint(IReadOnlyList<Int2> tiles, bool erase)
        {
            var map = _sim.Map;
            var cfg = _sim.Config.Walking;
            var economy = _sim.World.Economy;
            long tiles0 = 0, trees = 0;
            foreach (var t in tiles)
            {
                if (!map.InBounds(t)) continue;
                var surface = map.GetSurface(t);
                if (erase)
                {
                    if (surface == SurfaceType.WalkingArea) map.SetSurface(t.X, t.Y, SurfaceType.Natural);
                    continue;
                }
                if (surface != SurfaceType.Natural) continue;
                int density = map.GetTreeDensity(t.X, t.Y);
                if (density > 0) { trees += density; map.SetTreeDensity(t.X, t.Y, 0); }
                map.SetSurface(t.X, t.Y, SurfaceType.WalkingArea);
                tiles0++;
            }
            economy.AddExpense(LedgerCategory.Construction, tiles0 * cfg.CostPerTile);
            economy.AddExpense(LedgerCategory.TreeClearing, trees * _sim.Config.Terrain.TreeClearCostPerDensity);

            _sim.World.WalkingVersion++;
            Rebuild();
            _sim.RunSystem.RefreshConnections(); // trails may now end on a walking area
        }

        // =====================================================================
        // Zones
        // =====================================================================

        /// <summary>Flood-fills walking areas and parking into zones, then links lift stations to them.</summary>
        public void Rebuild()
        {
            var map = _sim.Map;
            int n = map.Width * map.Height;
            if (_zoneOf.Length != n) _zoneOf = new int[n];
            else Array.Clear(_zoneOf, 0, n);
            _zones.Clear();

            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    int key = map.TileKey(x, y);
                    if (_zoneOf[key] != 0 || !map.IsWalkable(x, y)) continue;
                    var zone = new WalkZone { Id = _zones.Count + 1 };
                    _zones.Add(zone);
                    Fill(x, y, zone);
                }

            RefreshLiftLinks();
        }

        private void Fill(int sx, int sy, WalkZone zone)
        {
            var map = _sim.Map;
            _queue.Clear();
            int start = map.TileKey(sx, sy);
            _zoneOf[start] = zone.Id;
            _queue.Enqueue(start);
            while (_queue.Count > 0)
            {
                var p = map.FromTileKey(_queue.Dequeue());
                zone.TileCount++;
                if (map.HasFlag(p.X, p.Y, TileFlags.BaseArea) || map.GetSurface(p) == SurfaceType.Parking) zone.IsBase = true;
                foreach (var o in Int2.Neighbours8)
                {
                    int nx = p.X + o.X, ny = p.Y + o.Y;
                    if (!map.InBounds(nx, ny)) continue;
                    int k = map.TileKey(nx, ny);
                    if (_zoneOf[k] != 0 || !map.IsWalkable(nx, ny)) continue;
                    _zoneOf[k] = zone.Id;
                    _queue.Enqueue(k);
                }
            }
        }

        /// <summary>Which lift stations open onto which zone. Called whenever lifts or zones change.</summary>
        public void RefreshLiftLinks()
        {
            float radius = _sim.Config.Walking.LiftLinkRadius;
            foreach (var z in _zones) { z.LiftBottoms.Clear(); z.LiftTops.Clear(); }
            foreach (var lift in _sim.World.Lifts)
            {
                ZoneNear(lift.BottomStation, radius)?.LiftBottoms.Add(lift.Id);
                ZoneNear(lift.TopStation, radius)?.LiftTops.Add(lift.Id);
            }
            Version++;
        }
    }
}
