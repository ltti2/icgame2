using System;
using System.Diagnostics;
using System.Linq;
using IceCoast.Simulation;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.World;
using IceCoast.Tests;

/// <summary>
/// Simulates a fresh resort with the starter rope tow and run for N days and prints a daily report.
/// Use it to tune SimConfig numbers without opening Unity.
/// </summary>
public static class BalanceRunner
{
    public static int Main(string[] args)
    {
        int days = args.Length > 0 ? int.Parse(args[0]) : 7;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 12345;
        bool small = args.Contains("--small");

        var config = small ? TestGames.SmallConfig(seed) : IceCoast.Simulation.Config.SimConfig.CreateDefault();
        config.Map.Seed = seed;

        var sw = Stopwatch.StartNew();
        var sim = SimulationFactory.CreateNewGame(config);
        Console.WriteLine($"World {config.Map.Width}x{config.Map.Height} generated in {sw.ElapsedMilliseconds} ms");

        var (lift, run) = TestGames.BuildStarterResort(sim);
        Console.WriteLine($"Built {lift.Name} ({lift.LengthTiles:0} tiles, ride {lift.RideTicks / (float)config.Time.TicksPerGameMinute:0.0} min) " +
                          $"and run \"{run.Name}\" ({run.ComputedRating}, avg {run.AvgSlope:0}% max {run.MaxSlope:0}%, {run.Tiles.Count} tiles)");
        Console.WriteLine($"Cash after build: ${sim.World.Economy.Cash:N0}\n");
        Console.WriteLine("Day  Date        Weather     Guests  Peak  Rides  Inj  Happy  Rep    Income   Expense   Cash");

        sw.Restart();
        long ticks = 0;
        while (sim.World.Economy.History.Count < days)
        {
            sim.Tick();
            ticks++;
            if (sim.Clock.DayAdvanced)
            {
                var d = sim.World.Economy.History.Last();
                Console.WriteLine($"{d.Day,3}  {DateFor(sim, d.Day),-10}  {sim.World.Weather.Type,-10}  {d.GuestsArrived,6}  {d.PeakGuests,4}  {d.LiftRides,4}  {d.Injuries,3}  {d.AverageHappiness,5:0}  {d.ReputationAtEndOfDay,5:0.0}  {d.TotalIncome,8:N0}  {d.TotalExpenses,8:N0}  {d.CashAtEndOfDay,9:N0}");
            }
        }
        double secs = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"\nSimulated {ticks:N0} ticks in {secs:0.00}s ({ticks / secs:N0} ticks/s, {ticks / secs / 10:0}x realtime at 10 tps)");

        if (args.Contains("--wear"))
        {
            Console.WriteLine("\nRun snow (quality:compaction) sampled across the run:");
            foreach (var t in run.Tiles.Where((t, i) => i % 7 == 0).Take(60))
            {
                var r = sim.Map.Locate(t.X, t.Y);
                Console.Write($"{(SnowQuality)r.Chunk.SnowQuality[r.Index]}:{r.Chunk.Compaction[r.Index]} ");
            }
            Console.WriteLine();
        }
        return 0;
    }

    private static string DateFor(GameSimulation sim, int day)
    {
        var t = new IceCoast.Simulation.Core.GameTime { TotalMinutes = day * 1440L, StartWeekday = sim.World.Time.StartWeekday };
        return t.FormatDate();
    }
}
