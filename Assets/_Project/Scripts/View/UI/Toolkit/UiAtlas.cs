using System.Collections.Generic;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// One small texture holding the pixel font, the 16x16 icons and a white texel for solid rectangles,
    /// so the entire interface draws from a single texture. Built at startup from <see cref="PixelArtData"/>;
    /// point-filtered so it stays crisp at 2x/3x interface scale.
    /// </summary>
    public sealed class UiAtlas
    {
        public struct Glyph
        {
            public int X, Y, Width, Height; // pixels in the atlas (top-left origin)
            public int Advance;
        }

        public const int Size = 256;
        public const int IconSize = 16;

        public Texture2D Texture { get; }
        /// <summary>Atlas pixel (top-left origin) that is solid white.</summary>
        public Vector2Int WhitePixel { get; }

        private readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();
        private readonly Dictionary<string, Vector2Int> _icons = new Dictionary<string, Vector2Int>();
        private readonly Glyph _fallback;

        public UiAtlas()
        {
            var pixels = new Color32[Size * Size];
            var white = new Color32(255, 255, 255, 255);

            // White block for solid fills (top-left 4x4).
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    Set(pixels, x, y, white);
            WhitePixel = new Vector2Int(1, 1);

            // Glyphs, packed in rows below the white block.
            int penX = 6, penY = 0, rowHeight = 10;
            foreach (var entry in PixelArtData.Glyphs)
            {
                char ch = entry[0];
                var rows = entry.Substring(2).Split(',');
                int w = rows[0].Length;
                if (penX + w + 1 > Size) { penX = 0; penY += rowHeight; }
                for (int ry = 0; ry < rows.Length; ry++)
                    for (int rx = 0; rx < rows[ry].Length; rx++)
                        if (rows[ry][rx] == '#') Set(pixels, penX + rx, penY + ry, white);
                _glyphs[ch] = new Glyph { X = penX, Y = penY, Width = w, Height = rows.Length, Advance = w + 1 };
                penX += w + 1;
            }
            _fallback = _glyphs.TryGetValue('?', out var q) ? q : default;

            // Icons in a grid below the glyphs.
            int iconTop = penY + rowHeight + 2;
            int perRow = Size / (IconSize + 1);
            for (int i = 0; i < PixelArtData.Icons.Length; i++)
            {
                int ox = (i % perRow) * (IconSize + 1);
                int oy = iconTop + (i / perRow) * (IconSize + 1);
                var rows = PixelArtData.Icons[i];
                for (int ry = 0; ry < IconSize && ry < rows.Length; ry++)
                    for (int rx = 0; rx < IconSize && rx < rows[ry].Length; rx++)
                    {
                        char c = rows[ry][rx];
                        if (c != '.') Set(pixels, ox + rx, oy + ry, IconColor(c));
                    }
                _icons[PixelArtData.IconNames[i]] = new Vector2Int(ox, oy);
            }

            Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "IceCoast UI Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            Texture.SetPixels32(pixels);
            Texture.Apply(false, true);
        }

        public Glyph GetGlyph(char c) => _glyphs.TryGetValue(c, out var g) ? g : _fallback;

        public bool TryGetIcon(string name, out Vector2Int topLeft) => _icons.TryGetValue(name, out topLeft);

        /// <summary>Width in pixels of <paramref name="text"/> drawn on one line.</summary>
        public int Measure(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int w = 0;
            foreach (char c in text) w += GetGlyph(c).Advance;
            return w - 1;
        }

        /// <summary>Atlas pixel (top-left origin) to texture UV.</summary>
        public Vector2 Uv(float x, float y) => new Vector2(x / Size, 1f - y / Size);

        private static void Set(Color32[] pixels, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            pixels[(Size - 1 - y) * Size + x] = c; // texture rows run bottom-up
        }

        private static Color32 IconColor(char c)
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
