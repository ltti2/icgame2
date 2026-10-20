using UnityEngine;

namespace IceCoast.View.UI.Toolkit
{
    /// <summary>
    /// A window colour scheme in the OpenTTD style: every widget is a flat fill with a one-pixel bevel
    /// (light top-left, dark bottom-right; reversed when pressed).
    /// </summary>
    public sealed class UiScheme
    {
        public Color32 Light;    // bevel highlight
        public Color32 Fill;     // panel / raised button face
        public Color32 Dark;     // bevel shadow
        public Color32 Lowered;  // pressed button / inset panel face
        public Color32 Caption;  // title bar
        public Color32 Text;     // default text on Fill
        public Color32 TextOnLowered;
        public Color32 Heading;  // section headings
    }

    /// <summary>Colours used by the interface. Change these to reskin everything.</summary>
    public static class UiTheme
    {
        public static readonly Color32 Black = new Color32(0, 0, 0, 255);
        public static readonly Color32 Outline = new Color32(12, 12, 12, 255);

        // Text colours (OpenTTD names).
        public static readonly Color32 TextBlack = new Color32(0, 0, 0, 255);
        public static readonly Color32 TextWhite = new Color32(252, 252, 252, 255);
        public static readonly Color32 TextOrange = new Color32(252, 156, 36, 255);
        public static readonly Color32 TextGold = new Color32(236, 196, 88, 255);
        public static readonly Color32 TextYellow = new Color32(252, 244, 64, 255);
        public static readonly Color32 TextRed = new Color32(240, 60, 52, 255);
        public static readonly Color32 TextGreen = new Color32(96, 212, 96, 255);
        public static readonly Color32 TextLightBlue = new Color32(156, 188, 252, 255);
        public static readonly Color32 TextGrey = new Color32(112, 112, 112, 255);
        public static readonly Color32 TextDisabled = new Color32(120, 120, 120, 255);

        public static readonly Color32 TooltipFill = new Color32(252, 236, 180, 255);
        public static readonly Color32 Highlight = new Color32(0, 0, 0, 255);

        /// <summary>Main toolbar, status bar, menus and settings.</summary>
        public static readonly UiScheme Grey = new UiScheme
        {
            Light = new Color32(216, 216, 216, 255),
            Fill = new Color32(168, 168, 168, 255),
            Dark = new Color32(100, 100, 100, 255),
            Lowered = new Color32(136, 136, 136, 255),
            Caption = new Color32(84, 84, 84, 255),
            Text = TextBlack,
            TextOnLowered = TextBlack,
            Heading = new Color32(32, 40, 120, 255),
        };

        /// <summary>Information windows (tiles, lifts, runs, the resort), like OpenTTD's town and station windows.</summary>
        public static readonly UiScheme Brown = new UiScheme
        {
            Light = new Color32(216, 172, 116, 255),
            Fill = new Color32(176, 128, 76, 255),
            Dark = new Color32(108, 72, 36, 255),
            Lowered = new Color32(144, 100, 56, 255),
            Caption = new Color32(96, 60, 28, 255),
            Text = TextBlack,
            TextOnLowered = TextBlack,
            Heading = new Color32(252, 236, 184, 255),
        };

        /// <summary>Construction tools (lifts, runs, terraform, trees), like OpenTTD's build toolbars.</summary>
        public static readonly UiScheme DarkGreen = new UiScheme
        {
            Light = new Color32(120, 176, 108, 255),
            Fill = new Color32(76, 128, 64, 255),
            Dark = new Color32(32, 72, 28, 255),
            Lowered = new Color32(52, 96, 44, 255),
            Caption = new Color32(28, 64, 24, 255),
            Text = TextWhite,
            TextOnLowered = TextYellow,
            Heading = TextYellow,
        };

        /// <summary>Finances and messages.</summary>
        public static readonly UiScheme Blue = new UiScheme
        {
            Light = new Color32(140, 164, 220, 255),
            Fill = new Color32(88, 112, 180, 255),
            Dark = new Color32(36, 52, 108, 255),
            Lowered = new Color32(64, 84, 148, 255),
            Caption = new Color32(32, 44, 96, 255),
            Text = TextWhite,
            TextOnLowered = TextYellow,
            Heading = TextYellow,
        };

        /// <summary>Warnings and confirmations.</summary>
        public static readonly UiScheme Red = new UiScheme
        {
            Light = new Color32(232, 120, 104, 255),
            Fill = new Color32(184, 64, 48, 255),
            Dark = new Color32(104, 20, 12, 255),
            Lowered = new Color32(148, 44, 32, 255),
            Caption = new Color32(96, 16, 8, 255),
            Text = TextWhite,
            TextOnLowered = TextYellow,
            Heading = TextYellow,
        };

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);
    }
}
