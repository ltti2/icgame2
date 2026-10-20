using System.Collections.Generic;
using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// Keeps the floating windows: z-order (click to bring forward), dragging by the caption, resizing,
    /// shading, pinning, and closing. Windows are identified by key so opening "the tile window" twice
    /// reuses the existing one. Like OpenTTD, Delete closes every window that isn't pinned.
    /// </summary>
    public sealed class WindowManager
    {
        private readonly List<UiWindow> _windows = new List<UiWindow>();
        private readonly List<UiWindow> _drawOrder = new List<UiWindow>();
        private int _nextLayer = 100;
        private int _cascade;

        private UiWindow _dragging;
        private Vector2 _dragOffset;
        private UiWindow _resizing;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;

        /// <summary>The region windows live in (between the toolbar and the status bar).</summary>
        public Rect Bounds;
        /// <summary>Areas new windows should not be placed over (e.g. the minimap panel). Set each frame.</summary>
        public readonly List<Rect> Avoid = new List<Rect>();

        public IReadOnlyList<UiWindow> Windows => _windows;
        public bool IsInteracting => _dragging != null || _resizing != null;

        // =====================================================================
        // Opening and closing
        // =====================================================================

        /// <summary>Opens <paramref name="window"/>, or brings forward the open window with the same key.</summary>
        public T Open<T>(T window) where T : UiWindow
        {
            var existing = Find(window.Key);
            if (existing is T same)
            {
                BringToFront(same);
                return same;
            }
            if (existing != null) Close(existing);

            window.LayerId = _nextLayer++;
            window.IsOpen = true;
            _windows.Add(window);
            Clamp(window);
            window.OnShown();
            return window;
        }

        public UiWindow Find(string key)
        {
            foreach (var w in _windows) if (w.Key == key) return w;
            return null;
        }

        public bool IsOpen(string key) => Find(key) != null;

        public void Close(UiWindow w)
        {
            if (w == null || !w.IsOpen) return;
            w.IsOpen = false;
            _windows.Remove(w);
            if (_dragging == w) _dragging = null;
            if (_resizing == w) _resizing = null;
            w.OnClosed();
        }

        public void Close(string key) => Close(Find(key));

        public void CloseAllUnpinned()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (i < _windows.Count && !_windows[i].Pinned) Close(_windows[i]);
        }

        public void CloseAll()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (i < _windows.Count) Close(_windows[i]);
        }

        public void BringToFront(UiWindow w)
        {
            if (_windows.Remove(w)) _windows.Add(w);
        }

        // =====================================================================
        // Placement
        // =====================================================================

        /// <summary>Dropdown placement: right under the button that opened it.</summary>
        public void PlaceBelow(UiWindow w, Rect anchor)
        {
            w.AnchorRect = anchor;
            w.Rect.x = anchor.x;
            w.Rect.y = anchor.yMax + 1;
            if (w.Rect.xMax > Bounds.xMax) w.Rect.x = Bounds.xMax - w.Rect.width;
            Clamp(w);
        }

        /// <summary>Next to a point (e.g. where the player clicked), kept on screen.</summary>
        public void PlaceNear(UiWindow w, Vector2 p)
        {
            w.Rect.x = p.x + 24;
            w.Rect.y = p.y - 30;
            if (w.Rect.xMax > Bounds.xMax) w.Rect.x = p.x - 24 - w.Rect.width;
            if (w.Rect.yMax > Bounds.yMax) w.Rect.y = Bounds.yMax - w.Rect.height;
            Clamp(w);
        }

        public void PlaceCentered(UiWindow w)
        {
            w.Rect.x = Mathf.Floor(Bounds.center.x - w.Rect.width * 0.5f);
            w.Rect.y = Mathf.Floor(Bounds.center.y - w.Rect.height * 0.5f);
            Clamp(w);
        }

        /// <summary>Default spot for a new window: cascading down from the top-left.</summary>
        public void PlaceCascade(UiWindow w)
        {
            w.Rect.x = Bounds.x + 30 + (_cascade % 8) * 18;
            w.Rect.y = Bounds.y + 20 + (_cascade % 8) * 18;
            _cascade++;
            Clamp(w);
        }

        /// <summary>
        /// Like OpenTTD: put the window in the first free space, scanning from the top-left, once its size is
        /// known. Falls back to cascading when the screen is full.
        /// </summary>
        public void PlaceInFreeSpace(UiWindow w)
        {
            PlaceCascade(w);
            w.PendingPlacement = true;
        }

        private void ResolvePlacement(UiWindow w)
        {
            w.PendingPlacement = false;
            const float step = 8f, margin = 4f;
            for (float y = Bounds.yMin + margin; y + w.Rect.height <= Bounds.yMax - margin; y += step)
                for (float x = Bounds.xMin + margin; x + w.Rect.width <= Bounds.xMax - margin; x += step)
                {
                    var candidate = new Rect(x, y, w.Rect.width, w.Rect.height);
                    if (IsFree(candidate, w))
                    {
                        w.Rect.x = x;
                        w.Rect.y = y;
                        return;
                    }
                }
        }

        private bool IsFree(Rect r, UiWindow self)
        {
            var padded = new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6);
            foreach (var other in _windows)
                if (other != self && padded.Overlaps(VisibleRect(other))) return false;
            foreach (var a in Avoid)
                if (padded.Overlaps(a)) return false;
            return true;
        }

        private void Clamp(UiWindow w)
        {
            if (Bounds.width <= 0) return;
            w.Rect.x = Mathf.Round(Mathf.Clamp(w.Rect.x, Bounds.xMin - w.Rect.width + 40f, Bounds.xMax - 40f));
            w.Rect.y = Mathf.Round(Mathf.Clamp(w.Rect.y, Bounds.yMin, Mathf.Max(Bounds.yMin, Bounds.yMax - UiWindow.CaptionHeight)));
        }

        // =====================================================================
        // Hit testing
        // =====================================================================

        private static Rect VisibleRect(UiWindow w)
            => w.Shaded ? new Rect(w.Rect.x, w.Rect.y, w.Rect.width, UiWindow.CaptionHeight) : w.Rect;

        public UiWindow TopmostAt(Vector2 p)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (VisibleRect(_windows[i]).Contains(p)) return _windows[i];
            return null;
        }

        // =====================================================================
        // Update + draw
        // =====================================================================

        /// <summary>Moves/resizes, handles caption buttons, and draws every window bottom to top.</summary>
        public void Draw(UiContext ui)
        {
            // Windows whose subject disappeared.
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (i < _windows.Count && !_windows[i].StillValid()) Close(_windows[i]);

            ContinueDragAndResize(ui);

            // Menus close when the player clicks somewhere else; any click raises the window under it.
            if (ui.LeftDown)
            {
                for (int i = _windows.Count - 1; i >= 0; i--)
                {
                    if (i >= _windows.Count) continue;
                    var w = _windows[i];
                    if (w.CloseOnOutsideClick && !w.UserMoved && ui.HotLayer != w.LayerId && !w.AnchorRect.Contains(ui.Mouse))
                        Close(w);
                }
                foreach (var w in _windows)
                    if (w.LayerId == ui.HotLayer) { BringToFront(w); break; }
            }

            _drawOrder.Clear();
            _drawOrder.AddRange(_windows);
            foreach (var w in _drawOrder)
            {
                if (!w.IsOpen) continue;
                ui.BeginLayer(w.LayerId, w.Scheme);
                DrawWindow(ui, w);
                if (w.IsOpen && w.PendingPlacement) ResolvePlacement(w);
            }
        }

        private void ContinueDragAndResize(UiContext ui)
        {
            if (_dragging != null)
            {
                if (ui.LeftHeld)
                {
                    var p = ui.Mouse - _dragOffset;
                    if (p.x != _dragging.Rect.x || p.y != _dragging.Rect.y) _dragging.UserMoved = true;
                    _dragging.Rect.x = p.x;
                    _dragging.Rect.y = p.y;
                    Clamp(_dragging);
                }
                else _dragging = null;
            }
            if (_resizing != null)
            {
                if (ui.LeftHeld)
                {
                    var d = ui.Mouse - _resizeStartMouse;
                    _resizing.Rect.width = Mathf.Round(Mathf.Max(_resizing.MinSize.x, _resizeStartSize.x + d.x));
                    _resizing.Rect.height = Mathf.Round(Mathf.Max(_resizing.MinSize.y, _resizeStartSize.y + d.y));
                }
                else _resizing = null;
            }
        }

        private void DrawWindow(UiContext ui, UiWindow w)
        {
            var R = ui.R;
            var s = w.Scheme;
            var full = VisibleRect(w);

            // ---- Caption bar: [x] title [action] [shade] [pin] ----
            var cap = new Rect(full.x, full.y, full.width, UiWindow.CaptionHeight);
            float bx = cap.xMax;
            Rect pinBox = default, shadeBox = default, actionBox = default;
            if (w.Pinnable) { bx -= 12; pinBox = new Rect(bx, cap.y, 12, cap.height); }
            if (w.Shadeable) { bx -= 12; shadeBox = new Rect(bx, cap.y, 12, cap.height); }
            if (w.CaptionAction != null) { bx -= 16; actionBox = new Rect(bx, cap.y, 16, cap.height); }
            var closeBox = w.Closable ? new Rect(cap.x, cap.y, 12, cap.height) : new Rect(cap.x, cap.y, 0, cap.height);
            var titleBox = new Rect(closeBox.xMax, cap.y, bx - closeBox.xMax, cap.height);

            if (w.Closable)
            {
                bool down = ui.Hover(closeBox) && ui.LeftHeld;
                CaptionBox(R, closeBox, s, down);
                R.TextCentered(new Rect(closeBox.x + 1, closeBox.y, closeBox.width, closeBox.height), "×", UiTheme.TextWhite);
                ui.SetTooltip(closeBox, "Close window");
            }
            CaptionBox(R, titleBox, s, false);
            R.PushClip(new Rect(titleBox.x + 1, titleBox.y, titleBox.width - 2, titleBox.height));
            R.Text(titleBox.x + 4, titleBox.y + 1, R.Ellipsize(w.Caption, titleBox.width - 8), UiTheme.TextWhite);
            R.PopClip();
            if (w.CaptionAction != null)
            {
                CaptionBox(R, actionBox, s, ui.Hover(actionBox) && ui.LeftHeld);
                R.Icon(w.CaptionAction, new Rect(actionBox.x, actionBox.y - 1, actionBox.width, actionBox.height), new Color32(255, 255, 255, 255));
                ui.SetTooltip(actionBox, w.CaptionActionTooltip);
            }
            if (w.Shadeable)
            {
                CaptionBox(R, shadeBox, s, w.Shaded);
                R.TextCentered(new Rect(shadeBox.x + 1, shadeBox.y, shadeBox.width, shadeBox.height), w.Shaded ? "▼" : "▲", UiTheme.TextWhite);
                ui.SetTooltip(shadeBox, w.Shaded ? "Unroll window" : "Roll window up to its title bar");
            }
            if (w.Pinnable)
            {
                CaptionBox(R, pinBox, s, w.Pinned);
                R.Fill(new Rect(pinBox.x + 4, pinBox.y + 4, 4, 4), w.Pinned ? UiTheme.TextYellow : UiTheme.TextWhite);
                R.Fill(new Rect(pinBox.x + 5, pinBox.y + 8, 2, 3), w.Pinned ? UiTheme.TextYellow : UiTheme.TextWhite);
                ui.SetTooltip(pinBox, w.Pinned ? "Pinned: stays open when you press Delete. Click to unpin." : "Pin window (keep it open when closing all windows with Delete)");
            }

            // Caption interactions. Buttons fire on click; anywhere else on the caption starts a drag.
            if (w.Closable && ui.Clicked(closeBox)) { Close(w); return; }
            if (w.Shadeable && ui.Clicked(shadeBox)) w.Shaded = !w.Shaded;
            if (w.Pinnable && ui.Clicked(pinBox)) w.Pinned = !w.Pinned;
            if (w.CaptionAction != null && ui.Clicked(actionBox)) w.OnCaptionAction();
            if (ui.Clicked(titleBox))
            {
                _dragging = w;
                _dragOffset = ui.Mouse - new Vector2(w.Rect.x, w.Rect.y);
            }

            if (w.Shaded) return;

            // ---- Body ----
            var body = new Rect(full.x, cap.yMax, full.width, full.height - UiWindow.CaptionHeight);
            R.Bevel(body, s, false);
            float gripRoom = w.Resizable ? UiWindow.GripSize - UiWindow.Padding + 2 : 0f; // keep content clear of the grip
            var content = new Rect(body.x + UiWindow.Padding, body.y + UiWindow.Padding,
                                   body.width - UiWindow.Padding * 2, body.height - UiWindow.Padding * 2 - gripRoom);

            R.PushClip(new Rect(body.x + 1, body.y + 1, body.width - 2, body.height - 2));
            w.ViewHeight = content.height;
            if (w.AutoHeight)
            {
                ui.BeginArea(new Rect(content.x, content.y, content.width, 100000f));
                w.DrawContent(ui);
                float used = ui.EndArea();
                w.Rect.height = Mathf.Max(w.MinSize.y, Mathf.Ceil(UiWindow.CaptionHeight + used + UiWindow.Padding * 2 + gripRoom + 1));
            }
            else
            {
                var scroll = w.BodyScroll;
                scroll.ViewHeight = content.height;
                if (ui.Hover(body) && ui.Wheel != 0f && !ui.WheelConsumed && scroll.ContentHeight > scroll.ViewHeight)
                {
                    scroll.Offset -= ui.Wheel * ui.R.LineHeight * 2;
                    ui.WheelConsumed = true;
                }
                scroll.Clamp();
                ui.BeginArea(new Rect(content.x, content.y - Mathf.Round(scroll.Offset), content.width, 100000f));
                w.DrawContent(ui);
                scroll.ContentHeight = ui.EndArea();
                scroll.Clamp();
            }
            R.PopClip();

            // Resize grip
            if (w.Resizable)
            {
                var grip = new Rect(full.xMax - UiWindow.GripSize, full.yMax - UiWindow.GripSize, UiWindow.GripSize, UiWindow.GripSize);
                for (int i = 0; i < 3; i++)
                {
                    float o = 2 + i * 3;
                    R.Fill(new Rect(grip.xMax - o - 1, grip.yMax - 3, 2, 1), s.Dark);
                    R.Fill(new Rect(grip.xMax - 3, grip.yMax - o - 1, 1, 2), s.Dark);
                }
                ui.SetTooltip(grip, "Drag to resize");
                if (ui.Clicked(grip))
                {
                    _resizing = w;
                    _resizeStartMouse = ui.Mouse;
                    _resizeStartSize = new Vector2(w.Rect.width, w.Rect.height);
                    w.AutoHeight = false;
                }
            }

            // Black outline around the whole window.
            R.Outline(new Rect(full.x - 1, full.y - 1, full.width + 2, full.height + 2), UiTheme.Outline);
        }

        private static void CaptionBox(UiRenderer R, Rect r, UiScheme s, bool lowered)
        {
            var light = Color32.Lerp(s.Caption, new Color32(255, 255, 255, 255), 0.3f);
            var dark = Color32.Lerp(s.Caption, new Color32(0, 0, 0, 255), 0.55f);
            R.Fill(r, lowered ? dark : s.Caption);
            R.Fill(new Rect(r.x, r.y, r.width, 1), lowered ? dark : light);
            R.Fill(new Rect(r.x, r.y + 1, 1, r.height - 1), lowered ? dark : light);
            R.Fill(new Rect(r.x + 1, r.yMax - 1, r.width - 1, 1), lowered ? light : dark);
            R.Fill(new Rect(r.xMax - 1, r.y + 1, 1, r.height - 2), lowered ? light : dark);
        }
    }
}
