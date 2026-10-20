using System;

namespace IceCoast.Simulation.Persistence
{
    /// <summary>
    /// FNV-1a hash over the important parts of the world state. Two simulations with equal hashes
    /// are (for practical purposes) in the same state. Used by determinism tests, and handy for
    /// detecting desyncs if replays or multiplayer ever happen.
    /// </summary>
    public static class StateHasher
    {
        public static ulong Hash(GameSimulation sim)
        {
            var h = new Fnv();
            var w = sim.World;
            h.Add(w.Tick);
            h.Add(w.Time.TotalMinutes);
            h.Add((long)w.Rng.State);
            h.Add(w.Economy.Cash);
            h.Add(w.Economy.Reputation);
            h.Add((int)w.Weather.Type);
            h.Add(w.Weather.MinutesRemaining);

            var g = w.Guests;
            h.Add(g.Count);
            for (int i = 0; i < g.HighWater; i++)
            {
                if (!g.Alive[i]) continue;
                h.Add(i);
                h.Add((int)g.State[i]);
                h.Add(g.PosX[i]);
                h.Add(g.PosY[i]);
                h.Add(g.Happiness[i]);
                h.Add(g.TargetId[i]);
                h.Add(g.Car[i]);
            }

            h.Add(w.NightSkiing ? 1 : 0);
            foreach (var light in w.Lights)
            {
                h.Add(light.Id);
                h.Add((int)light.Size);
                h.Add(light.Tile.X);
                h.Add(light.Tile.Y);
            }
            foreach (var lot in w.ParkingLots)
                foreach (var space in lot.Spaces)
                {
                    h.Add((int)space.State);
                    h.Add(space.Occupants);
                    h.Add(space.Timer);
                    h.Add(space.Color);
                }

            foreach (var lift in w.Lifts)
            {
                h.Add(lift.Id);
                h.Add(lift.QueueCount);
                h.Add(lift.Riders.Count);
                h.Add((int)lift.Status);
                h.Add((float)lift.LineClock);
                h.Add((int)lift.NextCarrier);
            }

            foreach (var chunk in w.Map.Chunks)
            {
                if (!chunk.IsSimulated) continue;
                for (int i = 0; i < chunk.SnowDepth.Length; i++)
                {
                    h.Add(chunk.SnowDepth[i]);
                    h.Add(chunk.SnowQuality[i]);
                    h.Add(chunk.Compaction[i]);
                }
            }
            return h.Value;
        }

        private struct Fnv
        {
            private ulong _h;
            private bool _init;

            public ulong Value => _init ? _h : 14695981039346656037UL;

            public void Add(long v)
            {
                if (!_init) { _h = 14695981039346656037UL; _init = true; }
                for (int i = 0; i < 8; i++)
                {
                    _h ^= (byte)(v >> (i * 8));
                    _h *= 1099511628211UL;
                }
            }

            public void Add(int v) => Add((long)v);
            public void Add(float v) => Add((long)BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        }
    }
}
