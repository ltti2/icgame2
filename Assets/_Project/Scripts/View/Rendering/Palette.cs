using IceCoast.Simulation.Guests;
using IceCoast.Simulation.Runs;
using IceCoast.Simulation.World;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Every colour used by the prototype renderer. When real pixel art arrives, most of these become
    /// sprite/texture lookups; keeping them in one place makes that swap mechanical.
    /// </summary>
    public static class Palette
    {
        public static readonly Color32 Sky = new Color32(118, 156, 196, 255);

        // Snow surfaces
        public static readonly Color32 Powder = new Color32(250, 252, 255, 255);
        public static readonly Color32 Groomed = new Color32(232, 240, 252, 255);
        public static readonly Color32 Packed = new Color32(222, 228, 236, 255);
        public static readonly Color32 Crud = new Color32(214, 214, 220, 255);
        public static readonly Color32 Slush = new Color32(196, 196, 178, 255);
        public static readonly Color32 Ice = new Color32(176, 204, 226, 255);

        // Bare ground
        public static readonly Color32 Grass = new Color32(116, 132, 82, 255);
        public static readonly Color32 Dirt = new Color32(132, 110, 84, 255);
        public static readonly Color32 Rock = new Color32(122, 122, 128, 255);
        public static readonly Color32 Scree = new Color32(150, 146, 140, 255);
        public static readonly Color32 Water = new Color32(70, 110, 160, 255);

        /// <summary>Road and parking lines; the asphalt texture darkens everything else (see TerrainDetailArt).</summary>
        public static readonly Color32 Paint = new Color32(228, 228, 220, 255);
        /// <summary>Tint of trampled, sanded snow on walking areas.</summary>
        public static readonly Color32 Walkway = new Color32(204, 190, 168, 255);
        /// <summary>Warm sodium-ish glow of trail lights.</summary>
        public static readonly Color32 LampLight = new Color32(255, 226, 170, 255);

        public static readonly Color32 TreeDark = new Color32(34, 66, 44, 255);
        public static readonly Color32 TreeLight = new Color32(52, 92, 60, 255);
        public static readonly Color32 TreeSnowy = new Color32(196, 214, 206, 255);

        public static readonly Color32 Unowned = new Color32(70, 82, 108, 255); // fog tint
        /// <summary>
        /// How strongly land you don't own is tinted. Kept light so the playable map and the scenery around it
        /// read as one landscape; the Ownership overlay shows exact boundaries.
        /// </summary>
        public const float UnownedTint = 0.18f;
        public const float UnownedDarken = 0.94f;

        // Run signage colours
        public static readonly Color32 Green = new Color32(46, 170, 80, 255);
        public static readonly Color32 Blue = new Color32(40, 100, 220, 255);
        public static readonly Color32 Black = new Color32(20, 20, 24, 255);
        public static readonly Color32 DoubleBlack = new Color32(60, 20, 70, 255);
        public static readonly Color32 TripleBlack = new Color32(150, 20, 20, 255);

        // Structures
        public static readonly Color32 Steel = new Color32(70, 74, 82, 255);
        public static readonly Color32 Cable = new Color32(30, 30, 34, 255);
        public static readonly Color32 StationRoof = new Color32(150, 60, 50, 255);
        public static readonly Color32 StationWall = new Color32(196, 180, 150, 255);
        public static readonly Color32 StationWallDark = new Color32(160, 144, 118, 255);
        public static readonly Color32 Chair = new Color32(220, 70, 50, 255);

        // Guests (skill-coloured outfits)
        public static readonly Color32 Skin = new Color32(240, 200, 170, 255);
        private static readonly Color32[] SkillColors =
        {
            new Color32(255, 214, 0, 255),   // First-timer: hi-vis yellow
            new Color32(255, 140, 0, 255),   // Beginner: orange
            new Color32(30, 160, 230, 255),  // Intermediate: sky blue
            new Color32(200, 30, 60, 255),   // Advanced: red
            new Color32(130, 40, 200, 255),  // Expert: purple
            new Color32(25, 25, 25, 255),    // Pro: black
        };

        // Tool feedback
        public static readonly Color32 Valid = new Color32(80, 220, 120, 140);
        public static readonly Color32 Invalid = new Color32(240, 70, 70, 140);
        public static readonly Color32 Hover = new Color32(255, 255, 255, 90);
        public static readonly Color32 Selection = new Color32(255, 220, 60, 160);
        /// <summary>Paint tool: tiles where skiers would have to travel uphill.</summary>
        public static readonly Color32 Uphill = new Color32(255, 200, 0, 170);
        /// <summary>Area editors: tiles an erase stroke will remove.</summary>
        public static readonly Color32 Erase = new Color32(255, 110, 50, 150);
        /// <summary>Walking-area editor: walking area tiles.</summary>
        public static readonly Color32 WalkPreview = new Color32(240, 200, 120, 120);

        public static Color32 Skill(SkillLevel s) => SkillColors[(int)s];

        public static Color32 ForDifficulty(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Green: return Green;
                case Difficulty.Blue: return Blue;
                case Difficulty.Black: return Black;
                case Difficulty.DoubleBlack: return DoubleBlack;
                default: return TripleBlack;
            }
        }

        public static Color32 Snow(SnowQuality q)
        {
            switch (q)
            {
                case SnowQuality.Powder: return Powder;
                case SnowQuality.Groomed: return Groomed;
                case SnowQuality.Packed: return Packed;
                case SnowQuality.Crud: return Crud;
                case SnowQuality.Slush: return Slush;
                case SnowQuality.Ice: return Ice;
                default: return Grass;
            }
        }

        public static Color32 Ground(TerrainType t)
        {
            switch (t)
            {
                case TerrainType.Dirt: return Dirt;
                case TerrainType.Rock: return Rock;
                case TerrainType.Scree: return Scree;
                case TerrainType.Water: return Water;
                default: return Grass;
            }
        }

        public static Color32 Scale(Color32 c, float f)
            => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), c.a);

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        public static Color32 DimUnowned(Color32 c) => Scale(Color32.Lerp(c, Unowned, UnownedTint), UnownedDarken);

        /// <summary>Blue-to-red heat map for overlays. t in 0..1.</summary>
        public static Color32 Heat(float t)
        {
            t = Mathf.Clamp01(t);
            Color c = t < 0.5f
                ? Color.Lerp(new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.9f, 0.3f), t * 2f)
                : Color.Lerp(new Color(0.95f, 0.9f, 0.3f), new Color(0.85f, 0.15f, 0.15f), (t - 0.5f) * 2f);
            return c;
        }
    }
}
