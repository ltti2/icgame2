using System.Collections.Generic;

namespace IceCoast.Simulation.Finance
{
    public enum LedgerCategory : byte
    {
        // Income
        Tickets,
        Food,
        Rentals,
        Lessons,
        Lumber,
        // Expenses
        LiftOperations,
        Wages,
        Utilities,
        Maintenance,
        Overhead,
        Construction,
        Land,
        Terraforming,
        TreeClearing,
        Lawsuits,
        /// <summary>Food buildings: ingredients for every sale, plus staff and power by the hour.</summary>
        FoodService,
    }

    /// <summary>One day's books. Kept in History for graphs and reputation.</summary>
    public sealed class DailyLedger
    {
        public static readonly int CategoryCount = System.Enum.GetValues(typeof(LedgerCategory)).Length;

        public int Day;
        public readonly long[] Income = new long[CategoryCount];
        public readonly long[] Expenses = new long[CategoryCount];

        public int GuestsArrived;
        public int GuestsDeparted;
        public double HappinessSum;   // summed over departed guests
        public int Injuries;
        public int PeakGuests;
        public int LiftRides;
        public int RunsCompleted;
        /// <summary>Cars that found the lot full and drove home, and the guests in them.</summary>
        public int CarsTurnedAway;
        public int GuestsTurnedAway;
        public long CashAtEndOfDay;
        public float ReputationAtEndOfDay;

        public long TotalIncome { get { long s = 0; foreach (var v in Income) s += v; return s; } }
        public long TotalExpenses { get { long s = 0; foreach (var v in Expenses) s += v; return s; } }
        public long Profit => TotalIncome - TotalExpenses;
        public float AverageHappiness => GuestsDeparted > 0 ? (float)(HappinessSum / GuestsDeparted) : 0f;
    }

    public sealed class Economy
    {
        public long Cash;
        public int TicketPrice;
        public float Reputation;      // 0..100
        public bool Sandbox;

        public int DaysInDebt;
        public bool Bankrupt;

        public DailyLedger Today = new DailyLedger();
        public readonly List<DailyLedger> History = new List<DailyLedger>();

        public bool CanAfford(long amount) => Sandbox || Cash >= amount;

        public void AddIncome(LedgerCategory category, long amount)
        {
            if (amount <= 0) return;
            Cash += amount;
            Today.Income[(int)category] += amount;
        }

        /// <summary>Records an expense. In sandbox mode it is tracked in the ledger but cash is not reduced.</summary>
        public void AddExpense(LedgerCategory category, long amount)
        {
            if (amount <= 0) return;
            if (!Sandbox) Cash -= amount;
            Today.Expenses[(int)category] += amount;
        }
    }
}
