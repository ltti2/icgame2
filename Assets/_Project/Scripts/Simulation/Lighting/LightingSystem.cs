using System;
using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Lighting
{
    /// <summary>
    /// Trail lights and night skiing.
    ///
    /// Each light adds light to the tiles around it (fading with distance; overlapping lights add up) into
    /// <see cref="Chunk.LightLevel"/>. A tile at or above <c>LitThreshold</c> is lit, and a trail with at least
    /// <c>RunLitFraction</c> of its tiles lit is a lit trail (<see cref="Run.LitFraction"/>).
    ///
    /// Every game minute it works out whether it is dark (sunset plus twilight) and whether the lights are on
    /// (night skiing enabled, from shortly before sunset until the night close). After dark:
    ///  - a lift only runs if a lit, open trail starts at its top (<see cref="LiftServesLitTerrain"/>);
    ///  - guests treat unlit trails as dark (<see cref="IsRunDark"/>) and only ski them when they must.
    ///
    /// Light levels and the flags are derived: nothing here is saved. <see cref="Version"/> bumps whenever any
    /// of them change so route planning can rebuild.
    /// </summary>
    public sealed class LightingSystem : ISimSystem
    {
        private readonly GameSimulation _sim;
        private readonly HashSet<int> _touchedRuns = new HashSet<int>();

        public LightingSystem(GameSimulation sim) { _sim = sim; }

        /// <summary>Past sunset plus twilight (or before dawn).</summary>
        public bool IsDark { get; private set; }
        /// <summary>The trail lights are switched on.</summary>
        public bool LightsOn { get; private set; }
        /// <summary>At least one lift has a lit, open trail off its top, so a night session is possible.</summary>
        public bool NightTerrainAvailable { get; private set; }
        /// <summary>Bumped when darkness, the lights or coverage change. Route caches key off it.</summary>
        public int Version { get; private set; }

        private SimConfig.LightingSettings Cfg => _sim.Config.Lighting;

        public void Tick()
        {
            if (_sim.Clock.MinuteAdvanced) RefreshFlags(announce: true);
        }

        /// <summary>Recomputes darkness, whether the lights are on and night availability.</summary>
        public void RefreshFlags(bool announce = false)
        {
            var world = _sim.World;
            var timeCfg = _sim.Config.Time;
            int minute = world.Time.MinuteOfDay;
            var time = _sim.TimeSystem;

            bool dark = time.IsDark;
            bool nightTerrain = ComputeNightTerrainAvailable();
            bool lightsOn = world.NightSkiing && world.Lights.Count > 0 && nightTerrain
                            && minute >= time.SunsetMinute - Cfg.LightsOnBeforeSunsetMinutes
                            && minute < timeCfg.NightCloseMinute + 15;

            bool changed = dark != IsDark || lightsOn != LightsOn || nightTerrain != NightTerrainAvailable;
            bool switchedOn = lightsOn && !LightsOn;
            IsDark = dark;
            NightTerrainAvailable = nightTerrain;
            if (lightsOn != LightsOn)
            {
                LightsOn = lightsOn;
                MarkLitChunks(); // the view draws the glow
            }
            if (!changed) return;
            Version++;
            if (announce && switchedOn)
                _sim.Events.Publish(SimEventKind.General, EventSeverity.Info, "The trail lights are on for night skiing.");
        }

        // =====================================================================
        // Queries
        // =====================================================================

        public bool IsTileLit(int x, int y) => _sim.Map.GetLightLevel(x, y) >= Cfg.LitThreshold;

        /// <summary>Enough of the trail is under lights to ski it at night (whether or not they are on now).</summary>
        public bool HasLights(Run run) => run.LitFraction >= Cfg.RunLitFraction;

        /// <summary>The trail is lit right now.</summary>
        public bool IsRunLit(Run run) => LightsOn && HasLights(run);

        /// <summary>It's dark and this trail's lights aren't on: guests only ski it if they have to.</summary>
        public bool IsRunDark(Run run) => IsDark && !IsRunLit(run);

        /// <summary>A lit, open trail starts at this lift's top, so it can run at night.</summary>
        public bool LiftServesLitTerrain(Lift lift)
        {
            foreach (int runId in lift.RunsFromTop)
            {
                var run = _sim.World.GetRun(runId);
                if (run != null && run.Open && HasLights(run)) return true;
            }
            return false;
        }

        private bool ComputeNightTerrainAvailable()
        {
            foreach (var lift in _sim.World.Lifts)
                if (lift.PlayerOpen && LiftServesLitTerrain(lift)) return true;
            return false;
        }

        /// <summary>Share of the trail's tiles that are lit (0..1).</summary>
        public float ComputeLitFraction(Run run)
        {
            if (run.Tiles.Count == 0) return 0f;
            int lit = 0;
            foreach (var t in run.Tiles)
                if (IsTileLit(t.X, t.Y)) lit++;
            return lit / (float)run.Tiles.Count;
        }

        /// <summary>Power bill for the lights this hour (only while they are on).</summary>
        public long HourlyOperatingCost()
        {
            if (!LightsOn) return 0;
            long total = 0;
            foreach (var light in _sim.World.Lights) total += _sim.Config.GetLight(light.Size).OperatingCostPerHour;
            return total;
        }

        public TrailLight GetLight(int id)
        {
            foreach (var l in _sim.World.Lights) if (l.Id == id) return l;
            return null;
        }

        public TrailLight LightAt(int x, int y)
        {
            ushort occ = _sim.Map.GetOccupant(x, y);
            return Occupant.KindOf(occ) == OccupantKind.Light ? GetLight(Occupant.IdOf(occ)) : null;
        }

        /// <summary>Light level a single light of <paramref name="radius"/> gives at <paramref name="distance"/>.</summary>
        public static int Contribution(float distance, float radius)
        {
            if (distance >= radius) return 0;
            float f = distance / radius;
            return (int)(255f * (1f - f * f));
        }

        // =====================================================================
        // Placement
        // =====================================================================

        public CommandResult QuotePlace(LightSize size, Int2 tile)
        {
            var map = _sim.Map;
            var def = _sim.Config.GetLight(size);
            if (!map.InBounds(tile)) return CommandResult.Fail("Outside the map.");
            long cost = def.Cost + map.GetTreeDensity(tile.X, tile.Y) * _sim.Config.Terrain.TreeClearCostPerDensity;
            if (_sim.World.NextLightId > Occupant.MaxId) return CommandResult.Fail("That's a lot of lights. No more fit.", cost);
            if (!map.IsOwned(tile)) return CommandResult.Fail("Lights go on land you own.", cost);
            if (map.GetOccupant(tile.X, tile.Y) != 0) return CommandResult.Fail("Something is already built here.", cost);
            if (map.GetRunId(tile.X, tile.Y) != 0) return CommandResult.Fail("Put lights beside the trail, not in the middle of it.", cost);
            if (map.IsPaved(tile.X, tile.Y)) return CommandResult.Fail("Not on the road or in the parking lot.", cost);
            if (map.HasFlag(tile.X, tile.Y, TileFlags.Blocked)) return CommandResult.Fail("Blocked terrain.", cost);
            if (!_sim.World.Economy.CanAfford(cost)) return CommandResult.Fail($"Not enough cash (need ${cost:N0}).", cost);
            return CommandResult.Success(cost);
        }

        public TrailLight Place(LightSize size, Int2 tile, long cost)
        {
            var world = _sim.World;
            var map = _sim.Map;
            var light = new TrailLight { Id = world.NextLightId++, Size = size, Tile = tile };

            long trees = map.GetTreeDensity(tile.X, tile.Y);
            if (trees > 0) map.SetTreeDensity(tile.X, tile.Y, 0);
            map.SetOccupant(tile.X, tile.Y, Occupant.Pack(OccupantKind.Light, light.Id));
            world.Lights.Add(light);

            long clearing = trees * _sim.Config.Terrain.TreeClearCostPerDensity;
            world.Economy.AddExpense(LedgerCategory.TreeClearing, clearing);
            world.Economy.AddExpense(LedgerCategory.Construction, cost - clearing);

            RecomputeAround(tile, _sim.Config.GetLight(size).RadiusTiles);
            return light;
        }

        public void Remove(TrailLight light)
        {
            var world = _sim.World;
            _sim.Map.SetOccupant(light.Tile.X, light.Tile.Y, 0);
            world.Lights.Remove(light);
            RecomputeAround(light.Tile, _sim.Config.GetLight(light.Size).RadiusTiles);
        }

        // =====================================================================
        // Coverage
        // =====================================================================

        /// <summary>Rebuilds every tile's light level and every trail's lit share (after a load or generation).</summary>
        public void RebuildAll()
        {
            var map = _sim.Map;
            foreach (var chunk in map.Chunks) Array.Clear(chunk.LightLevel, 0, chunk.LightLevel.Length);
            foreach (var light in _sim.World.Lights) AddLight(light, 0, 0, map.Width - 1, map.Height - 1);
            foreach (var run in _sim.World.Runs) run.LitFraction = ComputeLitFraction(run);
            _sim.World.LightsVersion++;
            RefreshFlags();
            Version++;
        }

        /// <summary>A light was added or removed: recompute the tiles in its reach and the trails there.</summary>
        private void RecomputeAround(Int2 center, float radius)
        {
            var map = _sim.Map;
            int r = (int)Math.Ceiling(radius);
            int x0 = Math.Max(0, center.X - r), y0 = Math.Max(0, center.Y - r);
            int x1 = Math.Min(map.Width - 1, center.X + r), y1 = Math.Min(map.Height - 1, center.Y + r);

            _touchedRuns.Clear();
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var t = map.Locate(x, y);
                    t.Chunk.LightLevel[t.Index] = 0;
                    int run = t.Chunk.RunId[t.Index];
                    if (run != 0) _touchedRuns.Add(run);
                }
            foreach (var light in _sim.World.Lights) AddLight(light, x0, y0, x1, y1);
            for (int cy = y0 >> Chunk.SizeShift; cy <= y1 >> Chunk.SizeShift; cy++)
                for (int cx = x0 >> Chunk.SizeShift; cx <= x1 >> Chunk.SizeShift; cx++)
                    map.GetChunk(cx, cy).MarkSurfaceChanged();

            foreach (int runId in _touchedRuns)
            {
                var run = _sim.World.GetRun(runId);
                if (run != null) run.LitFraction = ComputeLitFraction(run);
            }

            _sim.World.LightsVersion++;
            _sim.World.RunsVersion++;
            RefreshFlags();
            Version++;
            _sim.LiftSystem.RefreshStatuses(); // a lift may gain or lose its night session
        }

        /// <summary>Adds one light's contribution to the tiles inside the clip rectangle.</summary>
        private void AddLight(TrailLight light, int clipX0, int clipY0, int clipX1, int clipY1)
        {
            var map = _sim.Map;
            float radius = _sim.Config.GetLight(light.Size).RadiusTiles;
            int r = (int)Math.Ceiling(radius);
            int x0 = Math.Max(clipX0, light.Tile.X - r), y0 = Math.Max(clipY0, light.Tile.Y - r);
            int x1 = Math.Min(clipX1, light.Tile.X + r), y1 = Math.Min(clipY1, light.Tile.Y + r);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - light.Tile.X, dy = y - light.Tile.Y;
                    int add = Contribution((float)Math.Sqrt(dx * dx + dy * dy), radius);
                    if (add <= 0) continue;
                    var t = map.Locate(x, y);
                    t.Chunk.LightLevel[t.Index] = (byte)Math.Min(255, t.Chunk.LightLevel[t.Index] + add);
                }
        }

        /// <summary>Lights switched on or off: chunks with light in them need their glow redrawn.</summary>
        private void MarkLitChunks()
        {
            var map = _sim.Map;
            foreach (var light in _sim.World.Lights)
            {
                float radius = _sim.Config.GetLight(light.Size).RadiusTiles;
                int r = (int)Math.Ceiling(radius);
                int x0 = Math.Max(0, light.Tile.X - r) >> Chunk.SizeShift, y0 = Math.Max(0, light.Tile.Y - r) >> Chunk.SizeShift;
                int x1 = Math.Min(map.Width - 1, light.Tile.X + r) >> Chunk.SizeShift, y1 = Math.Min(map.Height - 1, light.Tile.Y + r) >> Chunk.SizeShift;
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                        map.GetChunk(cx, cy).MarkSurfaceChanged();
            }
            _sim.World.LightsVersion++;
        }
    }
}
