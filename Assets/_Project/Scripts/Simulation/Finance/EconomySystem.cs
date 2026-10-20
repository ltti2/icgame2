using System;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Events;

namespace IceCoast.Simulation.Finance
{
    /// <summary>
    /// Running costs, end-of-day books, reputation and bankruptcy.
    /// Income is booked where it happens (tickets in ArrivalSystem, etc.); this system handles the clock-driven parts.
    /// </summary>
    public sealed class EconomySystem : ISimSystem
    {
        private readonly GameSimulation _sim;

        public EconomySystem(GameSimulation sim) { _sim = sim; }

        private Economy Economy => _sim.World.Economy;

        public void Tick()
        {
            var clock = _sim.Clock;
            if (clock.MinuteAdvanced)
            {
                int guests = _sim.World.Guests.Count;
                if (guests > Economy.Today.PeakGuests) Economy.Today.PeakGuests = guests;
            }

            if (clock.HourAdvanced)
            {
                Economy.AddExpense(LedgerCategory.LiftOperations, _sim.LiftSystem.HourlyOperatingCost());
                Economy.AddExpense(LedgerCategory.Utilities, _sim.LightingSystem.HourlyOperatingCost());
            }

            if (clock.DayAdvanced)
                CloseDay();
        }

        private void CloseDay()
        {
            var economy = Economy;
            var cfg = _sim.Config.Economy;
            var today = economy.Today;

            economy.AddExpense(LedgerCategory.Overhead, cfg.DailyFixedCost);
            UpdateReputation(today);

            today.CashAtEndOfDay = economy.Cash;
            today.ReputationAtEndOfDay = economy.Reputation;
            economy.History.Add(today);

            string profit = today.Profit >= 0 ? $"${today.Profit:N0}" : $"-${-today.Profit:N0}";
            string summary = $"Profit today was {profit} ({today.GuestsArrived} guests" +
                             (today.GuestsDeparted > 0 ? $", {today.AverageHappiness:0}% happy" : "") +
                             (today.GuestsTurnedAway > 0 ? $", {today.GuestsTurnedAway} turned away by a full lot" : "") + ").";
            _sim.Events.Publish(SimEventKind.DayEnded, today.Profit >= 0 ? EventSeverity.Good : EventSeverity.Warning, summary);

            CheckBankruptcy();

            economy.Today = new DailyLedger { Day = _sim.World.Time.Day };
            _sim.LiftSystem.ResetDailyStats();
        }

        /// <summary>
        /// Reputation drifts toward the average happiness of the day's departing guests. Busier days move it
        /// more; every injury knocks it down a little regardless.
        /// </summary>
        private void UpdateReputation(DailyLedger today)
        {
            var economy = Economy;
            var cfg = _sim.Config.Economy;
            if (today.GuestsDeparted > 0)
            {
                float volume = Math.Min(1f, today.GuestsDeparted / 100f);
                float delta = (today.AverageHappiness - economy.Reputation) * cfg.ReputationGainRate * volume;
                economy.Reputation += delta;
            }
            economy.Reputation -= today.Injuries * 0.25f;
            // Word gets around when people drive up and can't park.
            var parking = _sim.Config.Parking;
            economy.Reputation -= Math.Min(parking.TurnAwayReputationMaxPerDay, today.GuestsTurnedAway * parking.TurnAwayReputationPenalty);
            economy.Reputation = SimMath.Clamp(economy.Reputation, 0f, cfg.ReputationMax);
        }

        private void CheckBankruptcy()
        {
            var economy = Economy;
            if (economy.Sandbox) return;

            if (economy.Cash >= 0)
            {
                economy.DaysInDebt = 0;
                return;
            }

            economy.DaysInDebt++;
            int grace = _sim.Config.Economy.BankruptcyGraceDays;
            if (economy.DaysInDebt >= grace && !economy.Bankrupt)
            {
                economy.Bankrupt = true;
                _sim.Events.Publish(SimEventKind.Bankrupt, EventSeverity.Danger, "The bank has foreclosed on the resort. Game over.");
            }
            else
            {
                _sim.Events.Publish(SimEventKind.CashWarning, EventSeverity.Danger,
                    $"You are ${-economy.Cash:N0} in debt. {grace - economy.DaysInDebt} days until foreclosure.");
            }
        }
    }
}
