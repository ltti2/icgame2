using System;

namespace IceCoast.Simulation.World
{
    public enum TerrainType : byte
    {
        Grass,
        Dirt,
        Rock,
        Scree,
        Water,
    }

    [Flags]
    public enum TileFlags : byte
    {
        None = 0,
        Owned = 1 << 0,
        /// <summary>Unused. Walking areas are a <see cref="SurfaceType"/> (Chunk.Surface) since save v5.</summary>
        Path = 1 << 1,
        /// <summary>Hard obstacle: cliffs, structures. Skiers and lifts cannot use it.</summary>
        Blocked = 1 << 2,
        Groomable = 1 << 3,
        /// <summary>Cannot be terraformed or cleared (e.g. protected wetlands, scenario rules).</summary>
        Protected = 1 << 4,
        /// <summary>Resort base village area.</summary>
        BaseArea = 1 << 5,
    }

    /// <summary>
    /// What is laid on the ground, stored per tile in <see cref="Chunk.Surface"/>. Separate from trails
    /// (Chunk.RunId): a tile is a trail, a walking area, a road or a parking lot, never two of them.
    /// </summary>
    public enum SurfaceType : byte
    {
        /// <summary>Snow, forest or open ground: skiable if a trail is painted on it.</summary>
        Natural,
        /// <summary>
        /// Packed-snow plaza where guests take their skis off and walk anywhere (the base area, mid-mountain
        /// decks). Buildings may only be placed on walking areas.
        /// </summary>
        WalkingArea,
        /// <summary>Plowed access road. Cars only.</summary>
        Road,
        /// <summary>Plowed parking lot. Guests walk across it to and from their cars.</summary>
        Parking,
    }

    public enum SnowQuality : byte
    {
        Bare,
        Powder,
        Packed,
        Groomed,
        Slush,
        Ice,
        Crud,
    }

    /// <summary>What kind of entity occupies a tile. Packed into the top bits of Chunk.OccupantId.</summary>
    public enum OccupantKind : byte
    {
        None = 0,
        LiftStation = 1,
        LiftTower = 2,
        Building = 3,
        SnowGun = 4,
        /// <summary>A trail light pole (see Lighting/TrailLight).</summary>
        Light = 5,
    }

    /// <summary>
    /// Packs (kind, entity id) into a ushort: 3 bits of kind, 13 bits of id (up to 8191 per kind).
    /// 0 always means empty.
    /// </summary>
    public static class Occupant
    {
        private const int KindShift = 13;
        private const int IdMask = (1 << KindShift) - 1;
        public const int MaxId = IdMask;

        public static ushort Pack(OccupantKind kind, int id)
        {
            if (id <= 0 || id > MaxId) throw new ArgumentOutOfRangeException(nameof(id));
            return (ushort)(((int)kind << KindShift) | id);
        }

        public static OccupantKind KindOf(ushort packed) => (OccupantKind)(packed >> KindShift);
        public static int IdOf(ushort packed) => packed & IdMask;
    }
}
