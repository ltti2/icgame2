using System;
using System.Collections.Generic;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Runs;

namespace IceCoast.Simulation.Guests
{
    /// <summary>
    /// How attractive every lift and run is for each skill level, <i>including what it leads to</i>.
    ///
    /// The mountain is a graph: lifts lead to the runs off their tops (and to lifts a short walk away);
    /// runs lead to the lifts and runs they pass (their exits) and to wherever they end. A lift is worth the
    /// best run you can reach from its top, and a run is worth its own skiing plus a discounted share of the
    /// best place it leads to. A few rounds of value iteration settle these values, so an expert at the base
    /// will happily ride a mediocre lower lift because its top connects to great terrain further up.
    ///
    /// Also computes "way down" values used by guests who want to go home: how directly a lift or run leads
    /// back to the base area.
    ///
    /// Walking areas join things up too: a trail that ends on a walking area leads to every lift that opens
    /// onto it, and a lift whose top opens onto one connects to those lifts.
    ///
    /// After dark, unlit trails are worth <c>DarkRunPenalty</c> less (and count half as a way down), so guests
    /// only ski them when nothing lit will do, and a lift with nothing lit off its top isn't worth riding.
    ///
    /// Values depend only on world state (runs, lifts, ratings, snow stats, lift status, darkness and lights,
    /// walking areas), never on queue lengths, which are applied live at decision time. The cache rebuilds
    /// whenever any input changes, so a freshly loaded game computes exactly the same values (determinism).
    /// </summary>
    public sealed class RouteValues
    {
        private const int Iterations = 6;
        private const float NoRunsValue = -1.5f;
        private const float DeadEndValue = -0.5f;
        private const float ClosedValue = -2f;
        /// <summary>Walking to another lift from a lift top is a little worse than skiing to it.</summary>
        private const float TransferDiscount = 0.95f;

        private static readonly int SkillCount = Enum.GetValues(typeof(SkillLevel)).Length;

        private readonly GameSimulation _sim;
        private readonly Dictionary<int, int> _liftIndex = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _runIndex = new Dictionary<int, int>();
        private float[,] _liftValue = new float[0, 0];
        private float[,] _runValue = new float[0, 0];
        private float[] _baseValue = new float[0];
        private float[] _liftDown = new float[0];
        private float[] _runDown = new float[0];
        private List<int>[] _liftTransfers = new List<int>[0];
        private (int, int, int, int, int, int) _builtKey = (-1, -1, -1, -1, -1, -1);

        public RouteValues(GameSimulation sim) { _sim = sim; }

        private float Lookahead => _sim.Config.Guests.RouteLookahead;

        // =====================================================================
        // Queries
        // =====================================================================

        /// <summary>Value of riding this lift (before queue time) for a skill level.</summary>
        public float Lift(SkillLevel skill, int liftId)
        {
            EnsureFresh();
            return _liftIndex.TryGetValue(liftId, out int i) ? _liftValue[(int)skill, i] : ClosedValue;
        }

        /// <summary>Value of skiing this run from its top, including where it leads.</summary>
        public float Run(SkillLevel skill, int runId)
        {
            EnsureFresh();
            return _runIndex.TryGetValue(runId, out int i) ? _runValue[(int)skill, i] : ClosedValue;
        }

        /// <summary>
        /// Discounted value of the best place this run leads to, considering only exits below
        /// <paramref name="fromDistance"/> (what's still ahead of a skier at that point) plus the run's end.
        /// </summary>
        public float Downstream(SkillLevel skill, Run run, float fromDistance)
        {
            EnsureFresh();
            return Lookahead * BestAhead(run, fromDistance, (int)skill);
        }

        /// <summary>How directly a lift leads back to base (0..1). For guests heading home.</summary>
        public float LiftWayDown(int liftId)
        {
            EnsureFresh();
            return _liftIndex.TryGetValue(liftId, out int i) ? _liftDown[i] : 0f;
        }

