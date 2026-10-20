using System;
using System.Collections.Generic;
using System.Text;
using IceCoast.Simulation.Config;
using IceCoast.Simulation.Guests;
using IceCoast.View.UI.Toolkit;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>One sprite in a sheet: where its cell is, and the code-drawn default.</summary>
    public sealed class ArtSprite
    {
        public string Name;
        public int Col, Row;
        /// <summary>The procedural default (drawn by SpriteArt, LiftArt, PropArt, BuildingArt or UiArt).</summary>
        public PixelCanvas Canvas;
        /// <summary>Top-left of the sprite's rectangle in the sheet (rows from the top), set by <see cref="ArtSheet.Layout"/>.</summary>
        public int X, Y;
    }

    /// <summary>
    /// A sprite sheet: a grid of equal cells, one sprite per cell. Within a sheet every sprite's pivot (feet,
    /// cable grip, tile centre) sits at the same spot in its cell, so animation frames and variants line up
    /// when you flip through them in Aseprite.
    /// </summary>
    public sealed class ArtSheet
    {
        /// <summary>Transparent border inside each cell, so there is room to draw a little bigger.</summary>
        public const int CellPadding = 2;

        public string Name;
        public string Note;
        /// <summary>Glyphs: only grow to the right and down, so the top-left (and the baseline) stays put.</summary>
        public bool GrowRightDownOnly;
        public readonly List<ArtSprite> Sprites = new List<ArtSprite>();

        public int Columns, Rows, CellWidth, CellHeight;
        public int Width => Columns * CellWidth;
        public int Height => Rows * CellHeight;

        public ArtSheet(string name, string note)
        {
            Name = name;
            Note = note;
        }

        public void Add(string name, int col, int row, PixelCanvas canvas)
        {
            if (canvas == null) return; // nothing to draw for this slot (e.g. a carpet has no carrier)
            Sprites.Add(new ArtSprite { Name = name, Col = col, Row = row, Canvas = canvas });
        }

        /// <summary>Sizes the cells to fit every sprite with its pivot at a common point, and places each one.</summary>
        public void Layout()
        {
            int left = 0, right = 0, up = 0, down = 0;
            Columns = Rows = 0;
            foreach (var s in Sprites)
            {
                var c = s.Canvas;
                left = Math.Max(left, c.PivotX);
                right = Math.Max(right, c.Width - c.PivotX);
                up = Math.Max(up, c.PivotY);
                down = Math.Max(down, c.Height - c.PivotY);
                Columns = Math.Max(Columns, s.Col + 1);
                Rows = Math.Max(Rows, s.Row + 1);
            }
            CellWidth = left + right + 2 * CellPadding;
            CellHeight = up + down + 2 * CellPadding;
            foreach (var s in Sprites)
            {
                s.X = s.Col * CellWidth + CellPadding + left - s.Canvas.PivotX;
                s.Y = s.Row * CellHeight + CellPadding + up - s.Canvas.PivotY;
            }
        }

        public ArtSprite Find(string name)
        {
            foreach (var s in Sprites) if (s.Name == name) return s;
            return null;
        }
    }

    /// <summary>Where a sprite sits in a sheet PNG. Written next to it as JSON; read with JsonUtility.</summary>
    [Serializable]
    public class SheetSpriteRecord
    {
        public string name;
        public int col, row;
        /// <summary>The sprite's rectangle in the sheet, in pixels from the top-left (as Aseprite shows them).</summary>
        public int x, y, w, h;
        /// <summary>The pivot inside that rectangle (feet, cable grip, tile centre on the ground).</summary>
        public int pivotX, pivotY;
    }

    [Serializable]
    public class SheetManifest
    {
        public string sheet;
        public string note;
        public int cellWidth, cellHeight, columns, rows;
        public SheetSpriteRecord[] sprites;
    }

    /// <summary>
    /// Every piece of pixel art in the game, organised into sprite sheets that can be exported to PNG, edited
    /// (in Aseprite or anything else) and loaded back. Engine-free apart from the colour types, so the same code
    /// runs in the game, in the editor's export menu and in the headless exporter (tools/SimHarness/ArtExport).
    ///
    /// Files live in StreamingAssets/Art (see <see cref="ArtLibrary"/>):
    ///  - one PNG per sheet plus a JSON with each sprite's rectangle and pivot,
    ///  - one 128x128 PNG per ground texture in Art/ground (these tile; 128 grey means "no change").
    /// A sprite without a PNG (or missing from its sheet's JSON) falls back to the code-drawn default.
    /// </summary>
    public static class ArtSheets
    {
        public const int ManifestVersion = 1;

        // ---------- Names (shared by the sheets and the atlas lookups) ----------

        public static string TreeName(TreeKind kind, int variant, bool snowy) => $"tree_{Lower(kind)}_{variant}_{(snowy ? "snowy" : "bare")}";
        public static string ClusterName(TreeKind kind, int variant, bool snowy) => $"cluster_{Lower(kind)}_{variant}_{(snowy ? "snowy" : "bare")}";
        public static string SkierName(int skill, int look, SkierPose pose) => $"skier_{Lower((SkillLevel)skill)}_{look}_{Lower(pose)}";
        public static string CarrierName(LiftType type, bool occupied) => $"carrier_{Lower(type)}_{(occupied ? "full" : "empty")}";
        public const string TBarName = "tbar";
        public const string RopeHandleName = "rope_handle";
        public const string SheavesName = "sheaves";
        public const string ColumnName = "column";
        public static string TowerName(LiftType type) => $"tower_{Lower(type)}";
        public static string BullwheelName(LiftType type) => $"bullwheel_{Lower(type)}";
        public static string TerminalName(LiftType type) => $"terminal_{Lower(type)}";
        public static string HutName(LiftType type, bool bottom) => $"hut_{Lower(type)}_{(bottom ? "bottom" : "top")}";
        public static string CarName(int color, bool noseTowardCamera) => $"car_{color}_{(noseTowardCamera ? "nose" : "tail")}";
        public static string PoleName(LightSize size) => $"pole_{Lower(size)}";
        public static string LampName(LightSize size) => $"lamp_{Lower(size)}";
        public static string BuildingName(BuildingType type) => $"building_{Lower(type)}";
        public static string IconName(string icon) => $"icon_{icon}";
        public static string GlyphName(char c) => $"glyph_{(int)c:x2}";
        public static string GroundName(SurfaceDetail d) => Lower(d);

        private static string Lower<T>(T value) => value.ToString().ToLowerInvariant();

        // ---------- Sheets ----------

        /// <summary>All the sheets, with their code-drawn defaults, laid out.</summary>
        public static List<ArtSheet> All()
        {
            var sheets = new List<ArtSheet>
            {
                Trees(cluster: false),
                Trees(cluster: true),
                Skiers(),
                Carriers(),
                LiftParts(),
                LiftStructures(),
                Cars(),
                TrailLights(),
                Buildings(),
                UiIcons(),
                UiFont(),
            };
            foreach (var s in sheets) s.Layout();
            return sheets;
        }

        private static ArtSheet Trees(bool cluster)
        {
            var sheet = new ArtSheet(cluster ? "tree_clusters" : "trees",
                (cluster ? "Stands of three trees." : "Single trees.") +
                " Rows: spruce, fir, hardwood, each bare then snow-laden. Columns: two variants. Pivot: the base of the trunk.");
            for (int k = 0; k < (int)TreeKind.Count; k++)
                for (int snow = 0; snow < 2; snow++)
                    for (int v = 0; v < WorldAtlasConstants.TreeVariants; v++)
                    {
                        var kind = (TreeKind)k;
                        bool snowy = snow == 1;
                        sheet.Add(cluster ? ClusterName(kind, v, snowy) : TreeName(kind, v, snowy), v, k * 2 + snow,
                                  cluster ? SpriteArt.Cluster(kind, v, snowy) : SpriteArt.Tree(kind, v, snowy));
                    }
            return sheet;
        }

        private static ArtSheet Skiers()
        {
            var sheet = new ArtSheet("skiers",
                "Rows: skill (first-timer, beginner, intermediate, advanced, expert, pro) x 4 looks. Columns: poses " +
                "(ski side, ski front, back, walk 1, walk 2, seated, fallen, back idle). Side and walk poses face right; " +
                "the game mirrors them. Pivot: between the feet (seated: the lap, on the chair).");
            for (int skill = 0; skill < 6; skill++)
                for (int look = 0; look < WorldAtlasConstants.SkierLooks; look++)
                    for (int pose = 0; pose < (int)SkierPose.Count; pose++)
                        sheet.Add(SkierName(skill, look, (SkierPose)pose), pose, skill * WorldAtlasConstants.SkierLooks + look,
                                  SpriteArt.Skier(skill, look, (SkierPose)pose));
            return sheet;
        }

        private static ArtSheet Carriers()
        {
            var sheet = new ArtSheet("lift_carriers",
                "Chairs and cabins on the cable. Rows: lift types that have one. Columns: empty, with riders (gondola " +
                "faces in the windows; chair riders are drawn separately). Pivot: the grip on the cable.");
            int row = 0;
            foreach (LiftType type in Enum.GetValues(typeof(LiftType)))
            {
                var empty = SpriteArt.Carrier(type, false);
                if (empty == null) continue;
                sheet.Add(CarrierName(type, false), 0, row, empty);
                sheet.Add(CarrierName(type, true), 1, row, SpriteArt.Carrier(type, true));
                row++;
            }
            return sheet;
        }

        private static ArtSheet LiftParts()
        {
            var sheet = new ArtSheet("lift_parts",
                "T-bar crosspiece, rope-tow handle, sheave train and terminal column. The column is stretched to height in game.");
            sheet.Add(TBarName, 0, 0, SpriteArt.TBarCross());
            sheet.Add(RopeHandleName, 1, 0, SpriteArt.RopeHandle());
            sheet.Add(SheavesName, 2, 0, LiftArt.Sheaves());
            sheet.Add(ColumnName, 3, 0, LiftArt.Column());
            return sheet;
        }

        private static ArtSheet LiftStructures()
        {
            var sheet = new ArtSheet("lift_structures",
                "Rows: rope tow, magic carpet, T-bar, fixed-grip chair, detachable chair, bubble chair, gondola. Columns: " +
                "line tower (stretched to the cable height in game), bullwheel, terminal, top hut, bottom hut. " +
                "Pivot: the tile centre on the ground (towers, huts) or at cable height (bullwheels, terminals).");
            foreach (LiftType type in Enum.GetValues(typeof(LiftType)))
            {
                int row = (int)type;
                sheet.Add(TowerName(type), 0, row, LiftArt.Tower(type));
                sheet.Add(BullwheelName(type), 1, row, LiftArt.Bullwheel(type));
                if (LiftArt.HasTerminal(type)) sheet.Add(TerminalName(type), 2, row, LiftArt.Terminal(type));
                sheet.Add(HutName(type, false), 3, row, LiftArt.Hut(type, false));
                sheet.Add(HutName(type, true), 4, row, LiftArt.Hut(type, true));
            }
            return sheet;
        }

        private static ArtSheet Cars()
        {
            var sheet = new ArtSheet("cars",
                "Rows: paint jobs. Columns: boot toward the camera, bonnet toward the camera. Cars point along grid x; " +
                "the game mirrors them for grid y. Pivot: the middle of the car on the ground.");
            for (int col = 0; col < PropArt.CarColorCount; col++)
                for (int nose = 0; nose < 2; nose++)
                    sheet.Add(CarName(col, nose == 1), nose, col, PropArt.Car(col, nose == 1));
            return sheet;
        }

        private static ArtSheet TrailLights()
        {
            var sheet = new ArtSheet("trail_lights",
                "Rows: small, medium, large. Columns: pole, lamp heads (drawn on top, and glowing at night). Pivot: the foot of the pole.");
            foreach (var def in SimConfig.DefaultLights())
            {
                sheet.Add(PoleName(def.Size), 0, (int)def.Size, PropArt.Pole(def));
                sheet.Add(LampName(def.Size), 1, (int)def.Size, PropArt.LampHeads(def));
            }
            return sheet;
        }

        private static ArtSheet Buildings()
        {
            var sheet = new ArtSheet("buildings",
                "One building per row (Waffle Cabin). Pivot: the centre of its tile on the ground; the tile is 64x32.");
            foreach (BuildingType type in Enum.GetValues(typeof(BuildingType)))
                sheet.Add(BuildingName(type), 0, (int)type, BuildingArt.For(type));
            return sheet;
        }

        public const int IconColumns = 12;

        private static ArtSheet UiIcons()
        {
            var sheet = new ArtSheet("ui_icons", "16x16 interface icons, in the order listed below.");
            for (int i = 0; i < UiArt.IconCount; i++)
                sheet.Add(IconName(UiArt.IconName(i)), i % IconColumns, i / IconColumns, UiArt.Icon(i));
            return sheet;
        }

        public const int GlyphColumns = 16;

        private static ArtSheet UiFont()
        {
            var sheet = new ArtSheet("ui_font",
                "The interface font. Any opaque pixel is ink (drawn in the text colour). Row 7 of each glyph is the " +
                "baseline; rows 8 and 9 are descenders. A glyph's width is its JSON w (it grows if you draw past it); " +
                "letters are spaced one pixel apart.") { GrowRightDownOnly = true };
            for (int i = 0; i < UiArt.GlyphCount; i++)
                sheet.Add(GlyphName(UiArt.GlyphChar(i)), i % GlyphColumns, i / GlyphColumns, UiArt.Glyph(i));
            return sheet;
        }

        // ---------- Export ----------

        /// <summary>The sheet's pixels (rows from the top), with every code-drawn sprite in its cell.</summary>
        public static Color32[] Compose(ArtSheet sheet)
        {
            var px = new Color32[sheet.Width * sheet.Height];
            foreach (var s in sheet.Sprites)
                for (int y = 0; y < s.Canvas.Height; y++)
                    for (int x = 0; x < s.Canvas.Width; x++)
                    {
                        var c = s.Canvas.Get(x, y);
                        if (c.a == 0) continue;
                        px[(s.Y + y) * sheet.Width + s.X + x] = c;
                    }
            return px;
        }

        public static string ManifestJson(ArtSheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"sheet\": \"{sheet.Name}\",\n");
            sb.Append($"  \"note\": \"{Escape(sheet.Note)} Each sprite's rectangle (x, y, w, h, from the top-left of the PNG) and pivot " +
                      "(inside the rectangle) are listed below. Draw inside the sprite's cell; drawing past its rectangle grows it. " +
                      "To shrink a sprite or move its pivot, edit the numbers here.\",\n");
            sb.Append($"  \"cellWidth\": {sheet.CellWidth},\n  \"cellHeight\": {sheet.CellHeight},\n");
            sb.Append($"  \"columns\": {sheet.Columns},\n  \"rows\": {sheet.Rows},\n");
            sb.Append("  \"sprites\": [\n");
            for (int i = 0; i < sheet.Sprites.Count; i++)
            {
                var s = sheet.Sprites[i];
                sb.Append($"    {{ \"name\": \"{s.Name}\", \"col\": {s.Col}, \"row\": {s.Row}, \"x\": {s.X}, \"y\": {s.Y}, " +
                          $"\"w\": {s.Canvas.Width}, \"h\": {s.Canvas.Height}, \"pivotX\": {s.Canvas.PivotX}, \"pivotY\": {s.Canvas.PivotY} }}");
                sb.Append(i + 1 < sheet.Sprites.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>The manifest describing the code layout (used when a PNG has no JSON beside it).</summary>
        public static SheetManifest DefaultManifest(ArtSheet sheet)
        {
            var m = new SheetManifest
            {
                sheet = sheet.Name, note = sheet.Note,
                cellWidth = sheet.CellWidth, cellHeight = sheet.CellHeight, columns = sheet.Columns, rows = sheet.Rows,
                sprites = new SheetSpriteRecord[sheet.Sprites.Count],
            };
            for (int i = 0; i < sheet.Sprites.Count; i++)
            {
                var s = sheet.Sprites[i];
                m.sprites[i] = new SheetSpriteRecord
                {
                    name = s.Name, col = s.Col, row = s.Row, x = s.X, y = s.Y,
                    w = s.Canvas.Width, h = s.Canvas.Height, pivotX = s.Canvas.PivotX, pivotY = s.Canvas.PivotY,
                };
            }
            return m;
        }

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ---------- Import ----------

        /// <summary>
        /// Cuts every sprite of <paramref name="sheet"/> out of an edited sheet image (rows from the top). A sprite's
        /// rectangle is the one in the manifest, grown to take in anything drawn elsewhere in its cell; its pivot
        /// stays on the same pixel. Sprites the manifest doesn't list keep their code-drawn default.
        /// </summary>
        public static Dictionary<string, PixelCanvas> Extract(ArtSheet sheet, SheetManifest manifest, Color32[] px, int width, int height, List<string> warnings)
        {
            var result = new Dictionary<string, PixelCanvas>();
            var records = new Dictionary<string, SheetSpriteRecord>();
            if (manifest?.sprites != null)
                foreach (var r in manifest.sprites)
                    if (r != null && !string.IsNullOrEmpty(r.name)) records[r.name] = r;
            int cellW = manifest != null && manifest.cellWidth > 0 ? manifest.cellWidth : sheet.CellWidth;
            int cellH = manifest != null && manifest.cellHeight > 0 ? manifest.cellHeight : sheet.CellHeight;

            foreach (var s in sheet.Sprites)
            {
                if (!records.TryGetValue(s.Name, out var r))
                {
                    warnings?.Add($"{sheet.Name}: '{s.Name}' is not in the JSON, using the built-in art.");
                    result[s.Name] = s.Canvas;
                    continue;
                }

                // Its cell, clipped to the image.
                int cx0 = Math.Max(0, r.col * cellW), cy0 = Math.Max(0, r.row * cellH);
                int cx1 = Math.Min(width, cx0 + cellW), cy1 = Math.Min(height, cy0 + cellH);

                // Rectangle from the JSON, grown to everything opaque in the cell.
                int x0 = r.x, y0 = r.y, x1 = r.x + Math.Max(0, r.w), y1 = r.y + Math.Max(0, r.h);
                for (int y = cy0; y < cy1; y++)
                    for (int x = cx0; x < cx1; x++)
                    {
                        if (px[y * width + x].a < 128) continue;
                        if (!sheet.GrowRightDownOnly) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); }
                        x1 = Math.Max(x1, x + 1);
                        y1 = Math.Max(y1, y + 1);
                    }

                int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
                var c = new PixelCanvas(w, h) { PivotX = r.x + r.pivotX - x0, PivotY = r.y + r.pivotY - y0 };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int sx = x0 + x, sy = y0 + y;
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                        var p = px[sy * width + sx];
                        if (p.a >= 128) c.Set(x, y, new Color32(p.r, p.g, p.b, 255));
                    }
                result[s.Name] = c;
            }
            return result;
        }

        // ---------- Ground textures ----------

        /// <summary>
        /// A ground texture as stored on disk: the atlas value (the shader multiplies by 2), so 128 grey means "leave
        /// the ground colour as it is", lighter brightens and darker darkens. Linear values, no gamma.
        /// </summary>
        public static Color32[] GroundPixels(SurfaceDetail detail)
        {
            int n = WorldAtlasConstants.TerrainCellSize;
            var m = TerrainDetailArt.Draw(detail, n);
            var px = new Color32[n * n];
            // Texture rows count from the top in the PNG; the cell's texel (x, y) is grid (x, y), y down the image.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var c = m[y * n + x];
                    px[y * n + x] = new Color32(ToByte(c.r * 0.5f), ToByte(c.g * 0.5f), ToByte(c.b * 0.5f), 255);
                }
            return px;
        }

        private static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
    }

    /// <summary>Atlas constants the art code needs, kept here so it compiles without the Unity texture code.</summary>
    public static class WorldAtlasConstants
    {
        public const int TerrainCellSize = 128;
        public const int TilesPerCell = 4;
        public const int TexelsPerTile = TerrainCellSize / TilesPerCell;
        public const int TreeVariants = 2;
        public const int SkierLooks = 4;
    }
}
