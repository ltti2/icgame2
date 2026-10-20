using System.Collections.Generic;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Land;
using IceCoast.Simulation.Lifts;
using IceCoast.Simulation.Lighting;
using IceCoast.Simulation.Parking;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.Snow;
using IceCoast.Simulation.Terrain;
using IceCoast.Simulation.Time;
using IceCoast.Simulation.Trees;
using IceCoast.Simulation.Walking;
using IceCoast.Simulation.Weather;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation
{
    /// <summary>
    /// The composition root of the simulation: owns the world state, the systems and the command queue,
    /// and advances everything one fixed tick at a time. Has no idea Unity exists.
    ///
    /// Update order per tick (from the design doc):
    ///   0. queued player commands
    ///   1. time, weather and daylight (dark? lights on?)
    ///   2. economy events (arrivals, cars parking, hourly costs)
    ///   3. lifts (status, loading, riders)
    ///   4. guests
    ///   5. vehicles and staff (future)
    ///   6. snow (chunk-staggered), run stats
    ///   7. end-of-day bookkeeping and notifications
    /// </summary>
    public sealed class GameSimulation
    {
        public readonly SimConfig Config;
        public readonly WorldState World;
        public readonly EventLog Events = new EventLog();
        public readonly TerrainSettingsView TerrainScale;

        /// <summary>Formats values for display (imperial by default). Shared by event messages and the UI.</summary>
        public UnitFormatter Units { get; }

        // Ticking systems
        public TimeSystem TimeSystem { get; }
        public WeatherSystem WeatherSystem { get; }
        public LightingSystem LightingSystem { get; }
        public ArrivalSystem ArrivalSystem { get; }
        public ParkingSystem ParkingSystem { get; }
        public EconomySystem EconomySystem { get; }
        public LiftSystem LiftSystem { get; }
        public GuestSystem GuestSystem { get; }
        public SnowSystem SnowSystem { get; }
        public RunSystem RunSystem { get; }

        // Services (rules invoked by commands, no per-tick work)
        public ParcelSystem ParcelSystem { get; }
        public TerraformSystem TerraformSystem { get; }
        public TreeSystem TreeSystem { get; }
        public WalkingAreaSystem WalkingAreaSystem { get; }

        /// <summary>Minute / hour / day edges for the current tick. Set by TimeSystem before other systems run.</summary>
        public ClockSignals Clock;

        private readonly ISimSystem[] _tickOrder;
        private readonly Queue<ISimCommand> _commands = new Queue<ISimCommand>();

        public GameSimulation(SimConfig config, WorldState world)
        {
            Config = config;
            World = world;
            TerrainScale = new TerrainSettingsView(config.Terrain);
            Units = new UnitFormatter(config.Units);
            World.Economy.Sandbox = config.SandboxMode;

            TimeSystem = new TimeSystem(this);
            WeatherSystem = new WeatherSystem(this);
            LightingSystem = new LightingSystem(this);
            ArrivalSystem = new ArrivalSystem(this);
            ParkingSystem = new ParkingSystem(this);
            EconomySystem = new EconomySystem(this);
            LiftSystem = new LiftSystem(this);
            GuestSystem = new GuestSystem(this);
            SnowSystem = new SnowSystem(this);
            RunSystem = new RunSystem(this);

            ParcelSystem = new ParcelSystem(this);
            TerraformSystem = new TerraformSystem(this);
            TreeSystem = new TreeSystem(this);
            WalkingAreaSystem = new WalkingAreaSystem(this);

            _tickOrder = new ISimSystem[]
            {
                TimeSystem,
                WeatherSystem,
                LightingSystem,
                ArrivalSystem,
                ParkingSystem,
                EconomySystem,
                LiftSystem,
                GuestSystem,
                SnowSystem,
                RunSystem,
            };
        }

        public WorldMap Map => World.Map;
        public long CurrentTick => World.Tick;

        /// <summary>Queue a player action for the start of the next tick.</summary>
        public void Enqueue(ISimCommand command) => _commands.Enqueue(command);

        /// <summary>Validate without applying. Use for tool previews and cost quotes.</summary>
        public CommandResult Preview(ISimCommand command) => command.Validate(this);

        /// <summary>Validate and apply right now (setup code, tests, debug console).</summary>
        public CommandResult ExecuteImmediate(ISimCommand command)
        {
            var result = command.Validate(this);
            if (result.Ok) command.Execute(this);
            return result;
        }

        public void Tick()
        {
            World.Tick++;
            Events.SetTick(World.Tick);
            World.Guests.SnapshotPositions();

            while (_commands.Count > 0)
            {
                var command = _commands.Dequeue();
                var result = ExecuteImmediate(command);
                if (!result.Ok)
                    Events.Publish(SimEventKind.CommandRejected, EventSeverity.Warning, result.Error);
            }

            for (int i = 0; i < _tickOrder.Length; i++)
                _tickOrder[i].Tick();
        }

        /// <summary>Called after a load or world generation so systems can rebuild derived caches.</summary>
        public void RebuildDerivedState()
        {
            WalkingAreaSystem.Rebuild();
            LightingSystem.RebuildAll();
            RunSystem.RebuildAll();
            LightingSystem.RefreshFlags();
            LiftSystem.RefreshStatuses();
        }
    }
}
