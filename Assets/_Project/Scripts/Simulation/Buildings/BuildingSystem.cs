using System;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.Finance;
using IceCoast.Simulation.Guests;
using IceCoast.Simulation.World;

namespace IceCoast.Simulation.Buildings
{
    /// <summary>
    /// Buildings on walking areas (Waffle Cabins, for now): placing and demolishing them, opening hours,
    /// running costs, and serving the guests lined up at each window.
    ///
    /// How a sale happens: a hungry guest choosing what to do next (GuestSystem.DecideNext) looks for an open
    /// cabin in walking reach with a short enough line, and buys if the price is no more than they are
    /// personally willing to pay (<see cref="WillingToPay"/>). They walk to the back of the line; this system
    /// serves the front of the line every <see cref="BuildingDefinition.ServiceMinutes"/>. The guest pays,
    /// the ingredients are booked, and they eat nearby for a few minutes (GuestSystem.OnServedFood), which
    /// makes them happier (more so when the price is low) and gives back some energy.
    /// </summary>
    public sealed class BuildingSystem : ISimSystem
    {
        private readonly GameSimulation _sim;

        public BuildingSystem(GameSimulation sim) { _sim = sim; }

        private WorldState World => _sim.World;

        public void Tick()
        {
            var buildings = World.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (!IsServing(b))
                {
                    if (b.Queue.Count > 0) SendQueueAway(b);
                    b.ServiceTimer = 0;
                    continue;
                }
                DropStaleFromFront(b);
                if (b.Queue.Count == 0) { b.ServiceTimer = 0; continue; }

                if (b.ServiceTimer <= 0) b.ServiceTimer = ServiceTicks(b);
                if (--b.ServiceTimer > 0) continue;

                int g = b.Queue[0];
                b.Queue.RemoveAt(0);
                Sell(b, g);
            }
        }

        // =====================================================================
        // Queries
        // =====================================================================

        public BuildingDefinition Def(Building b) => _sim.Config.GetBuilding(b.Type);

