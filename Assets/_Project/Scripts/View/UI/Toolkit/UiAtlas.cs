using System.Collections.Generic;
using IceCoast.View.Rendering;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// One small texture holding the pixel font, the 16x16 icons and a white texel for solid rectangles,
    /// so the entire interface draws from a single texture. Built at startup from the art library (the editable
    /// ui_icons and ui_font sheets, or the code-drawn defaults in <see cref="UiArt"/>); point-filtered so it stays
    /// crisp at 2x/3x interface scale.
    /// </summary>
    public sealed class UiAtlas
    {
        public struct Glyph
        {
            public int X, Y, Width, Height; // pixels in the atlas (top-left origin)
            public int Advance;
        }

        public const int Size = 256;
        public const int IconSize = UiArt.IconSize;

        public Texture2D Texture { get; }
        /// <summary>Atlas pixel (top-left origin) that is solid white.</summary>
        public Vector2Int WhitePixel { get; }

        private readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();
        private readonly Dictionary<string, Vector2Int> _icons = new Dictionary<string, Vector2Int>();
        private Glyph _fallback;

        public UiAtlas() : this(ArtLibrary.Current) { }

        public UiAtlas(ArtLibrary art)
        {
            Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "IceCoast UI Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            WhitePixel = new Vector2Int(1, 1);
            Paint(art);
            ArtLibrary.Reloaded += Reload;
        }

        /// <summary>Redraws the atlas from edited art (glyph widths may change; icons stay 16x16).</summary>
        public void Reload(ArtLibrary art) => Paint(art);

        private void Paint(ArtLibrary art)
        {
            var pixels = new Color32[Size * Size];
            var white = new Color32(255, 255, 255, 255);
            _glyphs.Clear();
            _icons.Clear();

            // White block for solid fills (top-left 4x4).
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    Set(pixels, x, y, white);

            // Glyphs, packed in rows below the white block. Any opaque pixel in the font sheet is ink.
            int penX = 6, penY = 0, rowHeight = 10;
            for (int i = 0; i < UiArt.GlyphCount; i++)
            {
                char ch = UiArt.GlyphChar(i);
                var glyph = art.Sprite(ArtSheets.GlyphName(ch)) ?? UiArt.Glyph(i);
                int w = glyph.Width, h = Mathf.Min(glyph.Height, rowHeight);
                if (penX + w + 1 > Size) { penX = 0; penY += rowHeight; }
                for (int ry = 0; ry < h; ry++)
                    for (int rx = 0; rx < w; rx++)
                        if (glyph.IsSet(rx, ry)) Set(pixels, penX + rx, penY + ry, white);
                _glyphs[ch] = new Glyph { X = penX, Y = penY, Width = w, Height = h, Advance = w + 1 };
                penX += w + 1;
            }
            _fallback = _glyphs.TryGetValue('?', out var q) ? q : default;

            // Icons in a grid below the glyphs.
            int iconTop = penY + rowHeight + 2;
            int perRow = Size / (IconSize + 1);
            for (int i = 0; i < UiArt.IconCount; i++)
            {
                int ox = (i % perRow) * (IconSize + 1);
                int oy = iconTop + (i / perRow) * (IconSize + 1);
                var icon = art.Sprite(ArtSheets.IconName(UiArt.IconName(i))) ?? UiArt.Icon(i);
                for (int ry = 0; ry < IconSize && ry < icon.Height; ry++)
                    for (int rx = 0; rx < IconSize && rx < icon.Width; rx++)
                        if (icon.IsSet(rx, ry)) Set(pixels, ox + rx, oy + ry, icon.Get(rx, ry));
                _icons[UiArt.IconName(i)] = new Vector2Int(ox, oy);
            }

            Texture.SetPixels32(pixels);
            Texture.Apply(false, false);
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
    }
}
