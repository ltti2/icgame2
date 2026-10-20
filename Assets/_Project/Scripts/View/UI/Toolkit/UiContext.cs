using System.Collections.Generic;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>Scroll position and extent of a scrolling list. Keep one per list.</summary>
    public sealed class ScrollState
    {
        public float Offset;
        public float ContentHeight;
        public float ViewHeight;

        public void Clamp() => Offset = Mathf.Clamp(Offset, 0f, Mathf.Max(0f, ContentHeight - ViewHeight));
        public void ScrollToBottom() => Offset = Mathf.Max(0f, ContentHeight - ViewHeight);
    }

    /// <summary>
    /// A small immediate-mode widget kit in the OpenTTD style, drawn with <see cref="UiRenderer"/>.
    ///
    /// Each frame: <see cref="BeginFrame"/> reads input, the HUD decides which layer (window or panel) is
    /// under the mouse (<see cref="HotLayer"/>), then windows draw themselves bottom to top. Widgets only react
    /// when their layer is the hot one, so a click never goes through a window to the one beneath it.
    /// Layout is a simple top-to-bottom cursor (<see cref="BeginArea"/> / <see cref="Row"/>); widgets also
    /// have overloads that take an explicit rect.
    /// </summary>
    public sealed class UiContext
    {
        public const float RowSpacing = 2f;
        public const float ButtonHeight = 13f;
        public const float ArrowSize = 11f;

        private struct Area
        {
            public Rect Rect;
            public float Y;
        }

        private readonly Stack<Area> _areas = new Stack<Area>();
        private readonly List<string> _wrapLines = new List<string>();
        /// <summary>A keystroke for the focused text field: a special key, or a typed character.</summary>
        private struct KeyStroke
        {
            public KeyCode Key;
            public char Char;
        }

        // Kept in one list so typing and backspaces apply in the order they happened.
        private readonly List<KeyStroke> _keys = new List<KeyStroke>();
        private readonly List<KeyStroke> _pendingKeys = new List<KeyStroke>();

        public UiRenderer R { get; }
        public float Width { get; private set; }
        public float Height { get; private set; }
        /// <summary>Screen pixels per interface pixel. Whole numbers, or 1.5x for in-between screens.</summary>
        public float Scale { get; private set; } = 1f;

        // ---------- Input (interface pixels, top-left origin) ----------
        public Vector2 Mouse { get; private set; }
        public bool LeftDown { get; private set; }
        public bool LeftHeld { get; private set; }
        public bool LeftUp { get; private set; }
        public bool RightDown { get; private set; }
        public float Wheel { get; private set; }
        /// <summary>Set when a widget used this frame's click, so nothing else (including the world) does.</summary>
        public bool MouseConsumed;
        public bool WheelConsumed;

        // ---------- Interaction state ----------
        public int Layer { get; private set; }
        public int HotLayer;
        public UiScheme Scheme = UiTheme.Grey;
        private int _idCounter;
        /// <summary>Widget being held down (counter arrows, scrollbar thumb).</summary>
        public int ActiveId { get; private set; }
        /// <summary>Text field with keyboard focus (0 = none).</summary>
        public int FocusId { get; private set; }
        public bool HasTextFocus => FocusId != 0;
        private bool _focusTouched;
        private float _repeatAt;
        private float _dragGrab;

        // Tooltip
        private string _tooltip;
        private int _tooltipOwner, _hoverOwner;
        private float _hoverSince;
        private const float TooltipDelay = 0.45f;

        public UiContext(UiRenderer renderer) { R = renderer; }

        // =====================================================================
        // Frame
        // =====================================================================

        public void BeginFrame(float scale, Vector2 mouseScreenBottomLeft, bool leftDown, bool leftHeld, bool leftUp, bool rightDown, float wheel)
        {
            Scale = Mathf.Max(1f, scale);
            Width = Screen.width / (float)Scale;
            Height = Screen.height / (float)Scale;
            Mouse = new Vector2(mouseScreenBottomLeft.x, Screen.height - mouseScreenBottomLeft.y) / Scale;
            LeftDown = leftDown;
            LeftHeld = leftHeld;
            LeftUp = leftUp;
            RightDown = rightDown;
            Wheel = wheel;
            MouseConsumed = false;
            WheelConsumed = false;
            _focusTouched = false;

            _keys.Clear();
            _keys.AddRange(_pendingKeys);
            _pendingKeys.Clear();

            if (!leftHeld && !leftDown) ActiveId = 0;
            _tooltip = null;
            _tooltipOwner = 0;
            R.Begin(Width, Height);
            _areas.Clear();
        }

        /// <summary>Call after everything is drawn: clicking anywhere outside the focused field drops focus.</summary>
        public void EndFrame()
        {
            if (LeftDown && !_focusTouched) FocusId = 0;
            if (_tooltipOwner != _hoverOwner)
            {
                _hoverOwner = _tooltipOwner;
                _hoverSince = Time.unscaledTime;
            }
            if (_tooltip != null && Time.unscaledTime - _hoverSince >= TooltipDelay) DrawTooltip(_tooltip, Mouse.x + 10, Mouse.y + 14, null);
        }

        /// <summary>Keyboard events from OnGUI, for the focused text field. Applied next frame.</summary>
        public void QueueKey(KeyCode key, char c)
        {
            if (c != '\0' && c >= ' ' && c != 127) _pendingKeys.Add(new KeyStroke { Char = c });
            else if (key == KeyCode.Backspace || key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape)
                _pendingKeys.Add(new KeyStroke { Key = key });
        }

        public void ClearFocus() => FocusId = 0;

        public void BeginLayer(int layer, UiScheme scheme)
        {
            Layer = layer;
            Scheme = scheme;
            _idCounter = 0;
        }

        private int NextId() => (Layer << 12) + (++_idCounter);

        public bool LayerIsHot => Layer == HotLayer;

        /// <summary>Mouse is over <paramref name="r"/>, inside the clip rect, on the hot layer.</summary>
        public bool Hover(Rect r) => Layer == HotLayer && r.Contains(Mouse) && R.Clip.Contains(Mouse);

        /// <summary>A fresh left click on <paramref name="r"/>. Consumes the click.</summary>
        public bool Clicked(Rect r)
        {
            if (!LeftDown || MouseConsumed || !Hover(r)) return false;
            MouseConsumed = true;
            return true;
        }

        public void SetTooltip(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text) || !Hover(r)) return;
            _tooltip = text;
            _tooltipOwner = (Layer << 16) ^ ((int)r.x << 8) ^ (int)r.y;
        }

        // =====================================================================
        // Layout
        // =====================================================================

        public void BeginArea(Rect r)
        {
            _areas.Push(new Area { Rect = r, Y = r.y });
        }

        /// <summary>Ends the area and returns the height used.</summary>
        public float EndArea()
        {
            var a = _areas.Pop();
            return Mathf.Max(0f, a.Y - a.Rect.y - RowSpacing);
        }

        public Rect AreaRect => _areas.Count > 0 ? _areas.Peek().Rect : new Rect(0, 0, Width, Height);
        public float AreaWidth => AreaRect.width;
        public float CursorY => _areas.Count > 0 ? _areas.Peek().Y : 0f;

        /// <summary>Takes the next full-width row of the current area.</summary>
        public Rect Row(float height)
        {
            var a = _areas.Pop();
            var r = new Rect(a.Rect.x, a.Y, a.Rect.width, height);
            a.Y += height + RowSpacing;
            _areas.Push(a);
            return r;
        }

        public void Space(float h)
        {
            var a = _areas.Pop();
            a.Y += h;
            _areas.Push(a);
        }

        /// <summary>The <paramref name="index"/>-th of <paramref name="count"/> equal cells across a row.</summary>
        public static Rect Cell(Rect row, int index, int count, float gap = 1f)
        {
            float w = (row.width - gap * (count - 1)) / count;
            float x = Mathf.Floor(row.x + index * (w + gap));
            float x1 = index == count - 1 ? row.xMax : Mathf.Floor(row.x + index * (w + gap) + w);
            return new Rect(x, row.y, x1 - x, row.height);
        }

        public static Rect SplitLeft(Rect r, float width) => new Rect(r.x, r.y, width, r.height);
        public static Rect SplitRight(Rect r, float width) => new Rect(r.xMax - width, r.y, width, r.height);

        // =====================================================================
        // Text
        // =====================================================================

        public void Label(string text) => Label(text, Scheme.Text);

        public void Label(string text, Color32 color)
        {
            var r = Row(R.LineHeight);
            R.Text(r.x, r.y, R.Ellipsize(text, r.width), color);
        }

        public void Label(Rect r, string text, Color32 color) => R.Text(r.x, Mathf.Floor(r.center.y - R.LineHeight * 0.5f), R.Ellipsize(text, r.width), color);

        /// <summary>Multi-line text that wraps to the area width.</summary>
        public void Paragraph(string text, Color32 color)
        {
            R.Wrap(text, AreaWidth, _wrapLines);
            foreach (var line in _wrapLines)
            {
                var r = Row(R.LineHeight - 1);
                R.Text(r.x, r.y, line, color);
            }
        }

        public void Heading(string text)
        {
            var r = Row(R.LineHeight);
            R.Text(r.x, r.y, R.Ellipsize(text, r.width), Scheme.Heading);
        }

        /// <summary>"Label: value" with the value right-aligned, OpenTTD info-panel style.</summary>
        public void Field(string label, string value) => Field(label, value, Scheme.Text);

        public void Field(string label, string value, Color32 valueColor)
        {
            var r = Row(R.LineHeight);
            int vw = R.Measure(value);
            R.Text(r.x, r.y, R.Ellipsize(label, r.width - vw - 6), Scheme.Text);
            R.Text(r.xMax - vw, r.y, value, valueColor);
        }

        public void Separator()
        {
            var r = Row(2);
            R.Fill(new Rect(r.x, r.y, r.width, 1), Scheme.Dark);
            R.Fill(new Rect(r.x, r.y + 1, r.width, 1), Scheme.Light);
        }

        // =====================================================================
        // Buttons
        // =====================================================================

        public bool Button(string text, bool enabled = true, bool down = false, string tooltip = null)
            => Button(Row(ButtonHeight), text, enabled, down, tooltip);

        public bool Button(Rect r, string text, bool enabled = true, bool down = false, string tooltip = null)
        {
            bool hover = enabled && Hover(r);
            bool pressed = hover && LeftHeld && !MouseConsumed;
            R.Bevel(r, Scheme, down || pressed);
            var color = !enabled ? UiTheme.TextDisabled : down ? Scheme.TextOnLowered : Scheme.Text;
            R.TextCentered(r, R.Ellipsize(text, r.width - 4), color);
            SetTooltip(r, tooltip);
            return enabled && Clicked(r);
        }

        public bool IconButton(Rect r, string icon, bool down = false, string tooltip = null, bool enabled = true)
        {
            bool hover = enabled && Hover(r);
            bool pressed = hover && LeftHeld && !MouseConsumed;
            bool lowered = down || pressed;
            R.Bevel(r, Scheme, lowered);
            var ir = lowered ? new Rect(r.x + 1, r.y + 1, r.width, r.height) : r;
            R.Icon(icon, ir, enabled ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 110));
            SetTooltip(r, tooltip);
            return enabled && Clicked(r);
        }

        /// <summary>A row of mutually exclusive buttons. Returns the selected index (changed if clicked).</summary>
        public int Toggles(int selected, params string[] labels)
        {
            var row = Row(ButtonHeight);
            for (int i = 0; i < labels.Length; i++)
                if (Button(Cell(row, i, labels.Length), labels[i], true, i == selected)) selected = i;
            return selected;
        }

        /// <summary>A small square "arrow" button that repeats while held. Returns true when it fires.</summary>
        public bool ArrowButton(Rect r, string glyph, bool enabled)
        {
            int id = NextId();
            bool hover = enabled && Hover(r);
            bool fire = false;
            if (hover && LeftDown && !MouseConsumed)
            {
                MouseConsumed = true;
                ActiveId = id;
                _repeatAt = Time.unscaledTime + 0.4f;
                fire = true;
            }
            else if (ActiveId == id && LeftHeld && hover && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + 0.07f;
                fire = true;
            }
            R.Bevel(r, Scheme, ActiveId == id && LeftHeld && hover);
            R.TextCentered(new Rect(r.x + 1, r.y, r.width, r.height), glyph, enabled ? Scheme.Text : UiTheme.TextDisabled);
            return fire;
        }

        /// <summary>
        /// A number setting: label on the left, then [▼] value [▲] on the right. Hold an arrow to repeat.
        /// <paramref name="display"/> overrides how the value is shown (e.g. "5 x 5").
        /// </summary>
        public int Counter(string label, int value, int min, int max, int step = 1, string display = null, string tooltip = null)
        {
            var row = Row(ArrowSize + 1);
            string shown = display ?? value.ToString();
            float valueW = Mathf.Max(30f, R.Measure(shown) + 8f);
            var up = new Rect(row.xMax - ArrowSize, row.y, ArrowSize, ArrowSize);
            var box = new Rect(up.x - valueW - 1, row.y, valueW, ArrowSize);
            var down = new Rect(box.x - ArrowSize - 1, row.y, ArrowSize, ArrowSize);

            R.Text(row.x, row.y, R.Ellipsize(label, down.x - row.x - 4), Scheme.Text);
            if (ArrowButton(down, "▼", value > min)) value = Mathf.Max(min, value - step);
            R.Bevel(box, Scheme, true);
            R.TextCentered(box, shown, Scheme.TextOnLowered);
            if (ArrowButton(up, "▲", value < max)) value = Mathf.Min(max, value + step);
            SetTooltip(row, tooltip);
            return value;
        }

        /// <summary>OpenTTD-style on/off setting: a little box with a tick, then the label.</summary>
        public bool Checkbox(string label, bool value, string tooltip = null)
        {
            var row = Row(ArrowSize + 1);
            var box = new Rect(row.x, row.y, ArrowSize, ArrowSize);
            R.Bevel(box, Scheme, true);
            if (value) R.TextCentered(new Rect(box.x + 1, box.y, box.width, box.height), "✓", Scheme.TextOnLowered);
            R.Text(box.xMax + 4, row.y, R.Ellipsize(label, row.width - ArrowSize - 4), Scheme.Text);
            SetTooltip(row, tooltip);
            if (Clicked(row)) value = !value;
            return value;
        }

        /// <summary>A clickable list row. Selected rows are drawn inverted, like OpenTTD lists.</summary>
        public bool ListRow(string text, bool selected, Color32 color, string rightText = null)
        {
            var r = Row(R.LineHeight);
            bool hover = Hover(r);
            if (selected) R.Fill(r, UiTheme.Highlight);
            else if (hover) R.Fill(r, UiTheme.WithAlpha(UiTheme.Black, 40));
            var c = selected ? UiTheme.TextWhite : color;
            float rightW = rightText != null ? R.Measure(rightText) + 6 : 0;
            R.Text(r.x + 2, r.y, R.Ellipsize(text, r.width - 4 - rightW), c);
            if (rightText != null) R.TextRight(r.xMax - 2, r.y, rightText, c);
            return Clicked(r);
        }

        // =====================================================================
        // Text field
        // =====================================================================

        /// <summary>Single-line text input. Returns true on Enter.</summary>
        public bool TextField(ref string text, int maxLength)
        {
            var r = Row(ButtonHeight);
            int id = NextId();
            if (Clicked(r))
            {
                FocusId = id;
                _focusTouched = true;
            }
            bool focused = FocusId == id;
            bool submitted = false;
            if (focused)
            {
                if (r.Contains(Mouse)) _focusTouched = true;
                text = text ?? "";
                foreach (var k in _keys)
                {
                    if (k.Char != '\0') { if (text.Length < maxLength) text += k.Char; }
                    else if (k.Key == KeyCode.Backspace && text.Length > 0) text = text.Substring(0, text.Length - 1);
                    else if (k.Key == KeyCode.Return || k.Key == KeyCode.KeypadEnter) { submitted = true; FocusId = 0; break; }
                    else if (k.Key == KeyCode.Escape) { FocusId = 0; break; }
                }
                _keys.Clear();
            }

            R.Bevel(r, Scheme, true);
            R.Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), UiTheme.Black);
            string shown = text ?? "";
            float maxW = r.width - 8;
            while (shown.Length > 0 && R.Measure(shown) > maxW) shown = shown.Substring(1); // keep the end visible
            float end = R.Text(r.x + 3, r.y + 1, shown, UiTheme.TextWhite);
            if (focused && (Time.unscaledTime * 2f % 2f) < 1.2f) R.Fill(new Rect(end, r.y + 3, 1, 8), UiTheme.TextWhite);
            return submitted;
        }

        // =====================================================================
        // Scrolling
        // =====================================================================

        /// <summary>Starts a scrolling inset panel of <paramref name="height"/> pixels. Pair with EndScroll.</summary>
        public void BeginScroll(ScrollState s, float height)
        {
            var outer = Row(height);
            R.Bevel(outer, Scheme, true);
            var view = new Rect(outer.x + 2, outer.y + 2, outer.width - ArrowSize - 4, outer.height - 4);
            s.ViewHeight = view.height;
            s.Clamp();

            // Scrollbar
            var bar = new Rect(outer.xMax - ArrowSize - 1, outer.y + 1, ArrowSize, outer.height - 2);
            var upR = new Rect(bar.x, bar.y, ArrowSize, ArrowSize);
            var downR = new Rect(bar.x, bar.yMax - ArrowSize, ArrowSize, ArrowSize);
            float step = R.LineHeight * 2;
            if (ArrowButton(upR, "▲", s.Offset > 0)) s.Offset -= step;
            if (ArrowButton(downR, "▼", s.Offset < s.ContentHeight - s.ViewHeight)) s.Offset += step;
            var track = new Rect(bar.x, upR.yMax, ArrowSize, downR.y - upR.yMax);
            R.Fill(track, Scheme.Lowered);
            if (s.ContentHeight > s.ViewHeight && track.height > 6)
            {
                float frac = s.ViewHeight / s.ContentHeight;
                float thumbH = Mathf.Max(6f, track.height * frac);
                float range = track.height - thumbH;
                float maxOffset = s.ContentHeight - s.ViewHeight;
                var thumb = new Rect(track.x, track.y + range * (s.Offset / maxOffset), ArrowSize, thumbH);
                int id = NextId();
                if (Hover(thumb) && LeftDown && !MouseConsumed)
                {
                    MouseConsumed = true;
                    ActiveId = id;
                    _dragGrab = Mouse.y - thumb.y;
                }
                if (ActiveId == id && LeftHeld && range > 0f)
                    s.Offset = Mathf.Clamp01((Mouse.y - _dragGrab - track.y) / range) * maxOffset;
                R.Bevel(thumb, Scheme, false);
            }

            if (Hover(outer) && Wheel != 0f && !WheelConsumed)
            {
                s.Offset -= Wheel * step;
                WheelConsumed = true;
            }
            s.Clamp();

            R.PushClip(view);
            BeginArea(new Rect(view.x, view.y - Mathf.Round(s.Offset), view.width, 100000f));
        }

        public void EndScroll(ScrollState s)
        {
            s.ContentHeight = EndArea();
            R.PopClip();
            s.Clamp();
        }

        // =====================================================================
        // Tooltip
        // =====================================================================

        /// <summary>Draws an OpenTTD-style tooltip box. Error text is drawn in red.</summary>
        public void DrawTooltip(string text, float x, float y, Color32? color)
        {
            if (string.IsNullOrEmpty(text)) return;
            R.Wrap(text, 260, _wrapLines);
            float w = 0;
            foreach (var l in _wrapLines) w = Mathf.Max(w, R.Measure(l));
            float h = _wrapLines.Count * (R.LineHeight - 1) + 4;
            w += 8;
            if (x + w > Width) x = Width - w - 2;
            if (y + h > Height) y = Height - h - 2;
            var box = new Rect(Mathf.Floor(x), Mathf.Floor(y), w, h);
            R.Fill(box, UiTheme.TooltipFill);
            R.Outline(box, UiTheme.Black);
            float ly = box.y + 2;
            foreach (var l in _wrapLines)
            {
                R.Text(box.x + 4, ly, l, color ?? UiTheme.TextBlack);
                ly += R.LineHeight - 1;
            }
        }
    }
}