        /// <summary>How directly the rest of a run (below <paramref name="fromDistance"/>) leads back to base.</summary>
        public float RunWayDown(Run run, float fromDistance)
        {
            EnsureFresh();
            return BestDownAhead(run, fromDistance);
        }

        // =====================================================================
        // Building
        // =====================================================================

        private void EnsureFresh()
        {
            var key = (_sim.World.LiftsVersion, _sim.World.RunsVersion, _sim.RunSystem.ConnectionsVersion, _sim.LiftSystem.StatusVersion,
                       _sim.LightingSystem.Version, _sim.WalkingAreaSystem.Version);
            if (key == _builtKey) return;
            _builtKey = key;
            Rebuild();
        }

        /// <summary>Forces a rebuild on next use (e.g. after loading a save).</summary>
        public void Invalidate() => _builtKey = (-1, -1, -1, -1, -1, -1);

        private void Rebuild()
        {
            var world = _sim.World;
            var lifts = world.Lifts;
            var runs = world.Runs;

            _liftIndex.Clear();
            _runIndex.Clear();
            for (int i = 0; i < lifts.Count; i++) _liftIndex[lifts[i].Id] = i;
            for (int i = 0; i < runs.Count; i++) _runIndex[runs[i].Id] = i;

            _liftValue = new float[SkillCount, lifts.Count];
            _runValue = new float[SkillCount, runs.Count];
            _baseValue = new float[SkillCount];
            _liftDown = new float[lifts.Count];
            _runDown = new float[runs.Count];
            BuildTransfers();

            float gamma = Lookahead;
            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int s = 0; s < SkillCount; s++)
                {
                    // Base area: guests can walk to any lift there.
                    float baseValue = DeadEndValue;
                    for (int l = 0; l < lifts.Count; l++)
                        if (_sim.RunSystem.IsAtBase(lifts[l].BottomStation)) baseValue = Math.Max(baseValue, _liftValue[s, l]);
                    _baseValue[s] = baseValue;

                    for (int r = 0; r < runs.Count; r++)
                    {
                        var run = runs[r];
                        _runValue[s, r] = run.Open
                            ? RunSkiing((SkillLevel)s, run) + gamma * BestAhead(run, float.MaxValue, s) - DarkPenalty(run)
                            : ClosedValue;
                    }

                    for (int l = 0; l < lifts.Count; l++)
                    {
                        var lift = lifts[l];
                        if (!_sim.LiftSystem.Boardable(lift)) { _liftValue[s, l] = ClosedValue; continue; }
                        float v = NoRunsValue;
                        foreach (int runId in lift.RunsFromTop)
                            if (_runIndex.TryGetValue(runId, out int r)) v = Math.Max(v, _runValue[s, r]);
                        foreach (int other in _liftTransfers[l])
                            v = Math.Max(v, TransferDiscount * _liftValue[s, other]);
                        _liftValue[s, l] = v;
                    }
                }

