using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Lifts;

namespace IceCoast.Simulation.Commands
{
    public sealed class BuildLiftCommand : ISimCommand
    {
        public readonly LiftType Type;
        public readonly Int2 Bottom;
        public readonly Int2 Top;
        private LiftQuote _quote;

        public BuildLiftCommand(LiftType type, Int2 bottom, Int2 top)
        {
            Type = type;
            Bottom = bottom;
            Top = top;
        }

        /// <summary>The full quote from the last Validate (line tiles, towers...), for previews.</summary>
        public LiftQuote LastQuote => _quote;

        public CommandResult Validate(GameSimulation sim)
        {
            _quote = LiftPlacement.Quote(sim, Type, Bottom, Top);
            return _quote.Valid ? CommandResult.Success(_quote.Cost) : CommandResult.Fail(_quote.Error, _quote.Cost);
        }

        public void Execute(GameSimulation sim) => sim.LiftSystem.CreateLift(Type, Bottom, Top, _quote);
    }

    public sealed class DemolishLiftCommand : ISimCommand
    {
        public readonly int LiftId;
        public DemolishLiftCommand(int liftId) { LiftId = liftId; }

        public CommandResult Validate(GameSimulation sim)
            => sim.World.GetLift(LiftId) != null ? CommandResult.Success() : CommandResult.Fail("That lift no longer exists.");

        public void Execute(GameSimulation sim) => sim.LiftSystem.Demolish(sim.World.GetLift(LiftId));
    }

    public sealed class SetLiftOpenCommand : ISimCommand
    {
        public readonly int LiftId;
        public readonly bool Open;

        public SetLiftOpenCommand(int liftId, bool open)
        {
            LiftId = liftId;
            Open = open;
        }

        public CommandResult Validate(GameSimulation sim)
            => sim.World.GetLift(LiftId) != null ? CommandResult.Success() : CommandResult.Fail("That lift no longer exists.");

        public void Execute(GameSimulation sim) => sim.LiftSystem.SetPlayerOpen(sim.World.GetLift(LiftId), Open);
    }
}
