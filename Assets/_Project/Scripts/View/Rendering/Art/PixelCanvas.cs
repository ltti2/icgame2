using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// A tiny drawing surface for authoring pixel-art sprites in code. Rows count from the top, like the
    /// character art in <see cref="SpriteArt"/>. Colours are sRGB; alpha 0 is transparent.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width, Height;
        private readonly Color32[] _px;

        /// <summary>Anchor point in texels from the top-left (feet, or the grip on the cable).</summary>
        public int PivotX, PivotY;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            _px = new Color32[width * height];
            PivotX = width / 2;
            PivotY = height - 1;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Color32 Get(int x, int y) => InBounds(x, y) ? _px[y * Width + x] : default;
        public bool IsSet(int x, int y) => Get(x, y).a >= 128;

        public void Set(int x, int y, Color32 c)
        {
            if (InBounds(x, y)) _px[y * Width + x] = c;
        }

        public void Clear(int x, int y)
        {
            if (InBounds(x, y)) _px[y * Width + x] = default;
        }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    Set(i, j, c);
        }

        public void HLine(int x0, int x1, int y, Color32 c)
        {
            for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++) Set(x, y, c);
        }

        public void VLine(int x, int y0, int y1, Color32 c)
        {
            for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++) Set(x, y, c);
        }

        /// <summary>Bresenham line.</summary>
        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Fills a polygon (vertices in canvas pixels, rows from the top) by pixel centres.</summary>
        public void FillPolygon(float[] xs, float[] ys, Color32 c) => FillPolygon(xs, ys, (x, y) => c);

        /// <summary>Fills a polygon, asking <paramref name="shade"/> for each pixel's colour (for textures).</summary>
        public void FillPolygon(float[] xs, float[] ys, System.Func<int, int, Color32> shade)
        {
            int n = xs.Length;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++) { minY = Mathf.Min(minY, ys[i]); maxY = Mathf.Max(maxY, ys[i]); }
            var hits = new System.Collections.Generic.List<float>(8);
            for (int y = Mathf.Max(0, Mathf.FloorToInt(minY)); y <= Mathf.Min(Height - 1, Mathf.CeilToInt(maxY)); y++)
            {
                float cy = y + 0.5f;
                hits.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float y0 = ys[j], y1 = ys[i];
                    if ((y0 <= cy) == (y1 <= cy)) continue;
                    hits.Add(xs[j] + (cy - y0) / (y1 - y0) * (xs[i] - xs[j]));
                }
                hits.Sort();
                for (int k = 0; k + 1 < hits.Count; k += 2)
                    for (int x = Mathf.Max(0, Mathf.CeilToInt(hits[k] - 0.5f)); x <= Mathf.Min(Width - 1, Mathf.FloorToInt(hits[k + 1] - 0.5f)); x++)
                        Set(x, y, shade(x, y));
            }
        }

        /// <summary>
        /// Paints rows of character art: each character is looked up in <paramref name="palette"/>
        /// (pairs of key and colour); '.' and unknown characters are left transparent.
        /// </summary>
        public void Paint(int ox, int oy, string[] rows, params (char key, Color32 color)[] palette)
        {
            for (int y = 0; y < rows.Length; y++)
            {
                string row = rows[y];
                for (int x = 0; x < row.Length; x++)
                {
                    char ch = row[x];
                    if (ch == '.') continue;
                    for (int k = 0; k < palette.Length; k++)
                        if (palette[k].key == ch) { Set(ox + x, oy + y, palette[k].color); break; }
                }
            }
        }
    }
}
