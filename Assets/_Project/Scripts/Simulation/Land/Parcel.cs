using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Land
{
    /// <summary>
    /// A purchasable region. Rectangular for now; irregular parcels can later be defined by a
    /// per-tile parcel index without changing anything outside ParcelSystem.
    /// </summary>
    public sealed class Parcel
    {
        public int Id;
        public string Name;
        public int X, Y, Width, Height;
        public long Cost;
        public float RequiredReputation;
        /// <summary>0 = valley floor .. 4 = alpine. Used for pricing and UI colouring.</summary>
        public int ElevationBand;
        public bool Owned;

        public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
        public Int2 Center => new Int2(X + Width / 2, Y + Height / 2);
    }
}
