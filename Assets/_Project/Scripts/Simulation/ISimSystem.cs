namespace IceCoast.Simulation
{
    /// <summary>
    /// A simulation system that runs every tick, in the fixed order defined by <see cref="GameSimulation"/>.
    /// Systems hold rules; all state lives in <see cref="WorldState"/> (so saving never needs to ask a system).
    /// </summary>
    public interface ISimSystem
    {
        void Tick();
    }

    /// <summary>Calendar edges detected this tick. Systems use these to run hourly / daily work.</summary>
    public struct ClockSignals
    {
        public bool MinuteAdvanced;
        public bool HourAdvanced;
        public bool DayAdvanced;
    }
}
