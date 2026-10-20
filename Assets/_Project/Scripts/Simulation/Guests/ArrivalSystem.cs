using System;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Parking;

namespace IceCoast.Simulation.Guests
{
    /// <summary>
    /// Brings guests to the resort. Implements the design-doc demand formula:
    ///   arrivals/hr = baseDemand * reputation * weather * snow * price * dayOfWeek * marketing
    /// The hourly rate is recomputed once per game minute and spread across ticks with an accumulator.
    ///
    /// Guests come by car, in parties of one to four. Each car needs a parking space
    /// (<see cref="ParkingSystem.TryAdmitCar"/>); a full lot turns them away. The party steps out when the car
    /// parks (<see cref="SpawnParty"/>), buys tickets and heads for the lifts.
    ///
    /// Besides the morning rush, an evening crowd comes for night skiing when trails are lit: a share of day
    /// demand scaled by how much of the open terrain has lights.
    /// </summary>
    public sealed class ArrivalSystem : ISimSystem
    {
        private readonly GameSimulation _sim;

        public ArrivalSystem(GameSimulation sim) { _sim = sim; }

        /// <summary>Last computed rate, exposed for the UI ("~140 guests/hr expected").</summary>
        public float CurrentRatePerHour { get; private set; }
        public float LastPriceFactor { get; private set; }
        public float LastFairPrice { get; private set; }

        /// <summary>The rate is cached between game minutes, so saves carry it to stay deterministic.</summary>
        public void RestoreRate(float ratePerHour) => CurrentRatePerHour = ratePerHour;

        public void Tick()
        {
            if (_sim.Clock.MinuteAdvanced)
            {
                CurrentRatePerHour = ComputeRatePerHour();
                RollEarlyBird();
            }
            if (CurrentRatePerHour <= 0f) return;

            var world = _sim.World;
            world.ArrivalAccumulator += CurrentRatePerHour / _sim.TimeSystem.TicksPerHour;
            while (world.ArrivalAccumulator >= 1f)
            {
                if (world.Guests.IsFull) { world.ArrivalAccumulator = 0f; break; }
                int party = 1 + world.Rng.PickWeighted(_sim.Config.Parking.PartySizeWeights);
                world.ArrivalAccumulator -= party;
                if (world.ParkingLots.Count == 0)
                {
                    // No lot at all (a hand-built test world): they just appear at the entrance.
                    var e = world.ResortEntrance;
                    SpawnParty(ParkingSystem.NoCar, e.X + 0.5f, e.Y + 0.5f, party);
                    continue;
                }
                _sim.ParkingSystem.TryAdmitCar(party);
            }
        }

        /// <summary>
        /// Before the morning rush, now and then a car of keen skiers pulls in: one dice roll per game minute,
        /// more likely the closer it gets to the rush. They wander the base until it's time to line up.
        /// </summary>
        private void RollEarlyBird()
        {
            var world = _sim.World;
            var tcfg = _sim.Config.Time;
            int minute = world.Time.MinuteOfDay;
            if (minute < tcfg.EarlyArrivalsStartMinute || minute >= tcfg.ArrivalsStartMinute) return;
            if (world.Lifts.Count == 0 || world.Guests.IsFull) return;

            float t = (minute - tcfg.EarlyArrivalsStartMinute) / (float)Math.Max(1, tcfg.ArrivalsStartMinute - tcfg.EarlyArrivalsStartMinute);
            float reputation = 0.2f + world.Economy.Reputation / 50f;
            float perMinute = _sim.Config.Economy.EarlyBirdCarsPerHour / 60f * (0.25f + 1.5f * t)
                              * reputation * _sim.WeatherSystem.AttendanceFactor;
            if (!world.Rng.Chance(perMinute)) return;

            int party = 1 + world.Rng.PickWeighted(_sim.Config.Parking.PartySizeWeights);
            if (world.ParkingLots.Count == 0)
            {
                var e = world.ResortEntrance;
                SpawnParty(ParkingSystem.NoCar, e.X + 0.5f, e.Y + 0.5f, party);
                return;
            }
            _sim.ParkingSystem.TryAdmitCar(party);
        }

