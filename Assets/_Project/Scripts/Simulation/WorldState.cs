using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Land;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Lighting;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.Weather;
using IceCoast.Simulation.World;
using IceCoast.Simulation.Finance;

namespace IceCoast.Simulation
{
    /// <summary>
    /// The complete, serializable game state. Plain data: no behaviour beyond lookups.
    /// Systems (in their own folders) hold the rules and mutate this.
    /// </summary>
    public sealed class WorldState
    {
        public long Tick;
        public readonly GameTime Time = new GameTime();
        public readonly WeatherState Weather = new WeatherState();
        public readonly Economy Economy = new Economy();
        public readonly WorldMap Map;
        public readonly List<Parcel> Parcels = new List<Parcel>();
        public readonly List<Lift> Lifts = new List<Lift>();
        public readonly List<Run> Runs = new List<Run>();
        public readonly List<TrailLight> Lights = new List<TrailLight>();
        public readonly List<ParkingLot> ParkingLots = new List<ParkingLot>();
        /// <summary>
        /// The access road, as a polyline of tile coordinates from its far end (in the scenery, beyond the map
        /// edge) to where it meets the parking lot. Cars drive along it.
        /// </summary>
        public readonly List<Int2> RoadPath = new List<Int2>();
        public readonly GuestPool Guests;
        public readonly SimRandom Rng;

        /// <summary>Seed the terrain was generated from. Saved, so scenery can be regenerated identically on load.</summary>
        public int MapSeed;

        /// <summary>Generated terrain around the map edge. Visual only; not saved (rebuilt from <see cref="MapSeed"/>).</summary>
        public Scenery Scenery;

        /// <summary>Where guests arrive and leave (the parking lot / base village).</summary>
        public Int2 ResortEntrance;

        /// <summary>Fractional guests accumulated toward the next arrival.</summary>
        public float ArrivalAccumulator;

        public int NextLiftId = 1;
        public int NextRunId = 1;
        public int NextLightId = 1;
        public int NextLotId = 1;

        /// <summary>
        /// Player switch: run a night session on lifts that serve lit trails, with the trail lights on after
        /// sunset. Off = everything closes at the day close.
        /// </summary>
        public bool NightSkiing = true;

        /// <summary>Bumped whenever lifts are added/removed/changed, so views can rebuild static meshes.</summary>
        public int LiftsVersion;
        /// <summary>Bumped whenever runs are added/removed/changed.</summary>
        public int RunsVersion;
        /// <summary>Bumped when parcels are bought.</summary>
        public int ParcelsVersion;
        /// <summary>Bumped when trail lights are added or removed, or switch on or off.</summary>
        public int LightsVersion;
        /// <summary>Bumped when walking areas are painted or erased.</summary>
        public int WalkingVersion;
        /// <summary>Bumped when parking lots or the road change.</summary>
        public int ParkingVersion;

        public WorldState(int width, int height, int maxGuests, ulong seed)
        {
            Map = new WorldMap(width, height);
            Guests = new GuestPool(maxGuests);
            Rng = new SimRandom(seed);
        }

        public Lift GetLift(int id)
        {
            for (int i = 0; i < Lifts.Count; i++)
                if (Lifts[i].Id == id) return Lifts[i];
            return null;
        }

        public Run GetRun(int id)
        {
            for (int i = 0; i < Runs.Count; i++)
                if (Runs[i].Id == id) return Runs[i];
            return null;
        }

        public Parcel GetParcel(int id)
        {
            for (int i = 0; i < Parcels.Count; i++)
                if (Parcels[i].Id == id) return Parcels[i];
            return null;
        }
    }
}
