using System.Collections.Generic;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// Collects the interface as textured quads during Update and draws them in OnGUI's repaint with
    /// immediate-mode GL, in submission order (so later = on top). Coordinates are interface pixels with a
    /// top-left origin; <see cref="Render"/> scales them by the interface scale. Supports nested clip rects,
    /// clipping quads on the CPU so scrolling lists and window contents never spill out.
    /// </summary>
    public sealed class UiRenderer
    {
        public const string ShaderName = "IceCoast/UI";

        private struct Quad
        {
            public float X0, Y0, X1, Y1, U0, V0, U1, V1;
            public Color32 Color;
            public Texture Texture;
        }

        private readonly List<Quad> _quads = new List<Quad>(4096);
        private readonly Stack<Rect> _clipStack = new Stack<Rect>();
        private readonly Material _material;
        private Rect _clip;

        public UiAtlas Atlas { get; }
        public int LineHeight => PixelArtData.LineHeight;
        public Rect Clip => _clip;

        public UiRenderer(UiAtlas atlas)
        {
            Atlas = atlas;
            var shader = Resources.Load<Shader>("IceCoastUi");
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("UI shader 'IceCoast/UI' not found (Assets/_Project/Resources/IceCoastUi.shader). Falling back to Sprites/Default.");
                shader = Shader.Find("Sprites/Default");
            }
            _material = new Material(shader) { name = "IceCoast UI", hideFlags = HideFlags.HideAndDontSave };
        }

        public void Begin(float width, float height)
        {
            _quads.Clear();
            _clipStack.Clear();
            _clip = new Rect(0, 0, width, height);
        }

        // ---------- Clipping ----------

        public void PushClip(Rect r)
        {
            _clipStack.Push(_clip);
            float x0 = Mathf.Max(_clip.xMin, r.xMin), y0 = Mathf.Max(_clip.yMin, r.yMin);
            float x1 = Mathf.Min(_clip.xMax, r.xMax), y1 = Mathf.Min(_clip.yMax, r.yMax);
            _clip = Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }

        public void PopClip()
        {
            if (_clipStack.Count > 0) _clip = _clipStack.Pop();
        }

        // ---------- Primitives ----------

        public void Fill(Rect r, Color32 color)
        {
            var w = Atlas.WhitePixel;
            var uv = Atlas.Uv(w.x + 0.5f, w.y + 0.5f);
            AddQuad(r.xMin, r.yMin, r.xMax, r.yMax, uv.x, uv.y, uv.x, uv.y, color, Atlas.Texture);
        }

        public void Outline(Rect r, Color32 color)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), color);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Fill(new Rect(r.x, r.y + 1, 1, r.height - 2), color);
            Fill(new Rect(r.xMax - 1, r.y + 1, 1, r.height - 2), color);
        }

        /// <summary>An OpenTTD widget: flat face with a one-pixel bevel. Lowered = pressed / inset.</summary>
        public void Bevel(Rect r, UiScheme s, bool lowered)
        {
            Fill(r, lowered ? s.Lowered : s.Fill);
            var hi = lowered ? s.Dark : s.Light;
            var lo = lowered ? s.Light : s.Dark;
            Fill(new Rect(r.x, r.y, r.width, 1), hi);
            Fill(new Rect(r.x, r.y + 1, 1, r.height - 1), hi);
            Fill(new Rect(r.x + 1, r.yMax - 1, r.width - 1, 1), lo);
            Fill(new Rect(r.xMax - 1, r.y + 1, 1, r.height - 2), lo);
        }

        /// <summary>Draws text with its line box's top-left at (x, y). Returns the x after the last glyph.</summary>
        public float Text(float x, float y, string text, Color32 color)
        {
            if (string.IsNullOrEmpty(text)) return x;
            var tex = Atlas.Texture;
            float top = y + 1f;
            foreach (char c in text)
            {
                var g = Atlas.GetGlyph(c);
                if (c != ' ')
                {
                    var uv0 = Atlas.Uv(g.X, g.Y);
                    var uv1 = Atlas.Uv(g.X + g.Width, g.Y + g.Height);
                    AddQuad(x, top, x + g.Width, top + g.Height, uv0.x, uv0.y, uv1.x, uv1.y, color, tex);
                }
                x += g.Advance;
            }
            return x;
        }

        /// <summary>Text with a one-pixel drop shadow, for readability over the map.</summary>
        public void ShadowText(float x, float y, string text, Color32 color)
        {
            Text(x + 1, y + 1, text, UiTheme.Black);
            Text(x, y, text, color);
        }

        public void TextRight(float right, float y, string text, Color32 color) => Text(right - Measure(text), y, text, color);

        public void TextCentered(Rect r, string text, Color32 color)
            => Text(Mathf.Floor(r.center.x - Measure(text) * 0.5f), Mathf.Floor(r.center.y - LineHeight * 0.5f), text, color);

        public int Measure(string text) => Atlas.Measure(text);

        /// <summary>Shortens text with "…" so it fits in <paramref name="maxWidth"/> pixels.</summary>
        public string Ellipsize(string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || Measure(text) <= maxWidth) return text;
            const string dots = "…";
            float dotsW = Measure(dots) + 1;
            int len = text.Length;
            while (len > 0 && Measure(text.Substring(0, len)) + dotsW > maxWidth) len--;
            return text.Substring(0, len) + dots;
        }

        /// <summary>Splits text into lines no wider than <paramref name="maxWidth"/>.</summary>
        public void Wrap(string text, float maxWidth, List<string> lines)
        {
            lines.Clear();
            if (string.IsNullOrEmpty(text)) return;
            foreach (var paragraph in text.Split('\n'))
            {
                var words = paragraph.Split(' ');
                string line = "";
                foreach (var word in words)
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Measure(candidate) > maxWidth)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else line = candidate;
                }
                lines.Add(line);
            }
        }

        public void Icon(string name, float x, float y, Color32 tint)
        {
            if (!Atlas.TryGetIcon(name, out var p)) return;
            var uv0 = Atlas.Uv(p.x, p.y);
            var uv1 = Atlas.Uv(p.x + UiAtlas.IconSize, p.y + UiAtlas.IconSize);
            AddQuad(x, y, x + UiAtlas.IconSize, y + UiAtlas.IconSize, uv0.x, uv0.y, uv1.x, uv1.y, tint, Atlas.Texture);
        }

        public void Icon(string name, Rect r, Color32 tint)
            => Icon(name, Mathf.Floor(r.center.x - UiAtlas.IconSize * 0.5f), Mathf.Floor(r.center.y - UiAtlas.IconSize * 0.5f), tint);

        /// <summary>Draws any texture (e.g. the minimap). <paramref name="uv"/> uses Unity's bottom-left UV origin.</summary>
        public void Image(Texture texture, Rect r, Rect uv, Color32 tint)
            => AddQuad(r.xMin, r.yMin, r.xMax, r.yMax, uv.xMin, uv.yMax, uv.xMax, uv.yMin, tint, texture);

        // ---------- Quads ----------

        private void AddQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Color32 color, Texture tex)
        {
            if (x1 <= _clip.xMin || x0 >= _clip.xMax || y1 <= _clip.yMin || y0 >= _clip.yMax) return;
            if (x0 < _clip.xMin) { u0 = Mathf.Lerp(u0, u1, (_clip.xMin - x0) / (x1 - x0)); x0 = _clip.xMin; }
            if (x1 > _clip.xMax) { u1 = Mathf.Lerp(u1, u0, (x1 - _clip.xMax) / (x1 - x0)); x1 = _clip.xMax; }
            if (y0 < _clip.yMin) { v0 = Mathf.Lerp(v0, v1, (_clip.yMin - y0) / (y1 - y0)); y0 = _clip.yMin; }
            if (y1 > _clip.yMax) { v1 = Mathf.Lerp(v1, v0, (y1 - _clip.yMax) / (y1 - y0)); y1 = _clip.yMax; }
            _quads.Add(new Quad { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, U0 = u0, V0 = v0, U1 = u1, V1 = v1, Color = color, Texture = tex });
        }

        /// <summary>Call from OnGUI during the Repaint event.</summary>
        public void Render(float uiWidth, float uiHeight)
        {
            if (_quads.Count == 0) return;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, uiWidth, uiHeight, 0f);
            Texture current = null;
            bool open = false;
            for (int i = 0; i < _quads.Count; i++)
            {
                var q = _quads[i];
                if (!open || q.Texture != current)
                {
                    if (open) GL.End();
                    current = q.Texture;
                    _material.mainTexture = current;
                    _material.SetPass(0);
                    GL.Begin(GL.QUADS);
                    open = true;
                }
                GL.Color(q.Color);
                GL.TexCoord2(q.U0, q.V0); GL.Vertex3(q.X0, q.Y0, 0f);
                GL.TexCoord2(q.U1, q.V0); GL.Vertex3(q.X1, q.Y0, 0f);
                GL.TexCoord2(q.U1, q.V1); GL.Vertex3(q.X1, q.Y1, 0f);
                GL.TexCoord2(q.U0, q.V1); GL.Vertex3(q.X0, q.Y1, 0f);
            }
            if (open) GL.End();
            GL.PopMatrix();
        }
    }
}