        public float ComputeRatePerHour()
        {
            var world = _sim.World;
            var time = world.Time;
            var tcfg = _sim.Config.Time;
            var ecfg = _sim.Config.Economy;

            int minute = time.MinuteOfDay;
            bool day = minute >= tcfg.ArrivalsStartMinute && minute < tcfg.ArrivalsEndMinute;
            bool night = _sim.TimeSystem.NightSessionAvailable && minute >= tcfg.NightArrivalsStartMinute && minute < tcfg.NightArrivalsEndMinute;
            if (!day && !night) return 0f;
            if (minute >= _sim.TimeSystem.ClosingMinute) return 0f;
            if (world.Lifts.Count == 0) return 0f; // nothing to sell yet

            var economy = world.Economy;
            float reputation = 0.2f + economy.Reputation / 50f;
            float weather = _sim.WeatherSystem.AttendanceFactor;
            float snow = 0.5f + 0.7f * _sim.RunSystem.AverageOpenSnowQuality();
            if (world.Weather.IsSnowing) snow += 0.1f; // powder day buzz

            LastFairPrice = Math.Min(ecfg.FairPriceMax, ecfg.BaseFairPrice + ecfg.FairPricePerRunTile * _sim.RunSystem.OpenRunTiles());
            float price = economy.TicketPrice <= 0 ? 2.5f
                : Math.Max(0.05f, Math.Min(2.5f, (float)Math.Pow(LastFairPrice / economy.TicketPrice, ecfg.PriceElasticity)));
            LastPriceFactor = price;

            float dayOfWeek = time.IsHolidayPeriod ? ecfg.HolidayDemandMultiplier : time.IsWeekend ? ecfg.WeekendDemandMultiplier : 1f;

            float shape;
            if (day)
            {
                // Morning rush: most people show up early.
                float span = tcfg.ArrivalsEndMinute - tcfg.ArrivalsStartMinute;
                float t = (minute - tcfg.ArrivalsStartMinute) / span;
                shape = 1.7f - 1.4f * t; // averages ~1.0 across the window
            }
            else
            {
                // After work: only worth the drive in proportion to how much is lit.
                shape = ecfg.NightDemandFactor * (float)Math.Sqrt(_sim.RunSystem.LitOpenRunShare());
            }

            const float marketing = 1f; // future: marketing campaigns
            return ecfg.BaseDemandPerHour * reputation * weather * snow * price * dayOfWeek * marketing * shape;
        }

        /// <summary>
        /// A car has parked: up to <paramref name="partySize"/> guests get out at (x, y), buy tickets and start
        /// their day. The party leaves together (same planned departure). Returns how many actually spawned
        /// (fewer if the resort is at its guest limit).
        /// </summary>
        public int SpawnParty(int car, float x, float y, int partySize)
        {
            var world = _sim.World;
            var rng = world.Rng;
            var cfg = _sim.Config.Guests;
            int now = world.Time.MinuteOfDay;
            int skiingFrom = Math.Max(now, _sim.Config.Time.LiftsOpenMinute); // early birds' day starts when the lifts do
            int departure = Math.Min(_sim.TimeSystem.ClosingMinute, skiingFrom + rng.Range(cfg.MinStayMinutes, cfg.MaxStayMinutes));

            int spawned = 0;
            for (int k = 0; k < partySize; k++)
            {
                int g = SpawnGuest(car, x + rng.Range(-0.25f, 0.25f), y + rng.Range(-0.25f, 0.25f), departure);
                if (g < 0) break;
                spawned++;
            }
            return spawned;
        }

        private int SpawnGuest(int car, float x, float y, int departureMinute)
        {
            var world = _sim.World;
            var pool = world.Guests;
            var rng = world.Rng;
            var cfg = _sim.Config.Guests;

            int g = pool.Spawn();
            if (g < 0) return -1;

            pool.Skill[g] = (SkillLevel)rng.PickWeighted(cfg.SkillWeights);
            pool.Money[g] = rng.Range(40, 300);
            pool.Lane[g] = rng.Range(-0.3f, 0.3f);
            pool.Happiness[g] = rng.Range(55f, 70f);
            pool.Hunger[g] = (byte)rng.Range(0, Math.Max(1, _sim.Config.Food.StartingHungerMax + 1));
            pool.Car[g] = car;
            pool.Teleport(g, x, y);

            pool.ArrivalMinute[g] = world.Time.MinuteOfDay;
            pool.DepartureMinute[g] = departureMinute;

            // Buy a ticket.
            int price = world.Economy.TicketPrice;
            pool.Money[g] -= Math.Min(pool.Money[g], price);
            world.Economy.AddIncome(LedgerCategory.Tickets, price);
            world.Economy.Today.GuestsArrived++;

            _sim.GuestSystem.OnArrived(g);
            return g;
        }
    }
}
