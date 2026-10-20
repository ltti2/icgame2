using System;

namespace IceCoast.Simulation.Core
{
    /// <summary>
    /// Integer grid coordinate. The simulation never uses UnityEngine.Vector2Int so that it
    /// stays engine-independent; the view layer converts at the boundary.
    /// </summary>
    [Serializable]
    public struct Int2 : IEquatable<Int2>
    {
        public int X;
        public int Y;

        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly Int2 Zero = new Int2(0, 0);
        public static readonly Int2 Invalid = new Int2(int.MinValue, int.MinValue);

        public bool IsValid => X != int.MinValue;

        public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.X + b.X, a.Y + b.Y);
        public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Int2 a, Int2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Int2 a, Int2 b) => !(a == b);

        public int ManhattanDistance(Int2 other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        public int ChebyshevDistance(Int2 other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        public float Distance(Int2 other)
        {
            float dx = X - other.X, dy = Y - other.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(Int2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
        public override string ToString() => $"({X}, {Y})";

        /// <summary>The 8 neighbour offsets, orthogonal first.</summary>
        public static readonly Int2[] Neighbours8 =
        {
            new Int2(1, 0), new Int2(-1, 0), new Int2(0, 1), new Int2(0, -1),
            new Int2(1, 1), new Int2(-1, -1), new Int2(1, -1), new Int2(-1, 1),
        };
    }
}
