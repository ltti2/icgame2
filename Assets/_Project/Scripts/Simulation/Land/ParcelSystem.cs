using System;
using IceCoast.Simulation.Commands;
using IceCoast.Simulation.Events;
using IceCoast.Simulation.Finance;

namespace IceCoast.Simulation.Land
{
    /// <summary>Land ownership: the parcel grid, pricing and purchases.</summary>
    public sealed class ParcelSystem
    {
        private static readonly string[] BandNames = { "Valley", "Foothills", "Mid-Mountain", "Upper Mountain", "Alpine" };

        private readonly GameSimulation _sim;

        public ParcelSystem(GameSimulation sim) { _sim = sim; }

        /// <summary>Divides the map into a grid of parcels and prices them by elevation. Called at world generation.</summary>
        public void CreateGrid()
        {
            var world = _sim.World;
            var map = world.Map;
            var terrain = _sim.Config.Terrain;
            int size = _sim.Config.Map.ParcelSize;
            world.Parcels.Clear();

            int id = 1;
            var bandCounts = new int[BandNames.Length];
            for (int py = 0; py < map.Height; py += size)
            {
                for (int px = 0; px < map.Width; px += size)
                {
                    int w = Math.Min(size, map.Width - px), h = Math.Min(size, map.Height - py);
                    float avg = AverageHeight(px, py, w, h);
                    float t = (avg - terrain.BaseElevationSteps) / Math.Max(1f, terrain.SummitElevationSteps - terrain.BaseElevationSteps);
                    int band = Math.Max(0, Math.Min(BandNames.Length - 1, (int)(t * BandNames.Length)));
                    bandCounts[band]++;

                    world.Parcels.Add(new Parcel
                    {
                        Id = id++,
                        Name = $"{BandNames[band]} {bandCounts[band]}",
                        X = px,
                        Y = py,
                        Width = w,
                        Height = h,
                        ElevationBand = band,
                        Cost = 50_000 + band * 90_000L + (long)(t * 40_000),
                        RequiredReputation = band * 15f,
                    });
                }
            }
            world.ParcelsVersion++;
        }

        private float AverageHeight(int x0, int y0, int w, int h)
        {
            var map = _sim.Map;
            long sum = 0;
            int n = 0;
            for (int y = y0; y < y0 + h; y += 4)
                for (int x = x0; x < x0 + w; x += 4)
                {
                    sum += map.GetVertexHeight(x, y);
                    n++;
                }
            return n > 0 ? (float)sum / n : 0f;
        }

        public Parcel GetParcelAt(int x, int y)
        {
            foreach (var p in _sim.World.Parcels)
                if (p.Contains(x, y)) return p;
            return null;
        }

        public CommandResult ValidatePurchase(Parcel parcel)
        {
            if (parcel == null) return CommandResult.Fail("No parcel there.");
            if (parcel.Owned) return CommandResult.Fail("You already own this land.");

            var economy = _sim.World.Economy;
            if (!_sim.Config.SandboxMode && economy.Reputation < parcel.RequiredReputation)
                return CommandResult.Fail($"The landowner wants a resort with reputation {parcel.RequiredReputation:0}+.", parcel.Cost);
            if (!IsAdjacentToOwned(parcel))
                return CommandResult.Fail("You can only buy land next to land you already own.", parcel.Cost);
            if (!economy.CanAfford(parcel.Cost))
                return CommandResult.Fail($"Not enough cash (need ${parcel.Cost:N0}).", parcel.Cost);
            return CommandResult.Success(parcel.Cost);
        }

        private bool IsAdjacentToOwned(Parcel parcel)
        {
            foreach (var other in _sim.World.Parcels)
            {
                if (!other.Owned) continue;
                bool touchX = other.X + other.Width == parcel.X || parcel.X + parcel.Width == other.X;
                bool touchY = other.Y + other.Height == parcel.Y || parcel.Y + parcel.Height == other.Y;
                bool overlapX = other.X < parcel.X + parcel.Width && parcel.X < other.X + other.Width;
                bool overlapY = other.Y < parcel.Y + parcel.Height && parcel.Y < other.Y + other.Height;
                if ((touchX && overlapY) || (touchY && overlapX)) return true;
            }
            return false;
        }

        public void Purchase(Parcel parcel)
        {
            _sim.World.Economy.AddExpense(LedgerCategory.Land, parcel.Cost);
            Grant(parcel);
            _sim.Events.Publish(SimEventKind.ParcelBought, EventSeverity.Good,
                $"Bought {parcel.Name} for ${parcel.Cost:N0}.", parcel.Center, parcel.Id);
        }

        /// <summary>Marks a parcel owned without charging (starting land, scenarios).</summary>
        public void Grant(Parcel parcel)
        {
            var map = _sim.Map;
            parcel.Owned = true;
            for (int y = parcel.Y; y < parcel.Y + parcel.Height; y++)
                for (int x = parcel.X; x < parcel.X + parcel.Width; x++)
                    map.SetOwned(x, y, true);
            _sim.World.ParcelsVersion++;
        }
    }
}
