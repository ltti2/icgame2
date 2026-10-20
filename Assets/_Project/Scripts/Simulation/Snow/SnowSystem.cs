using System;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Snow
{
    /// <summary>
    /// Per-tile snow simulation (version 0 of milestone M3).
    ///
    /// Staggered: each owned chunk is processed once every UpdateIntervalTicks, offset by its index,
    /// so cost is spread evenly over ticks instead of spiking. Unowned chunks are never touched.
    ///
    /// Quality is a small state machine over depth, compaction, moisture and temperature.
    /// Not yet modelled (later milestones): wind drift, snowmaking, grooming schedules.
    /// </summary>
    public sealed class SnowSystem : ISimSystem
    {
        private readonly GameSimulation _sim;

        public SnowSystem(GameSimulation sim) { _sim = sim; }

        public void Tick()
        {
            var cfg = _sim.Config.Snow;
            int interval = Math.Max(1, cfg.UpdateIntervalTicks);
            long phase = _sim.World.Tick % interval;
            var chunks = _sim.Map.Chunks;

            for (int c = (int)phase; c < chunks.Length; c += interval)
            {
                var chunk = chunks[c];
                if (chunk.IsSimulated) UpdateChunk(chunk, interval);
            }
        }

        private void UpdateChunk(Chunk chunk, int intervalTicks)
        {
            var cfg = _sim.Config.Snow;
            var weather = _sim.World.Weather;
            var rng = _sim.World.Rng;
            var map = _sim.Map;
            float hours = intervalTicks / (float)_sim.TimeSystem.TicksPerHour;
            bool changed = false;

            int baseX = chunk.ChunkX * Chunk.Size, baseY = chunk.ChunkY * Chunk.Size;
            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    int i = Chunk.TileIndex(lx, ly);
                    if ((chunk.Flags[i] & (byte)TileFlags.Owned) == 0) continue;

                    // Roads and parking lots are plowed.
                    byte surface = chunk.Surface[i];
                    if (surface == (byte)SurfaceType.Road || surface == (byte)SurfaceType.Parking)
                    {
                        chunk.Traffic[i] = 0;
                        if (chunk.SnowDepth[i] != 0) { chunk.SnowDepth[i] = 0; chunk.SnowQuality[i] = (byte)SnowQuality.Bare; changed = true; }
                        continue;
                    }

                    int x = baseX + lx, y = baseY + ly;
                    float temp = _sim.WeatherSystem.TemperatureAt(x, y);
                    float depth = chunk.SnowDepth[i];
                    var quality = (SnowQuality)chunk.SnowQuality[i];
                    int compaction = chunk.Compaction[i];
                    int moisture = chunk.Moisture[i];
                    int oldDepthBucket = chunk.SnowDepth[i] / 10;
                    var oldQuality = quality;

                    // Precipitation
                    if (weather.Precipitation > 0f)
                    {
                        float amount = weather.Precipitation * hours;
                        if (temp <= 0.5f)
                        {
                            float elev = TerrainQueries.ElevationMeters(map, _sim.TerrainScale, x, y);
                            depth += amount * _sim.WeatherSystem.SnowfallElevationFactor(elev);
                            if (amount * 4f >= 1f || rng.Chance(amount * 4f))
                            {
                                quality = SnowQuality.Powder;
                                compaction = compaction * 7 / 10;
                            }
                        }
                        else
                        {
                            moisture += (int)(60f * amount);
                            depth -= amount * 0.5f; // rain eats snow
                        }
                    }

                    // Melt
                    if (temp > 0f)
                    {
                        float sun = Math.Max(0f, TerrainQueries.SunExposure(map, x, y));
                        float melt = cfg.MeltCmPerDegreeHour * temp * hours * (1f + sun * cfg.SouthAspectMeltBonus);
                        depth -= melt;
                        moisture += (int)(melt * 8f);
                    }
                    else
                    {
                        moisture -= (int)(6f * hours * 10f); // dries out slowly
                    }

                    // Skier traffic packs the snow
                    int traffic = chunk.Traffic[i];
                    if (traffic > 0)
                    {
                        compaction += traffic * cfg.CompactionPerSkier;
                        chunk.Traffic[i] = 0;
                    }

                    depth = Math.Max(0f, depth);
                    compaction = SimMath.Clamp(compaction, 0, 255);
                    moisture = SimMath.Clamp(moisture, 0, 255);
                    quality = NextQuality(quality, depth, compaction, moisture, temp, cfg);

                    chunk.SnowDepth[i] = (ushort)Math.Min(ushort.MaxValue, depth + (rng.Chance(depth - (float)Math.Floor(depth)) ? 1 : 0));
                    chunk.Compaction[i] = (byte)compaction;
                    chunk.Moisture[i] = (byte)moisture;
                    chunk.SnowQuality[i] = (byte)quality;

                    if (quality != oldQuality || chunk.SnowDepth[i] / 10 != oldDepthBucket) changed = true;
                }
            }

            if (changed) chunk.MarkSurfaceChanged();
        }

        /// <summary>The snow quality state machine. Pure function: easy to unit test and tune.</summary>
        public static SnowQuality NextQuality(SnowQuality q, float depth, int compaction, int moisture, float temp,
            Config.SimConfig.SnowSettings cfg)
        {
            if (depth < cfg.BareDepthCm) return SnowQuality.Bare;
            if (temp > 0.5f && moisture > 60) return SnowQuality.Slush;
            if (temp < -1f && (q == SnowQuality.Slush || moisture > 120)) return SnowQuality.Ice; // refreeze

            switch (q)
            {
                case SnowQuality.Powder:
                    return compaction >= cfg.PackedThreshold ? SnowQuality.Packed
                         : compaction >= cfg.CrudThreshold ? SnowQuality.Crud : SnowQuality.Powder;
                case SnowQuality.Crud:
                    return compaction >= cfg.PackedThreshold ? SnowQuality.Packed : SnowQuality.Crud;
                case SnowQuality.Groomed:
                    return compaction >= cfg.PackedThreshold ? SnowQuality.Packed : SnowQuality.Groomed;
                case SnowQuality.Packed:
                    return compaction >= cfg.IceThreshold ? SnowQuality.Ice : SnowQuality.Packed;
                case SnowQuality.Slush:
                    return temp <= 0.5f ? SnowQuality.Packed : SnowQuality.Slush;
                case SnowQuality.Bare:
                    return SnowQuality.Packed;
                default:
                    return q; // Ice stays until groomed or buried
            }
        }

        /// <summary>Grooming a tile (milestone M4 groomers will call this).</summary>
        public void Groom(int x, int y)
        {
            var t = _sim.Map.Locate(x, y);
            var chunk = t.Chunk;
            if (chunk.SnowDepth[t.Index] < _sim.Config.Snow.BareDepthCm) return;
            chunk.SnowQuality[t.Index] = (byte)SnowQuality.Groomed;
            chunk.Compaction[t.Index] = 0;
            chunk.Moisture[t.Index] = (byte)(chunk.Moisture[t.Index] / 2);
            chunk.MarkSurfaceChanged();
        }
    }
}