                // Ways down: 1 at the base, discounted per hop. Riding a lift up to go home counts for little.
                for (int r = 0; r < runs.Count; r++)
                    _runDown[r] = runs[r].Open ? BestDownAhead(runs[r], float.MaxValue) * (_sim.LightingSystem.IsRunDark(runs[r]) ? DarkWayDownFactor : 1f) : 0f;
                for (int l = 0; l < lifts.Count; l++)
                {
                    float v = 0f;
                    if (lifts[l].IsRunning)
                        foreach (int runId in lifts[l].RunsFromTop)
                            if (_runIndex.TryGetValue(runId, out int r)) v = Math.Max(v, _runDown[r]);
                    _liftDown[l] = v;
                }
            }
        }

        /// <summary>A dark trail counts this much as a way home: used if it's the only way, avoided otherwise.</summary>
        private const float DarkWayDownFactor = 0.5f;

        private float DarkPenalty(Run run) => _sim.LightingSystem.IsRunDark(run) ? _sim.Config.Lighting.DarkRunPenalty : 0f;

        /// <summary>
        /// Lifts whose bottom station is a short walk from each lift's top station, or opens onto the same
        /// walking area as it (however far across it).
        /// </summary>
        private void BuildTransfers()
        {
            var lifts = _sim.World.Lifts;
            var walking = _sim.WalkingAreaSystem;
            float radius = _sim.Config.Guests.LiftWalkRadius;
            _liftTransfers = new List<int>[lifts.Count];
            for (int a = 0; a < lifts.Count; a++)
            {
                _liftTransfers[a] = new List<int>();
                for (int b = 0; b < lifts.Count; b++)
                    if (a != b && lifts[a].TopStation.Distance(lifts[b].BottomStation) <= radius) _liftTransfers[a].Add(b);
            }
            foreach (var zone in walking.Zones)
            {
                if (zone.LiftTops.Count == 0 || zone.LiftBottoms.Count == 0) continue;
                foreach (int topId in zone.LiftTops)
                {
                    if (!_liftIndex.TryGetValue(topId, out int a)) continue;
                    foreach (int bottomId in zone.LiftBottoms)
                        if (_liftIndex.TryGetValue(bottomId, out int b) && a != b && !_liftTransfers[a].Contains(b)) _liftTransfers[a].Add(b);
                }
            }
        }

        /// <summary>Generic (not guest-specific) enjoyment of skiing a run: skill fit plus snow.</summary>
        public static float RunSkiing(SkillLevel skill, Run run)
            => GuestChoices.SkillMatchFor(skill, run.DisplayedRating) + run.AvgSnowQuality * 0.6f;

        private float BestAhead(Run run, float fromDistance, int skill)
        {
            float best = EndValue(run, skill);
            foreach (var e in run.Exits)
            {
                if (e.Distance >= fromDistance - 0.5f) continue;
                float v = ExitValue(e, skill);
                if (v > best) best = v;
            }
            return best;
        }

        private float EndValue(Run run, int skill)
        {
            if (run.EndsAtBase) return _baseValue[skill];
            float best = DeadEndValue;
            bool any = false;
            if (run.EndLiftId != 0 && _liftIndex.TryGetValue(run.EndLiftId, out int l)) { best = _liftValue[skill, l]; any = true; }
            else if (run.ContinuesIntoRunId != 0 && _runIndex.TryGetValue(run.ContinuesIntoRunId, out int r)) { best = _runValue[skill, r]; any = true; }

            // Ends on a walking area: every lift opening onto it is a short walk away.
            var zone = _sim.WalkingAreaSystem.GetZone(run.EndZoneId);
            if (zone != null)
                foreach (int liftId in zone.LiftBottoms)
                    if (_liftIndex.TryGetValue(liftId, out int zl))
                    {
                        float v = TransferDiscount * _liftValue[skill, zl];
                        if (!any || v > best) { best = v; any = true; }
                    }
            return best;
        }

        private float ExitValue(RunExit e, int skill)
        {
            if (e.Kind == RunExitKind.Lift)
                return _liftIndex.TryGetValue(e.TargetId, out int l) ? _liftValue[skill, l] : ClosedValue;
            return _runIndex.TryGetValue(e.TargetId, out int r) ? _runValue[skill, r] : ClosedValue;
        }

        private float BestDownAhead(Run run, float fromDistance)
        {
            float gamma = Lookahead;
            float best;
            if (run.EndsAtBase) best = 1f;
            else if (run.ContinuesIntoRunId != 0 && _runIndex.TryGetValue(run.ContinuesIntoRunId, out int c)) best = gamma * _runDown[c];
            else if (run.EndLiftId != 0 && _liftIndex.TryGetValue(run.EndLiftId, out int el)) best = gamma * gamma * _liftDown[el];
            else best = 0f;

            foreach (var e in run.Exits)
            {
                if (e.Distance >= fromDistance - 0.5f) continue;
                float v = e.Kind == RunExitKind.Run
                    ? (_runIndex.TryGetValue(e.TargetId, out int r) ? gamma * _runDown[r] : 0f)
                    : (_liftIndex.TryGetValue(e.TargetId, out int l) ? gamma * gamma * _liftDown[l] : 0f);
                if (v > best) best = v;
            }
            return best;
        }
    }
}