        public Building GetBuilding(int id)
        {
            var list = World.Buildings;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id == id) return list[i];
            return null;
        }

        public Building BuildingAt(int x, int y)
        {
            ushort occ = _sim.Map.GetOccupant(x, y);
            return Occupant.KindOf(occ) == OccupantKind.Building ? GetBuilding(Occupant.IdOf(occ)) : null;
        }

        /// <summary>Opening hours for food: from a little before the lifts open until the last lift closes.</summary>
        public bool WithinOpeningHours
        {
            get
            {
                var time = _sim.TimeSystem;
                if (time.LiftsOperating) return true;
                int m = World.Time.MinuteOfDay;
                int opens = _sim.Config.Time.LiftsOpenMinute - Math.Max(0, _sim.Config.Food.OpensBeforeLiftsMinutes);
                return m >= opens && m < _sim.Config.Time.LiftsOpenMinute;
            }
        }

        /// <summary>Open (by the player) and within opening hours: serving, and costing money to run.</summary>
        public bool IsServing(Building b) => b.PlayerOpen && WithinOpeningHours;

        public int ServiceTicks(Building b)
            => Math.Max(1, SimMath.RoundToInt(Def(b).ServiceMinutes * _sim.Config.Time.TicksPerGameMinute));

        /// <summary>Minutes a guest joining the back of the line now would wait.</summary>
        public float EstimatedWaitMinutes(Building b) => b.Queue.Count * Def(b).ServiceMinutes;

        /// <summary>
        /// The most this guest will pay. Everyone pays the fair price; above it, fewer and fewer guests do, and
        /// nobody pays the definition's MaxPrice. Fixed per guest (hashed from their slot and arrival), so a
        /// guest gives the same answer every time they look.
        /// </summary>
        public static float PersonalMaxPrice(BuildingDefinition def, int guest, int arrivalMinute)
        {
            float u = SimRandom.Hash01(guest, arrivalMinute, 0x5AFF1E);
            return def.FairPrice + (def.MaxPrice - def.FairPrice) * SimMath.Pow(u, 1.5f);
        }

        public bool WillingToPay(Building b, int g)
        {
            var pool = World.Guests;
            if (pool.Money[g] < b.Price) return false;
            var def = Def(b);
            return b.Price <= def.FairPrice || b.Price < PersonalMaxPrice(def, g, pool.ArrivalMinute[g]);
        }

        /// <summary>
        /// Happiness from eating at <paramref name="price"/>: the definition's boost at the fair price, up to
        /// half as much again when it's a bargain, down to half when it's dear.
        /// </summary>
        public static float HappinessFor(BuildingDefinition def, int price)
        {
            float value = price <= 0 ? 1.5f : def.FairPrice / (float)price;
            return def.HappinessBoost * SimMath.Clamp(value, 0.5f, 1.5f);
        }

        /// <summary>What the open buildings cost to run for an hour (staff and power).</summary>
        public long HourlyOperatingCost()
        {
            long total = 0;
            foreach (var b in World.Buildings)
                if (IsServing(b)) total += Def(b).OperatingCostPerHour;
            return total;
        }

        /// <summary>Books an hour's running costs against each open building. Returns the total (EconomySystem pays it).</summary>
        public long ChargeHourlyCosts()
        {
            long total = 0;
            foreach (var b in World.Buildings)
            {
                if (!IsServing(b)) continue;
                long cost = Def(b).OperatingCostPerHour;
                b.CostToday += cost;
                total += cost;
            }
            return total;
        }

        public void ResetDailyStats()
        {
            foreach (var b in World.Buildings)
            {
                b.SoldToday = 0;
                b.RevenueToday = 0;
                b.CostToday = 0;
                b.PricedOutToday = 0;
            }
        }

        // =====================================================================
        // Lines and sales
        // =====================================================================

        public void Enqueue(Building b, int g)
        {
            if (!b.Queue.Contains(g)) b.Queue.Add(g);
        }

        /// <summary>Slot in the line (0 = at the window), or -1 if not in it.</summary>
        public int QueuePosition(Building b, int g) => b.Queue.IndexOf(g);

        /// <summary>Where the guest in slot <paramref name="slot"/> stands: two abreast, back from the window along +x.</summary>
        public static void QueueSpot(Building b, int slot, out float x, out float y)
        {
            int row = Math.Max(0, slot) / 2, side = Math.Max(0, slot) % 2;
            x = b.WindowX + row * 0.32f;
            y = b.WindowY + (side == 0 ? -0.14f : 0.14f);
        }

        private void DropStaleFromFront(Building b)
        {
            var pool = World.Guests;
            for (int i = b.Queue.Count - 1; i >= 0; i--)
            {
                int g = b.Queue[i];
                if (g < 0 || g >= pool.HighWater || !pool.Alive[g] || pool.State[g] != GuestState.WaitingForFood || pool.TargetId[g] != b.Id)
                    b.Queue.RemoveAt(i);
            }
        }

        private void SendQueueAway(Building b)
        {
            var waiting = b.Queue.ToArray();
            b.Queue.Clear();
            foreach (int g in waiting) _sim.GuestSystem.OnFoodUnavailable(g, b);
        }

        private void Sell(Building b, int g)
        {
            var pool = World.Guests;
            var def = Def(b);
            var economy = World.Economy;
            int price = b.Price;
            if (pool.Money[g] < price)
            {
                _sim.GuestSystem.OnFoodUnavailable(g, b);
                return;
            }

            pool.Money[g] -= price;
            economy.AddIncome(LedgerCategory.Food, price);
            economy.AddExpense(LedgerCategory.FoodService, def.IngredientCostPerSale);
            b.CostToday += def.IngredientCostPerSale;
            b.SoldToday++;
            b.SoldTotal++;
            b.RevenueToday += price;
            b.RevenueTotal += price;
            _sim.GuestSystem.OnServedFood(g, b, HappinessFor(def, price), def);
        }

        // =====================================================================
        // Construction
        // =====================================================================

        public CommandResult QuotePlace(BuildingType type, Int2 origin)
        {
            var def = _sim.Config.GetBuilding(type);
            long cost = def.BuildCost;
            if (World.NextBuildingId > Occupant.MaxId) return CommandResult.Fail("No room for more buildings.", cost);
            if (World.Economy.Reputation < def.RequiredReputation && !_sim.Config.SandboxMode)
                return CommandResult.Fail($"Needs {def.RequiredReputation:0} reputation.", cost);
            var fit = BuildingRules.CanPlaceFootprint(_sim, origin, def.FootprintWidth, def.FootprintHeight);
            if (!fit.Ok) return CommandResult.Fail(fit.Error, cost);
            if (!World.Economy.CanAfford(cost)) return CommandResult.Fail($"Not enough cash (need ${cost:N0}).", cost);
            return CommandResult.Success(cost);
        }

        public Building Place(BuildingType type, Int2 origin, long cost)
        {
            var def = _sim.Config.GetBuilding(type);
            int sameType = 1;
            foreach (var other in World.Buildings) if (other.Type == type) sameType++;

            var b = new Building
            {
                Id = World.NextBuildingId++,
                Type = type,
                Name = $"{def.DisplayName} {sameType}",
                Origin = origin,
                Width = Math.Max(1, def.FootprintWidth),
                Height = Math.Max(1, def.FootprintHeight),
                Price = def.DefaultPrice,
            };
            var map = _sim.Map;
            for (int y = origin.Y; y < origin.Y + b.Height; y++)
                for (int x = origin.X; x < origin.X + b.Width; x++)
                {
                    if (map.GetTreeDensity(x, y) > 0) map.SetTreeDensity(x, y, 0);
                    map.SetOccupant(x, y, Occupant.Pack(OccupantKind.Building, b.Id));
                }

            World.Economy.AddExpense(LedgerCategory.Construction, cost);
            World.Buildings.Add(b);
            World.BuildingsVersion++;
            return b;
        }

        /// <summary>Knocks the building down (no refund, like lifts). Anyone in line goes elsewhere.</summary>
        public void Demolish(Building b)
        {
            SendQueueAway(b);
            var map = _sim.Map;
            ushort mine = Occupant.Pack(OccupantKind.Building, b.Id);
            for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    if (map.GetOccupant(x, y) == mine) map.SetOccupant(x, y, 0);

            World.Buildings.Remove(b);
            World.BuildingsVersion++;
            _sim.GuestSystem.OnBuildingRemoved(b.Id);
        }
    }
}
