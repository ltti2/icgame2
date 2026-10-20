using System.Collections.Generic;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Runs;

namespace IceCoast.Simulation.Commands
{
    public sealed class CreateRunCommand : ISimCommand
    {
        public readonly List<Int2> Tiles;
        public readonly string Name;

        public CreateRunCommand(IEnumerable<Int2> tiles, string name = null)
        {
            Tiles = new List<Int2>(tiles);
            Name = name;
        }

        public CommandResult Validate(GameSimulation sim) => sim.RunSystem.ValidateNewRun(Tiles);
        public void Execute(GameSimulation sim) => sim.RunSystem.CreateRun(Tiles, Name);
    }

    /// <summary>
    /// Reshape an existing run: add tiles to it and/or erase tiles from it, keeping its name, sign and
    /// settings. The run must stay one connected trail.
    /// </summary>
    public sealed class EditRunTilesCommand : ISimCommand
    {
        public readonly int RunId;
        public readonly List<Int2> Add;
        public readonly List<Int2> Remove;

        public EditRunTilesCommand(int runId, IEnumerable<Int2> add, IEnumerable<Int2> remove)
        {
            RunId = runId;
            Add = add != null ? new List<Int2>(add) : new List<Int2>();
            Remove = remove != null ? new List<Int2>(remove) : new List<Int2>();
        }

        public CommandResult Validate(GameSimulation sim) => sim.RunSystem.ValidateEdit(sim.World.GetRun(RunId), Add, Remove);
        public void Execute(GameSimulation sim) => sim.RunSystem.ApplyEdit(sim.World.GetRun(RunId), Add, Remove);
    }

    public sealed class DeleteRunCommand : ISimCommand
    {
        public readonly int RunId;
        public DeleteRunCommand(int runId) { RunId = runId; }

        public CommandResult Validate(GameSimulation sim)
            => sim.World.GetRun(RunId) != null ? CommandResult.Success() : CommandResult.Fail("That run no longer exists.");

        public void Execute(GameSimulation sim) => sim.RunSystem.DeleteRun(sim.World.GetRun(RunId));
    }

    /// <summary>Sign a run as any difficulty you like. Nobody is stopping you. (Lawyers might.)</summary>
    public sealed class SetRunRatingCommand : ISimCommand
    {
        public readonly int RunId;
        public readonly Difficulty? Rating;

        public SetRunRatingCommand(int runId, Difficulty? rating)
        {
            RunId = runId;
            Rating = rating;
        }

        public CommandResult Validate(GameSimulation sim)
            => sim.World.GetRun(RunId) != null ? CommandResult.Success() : CommandResult.Fail("That run no longer exists.");

        public void Execute(GameSimulation sim) => sim.RunSystem.SetRatingOverride(sim.World.GetRun(RunId), Rating);
    }

    public sealed class SetRunOpenCommand : ISimCommand
    {
        public readonly int RunId;
        public readonly bool Open;

        public SetRunOpenCommand(int runId, bool open)
        {
            RunId = runId;
            Open = open;
        }

        public CommandResult Validate(GameSimulation sim)
            => sim.World.GetRun(RunId) != null ? CommandResult.Success() : CommandResult.Fail("That run no longer exists.");

        public void Execute(GameSimulation sim) => sim.RunSystem.SetOpen(sim.World.GetRun(RunId), Open);
    }

    public sealed class RenameRunCommand : ISimCommand
    {
        public readonly int RunId;
        public readonly string Name;

        public RenameRunCommand(int runId, string name)
        {
            RunId = runId;
            Name = name;
        }

        public CommandResult Validate(GameSimulation sim)
        {
            if (sim.World.GetRun(RunId) == null) return CommandResult.Fail("That run no longer exists.");
            if (string.IsNullOrWhiteSpace(Name)) return CommandResult.Fail("A run needs a name.");
            return CommandResult.Success();
        }

        public void Execute(GameSimulation sim)
        {
            sim.World.GetRun(RunId).Name = Name.Trim();
            sim.World.RunsVersion++;
        }
    }
}
