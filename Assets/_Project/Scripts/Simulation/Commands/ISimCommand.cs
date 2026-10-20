namespace IceCoast.Simulation.Commands
{
    /// <summary>
    /// Result of validating or executing a command. Validation doubles as the "quote" the UI shows
    /// while the player is previewing an action (cost, reason it is not allowed).
    /// </summary>
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Error;
        public readonly long Cost;

        private CommandResult(bool ok, string error, long cost)
        {
            Ok = ok;
            Error = error;
            Cost = cost;
        }

        public static CommandResult Success(long cost = 0) => new CommandResult(true, null, cost);
        public static CommandResult Fail(string error, long cost = 0) => new CommandResult(false, error, cost);

        public override string ToString() => Ok ? $"OK (cost {Cost})" : $"Rejected: {Error}";
    }

    /// <summary>
    /// Every player action that changes the world is a command. The view never mutates WorldState
    /// directly; it builds a command and enqueues it. Commands run at the start of the next tick,
    /// which keeps the simulation deterministic and makes a replay/undo log possible later.
    /// </summary>
    public interface ISimCommand
    {
        /// <summary>Pure check: must not modify state. Safe to call every frame for previews.</summary>
        CommandResult Validate(GameSimulation sim);

        /// <summary>Applies the change. Only called right after a successful Validate.</summary>
        void Execute(GameSimulation sim);
    }
}
