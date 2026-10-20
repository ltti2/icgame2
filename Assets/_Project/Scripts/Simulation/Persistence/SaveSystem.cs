using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using IceCoast.Simulation.Buildings;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Land;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Lighting;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.Terrain;
using IceCoast.Simulation.Weather;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Persistence
{
    /// <summary>
    /// Binary save format, gzip-compressed. Versioned from day one:
    ///  - bump <see cref="CurrentVersion"/> whenever the layout changes,
    ///  - write new fields at the end of their section,
    ///  - in Read*, guard new fields with <c>if (version >= N)</c> and supply a default otherwise.
    /// Config is NOT saved (it comes from the SimConfigAsset); only state is.
    /// The save is complete enough that save -> load -> continue is bit-identical to never saving
    /// (see DeterminismTests).
    /// </summary>
    public static class SaveSystem
    {
        private const uint Magic = 0x56534349; // "ICSV"
        // v1: initial format.
        // v2: map seed (scenery is regenerated from it on load), per-guest run exit cursor.
        // v3: per-guest line across the run (skiers spread out and chase snow).
        // v4: discrete lift carriers.
        // v5: night skiing switch, trail lights, per-tile surface (walking areas, road, parking), parking lots,
        //     the road, each guest's car, cars turned away in the ledger. Older saves get the default road and lot.
        // v6: buildings (Waffle Cabins) with their lines and sales.
        public const int CurrentVersion = 6;

        public static void Save(GameSimulation sim, Stream output)
        {
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            using (var w = new BinaryWriter(gz))
            {
                w.Write(Magic);
                w.Write(CurrentVersion);
                WriteWorld(w, sim);
            }
        }

        public static GameSimulation Load(Stream input, SimConfig config)
        {
            using (var gz = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true))
            using (var r = new BinaryReader(gz))
            {
                if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not an Ice Coast save file.");
                int version = r.ReadInt32();
                if (version > CurrentVersion) throw new InvalidDataException($"Save is from a newer version ({version}).");
                return ReadWorld(r, version, config);
            }
        }

        public static void SaveToFile(GameSimulation sim, string path)
        {
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) Save(sim, fs);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path); // write-then-rename so a crash never leaves a half-written save
        }

        public static GameSimulation LoadFromFile(string path, SimConfig config)
        {
            using (var fs = File.OpenRead(path)) return Load(fs, config);
        }

        // =====================================================================
        // Write
        // =====================================================================

        private static void WriteWorld(BinaryWriter w, GameSimulation sim)
        {
            var world = sim.World;
            w.Write(world.Map.Width);
            w.Write(world.Map.Height);
            w.Write(world.Guests.Capacity);

            w.Write(world.Tick);
            w.Write(world.Time.TotalMinutes);
            w.Write((byte)world.Time.StartWeekday);
            w.Write(world.Rng.State);
            WriteInt2(w, world.ResortEntrance);
            w.Write(world.NextLiftId);
            w.Write(world.NextRunId);
            w.Write(world.ArrivalAccumulator);
            w.Write(sim.ArrivalSystem.CurrentRatePerHour);
            w.Write(world.MapSeed); // v2
            w.Write(world.NightSkiing); // v5
            w.Write(world.NextLightId);
            w.Write(world.NextLotId);

            WriteWeather(w, world.Weather);
            WriteEconomy(w, world.Economy);
            WriteMap(w, world.Map);
            WriteParcels(w, world.Parcels);
            WriteLifts(w, world.Lifts);
            WriteRuns(w, world.Runs);
            WriteGuests(w, world.Guests);
            WriteLights(w, world.Lights);         // v5
            WriteParking(w, world.ParkingLots, world.RoadPath); // v5
            w.Write(world.NextBuildingId);                       // v6
            WriteBuildings(w, world.Buildings);
        }

        private static void WriteBuildings(BinaryWriter w, List<Building> buildings)
        {
            w.Write(buildings.Count);
            foreach (var b in buildings)
            {
                w.Write(b.Id);
                w.Write((byte)b.Type);
                w.Write(b.Name ?? "");
                WriteInt2(w, b.Origin);
                w.Write(b.Width);
                w.Write(b.Height);
                w.Write(b.Price);
                w.Write(b.PlayerOpen);
                w.Write(b.ServiceTimer);
                w.Write(b.SoldToday);
                w.Write(b.RevenueToday);
                w.Write(b.PricedOutToday);
                w.Write(b.CostToday);
                w.Write(b.SoldTotal);
                w.Write(b.RevenueTotal);
                w.Write(b.Queue.Count);
                foreach (int g in b.Queue) w.Write(g);
            }
        }

        private static void ReadBuildings(BinaryReader r, List<Building> buildings)
        {
            int n = r.ReadInt32();
            buildings.Clear();
            for (int i = 0; i < n; i++)
            {
                var b = new Building
                {
                    Id = r.ReadInt32(),
                    Type = (BuildingType)r.ReadByte(),
                    Name = r.ReadString(),
                    Origin = ReadInt2(r),
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    Price = r.ReadInt32(),
                    PlayerOpen = r.ReadBoolean(),
                    ServiceTimer = r.ReadInt32(),
                    SoldToday = r.ReadInt32(),
                    RevenueToday = r.ReadInt64(),
                    PricedOutToday = r.ReadInt32(),
                    CostToday = r.ReadInt64(),
                    SoldTotal = r.ReadInt32(),
                    RevenueTotal = r.ReadInt64(),
                };
                int queued = r.ReadInt32();
                for (int k = 0; k < queued; k++) b.Queue.Add(r.ReadInt32());
                buildings.Add(b);
            }
        }

        private static void WriteLights(BinaryWriter w, List<TrailLight> lights)
        {
            w.Write(lights.Count);
            foreach (var l in lights)
            {
                w.Write(l.Id);
                w.Write((byte)l.Size);
                WriteInt2(w, l.Tile);
            }
        }

        private static void WriteParking(BinaryWriter w, List<ParkingLot> lots, List<Int2> road)
        {
            w.Write(road.Count);
            foreach (var p in road) WriteInt2(w, p);
            w.Write(lots.Count);
            foreach (var lot in lots)
            {
                w.Write(lot.Id);
                w.Write(lot.Name ?? "");
                w.Write(lot.X); w.Write(lot.Y); w.Write(lot.Width); w.Write(lot.Height);
                w.Write(lot.AisleY);
                WriteInt2(w, lot.Entrance);
                w.Write(lot.Spaces.Length);
                foreach (var sp in lot.Spaces)
                {
                    w.Write(sp.X); w.Write(sp.Y); w.Write(sp.Facing);
                    w.Write((byte)sp.State); w.Write(sp.Color); w.Write(sp.PartySize); w.Write(sp.Occupants);
                    w.Write(sp.Timer); w.Write(sp.DriveTicks);
                }
            }
        }

        private static void WriteWeather(BinaryWriter w, WeatherState s)
        {
            w.Write((byte)s.Type);
            w.Write(s.BaseTemperature);
            w.Write(s.MeanTemperature);
            w.Write(s.WindSpeed);
            w.Write(s.WindDirection);
            w.Write(s.Precipitation);
            w.Write(s.Visibility);
            w.Write(s.MinutesRemaining);
            w.Write(s.Forecast.Count);
            foreach (var f in s.Forecast)
            {
                w.Write((byte)f.Type);
                w.Write(f.DurationHours);
                w.Write(f.Confidence);
            }
        }

        private static void WriteLedger(BinaryWriter w, DailyLedger l)
        {
            w.Write(l.Day);
            w.Write(l.Income.Length);
            foreach (var v in l.Income) w.Write(v);
            foreach (var v in l.Expenses) w.Write(v);
            w.Write(l.GuestsArrived);
            w.Write(l.GuestsDeparted);
            w.Write(l.HappinessSum);
            w.Write(l.Injuries);
            w.Write(l.PeakGuests);
            w.Write(l.LiftRides);
            w.Write(l.RunsCompleted);
            w.Write(l.CashAtEndOfDay);
            w.Write(l.ReputationAtEndOfDay);
            w.Write(l.CarsTurnedAway); // v5
            w.Write(l.GuestsTurnedAway);
        }

        private static void WriteEconomy(BinaryWriter w, Economy e)
        {
            w.Write(e.Cash);
            w.Write(e.TicketPrice);
            w.Write(e.Reputation);
            w.Write(e.DaysInDebt);
            w.Write(e.Bankrupt);
            WriteLedger(w, e.Today);
            w.Write(e.History.Count);
            foreach (var l in e.History) WriteLedger(w, l);
        }

        private static void WriteMap(BinaryWriter w, WorldMap map)
        {
            foreach (var c in map.Chunks)
            {
                foreach (var v in c.VertexHeight) w.Write(v);
                w.Write(c.TerrainType);
                w.Write(c.TreeDensity);
                foreach (var v in c.SnowDepth) w.Write(v);
                w.Write(c.SnowQuality);
                w.Write(c.Compaction);
                w.Write(c.Moisture);
                foreach (var v in c.OccupantId) w.Write(v);
                foreach (var v in c.RunId) w.Write(v);
                w.Write(c.Flags);
                foreach (var v in c.Traffic) w.Write(v);
                w.Write(c.Surface); // v5
            }
        }

        private static void WriteParcels(BinaryWriter w, List<Parcel> parcels)
        {
            w.Write(parcels.Count);
            foreach (var p in parcels)
            {
                w.Write(p.Id);
                w.Write(p.Name ?? "");
                w.Write(p.X); w.Write(p.Y); w.Write(p.Width); w.Write(p.Height);
                w.Write(p.Cost);
                w.Write(p.RequiredReputation);
                w.Write(p.ElevationBand);
                w.Write(p.Owned);
            }
        }

        private static void WriteLifts(BinaryWriter w, List<Lift> lifts)
        {
            w.Write(lifts.Count);
            foreach (var l in lifts)
            {
                w.Write(l.Id);
                w.Write(l.Name ?? "");
                w.Write((byte)l.Type);
                WriteInt2(w, l.BottomStation);
                WriteInt2(w, l.TopStation);
                w.Write(l.Towers.Length);
                foreach (var t in l.Towers) WriteInt2(w, t);
                w.Write(l.LengthTiles);
                w.Write(l.RiseSteps);
                w.Write(l.RideTicks);
                w.Write(l.CapacityPerHour);
                w.Write(l.SpeedTilesPerMinute);
                w.Write((byte)l.Status);
                w.Write(l.PlayerOpen);
                w.Write(l.WindLimit);
                w.Write(l.PowerDraw);
                w.Write(l.StaffRequired);
                w.Write(l.OperatingCostPerHour);
                w.Write(l.LoadAccumulator);
                w.Write(l.RidersToday);
                w.Write(l.QueueGuests.Count);
                foreach (var g in l.QueueGuests) w.Write(g);
                w.Write(l.Riders.Count);
                foreach (var r in l.Riders) { w.Write(r.GuestIndex); w.Write(r.BoardClock); w.Write(r.Seat); }
                // v4: discrete carriers.
                w.Write(l.LineClock);
                w.Write(l.NextCarrier);
            }
        }

        private static void WriteRuns(BinaryWriter w, List<Run> runs)
        {
            w.Write(runs.Count);
            foreach (var r in runs)
            {
                w.Write(r.Id);
                w.Write(r.Name ?? "");
                w.Write(r.Tiles.Count);
                foreach (var t in r.Tiles) WriteInt2(w, t);
                w.Write(r.RatingOverride.HasValue);
                w.Write((byte)(r.RatingOverride ?? Difficulty.Green));
                w.Write(r.Open);
                w.Write(r.GroomPriority);
                w.Write(r.AvgSnowQuality);
                w.Write((byte)r.Issues);
                w.Write(r.SkiersToday);
                w.Write(r.InjuriesToday);
            }
        }

        private static void WriteGuests(BinaryWriter w, GuestPool p)
        {
            w.Write(p.HighWater);
            var free = p.GetFreeListSnapshot();
            w.Write(free.Length);
            foreach (var f in free) w.Write(f);

            for (int i = 0; i < p.HighWater; i++)
            {
                w.Write(p.Alive[i]);
                if (!p.Alive[i]) continue;
                w.Write((byte)p.State[i]);
                w.Write((byte)p.Skill[i]);
                w.Write((byte)p.Flags[i]);
                w.Write(p.Energy[i]);
                w.Write(p.Hunger[i]);
                w.Write(p.Warmth[i]);
                w.Write(p.Happiness[i]);
                w.Write(p.Money[i]);
                w.Write(p.PosX[i]); w.Write(p.PosY[i]);
                w.Write(p.PrevX[i]); w.Write(p.PrevY[i]);
                w.Write(p.Lane[i]);
                w.Write(p.TargetId[i]);
                w.Write(p.WaypointKey[i]);
                w.Write(p.StateTimer[i]);
                w.Write(p.ArrivalMinute[i]);
                w.Write(p.DepartureMinute[i]);
                w.Write(p.RunsSkied[i]);
                w.Write(p.Steps[i]);
                w.Write(p.ExitCursor[i]); // v2
                w.Write(p.LastRunId[i]);
                w.Write(p.AvoidMask[i]);
                w.Write(p.Line[i]); // v3
                w.Write(p.Car[i]);  // v5
            }
        }

        private static void WriteInt2(BinaryWriter w, Int2 p) { w.Write(p.X); w.Write(p.Y); }

        // =====================================================================
        // Read
        // =====================================================================

        private static GameSimulation ReadWorld(BinaryReader r, int version, SimConfig config)
        {
            int width = r.ReadInt32(), height = r.ReadInt32(), guestCapacity = r.ReadInt32();
            var world = new WorldState(width, height, guestCapacity, 1);

            world.Tick = r.ReadInt64();
            world.Time.TotalMinutes = r.ReadInt64();
            world.Time.StartWeekday = (Weekday)r.ReadByte();
            world.Rng.State = r.ReadUInt64();
            world.ResortEntrance = ReadInt2(r);
            world.NextLiftId = r.ReadInt32();
            world.NextRunId = r.ReadInt32();
            world.ArrivalAccumulator = r.ReadSingle();
            float arrivalRate = r.ReadSingle();
            world.MapSeed = version >= 2 ? r.ReadInt32() : config.Map.Seed;
            if (version >= 5)
            {
                world.NightSkiing = r.ReadBoolean();
                world.NextLightId = r.ReadInt32();
                world.NextLotId = r.ReadInt32();
            }

            ReadWeather(r, world.Weather);
            ReadEconomy(r, world.Economy, version);
            ReadMap(r, world.Map, version);
            ReadParcels(r, world.Parcels);
            ReadLifts(r, world.Lifts, version, world.Tick);
            var snowStats = ReadRuns(r, world.Runs);
            ReadGuests(r, world.Guests, version);
            if (version >= 5)
            {
                ReadLights(r, world.Lights);
                ReadParking(r, world.ParkingLots, world.RoadPath);
            }
            if (version >= 6)
            {
                world.NextBuildingId = r.ReadInt32();
                ReadBuildings(r, world.Buildings);
            }

            var sim = new GameSimulation(config, world);
            sim.ArrivalSystem.RestoreRate(arrivalRate);

            // The starting road now runs to the edge of the world: lengthen it in saves that stopped short.
            if (version >= 5 && world.RoadPath.Count == 2)
            {
                var road = sim.ParkingSystem.DefaultLayout().Road;
                if (world.RoadPath[1].Equals(road[1]) && world.RoadPath[0].Y == road[0].Y && world.RoadPath[0].X != road[0].X)
                {
                    world.RoadPath[0] = road[0];
                    world.ParkingVersion++;
                }
            }

            if (version < 5)
            {
                // Before parking existed: pave the default road and lot (and the base plaza) on the saved terrain.
                var layout = sim.ParkingSystem.DefaultLayout();
                layout.ApplySurfaces(world.Map);
                sim.ParkingSystem.CreateLot(layout, config.Parking.DefaultLotCapacity, "Main Lot");
            }

            foreach (var lift in world.Lifts)
            {
                // Lift stats follow the config, so retuned speeds and capacities reach lifts built earlier.
                sim.LiftSystem.ApplyDefinition(lift);
                lift.EffectiveCapacityPerHour = sim.LiftSystem.EffectiveCapacity(lift);
                // Pre-v4 saves had no carriers: start the next one at the current line position.
                if (version < 4) lift.NextCarrier = (long)Math.Ceiling(lift.LineClock / sim.LiftSystem.CarrierInterval(lift));
            }

            // Scenery is not saved: rebuild it from the seed the map was generated with.
            TerrainGenerator.RegenerateScenery(sim, world.MapSeed);

            // Rebuild caches (walking zones, light levels, flow fields, connections) but keep the saved
            // snapshot values so the loaded game continues exactly where it left off.
            var savedCursors = (byte[])world.Guests.ExitCursor.Clone();
            sim.WalkingAreaSystem.Rebuild();
            sim.LightingSystem.RebuildAll();
            sim.RunSystem.RebuildAll();
            sim.LightingSystem.RefreshFlags();
            Array.Copy(savedCursors, world.Guests.ExitCursor, savedCursors.Length);
            foreach (var run in world.Runs)
            {
                var (quality, icy) = snowStats[run.Id];
                run.AvgSnowQuality = quality;
                if (icy) run.Issues |= RunIssues.Icy; else run.Issues &= ~RunIssues.Icy;
            }
            return sim;
        }

        private static void ReadWeather(BinaryReader r, WeatherState s)
        {
            s.Type = (WeatherType)r.ReadByte();
            s.BaseTemperature = r.ReadSingle();
            s.MeanTemperature = r.ReadSingle();
            s.WindSpeed = r.ReadSingle();
            s.WindDirection = r.ReadSingle();
            s.Precipitation = r.ReadSingle();
            s.Visibility = r.ReadSingle();
            s.MinutesRemaining = r.ReadInt32();
            int n = r.ReadInt32();
            s.Forecast.Clear();
            for (int i = 0; i < n; i++)
                s.Forecast.Add(new ForecastEntry { Type = (WeatherType)r.ReadByte(), DurationHours = r.ReadInt32(), Confidence = r.ReadSingle() });
        }

        private static DailyLedger ReadLedger(BinaryReader r, int version)
        {
            var l = new DailyLedger { Day = r.ReadInt32() };
            int categories = r.ReadInt32();
            for (int i = 0; i < categories; i++) { long v = r.ReadInt64(); if (i < l.Income.Length) l.Income[i] = v; }
            for (int i = 0; i < categories; i++) { long v = r.ReadInt64(); if (i < l.Expenses.Length) l.Expenses[i] = v; }
            l.GuestsArrived = r.ReadInt32();
            l.GuestsDeparted = r.ReadInt32();
            l.HappinessSum = r.ReadDouble();
            l.Injuries = r.ReadInt32();
            l.PeakGuests = r.ReadInt32();
            l.LiftRides = r.ReadInt32();
            l.RunsCompleted = r.ReadInt32();
            l.CashAtEndOfDay = r.ReadInt64();
            l.ReputationAtEndOfDay = r.ReadSingle();
            if (version >= 5)
            {
                l.CarsTurnedAway = r.ReadInt32();
                l.GuestsTurnedAway = r.ReadInt32();
            }
            return l;
        }

        private static void ReadEconomy(BinaryReader r, Economy e, int version)
        {
            e.Cash = r.ReadInt64();
            e.TicketPrice = r.ReadInt32();
            e.Reputation = r.ReadSingle();
            e.DaysInDebt = r.ReadInt32();
            e.Bankrupt = r.ReadBoolean();
            e.Today = ReadLedger(r, version);
            int n = r.ReadInt32();
            e.History.Clear();
            for (int i = 0; i < n; i++) e.History.Add(ReadLedger(r, version));
        }

        private static void ReadMap(BinaryReader r, WorldMap map, int version)
        {
            foreach (var c in map.Chunks)
            {
                for (int i = 0; i < c.VertexHeight.Length; i++) c.VertexHeight[i] = r.ReadInt16();
                ReadBytes(r, c.TerrainType);
                ReadBytes(r, c.TreeDensity);
                for (int i = 0; i < c.SnowDepth.Length; i++) c.SnowDepth[i] = r.ReadUInt16();
                ReadBytes(r, c.SnowQuality);
                ReadBytes(r, c.Compaction);
                ReadBytes(r, c.Moisture);
                for (int i = 0; i < c.OccupantId.Length; i++) c.OccupantId[i] = r.ReadUInt16();
                for (int i = 0; i < c.RunId.Length; i++) c.RunId[i] = r.ReadUInt16();
                ReadBytes(r, c.Flags);
                for (int i = 0; i < c.Traffic.Length; i++) c.Traffic[i] = r.ReadUInt16();
                if (version >= 5) ReadBytes(r, c.Surface);

                c.OwnedTileCount = 0;
                foreach (var f in c.Flags) if ((f & (byte)TileFlags.Owned) != 0) c.OwnedTileCount++;
                c.MarkShapeChanged();
            }
        }

        private static void ReadBytes(BinaryReader r, byte[] into)
        {
            int read = 0;
            while (read < into.Length)
            {
                int n = r.Read(into, read, into.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
        }

        private static void ReadParcels(BinaryReader r, List<Parcel> parcels)
        {
            int n = r.ReadInt32();
            parcels.Clear();
            for (int i = 0; i < n; i++)
            {
                parcels.Add(new Parcel
                {
                    Id = r.ReadInt32(),
                    Name = r.ReadString(),
                    X = r.ReadInt32(), Y = r.ReadInt32(), Width = r.ReadInt32(), Height = r.ReadInt32(),
                    Cost = r.ReadInt64(),
                    RequiredReputation = r.ReadSingle(),
                    ElevationBand = r.ReadInt32(),
                    Owned = r.ReadBoolean(),
                });
            }
        }

        private static void ReadLifts(BinaryReader r, List<Lift> lifts, int version, long worldTick)
        {
            int n = r.ReadInt32();
            lifts.Clear();
            for (int i = 0; i < n; i++)
            {
                var l = new Lift
                {
                    Id = r.ReadInt32(),
                    Name = r.ReadString(),
                    Type = (LiftType)r.ReadByte(),
                    BottomStation = ReadInt2(r),
                    TopStation = ReadInt2(r),
                };
                l.Towers = new Int2[r.ReadInt32()];
                for (int t = 0; t < l.Towers.Length; t++) l.Towers[t] = ReadInt2(r);
                l.LengthTiles = r.ReadSingle();
                l.RiseSteps = r.ReadInt32();
                l.RideTicks = r.ReadInt32();
                l.CapacityPerHour = r.ReadInt32();
                l.SpeedTilesPerMinute = r.ReadSingle();
                l.Status = (LiftStatus)r.ReadByte();
                l.PlayerOpen = r.ReadBoolean();
                l.WindLimit = r.ReadSingle();
                l.PowerDraw = r.ReadSingle();
                l.StaffRequired = r.ReadInt32();
                l.OperatingCostPerHour = r.ReadInt64();
                l.LoadAccumulator = r.ReadSingle();
                l.RidersToday = r.ReadInt32();
                int q = r.ReadInt32();
                for (int k = 0; k < q; k++) l.QueueGuests.Enqueue(r.ReadInt32());
                int riders = r.ReadInt32();
                for (int k = 0; k < riders; k++)
                {
                    if (version >= 4)
                        l.Riders.Enqueue(new LiftRider { GuestIndex = r.ReadInt32(), BoardClock = r.ReadDouble(), Seat = r.ReadByte() });
                    else // v3 and older stored the world tick a rider boarded; the line clock was the world clock.
                        l.Riders.Enqueue(new LiftRider { GuestIndex = r.ReadInt32(), BoardClock = r.ReadInt64() });
                }
                if (version >= 4)
                {
                    l.LineClock = r.ReadDouble();
                    l.NextCarrier = r.ReadInt64();
                }
                else
                {
                    l.LineClock = worldTick;
                    l.NextCarrier = 0; // caught up on the first tick (CarrierInterval needs the config)
                }
                lifts.Add(l);
            }
        }

        private static Dictionary<int, (float, bool)> ReadRuns(BinaryReader r, List<Run> runs)
        {
            var snow = new Dictionary<int, (float, bool)>();
            int n = r.ReadInt32();
            runs.Clear();
            for (int i = 0; i < n; i++)
            {
                var run = new Run { Id = r.ReadInt32(), Name = r.ReadString() };
                int tiles = r.ReadInt32();
                for (int t = 0; t < tiles; t++) run.Tiles.Add(ReadInt2(r));
                bool hasOverride = r.ReadBoolean();
                var rating = (Difficulty)r.ReadByte();
                run.RatingOverride = hasOverride ? rating : (Difficulty?)null;
                run.Open = r.ReadBoolean();
                run.GroomPriority = r.ReadInt32();
                float quality = r.ReadSingle();
                var issues = (RunIssues)r.ReadByte();
                run.SkiersToday = r.ReadInt32();
                run.InjuriesToday = r.ReadInt32();
                snow[run.Id] = (quality, (issues & RunIssues.Icy) != 0);
                runs.Add(run);
            }
            return snow;
        }

        private static void ReadGuests(BinaryReader r, GuestPool p, int version)
        {
            int highWater = r.ReadInt32();
            var free = new int[r.ReadInt32()];
            for (int i = 0; i < free.Length; i++) free[i] = r.ReadInt32();

            for (int i = 0; i < highWater; i++)
            {
                p.Alive[i] = r.ReadBoolean();
                if (!p.Alive[i]) { p.State[i] = GuestState.Inactive; continue; }
                p.State[i] = (GuestState)r.ReadByte();
                p.Skill[i] = (SkillLevel)r.ReadByte();
                p.Flags[i] = (GuestFlags)r.ReadByte();
                p.Energy[i] = r.ReadByte();
                p.Hunger[i] = r.ReadByte();
                p.Warmth[i] = r.ReadByte();
                p.Happiness[i] = r.ReadSingle();
                p.Money[i] = r.ReadInt32();
                p.PosX[i] = r.ReadSingle(); p.PosY[i] = r.ReadSingle();
                p.PrevX[i] = r.ReadSingle(); p.PrevY[i] = r.ReadSingle();
                p.Lane[i] = r.ReadSingle();
                p.TargetId[i] = r.ReadInt32();
                p.WaypointKey[i] = r.ReadInt32();
                p.StateTimer[i] = r.ReadInt32();
                p.ArrivalMinute[i] = r.ReadInt32();
                p.DepartureMinute[i] = r.ReadInt32();
                p.RunsSkied[i] = r.ReadInt16();
                p.Steps[i] = r.ReadInt16();
                p.ExitCursor[i] = version >= 2 ? r.ReadByte() : (byte)0;
                p.LastRunId[i] = r.ReadInt32();
                p.AvoidMask[i] = r.ReadUInt64();
                p.Line[i] = version >= 3 ? r.ReadSingle() : 0.5f;
                p.Car[i] = version >= 5 ? r.ReadInt32() : ParkingSystem.NoCar;
            }
            p.RestoreAllocation(highWater, free);
        }

        private static void ReadLights(BinaryReader r, List<TrailLight> lights)
        {
            int n = r.ReadInt32();
            lights.Clear();
            for (int i = 0; i < n; i++)
                lights.Add(new TrailLight { Id = r.ReadInt32(), Size = (LightSize)r.ReadByte(), Tile = ReadInt2(r) });
        }

        private static void ReadParking(BinaryReader r, List<ParkingLot> lots, List<Int2> road)
        {
            int points = r.ReadInt32();
            road.Clear();
            for (int i = 0; i < points; i++) road.Add(ReadInt2(r));
            int n = r.ReadInt32();
            lots.Clear();
            for (int i = 0; i < n; i++)
            {
                var lot = new ParkingLot
                {
                    Id = r.ReadInt32(),
                    Name = r.ReadString(),
                    X = r.ReadInt32(), Y = r.ReadInt32(), Width = r.ReadInt32(), Height = r.ReadInt32(),
                    AisleY = r.ReadInt32(),
                    Entrance = ReadInt2(r),
                };
                lot.Spaces = new ParkingSpace[r.ReadInt32()];
                for (int k = 0; k < lot.Spaces.Length; k++)
                {
                    lot.Spaces[k] = new ParkingSpace
                    {
                        X = r.ReadSingle(), Y = r.ReadSingle(), Facing = r.ReadSByte(),
                        State = (CarState)r.ReadByte(), Color = r.ReadByte(), PartySize = r.ReadByte(), Occupants = r.ReadByte(),
                        Timer = r.ReadInt32(), DriveTicks = r.ReadInt32(),
                    };
                }
                lots.Add(lot);
            }
        }

        private static Int2 ReadInt2(BinaryReader r) => new Int2(r.ReadInt32(), r.ReadInt32());
    }
}
