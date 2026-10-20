using System;
using System.Collections.Generic;
using System.IO;
using IceCoast.Simulation;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Persistence;
using UnityEngine;

namespace IceCoast.View.Runtime
{
    /// <summary>
    /// Drives the simulation at a fixed tick rate, independent of frame rate (fixed-timestep accumulator),
    /// and is the only bridge from Unity into the simulation:
    ///  - views read <see cref="Sim"/> and <see cref="InterpolationAlpha"/>,
    ///  - tools send changes through <see cref="Submit"/>,
    ///  - everyone can subscribe to <see cref="SimulationChanged"/> (new game / load) and <see cref="EventRaised"/>.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        public static readonly float[] SpeedSteps = { 0f, 1f, 2f, 4f, 8f, 16f };

        [Tooltip("Simulation ticks per real second at 1x.")]
        [SerializeField] private int ticksPerSecond = 10;
        [Tooltip("Safety cap so a slow frame can't trigger a 'spiral of death'.")]
        [SerializeField] private int maxTicksPerFrame = 80;

        public GameSimulation Sim { get; private set; }
        public int TicksPerSecond => ticksPerSecond;
        public SimConfig Config { get; private set; }

        public int SpeedIndex { get; private set; } = 1;
        public float Speed => SpeedSteps[SpeedIndex];
        public bool Paused => SpeedIndex == 0;

        /// <summary>0..1 progress toward the next tick. Use to interpolate positions between ticks.</summary>
        public float InterpolationAlpha { get; private set; }

        /// <summary>How long the last frame's ticks took, in ms. Shown in the debug overlay.</summary>
        public float LastTickCostMs { get; private set; }

        public event Action<GameSimulation> SimulationChanged;
        public event Action<SimEvent> EventRaised;

        private double _accumulator;
        private int _speedBeforePause = 1;
        private readonly List<SimEvent> _eventBuffer = new List<SimEvent>();
        private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "saves");
        public static string QuickSavePath => Path.Combine(SaveDirectory, "quicksave.icsave");

        // ---------- Lifecycle ----------

        public void StartNewGame(SimConfig config)
        {
            Config = config;
            Install(SimulationFactory.CreateNewGame(config));
        }

        private void Install(GameSimulation sim)
        {
            Sim = sim;
            _accumulator = 0;
            InterpolationAlpha = 0f;
            SimulationChanged?.Invoke(sim);
        }

        private void Update()
        {
            if (Sim == null) return;

            _accumulator += Time.unscaledDeltaTime * ticksPerSecond * Speed;
            int steps = 0;
            _stopwatch.Restart();
            while (_accumulator >= 1.0 && steps < maxTicksPerFrame)
            {
                Sim.Tick();
                _accumulator -= 1.0;
                steps++;
            }
            if (steps == maxTicksPerFrame) _accumulator = 0; // drop time rather than fall further behind
            _stopwatch.Stop();
            LastTickCostMs = (float)_stopwatch.Elapsed.TotalMilliseconds;
            InterpolationAlpha = Mathf.Clamp01((float)_accumulator);

            _eventBuffer.Clear();
            Sim.Events.Drain(_eventBuffer);
            foreach (var e in _eventBuffer) EventRaised?.Invoke(e);
        }

        // ---------- Player actions ----------

        /// <summary>
        /// Validates and applies a command immediately (between ticks, on the main thread), so building
        /// works while paused and the player gets instant feedback. Failures are raised as events.
        /// </summary>
        public CommandResult Submit(ISimCommand command)
        {
            if (Sim == null) return CommandResult.Fail("No game running.");
            var result = Sim.ExecuteImmediate(command);
            if (!result.Ok)
                EventRaised?.Invoke(new SimEvent
                {
                    Kind = SimEventKind.CommandRejected,
                    Severity = EventSeverity.Warning,
                    Message = result.Error,
                    Location = Simulation.Core.Int2.Invalid,
                    Tick = Sim.CurrentTick,
                });
            return result;
        }

        public CommandResult Preview(ISimCommand command) => Sim != null ? Sim.Preview(command) : CommandResult.Fail("No game running.");

        // ---------- Speed ----------

        public void SetSpeedIndex(int index)
        {
            SpeedIndex = Mathf.Clamp(index, 0, SpeedSteps.Length - 1);
            if (SpeedIndex > 0) _speedBeforePause = SpeedIndex;
        }

        public void TogglePause() => SetSpeedIndex(Paused ? _speedBeforePause : 0);
        public void Faster() => SetSpeedIndex(SpeedIndex + 1);
        public void Slower() => SetSpeedIndex(Math.Max(1, SpeedIndex - 1));

        // ---------- Save / load ----------

        public bool Save(string path, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SaveSystem.SaveToFile(Sim, path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return false;
            }
        }

        public bool Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path))
            {
                error = "No save file found.";
                return false;
            }
            try
            {
                Install(SaveSystem.LoadFromFile(path, Config));
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return false;
            }
        }
    }
}
