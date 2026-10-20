using System;
using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Runs
{
    /// <summary>
    /// Owns run creation, flow fields, difficulty ratings and lift connections.
    /// Flow fields are rebuilt only when the run or the terrain under it changes.
    /// </summary>
    public sealed class RunSystem : ISimSystem
    {
        private readonly GameSimulation _sim;
        private readonly MinHeap<int> _heap = new MinHeap<int>();
        private readonly HashSet<int> _scratchKeys = new HashSet<int>();
        private readonly Queue<int> _scratchQueue = new Queue<int>();
        private readonly List<float> _scratchSlopes = new List<float>();

        private const int SnowRefreshMinutes = 30;
        private const float EndLiftRadius = 12f;

        public RunSystem(GameSimulation sim) { _sim = sim; }

        public void Tick()
        {
            if (_sim.Clock.MinuteAdvanced && _sim.World.Time.MinuteOfDay % SnowRefreshMinutes == 0)
                RefreshSnowStats();
            if (_sim.Clock.DayAdvanced)
                foreach (var run in _sim.World.Runs) { run.SkiersToday = 0; run.InjuriesToday = 0; }
        }

        // ---------- Creation ----------

        public CommandResult ValidateNewRun(IReadOnlyList<Int2> tiles)
        {
            var map = _sim.Map;
            int min = _sim.Config.Runs.MinRunTiles;
            if (tiles == null || tiles.Count < min) return CommandResult.Fail($"Paint at least {min} tiles.");

            _scratchKeys.Clear();
            foreach (var t in tiles)
            {
                if (map.InBounds(t) && map.GetRunId(t.X, t.Y) != 0) return CommandResult.Fail("Overlaps an existing run.");
                var check = CheckTileForRun(t);
                if (!check.Ok) return check;
                _scratchKeys.Add(map.TileKey(t.X, t.Y));
            }

            if (!IsContiguous(tiles[0], _scratchKeys)) return CommandResult.Fail("Run tiles must be connected.");
            return CommandResult.Success();
        }

        /// <summary>Can this tile be part of a trail at all (ignoring which trail is on it now)?</summary>
        public CommandResult CheckTileForRun(Int2 t)
        {
            var map = _sim.Map;
            if (!map.InBounds(t)) return CommandResult.Fail("Run goes off the map.");
            if (!map.IsOwned(t)) return CommandResult.Fail("Runs must be on land you own.");
            if (map.HasFlag(t.X, t.Y, TileFlags.Blocked)) return CommandResult.Fail("Crosses blocked terrain.");
            switch (map.GetSurface(t))
            {
                case SurfaceType.WalkingArea: return CommandResult.Fail("That's a walking area. Erase it there first.");
                case SurfaceType.Road: return CommandResult.Fail("You can't ski on the road.");
                case SurfaceType.Parking: return CommandResult.Fail("You can't ski through the parking lot.");
            }
            if (Occupant.KindOf(map.GetOccupant(t.X, t.Y)) == OccupantKind.Light)
                return CommandResult.Fail("A light pole is in the way. Trail lights go beside trails.");
            return CommandResult.Success();
        }

        // ---------- Editing an existing run ----------

        private readonly List<Int2> _editTiles = new List<Int2>();

        /// <summary>
        /// Adds and erases tiles on an existing run, keeping its name, sign and settings. Tiles to add that
        /// belong to this run already, and tiles to erase that aren't on it, are ignored. The result must still be
        /// one connected trail of at least MinRunTiles.
        /// </summary>
        public CommandResult ValidateEdit(Run run, IReadOnlyList<Int2> add, IReadOnlyList<Int2> remove)
        {
            if (run == null) return CommandResult.Fail("That run no longer exists.");
            var map = _sim.Map;
            int changes = 0;

            _scratchKeys.Clear();
            foreach (var t in run.Tiles) _scratchKeys.Add(map.TileKey(t.X, t.Y));

            if (remove != null)
                foreach (var t in remove)
                    if (map.InBounds(t) && _scratchKeys.Remove(map.TileKey(t.X, t.Y))) changes++;

            if (add != null)
                foreach (var t in add)
                {
                    if (!map.InBounds(t)) return CommandResult.Fail("Run goes off the map.");
                    int key = map.TileKey(t.X, t.Y);
                    if (_scratchKeys.Contains(key)) continue;
                    int other = map.GetRunId(t.X, t.Y);
                    if (other != 0 && other != run.Id) return CommandResult.Fail("Overlaps another run.");
                    if (other == run.Id) { _scratchKeys.Add(key); changes++; continue; } // re-adding an erased tile
                    var check = CheckTileForRun(t);
                    if (!check.Ok) return check;
                    _scratchKeys.Add(key);
                    changes++;
                }

            if (changes == 0) return CommandResult.Fail("Nothing to change.");
            int min = _sim.Config.Runs.MinRunTiles;
            if (_scratchKeys.Count < min)
                return CommandResult.Fail($"\"{run.Name}\" would be shorter than {min} tiles. Delete the run instead.");

            int seed = -1;
            foreach (int k in _scratchKeys) { seed = k; break; }
            if (!IsContiguous(map.FromTileKey(seed), _scratchKeys))
                return CommandResult.Fail($"That would cut \"{run.Name}\" in two.");
            return CommandResult.Success();
        }

        public void ApplyEdit(Run run, IReadOnlyList<Int2> add, IReadOnlyList<Int2> remove)
        {
            var map = _sim.Map;
            _scratchKeys.Clear();
            if (remove != null) foreach (var t in remove) if (map.InBounds(t)) _scratchKeys.Add(map.TileKey(t.X, t.Y));

            // Keep the original paint order for the tiles that stay, then append new ones.
            _editTiles.Clear();
            foreach (var t in run.Tiles)
            {
                if (_scratchKeys.Contains(map.TileKey(t.X, t.Y))) { if (map.GetRunId(t.X, t.Y) == run.Id) map.SetRunId(t.X, t.Y, 0); }
                else _editTiles.Add(t);
            }
            var kept = new HashSet<int>();
            foreach (var t in _editTiles) kept.Add(map.TileKey(t.X, t.Y));
            if (add != null)
                foreach (var t in add)
                {
                    if (!map.InBounds(t) || !kept.Add(map.TileKey(t.X, t.Y))) continue;
                    _editTiles.Add(t);
                    map.SetRunId(t.X, t.Y, run.Id);
                }

            run.Tiles.Clear();
            run.Tiles.AddRange(_editTiles);
            Rebuild(run);
            RefreshConnections();
            _sim.GuestSystem.OnRunReshaped(run.Id);
            _sim.World.RunsVersion++;
        }

        private bool IsContiguous(Int2 seed, HashSet<int> keys)
        {
            var map = _sim.Map;
            var seen = new HashSet<int>();
            _scratchQueue.Clear();
            int seedKey = map.TileKey(seed.X, seed.Y);
            _scratchQueue.Enqueue(seedKey);
            seen.Add(seedKey);
            while (_scratchQueue.Count > 0)
            {
                var p = map.FromTileKey(_scratchQueue.Dequeue());
                foreach (var o in Int2.Neighbours8)
                {
                    int nx = p.X + o.X, ny = p.Y + o.Y;
                    if (!map.InBounds(nx, ny)) continue;
                    int k = map.TileKey(nx, ny);
                    if (keys.Contains(k) && seen.Add(k)) _scratchQueue.Enqueue(k);
                }
            }
            return seen.Count == keys.Count;
        }

        public Run CreateRun(IReadOnlyList<Int2> tiles, string name = null)
        {
            var world = _sim.World;
            var run = new Run { Id = world.NextRunId++ };
            run.Tiles.AddRange(tiles);
            foreach (var t in tiles) _sim.Map.SetRunId(t.X, t.Y, run.Id);

            world.Runs.Add(run);
            Rebuild(run);
            run.Name = string.IsNullOrEmpty(name) ? RunNames.Pick(run.ComputedRating, run.Id) : name;
            RefreshConnections();
            world.RunsVersion++;

            _sim.Events.Publish(SimEventKind.RunCreated, EventSeverity.Good,
                $"New run \"{run.Name}\" ({RunRater.Label(run.ComputedRating)}).", run.Start, run.Id);
            return run;
        }

        public void DeleteRun(Run run)
        {
            foreach (var t in run.Tiles)
                if (_sim.Map.GetRunId(t.X, t.Y) == run.Id) _sim.Map.SetRunId(t.X, t.Y, 0);
            _sim.World.Runs.Remove(run);
            _sim.GuestSystem.OnRunRemoved(run.Id);
            RefreshConnections();
            _sim.World.RunsVersion++;
        }

        public void SetRatingOverride(Run run, Difficulty? rating)
        {
            run.RatingOverride = rating == run.ComputedRating ? null : rating;
            UpdateIssues(run);
            _sim.World.RunsVersion++;
            foreach (var t in run.Tiles) _sim.Map.ChunkAtTile(t.X, t.Y).MarkSurfaceChanged();
        }

        public void SetOpen(Run run, bool open)
        {
            run.Open = open;
            _sim.World.RunsVersion++;
        }

        // ---------- Derived data ----------

        /// <summary>Call after loading a save, or after terraforming anywhere near runs.</summary>
        public void RebuildAll()
        {
            foreach (var run in _sim.World.Runs) Rebuild(run);
            RefreshConnections();
            RefreshSnowStats();
            _sim.World.RunsVersion++;
        }

        /// <summary>Rebuilds a run's lookup, flow field and rating from its tile list.</summary>
        public void Rebuild(Run run)
        {
            var map = _sim.Map;
            run.SlotByKey.Clear();
            for (int i = 0; i < run.Tiles.Count; i++)
                run.SlotByKey[map.TileKey(run.Tiles[i].X, run.Tiles[i].Y)] = i;

            // Start = highest tile, End = lowest tile.
            float hi = float.MinValue, lo = float.MaxValue;
            foreach (var t in run.Tiles)
            {
                float h = map.TileCenterHeight(t.X, t.Y);
                if (h > hi) { hi = h; run.Start = t; }
                if (h < lo) { lo = h; run.End = t; }
            }

            BuildFlowField(run);
            BuildCrossSections(run);
            Rate(run);
            run.LitFraction = _sim.LightingSystem.ComputeLitFraction(run);
        }

        /// <summary>Thickness (in flow distance) of the slices used to measure across-the-run positions.</summary>
        private const float CrossBandSize = 2f;

        /// <summary>
        /// Gives every tile an across-the-run coordinate so skiers can hold their own line instead of all
        /// funnelling down the single shortest path. The run is sliced by flow distance (each slice is a
        /// cross-section of the trail, even where it bends); within a slice, tiles are placed by their sideways
        /// offset from the slice's centre, perpendicular to the local direction of travel.
        /// </summary>
        private void BuildCrossSections(Run run)
        {
            int n = run.Tiles.Count;
            if (run.Across.Length != n)
            {
                run.Across = new float[n];
                run.CrossWidth = new float[n];
            }

            float maxD = 0f;
            for (int i = 0; i < n; i++)
                if (run.DistanceToEnd[i] != float.MaxValue) maxD = Math.Max(maxD, run.DistanceToEnd[i]);
            int bands = (int)(maxD / CrossBandSize) + 1;

            var cx = new float[bands];
            var cy = new float[bands];
            var count = new int[bands];
            for (int i = 0; i < n; i++)
            {
                float d = run.DistanceToEnd[i];
                if (d == float.MaxValue) continue;
                int b = (int)(d / CrossBandSize);
                cx[b] += run.Tiles[i].X + 0.5f;
                cy[b] += run.Tiles[i].Y + 0.5f;
                count[b]++;
            }
            for (int b = 0; b < bands; b++)
                if (count[b] > 0) { cx[b] /= count[b]; cy[b] /= count[b]; }

            // Direction of travel per slice: from the slice above toward the slice below.
            float fallX = run.End.X - run.Start.X, fallY = run.End.Y - run.Start.Y;
            var dx = new float[bands];
            var dy = new float[bands];
            for (int b = 0; b < bands; b++)
            {
                int lower = b, upper = b;
                for (int k = 1; k <= 3 && lower == b; k++) if (b - k >= 0 && count[b - k] > 0) lower = b - k;
                for (int k = 1; k <= 3 && upper == b; k++) if (b + k < bands && count[b + k] > 0) upper = b + k;
                float vx = fallX, vy = fallY;
                if (lower != upper && count[lower] > 0 && count[upper] > 0)
                {
                    vx = cx[lower] - cx[upper];
                    vy = cy[lower] - cy[upper];
                }
                float len = (float)Math.Sqrt(vx * vx + vy * vy);
                if (len < 1e-4f) { vx = 1f; vy = 1f; len = 1.41421f; }
                dx[b] = vx / len;
                dy[b] = vy / len;
            }

            // Smooth directions a little so a lumpy slice doesn't twist the coordinates.
            var sx = new float[bands];
            var sy = new float[bands];
            for (int b = 0; b < bands; b++)
            {
                float ax = 0f, ay = 0f;
                for (int k = Math.Max(0, b - 2); k <= Math.Min(bands - 1, b + 2); k++) { ax += dx[k]; ay += dy[k]; }
                float len = (float)Math.Sqrt(ax * ax + ay * ay);
                if (len < 1e-4f) { sx[b] = dx[b]; sy[b] = dy[b]; }
                else { sx[b] = ax / len; sy[b] = ay / len; }
            }

            // Sideways offset of each tile, then normalise per slice.
            var lateral = new float[n];
            var min = new float[bands];
            var max = new float[bands];
            for (int b = 0; b < bands; b++) { min[b] = float.MaxValue; max[b] = float.MinValue; }
            for (int i = 0; i < n; i++)
            {
                float d = run.DistanceToEnd[i];
                if (d == float.MaxValue) continue;
                int b = (int)(d / CrossBandSize);
                float px = run.Tiles[i].X + 0.5f - cx[b], py = run.Tiles[i].Y + 0.5f - cy[b];
                float lat = sx[b] * py - sy[b] * px; // + = left of travel
                lateral[i] = lat;
                if (lat < min[b]) min[b] = lat;
                if (lat > max[b]) max[b] = lat;
            }
            for (int i = 0; i < n; i++)
            {
                float d = run.DistanceToEnd[i];
                if (d == float.MaxValue)
                {
                    run.Across[i] = 0.5f;
                    run.CrossWidth[i] = 1f;
                    continue;
                }
                int b = (int)(d / CrossBandSize);
                float span = max[b] - min[b];
                run.Across[i] = span >= 0.5f ? (lateral[i] - min[b]) / span : 0.5f;
                run.CrossWidth[i] = span + 1f;
            }

            // Bottom-to-top slot order, for quick "what's just below me" lookups. Ties by slot (deterministic).
            if (run.SlotsByDistance.Length != n) run.SlotsByDistance = new int[n];
            for (int i = 0; i < n; i++) run.SlotsByDistance[i] = i;
            var dist = run.DistanceToEnd;
            Array.Sort(run.SlotsByDistance, (a, b) =>
            {
                int c = dist[a].CompareTo(dist[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
        }

        /// <summary>Index into <see cref="Run.SlotsByDistance"/> of the first slot with distance >= <paramref name="d"/>.</summary>
        public static int LowerBoundByDistance(Run run, float d)
        {
            int lo = 0, hi = run.SlotsByDistance.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (run.DistanceToEnd[run.SlotsByDistance[mid]] < d) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Dijkstra from the end tile across the run's tiles. The cost of moving uphill is penalised,
        /// so skiers follow the fall line and only climb when the run forces them to.
        /// </summary>
        private void BuildFlowField(Run run)
        {
            var map = _sim.Map;
            int n = run.Tiles.Count;
            if (run.DistanceToEnd.Length != n) run.DistanceToEnd = new float[n];
            for (int i = 0; i < n; i++) run.DistanceToEnd[i] = float.MaxValue;

            float uphillPenalty = _sim.Config.Runs.UphillPenalty;
            int endSlot = run.SlotByKey[map.TileKey(run.End.X, run.End.Y)];
            run.DistanceToEnd[endSlot] = 0f;
            _heap.Clear();
            _heap.Push(endSlot, 0f);

            while (_heap.Count > 0)
            {
                int slot = _heap.Pop(out float d);
                if (d > run.DistanceToEnd[slot]) continue; // stale entry
                var u = run.Tiles[slot];
                float hu = map.TileCenterHeight(u.X, u.Y);

                for (int k = 0; k < Int2.Neighbours8.Length; k++)
                {
                    var o = Int2.Neighbours8[k];
                    int vx = u.X + o.X, vy = u.Y + o.Y;
                    if (!map.InBounds(vx, vy)) continue;
                    if (!run.SlotByKey.TryGetValue(map.TileKey(vx, vy), out int vSlot)) continue;

                    // A skier at v moves to u. Climbing = hu above hv.
                    float hv = map.TileCenterHeight(vx, vy);
                    float step = k < 4 ? 1f : 1.41421f;
                    float climb = Math.Max(0f, hu - hv);
                    float nd = d + step + climb * uphillPenalty;
                    if (nd < run.DistanceToEnd[vSlot])
                    {
                        run.DistanceToEnd[vSlot] = nd;
                        _heap.Push(vSlot, nd);
                    }
                }
            }
        }

        /// <summary>Walk the flow path from Start to End, sampling slopes, then apply the rating table.</summary>
        private void Rate(Run run)
        {
            var map = _sim.Map;
            var scale = _sim.TerrainScale;
            _scratchSlopes.Clear();

            bool uphill = false;
            var p = run.Start;
            int guard = run.Tiles.Count + 1;
            float length = 0f;
            while (guard-- > 0)
            {
                _scratchSlopes.Add(TerrainQueries.SlopePercent(map, scale, p.X, p.Y));
                if (p == run.End) break;
                if (!TryNextTile(run, p, out var next)) break;
                if (map.TileCenterHeight(next.X, next.Y) > map.TileCenterHeight(p.X, p.Y) + 2f) uphill = true;
                length += p.Distance(next);
                p = next;
            }

            float sum = 0f, maxSustained = 0f;
            for (int i = 0; i < _scratchSlopes.Count; i++)
            {
                sum += _scratchSlopes[i];
                // "Steepest sustained section": max of a 3-tile moving average.
                int a = Math.Max(0, i - 1), b = Math.Min(_scratchSlopes.Count - 1, i + 1);
                float window = 0f;
                for (int j = a; j <= b; j++) window += _scratchSlopes[j];
                maxSustained = Math.Max(maxSustained, window / (b - a + 1));
            }

            run.AvgSlope = _scratchSlopes.Count > 0 ? sum / _scratchSlopes.Count : 0f;
            run.MaxSlope = maxSustained;
            run.PathLength = Math.Max(1f, length);
            run.AverageWidth = run.Tiles.Count / run.PathLength;
            run.ComputedRating = RunRater.Rate(_sim.Config.Runs, run.AvgSlope, run.MaxSlope);
            run.DifficultyScore = RunRater.SlopeScore(run.MaxSlope);
            if (run.AverageWidth < 1.5f) run.DifficultyScore += 0.3f; // narrow = harder

            run.Issues = uphill ? RunIssues.HasUphill : RunIssues.None;
        }

        /// <summary>The neighbouring run tile with the lowest flow distance. False at the end or a local minimum.</summary>
        public bool TryNextTile(Run run, Int2 from, out Int2 next)
        {
            var map = _sim.Map;
            next = from;
            float best = run.DistanceAt(map.TileKey(from.X, from.Y));
            bool found = false;
            foreach (var o in Int2.Neighbours8)
            {
                int nx = from.X + o.X, ny = from.Y + o.Y;
                if (!map.InBounds(nx, ny)) continue;
                float d = run.DistanceAt(map.TileKey(nx, ny));
                if (d < best)
                {
                    best = d;
                    next = new Int2(nx, ny);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>The run tile closest to a point. Used to find where a guest joins a run.</summary>
        public Int2 NearestTile(Run run, float x, float y)
        {
            Int2 best = run.Start;
            float bestD = float.MaxValue;
            foreach (var t in run.Tiles)
            {
                float dx = t.X + 0.5f - x, dy = t.Y + 0.5f - y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        /// <summary>
        /// The closest run tile no higher than <paramref name="maxHeight"/>, so a skier traversing onto a run
        /// joins it level or below rather than climbing. Falls back to the closest tile of all.
        /// </summary>
        public Int2 NearestTileNotAbove(Run run, float x, float y, float maxHeight)
        {
            var map = _sim.Map;
            Int2 best = run.Start;
            float bestD = float.MaxValue;
            bool found = false;
            foreach (var t in run.Tiles)
            {
                if (map.TileCenterHeight(t.X, t.Y) > maxHeight) continue;
                float dx = t.X + 0.5f - x, dy = t.Y + 0.5f - y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = t; found = true; }
            }
            return found ? best : NearestTile(run, x, y);
        }

        // ---------- Connections ----------

        /// <summary>
        /// Recomputes how the mountain connects: which lifts feed which runs, where runs pass lift bottoms or
        /// meet other runs (exits), and where each run ends up. Cheap enough to run on every edit.
        /// </summary>
        public void RefreshConnections()
        {
            var world = _sim.World;
            float radius = _sim.Config.Guests.RunConnectRadius;
            var walking = _sim.WalkingAreaSystem;
            walking.RefreshLiftLinks();

            foreach (var lift in world.Lifts) lift.RunsFromTop.Clear();

            foreach (var run in world.Runs)
            {
                run.StartLiftId = 0;
                run.EndLiftId = 0;
                run.ContinuesIntoRunId = 0;

                float bestTop = radius;
                foreach (var lift in world.Lifts)
                {
                    float d = MinDistanceToRun(run, lift.TopStation);
                    if (d <= radius)
                    {
                        lift.RunsFromTop.Add(run.Id);
                        if (d <= bestTop) { bestTop = d; run.StartLiftId = lift.Id; }
                    }
                }

                float bestBottom = EndLiftRadius;
                foreach (var lift in world.Lifts)
                {
                    float d = run.End.Distance(lift.BottomStation);
                    if (d <= bestBottom) { bestBottom = d; run.EndLiftId = lift.Id; }
                }

                if (run.EndLiftId == 0) run.ContinuesIntoRunId = FindContinuation(run);
                run.EndsAtBase = IsAtBase(run.End);
                run.EndZoneId = walking.ZoneNear(run.End, _sim.Config.Walking.RunEndRadius)?.Id ?? 0;
                BuildExits(run);
            }

            UpdateReachability();
            foreach (var run in world.Runs) UpdateIssues(run);
            ConnectionsVersion++;
            _sim.GuestSystem.OnConnectionsChanged();
        }

        /// <summary>Bumped whenever connections or run statistics change. Route planning caches key off it.</summary>
        public int ConnectionsVersion { get; private set; }

        public bool IsAtBase(Int2 tile)
        {
            var e = _sim.World.ResortEntrance;
            return tile.Distance(e) <= _sim.Config.Guests.BaseAreaRadius
                || _sim.Map.HasFlag(tile.X, tile.Y, TileFlags.BaseArea)
                || _sim.WalkingAreaSystem.IsNearBaseZone(tile);
        }

        /// <summary>
        /// Finds every lift bottom the run passes within <c>LiftExitRadius</c>, and every other run it meets
        /// (see <see cref="AddJunctionExits"/>).
        /// </summary>
        private void BuildExits(Run run)
        {
            var map = _sim.Map;
            float exitRadius = _sim.Config.Guests.LiftExitRadius;
            run.Exits.Clear();

            foreach (var lift in _sim.World.Lifts)
            {
                Int2 best = run.Start;
                float bestD = float.MaxValue;
                foreach (var t in run.Tiles)
                {
                    float d = t.Distance(lift.BottomStation);
                    if (d < bestD) { bestD = d; best = t; }
                }
                if (bestD > exitRadius) continue;
                run.Exits.Add(new RunExit
                {
                    Kind = RunExitKind.Lift,
                    TargetId = lift.Id,
                    Tile = best,
                    JoinTile = lift.BottomStation,
                    Distance = run.DistanceAt(map.TileKey(best.X, best.Y)),
                });
            }

            AddJunctionExits(run);

            // Skiing order: highest (largest distance-to-end) first. Ties broken by id for determinism.
            run.Exits.Sort((a, b) =>
            {
                int c = b.Distance.CompareTo(a.Distance);
                return c != 0 ? c : (a.Kind == b.Kind ? a.TargetId.CompareTo(b.TargetId) : a.Kind.CompareTo(b.Kind));
            });
        }

        private readonly Dictionary<int, RunExit> _firstContact = new Dictionary<int, RunExit>();
        private readonly Dictionary<int, RunExit> _lastContact = new Dictionary<int, RunExit>();

        /// <summary>
        /// Junctions with other runs. Two runs connect where their tiles come within <c>JunctionRadius</c>
        /// of each other, so trails painted with a small gap still join (skiers traverse the gap).
        ///
        /// For each neighbouring run two exits are recorded when they differ:
        ///  - the <b>first</b> contact (highest up this run): where you can branch off early, e.g. into a glade;
        ///  - the <b>last</b> contact (lowest down this run): where this run merges into the other, e.g. a glade
        ///    rejoining the main trail, or an upper trail flowing into a lower one.
        /// At each contact the join tile is the highest reachable tile of the other run, so the skier gets as
        /// much of it as possible. Bad connections are allowed: guests will use them and may get stuck.
        /// </summary>
        private void AddJunctionExits(Run run)
        {
            var map = _sim.Map;
            float radius = _sim.Config.Runs.JunctionRadius;
            int r = (int)Math.Ceiling(radius);
            float r2 = radius * radius;
            _firstContact.Clear();
            _lastContact.Clear();

            foreach (var t in run.Tiles)
            {
                float here = run.DistanceAt(map.TileKey(t.X, t.Y));
                // Best join tile per neighbouring run as seen from this tile.
                for (int oy = -r; oy <= r; oy++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        if (ox * ox + oy * oy > r2) continue;
                        int nx = t.X + ox, ny = t.Y + oy;
                        if (!map.InBounds(nx, ny)) continue;
                        int other = map.GetRunId(nx, ny);
                        if (other == 0 || other == run.Id) continue;
                        var otherRun = _sim.World.GetRun(other);
                        if (otherRun == null) continue;
                        float otherDistance = otherRun.DistanceAt(map.TileKey(nx, ny));
                        if (otherDistance < 2f) continue; // joining right at the other run's end is pointless
                        // Only join the other run level with or below this one: skiers don't climb across.
                        if (map.TileCenterHeight(nx, ny) > map.TileCenterHeight(t.X, t.Y) + 0.5f) continue;

                        var candidate = new RunExit
                        {
                            Kind = RunExitKind.Run,
                            TargetId = other,
                            Tile = t,
                            JoinTile = new Int2(nx, ny),
                            Distance = here,
                        };
                        Consider(_firstContact, candidate, otherRun, preferHigh: true);
                        Consider(_lastContact, candidate, otherRun, preferHigh: false);
                    }
                }
            }

            foreach (var kv in _firstContact)
            {
                run.Exits.Add(kv.Value);
                if (_lastContact.TryGetValue(kv.Key, out var last) && last.Distance < kv.Value.Distance - 3f)
                    run.Exits.Add(last);
            }
        }

        /// <summary>
        /// Keeps the best contact per neighbouring run: highest (or lowest) on this run; at equal height, the
        /// join tile highest up the other run.
        /// </summary>
        private void Consider(Dictionary<int, RunExit> best, RunExit candidate, Run otherRun, bool preferHigh)
        {
            if (!best.TryGetValue(candidate.TargetId, out var current)) { best[candidate.TargetId] = candidate; return; }
            if (Math.Abs(candidate.Distance - current.Distance) > 0.01f)
            {
                bool better = preferHigh ? candidate.Distance > current.Distance : candidate.Distance < current.Distance;
                if (better) best[candidate.TargetId] = candidate;
                return;
            }
            var map = _sim.Map;
            float c = otherRun.DistanceAt(map.TileKey(candidate.JoinTile.X, candidate.JoinTile.Y));
            float k = otherRun.DistanceAt(map.TileKey(current.JoinTile.X, current.JoinTile.Y));
            if (c > k) best[candidate.TargetId] = candidate;
        }

        /// <summary>Index of the first exit a skier joining at <paramref name="distance"/> has not yet passed.</summary>
        public static int FirstExitBelow(Run run, float distance)
        {
            int i = 0;
            while (i < run.Exits.Count && run.Exits[i].Distance > distance + 0.5f) i++;
            return i;
        }

        /// <summary>
        /// The run this one flows into at its bottom: any other run within <c>JunctionRadius</c> of the end tile,
        /// preferring the one with the most skiing left from there.
        /// </summary>
        private int FindContinuation(Run run)
        {
            var map = _sim.Map;
            float radius = _sim.Config.Runs.JunctionRadius;
            int r = (int)Math.Ceiling(radius);
            int best = 0;
            float bestDistance = 2f; // must leave something to ski
            for (int oy = -r; oy <= r; oy++)
            {
                for (int ox = -r; ox <= r; ox++)
                {
                    if (ox * ox + oy * oy > radius * radius) continue;
                    int nx = run.End.X + ox, ny = run.End.Y + oy;
                    if (!map.InBounds(nx, ny)) continue;
                    int other = map.GetRunId(nx, ny);
                    if (other == 0 || other == run.Id) continue;
                    var otherRun = _sim.World.GetRun(other);
                    if (otherRun == null) continue;
                    float d = otherRun.DistanceAt(map.TileKey(nx, ny));
                    if (d > bestDistance) { bestDistance = d; best = other; }
                }
            }
            return best;
        }

        private static float MinDistanceToRun(Run run, Int2 p)
        {
            float best = float.MaxValue;
            foreach (var t in run.Tiles)
            {
                float d = t.Distance(p);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Works out, across the whole network, which runs can be reached from a lift (FedByLift) and which
        /// lead to a lift or the base (ReachesLift), following run-to-run connections through any number of
        /// trails. Closed runs still count: closing a trail is an operational choice, not a design flaw.
        /// </summary>
        private void UpdateReachability()
        {
            var runs = _sim.World.Runs;
            foreach (var run in runs)
            {
                run.FedByLift = run.StartLiftId != 0;
                bool liftExit = false;
                foreach (var e in run.Exits) if (e.Kind == RunExitKind.Lift) { liftExit = true; break; }
                var zone = _sim.WalkingAreaSystem.GetZone(run.EndZoneId);
                bool zoneLift = zone != null && zone.LiftBottoms.Count > 0;
                run.ReachesLift = run.EndsAtBase || run.EndLiftId != 0 || liftExit || zoneLift;
            }

            // Fixed point: propagate along run-to-run connections until nothing changes.
            bool changed = true;
            for (int guard = 0; changed && guard < runs.Count + 1; guard++)
            {
                changed = false;
                foreach (var run in runs)
                {
                    foreach (var e in run.Exits)
                    {
                        if (e.Kind != RunExitKind.Run) continue;
                        var other = _sim.World.GetRun(e.TargetId);
                        if (other == null) continue;
                        if (run.FedByLift && !other.FedByLift) { other.FedByLift = true; changed = true; }
                        if (other.ReachesLift && !run.ReachesLift) { run.ReachesLift = true; changed = true; }
                    }
                    if (run.ContinuesIntoRunId != 0)
                    {
                        var next = _sim.World.GetRun(run.ContinuesIntoRunId);
                        if (next != null && next.ReachesLift && !run.ReachesLift) { run.ReachesLift = true; changed = true; }
                        if (next != null && run.FedByLift && !next.FedByLift) { next.FedByLift = true; changed = true; }
                    }
                }
            }
        }

        private void UpdateIssues(Run run)
        {
            var keep = run.Issues & (RunIssues.HasUphill | RunIssues.Icy);
            run.Issues = keep;
            if (!run.FedByLift) run.Issues |= RunIssues.NoLiftAtTop;
            if (!run.ReachesLift) run.Issues |= RunIssues.WalkOut;
            if (run.RatingOverride.HasValue && run.RatingOverride.Value < run.ComputedRating) run.Issues |= RunIssues.Mislabelled;
        }

        // ---------- Snow ----------

        public static float QualityScore(SnowQuality q)
        {
            switch (q)
            {
                case SnowQuality.Groomed: return 1.0f;
                case SnowQuality.Powder: return 0.95f;
                case SnowQuality.Packed: return 0.75f;
                case SnowQuality.Crud: return 0.55f;
                case SnowQuality.Slush: return 0.4f;
                case SnowQuality.Ice: return 0.15f;
                default: return 0f;
            }
        }

        public void RefreshSnowStats()
        {
            var map = _sim.Map;
            foreach (var run in _sim.World.Runs)
            {
                float sum = 0f;
                int ice = 0;
                foreach (var t in run.Tiles)
                {
                    var q = map.GetSnowQuality(t.X, t.Y);
                    sum += QualityScore(q);
                    if (q == SnowQuality.Ice) ice++;
                }
                run.AvgSnowQuality = run.Tiles.Count > 0 ? sum / run.Tiles.Count : 0f;
                if (ice > run.Tiles.Count / 4) run.Issues |= RunIssues.Icy;
                else run.Issues &= ~RunIssues.Icy;
            }
            ConnectionsVersion++;
        }

        /// <summary>Total tiles of open, lift-served terrain. Feeds the "fair price" guests expect.</summary>
        public int OpenRunTiles()
        {
            int total = 0;
            foreach (var run in _sim.World.Runs)
                if (run.Open && (run.Issues & RunIssues.NoLiftAtTop) == 0) total += run.Tiles.Count;
            return total;
        }

        /// <summary>Share of open, lift-served terrain that has trail lights (drives the evening crowd).</summary>
        public float LitOpenRunShare()
        {
            int total = 0, lit = 0;
            var lighting = _sim.LightingSystem;
            foreach (var run in _sim.World.Runs)
            {
                if (!run.Open || (run.Issues & RunIssues.NoLiftAtTop) != 0) continue;
                total += run.Tiles.Count;
                if (lighting.HasLights(run)) lit += run.Tiles.Count;
            }
            return total > 0 ? lit / (float)total : 0f;
        }

        public float AverageOpenSnowQuality()
        {
            float sum = 0f;
            int tiles = 0;
            foreach (var run in _sim.World.Runs)
            {
                if (!run.Open) continue;
                sum += run.AvgSnowQuality * run.Tiles.Count;
                tiles += run.Tiles.Count;
            }
            return tiles > 0 ? sum / tiles : 0.7f;
        }
    }

    /// <summary>Auto-names for new runs, picked by rating. The player can rename (or misname) them.</summary>
    public static class RunNames
    {
        private static readonly string[][] Names =
        {
            new[] { "Easy Street", "Bunny Hop", "Sunday Drive", "Lollygag", "Cocoa Lane", "Meander", "Pizza Wedge", "Slowpoke" },
            new[] { "Blue Moon", "Cruiser", "Glide Path", "Hot Lap", "Carousel", "Ridge Runner", "Blueberry", "Swoosh" },
            new[] { "Black Diamond", "Mogul Alley", "Thunderbolt", "Rock Garden", "Dropout", "Gauntlet", "Hellbender", "Stairway" },
            new[] { "Widowmaker", "The Plunge", "Free Fall", "No Name", "Elevator Shaft", "Body Bag", "Vertigo", "Last Rites" },
            new[] { "Death Chute", "Couloir X", "The Abyss", "Goodbye", "Sheer Madness", "Oblivion", "Gravity Well", "Eulogy" },
        };

        public static string Pick(Difficulty rating, int id)
        {
            var list = Names[(int)rating];
            string name = list[(id - 1) % list.Length];
            int lap = (id - 1) / list.Length;
            return lap == 0 ? name : $"{name} {lap + 1}";
        }
    }
}
