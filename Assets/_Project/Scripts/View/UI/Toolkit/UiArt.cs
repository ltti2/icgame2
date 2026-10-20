using IceCoast.View.Rendering;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// The interface's pixel art as canvases: the 16x16 icons and the font's glyphs, drawn from the character
    /// art in <see cref="PixelArtData"/>. These are the defaults for the "ui_icons" and "ui_font" sheets
    /// (see <see cref="ArtSheets"/>); PNGs in StreamingAssets/Art replace them.
    /// </summary>
    public static class UiArt
    {
        public const int IconSize = 16;

        public static int IconCount => PixelArtData.Icons.Length;
        public static string IconName(int i) => PixelArtData.IconNames[i];
        public static int GlyphCount => PixelArtData.Glyphs.Length;
        public static char GlyphChar(int i) => PixelArtData.Glyphs[i][0];

        public static PixelCanvas Icon(int i)
        {
            var c = new PixelCanvas(IconSize, IconSize);
            var rows = PixelArtData.Icons[i];
            for (int ry = 0; ry < IconSize && ry < rows.Length; ry++)
                for (int rx = 0; rx < IconSize && rx < rows[ry].Length; rx++)
                {
                    char ch = rows[ry][rx];
                    if (ch != '.') c.Set(rx, ry, IconColor(ch));
                }
            c.PivotX = 0;
            c.PivotY = 0;
            return c;
        }

        /// <summary>A glyph as white ink on transparent. Its width is the character's advance minus one.</summary>
        public static PixelCanvas Glyph(int i)
        {
            string entry = PixelArtData.Glyphs[i];
            var rows = entry.Substring(2).Split(',');
            var c = new PixelCanvas(rows[0].Length, rows.Length);
            var white = new Color32(255, 255, 255, 255);
            for (int ry = 0; ry < rows.Length; ry++)
                for (int rx = 0; rx < rows[ry].Length; rx++)
                    if (rows[ry][rx] == '#') c.Set(rx, ry, white);
            c.PivotX = 0;
            c.PivotY = 0;
            return c;
        }

        public static Color32 IconColor(char c)
        {
            switch (c)
            {
                case 'k': return new Color32(16, 16, 16, 255);
                case 'w': return new Color32(252, 252, 252, 255);
                case 'l': return new Color32(200, 200, 200, 255);
                case 'g': return new Color32(150, 150, 150, 255);
                case 'd': return new Color32(92, 92, 92, 255);
                case 'r': return new Color32(212, 44, 32, 255);
                case 'R': return new Color32(124, 20, 20, 255);
                case 'y': return new Color32(252, 212, 40, 255);
                case 'o': return new Color32(232, 144, 32, 255);
                case 'n': return new Color32(152, 100, 52, 255);
                case 'N': return new Color32(92, 60, 28, 255);
                case 'G': return new Color32(60, 172, 60, 255);
                case 'D': return new Color32(28, 100, 40, 255);
                case 'b': return new Color32(52, 92, 224, 255);
                case 'B': return new Color32(28, 40, 124, 255);
                case 'c': return new Color32(64, 204, 232, 255);
                case 's': return new Color32(152, 204, 244, 255);
                case 'p': return new Color32(244, 152, 184, 255);
                default: return new Color32(255, 0, 255, 255);
            }
        }
    }
}
