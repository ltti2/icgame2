using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IceCoast.View.Input
{
    public enum GameKey
    {
        PanUp, PanDown, PanLeft, PanRight,
        Pause, SpeedDown, SpeedUp,
        Cancel, Confirm, Delete,
        Tool1, Tool2, Tool3, Tool4, Tool5, Tool6, Tool7, Tool8,
        CycleOverlay, BrushSmaller, BrushBigger,
        QuickSave, QuickLoad, Modifier,
        ToggleContours,
        Tool9,
    }

    /// <summary>
    /// Thin wrapper so gameplay code does not care whether the project uses the new Input System
    /// (Unity 6 default) or the legacy Input Manager. Key bindings live in one table here,
    /// so rebinding later only touches this file.
    /// Mouse positions are in screen pixels with (0,0) bottom-left, like Input.mousePosition.
    /// </summary>
    public static class GameInput
    {
        /// <summary>
        /// Set by the HUD while a text field has focus, so typing a run name doesn't switch tools or pan.
        /// </summary>
        public static bool KeyboardSuppressed;

#if ENABLE_INPUT_SYSTEM
        // Indexed by (int)GameKey.
        private static readonly Key[][] Bindings =
        {
            new[] { Key.W, Key.UpArrow },          // PanUp
            new[] { Key.S, Key.DownArrow },        // PanDown
            new[] { Key.A, Key.LeftArrow },        // PanLeft
            new[] { Key.D, Key.RightArrow },       // PanRight
            new[] { Key.Space },                   // Pause
            new[] { Key.Comma, Key.Minus },        // SpeedDown
            new[] { Key.Period, Key.Equals },      // SpeedUp
            new[] { Key.Escape },                  // Cancel
            new[] { Key.Enter, Key.NumpadEnter },  // Confirm
            new[] { Key.Delete },                  // Delete (close all unpinned windows)
            new[] { Key.Digit1 }, new[] { Key.Digit2 }, new[] { Key.Digit3 },
            new[] { Key.Digit4 }, new[] { Key.Digit5 }, new[] { Key.Digit6 },
            new[] { Key.Digit7 }, new[] { Key.Digit8 },
            new[] { Key.O },                       // CycleOverlay
            new[] { Key.LeftBracket },             // BrushSmaller
            new[] { Key.RightBracket },            // BrushBigger
            new[] { Key.F5 },                      // QuickSave
            new[] { Key.F9 },                      // QuickLoad
            new[] { Key.LeftShift, Key.RightShift }, // Modifier
            new[] { Key.C },                       // ToggleContours
            new[] { Key.Digit9 },                  // Tool9
        };

        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        /// <summary>-1, 0 or +1 per frame. Raw values differ wildly between platforms, so we only use the sign.</summary>
        public static float ScrollDelta
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                return y > 0f ? 1f : y < 0f ? -1f : 0f;
            }
        }

        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool LeftUp => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool MiddleHeld => Mouse.current != null && Mouse.current.middleButton.isPressed;

        public static bool Held(GameKey key)
        {
            var kb = Keyboard.current;
            if (kb == null || KeyboardSuppressed) return false;
            foreach (var k in Bindings[(int)key]) if (kb[k].isPressed) return true;
            return false;
        }

        public static bool Pressed(GameKey key)
        {
            var kb = Keyboard.current;
            if (kb == null || KeyboardSuppressed) return false;
            foreach (var k in Bindings[(int)key]) if (kb[k].wasPressedThisFrame) return true;
            return false;
        }
#else
        private static readonly KeyCode[][] Bindings =
        {
            new[] { KeyCode.W, KeyCode.UpArrow },
            new[] { KeyCode.S, KeyCode.DownArrow },
            new[] { KeyCode.A, KeyCode.LeftArrow },
            new[] { KeyCode.D, KeyCode.RightArrow },
            new[] { KeyCode.Space },
            new[] { KeyCode.Comma, KeyCode.Minus },
            new[] { KeyCode.Period, KeyCode.Equals },
            new[] { KeyCode.Escape },
            new[] { KeyCode.Return, KeyCode.KeypadEnter },
            new[] { KeyCode.Delete },
            new[] { KeyCode.Alpha1 }, new[] { KeyCode.Alpha2 }, new[] { KeyCode.Alpha3 },
            new[] { KeyCode.Alpha4 }, new[] { KeyCode.Alpha5 }, new[] { KeyCode.Alpha6 },
            new[] { KeyCode.Alpha7 }, new[] { KeyCode.Alpha8 },
            new[] { KeyCode.O },
            new[] { KeyCode.LeftBracket },
            new[] { KeyCode.RightBracket },
            new[] { KeyCode.F5 },
            new[] { KeyCode.F9 },
            new[] { KeyCode.LeftShift, KeyCode.RightShift },
            new[] { KeyCode.C },
            new[] { KeyCode.Alpha9 },
        };

        public static Vector2 MousePosition => UnityEngine.Input.mousePosition;

        public static float ScrollDelta
        {
            get
            {
                float y = UnityEngine.Input.mouseScrollDelta.y;
                return y > 0f ? 1f : y < 0f ? -1f : 0f;
            }
        }

        public static bool LeftDown => UnityEngine.Input.GetMouseButtonDown(0);
        public static bool LeftHeld => UnityEngine.Input.GetMouseButton(0);
        public static bool LeftUp => UnityEngine.Input.GetMouseButtonUp(0);
        public static bool RightDown => UnityEngine.Input.GetMouseButtonDown(1);
        public static bool MiddleHeld => UnityEngine.Input.GetMouseButton(2);

        public static bool Held(GameKey key)
        {
            if (KeyboardSuppressed) return false;
            foreach (var k in Bindings[(int)key]) if (UnityEngine.Input.GetKey(k)) return true;
            return false;
        }

        public static bool Pressed(GameKey key)
        {
            if (KeyboardSuppressed) return false;
            foreach (var k in Bindings[(int)key]) if (UnityEngine.Input.GetKeyDown(k)) return true;
            return false;
        }
#endif
    }
}
