using System.Collections.Generic;
using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>A sprite in the world atlas: its UV rectangle, size and pivot in texels.</summary>
    public struct WorldSprite
    {
        /// <summary>Bottom-left and top-right UVs.</summary>
        public Vector2 Uv0, Uv1;
        public int Width, Height;
        /// <summary>Pivot from the sprite's bottom-left corner, in texels (usually the feet or the hanger grip).</summary>
        public float PivotX, PivotY;
        public bool IsValid => Width > 0;
    }

    /// <summary>Ground surface textures, multiplied over the tile's colour.</summary>
    public enum SurfaceDetail
    {
        None = -1,
        Powder,
        Packed,
        /// <summary>Corduroy lines along grid x.</summary>
        GroomedX,
        /// <summary>Corduroy lines along grid y.</summary>
        GroomedY,
        /// <summary>Corduroy along +x+y, straight down the screen.</summary>
        GroomedDown,
        /// <summary>Corduroy along +x-y, straight across the screen.</summary>
        GroomedAcross,
        Slush,
        Ice,
        Crud,
        SnowyRock,
        Grass,
        Dirt,
        Rock,
        Scree,
        Water,
        /// <summary>Trampled snow with boot prints: walking areas.</summary>
        Walkway,
        /// <summary>Asphalt road with a dashed centre line along grid x.</summary>
        RoadX,
        /// <summary>Asphalt road with a dashed centre line along grid y.</summary>
        RoadY,
        /// <summary>Parking stalls: asphalt with painted dividers every half tile along grid x.</summary>
        ParkingStalls,
        /// <summary>The parking lot's driving aisle: plain asphalt.</summary>
        ParkingAisle,
        Count,
    }

    public enum TreeKind { Spruce, Fir, Hardwood, Count }

    public enum SkierPose
    {
        /// <summary>Skiing across the hill, facing screen right (mirror for left).</summary>
        SkiSide,
        /// <summary>Skiing straight at the camera.</summary>
        SkiFront,
        /// <summary>Back to the camera: queueing, or towed up a surface lift.</summary>
        Back,
        /// <summary>Walking with skis on the shoulder, two frames, facing right.</summary>
        Walk1,
        Walk2,
        /// <summary>Sitting on a chair, facing the camera.</summary>
        Seated,
        Fallen,
        /// <summary>Second idle frame while standing in line (a shuffle).</summary>
        BackIdle,
        Count,
    }

    /// <summary>
    /// The one texture behind every world object: ground surface textures plus pixel-art sprites for trees,
    /// skiers and lift carriers, all drawn procedurally at startup (no image files to import).
    ///
    /// Shading convention (see IceCoastUnlit.shader): final colour = vertex colour x texel x 2, so a texel of
    /// 0.5 leaves the vertex colour unchanged. Ground textures are subtle multipliers around 0.5 that sit on
    /// top of the snow/ground colour; sprites store their real colour at half strength and are drawn with a
    /// white vertex colour (tinted only to dim unowned land or add haze). Texels with alpha below 0.5 are cut out.
    ///
    /// Layout (texture rows count up from the bottom): a neutral block at (0,0) so meshes without UVs draw
    /// unchanged, sprites packed on shelves above it, and ground textures in a strip of three rows along the
    /// top, each a 128x128 cell that tiles every 4x4 map tiles (32 texels per tile: a texel is 2x1 screen
    /// pixels at zoom 1). Sprites currently fill about a third of the space below the strip.
    /// </summary>
    public static class WorldAtlas
    {
        public const int Size = 1024;
        public const int TerrainCellSize = 128;
        public const int TilesPerCell = 4;
        public const int TexelsPerTile = TerrainCellSize / TilesPerCell;
        private const int TerrainRows = 3;
        private const int TerrainStripY = Size - TerrainRows * TerrainCellSize;
        private const int NeutralBlock = 8;

        /// <summary>World units per sprite texel: sprites are pixel-exact at zoom 1.</summary>
        public const float UnitsPerTexel = 1f / 32f;

        public const int SkierLooks = 4;
        public const int TreeVariants = 2;

        public static Texture2D Texture { get; private set; }
        public static Vector2 NeutralUv { get; private set; } = new Vector2(2f / Size, 2f / Size);

        private static WorldSprite[,,] _trees;   // kind, variant, snowy
        private static WorldSprite[,,] _clusters; // kind, variant, snowy: three trees in one sprite
        private static WorldSprite[,,] _skiers;  // skill, look, pose
        private static WorldSprite[,] _carriers; // lift type, occupied
        private static WorldSprite _tBar, _ropeHandle;
        private static WorldSprite[] _towers, _bullwheels, _terminals;
        private static WorldSprite[,] _huts; // lift type, bottom
        private static WorldSprite _sheaves, _column;
        private static WorldSprite[,] _cars; // colour, nose toward camera
        private static WorldSprite[] _poles, _lampHeads; // light size

        public static WorldSprite Tree(TreeKind kind, int variant, bool snowy) => _trees[(int)kind, variant % TreeVariants, snowy ? 1 : 0];
        public static WorldSprite TreeCluster(TreeKind kind, int variant, bool snowy) => _clusters[(int)kind, variant % TreeVariants, snowy ? 1 : 0];
        public static WorldSprite Tower(LiftType type) => _towers[(int)type];
        public static WorldSprite Bullwheel(LiftType type) => _bullwheels[(int)type];
        public static WorldSprite Terminal(LiftType type) => _terminals[(int)type];
        public static WorldSprite Hut(LiftType type, bool bottom) => _huts[(int)type, bottom ? 1 : 0];
        public static WorldSprite Sheaves => _sheaves;
        public static WorldSprite Column => _column;
        public static WorldSprite Skier(int skill, int look, SkierPose pose) => _skiers[skill, look % SkierLooks, (int)pose];
        public static WorldSprite Carrier(LiftType type, bool occupied) => _carriers[(int)type, occupied ? 1 : 0];
        public static WorldSprite TBar => _tBar;
        /// <summary>A car pointing along grid x; mirror it for grid y.</summary>
        public static WorldSprite Car(int color, bool noseTowardCamera) => _cars[color % PropArt.CarColorCount, noseTowardCamera ? 1 : 0];
        public static WorldSprite LightPole(LightSize size) => _poles[(int)size];
        public static WorldSprite LampHead(LightSize size) => _lampHeads[(int)size];
        public static WorldSprite RopeHandle => _ropeHandle;

        /// <summary>
        /// UVs of a tile's four corners (grid order: (x,y), (x+1,y), (x,y+1), (x+1,y+1)) for a ground texture.
        /// Neighbouring tiles continue the same texture, which repeats every 4x4 tiles.
        /// </summary>
        public static void TileUvs(SurfaceDetail detail, int x, int y, out Vector2 uv00, out Vector2 uv10, out Vector2 uv01, out Vector2 uv11)
        {
            if (detail == SurfaceDetail.None || Texture == null)
            {
                uv00 = uv10 = uv01 = uv11 = NeutralUv;
                return;
            }
            int cell = (int)detail;
            float cx = (cell % 8) * TerrainCellSize, cy = TerrainStripY + (cell / 8) * TerrainCellSize;
            int tx = ((x % TilesPerCell) + TilesPerCell) % TilesPerCell, ty = ((y % TilesPerCell) + TilesPerCell) % TilesPerCell;
            // Inset the outer edges of the cell by a hair so point sampling never reads the neighbouring cell.
            const float inset = 0.02f;
            float u0 = cx + tx * TexelsPerTile + (tx == 0 ? inset : 0f);
            float u1 = cx + (tx + 1) * TexelsPerTile - (tx == TilesPerCell - 1 ? inset : 0f);
            float v0 = cy + ty * TexelsPerTile + (ty == 0 ? inset : 0f);
            float v1 = cy + (ty + 1) * TexelsPerTile - (ty == TilesPerCell - 1 ? inset : 0f);
            const float s = 1f / Size;
            uv00 = new Vector2(u0 * s, v0 * s);
            uv10 = new Vector2(u1 * s, v0 * s);
            uv01 = new Vector2(u0 * s, v1 * s);
            uv11 = new Vector2(u1 * s, v1 * s);
        }

        // =====================================================================
        // Building
        // =====================================================================

        private static Color[] _pixels;
        private static int _shelfX, _shelfY, _shelfH;

        /// <summary>Draws the whole atlas. Called once by RenderMaterials.</summary>
        public static Texture2D Build()
        {
            if (Texture != null) return Texture;
            _pixels = new Color[Size * Size]; // transparent black

            // Neutral block at the origin (UV 0,0 = untextured).
            var neutral = new Color(0.5f, 0.5f, 0.5f, 1f);
            for (int y = 0; y < NeutralBlock; y++)
                for (int x = 0; x < NeutralBlock; x++)
                    _pixels[y * Size + x] = neutral;
            NeutralUv = new Vector2((NeutralBlock * 0.5f) / Size, (NeutralBlock * 0.5f) / Size);

            // Ground textures.
            for (int i = 0; i < (int)SurfaceDetail.Count; i++)
            {
                int cx = (i % 8) * TerrainCellSize, cy = TerrainStripY + (i / 8) * TerrainCellSize;
                var m = TerrainDetailArt.Draw((SurfaceDetail)i, TerrainCellSize);
                for (int y = 0; y < TerrainCellSize; y++)
                    for (int x = 0; x < TerrainCellSize; x++)
                    {
                        var c = m[y * TerrainCellSize + x];
                        _pixels[(cy + y) * Size + cx + x] = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f);
                    }
            }

            // Sprites, packed on shelves from just right of the neutral block.
            _shelfX = NeutralBlock + 4;
            _shelfY = 0;
            _shelfH = NeutralBlock;

            _trees = new WorldSprite[(int)TreeKind.Count, TreeVariants, 2];
            for (int k = 0; k < (int)TreeKind.Count; k++)
                for (int v = 0; v < TreeVariants; v++)
                    for (int snow = 0; snow < 2; snow++)
                        _trees[k, v, snow] = AddTreeCell(SpriteArt.Tree((TreeKind)k, v, snow == 1));
            _clusters = new WorldSprite[(int)TreeKind.Count, TreeVariants, 2];
            for (int k = 0; k < (int)TreeKind.Count; k++)
                for (int v = 0; v < TreeVariants; v++)
                    for (int snow = 0; snow < 2; snow++)
                        _clusters[k, v, snow] = AddTreeCell(SpriteArt.Cluster((TreeKind)k, v, snow == 1));

            _skiers = new WorldSprite[6, SkierLooks, (int)SkierPose.Count];
            for (int skill = 0; skill < 6; skill++)
                for (int look = 0; look < SkierLooks; look++)
                    for (int pose = 0; pose < (int)SkierPose.Count; pose++)
                        _skiers[skill, look, pose] = Add(SpriteArt.Skier(skill, look, (SkierPose)pose));

            int liftTypes = System.Enum.GetValues(typeof(LiftType)).Length;
            _carriers = new WorldSprite[liftTypes, 2];
            for (int t = 0; t < liftTypes; t++)
                for (int o = 0; o < 2; o++)
                {
                    var canvas = SpriteArt.Carrier((LiftType)t, o == 1);
                    _carriers[t, o] = canvas != null ? Add(canvas) : default;
                }
            _tBar = Add(SpriteArt.TBarCross());
            _ropeHandle = Add(SpriteArt.RopeHandle());

            // Lift structures.
            _towers = new WorldSprite[liftTypes];
            _bullwheels = new WorldSprite[liftTypes];
            _terminals = new WorldSprite[liftTypes];
            _huts = new WorldSprite[liftTypes, 2];
            for (int t = 0; t < liftTypes; t++)
            {
                var type = (LiftType)t;
                _towers[t] = Add(LiftArt.Tower(type));
                _bullwheels[t] = Add(LiftArt.Bullwheel(type));
                if (LiftArt.HasTerminal(type)) _terminals[t] = Add(LiftArt.Terminal(type));
                for (int b = 0; b < 2; b++)
                {
                    var hut = LiftArt.Hut(type, b == 1);
                    if (hut != null) _huts[t, b] = Add(hut);
                }
            }
            _sheaves = Add(LiftArt.Sheaves());
            _column = Add(LiftArt.Column());

            // Props: cars and trail lights.
            _cars = new WorldSprite[PropArt.CarColorCount, 2];
            for (int col = 0; col < PropArt.CarColorCount; col++)
                for (int nose = 0; nose < 2; nose++)
                    _cars[col, nose] = Add(PropArt.Car(col, nose == 1));
            var lightDefs = SimConfig.DefaultLights();
            _poles = new WorldSprite[lightDefs.Length];
            _lampHeads = new WorldSprite[lightDefs.Length];
            foreach (var def in lightDefs)
            {
                _poles[(int)def.Size] = Add(PropArt.Pole(def));
                _lampHeads[(int)def.Size] = Add(PropArt.LampHeads(def));
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBAHalf, true, true)
            {
                name = "IceCoast World Atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            WriteMips(tex, _pixels);
            tex.Apply(false, true);
            _pixels = null;
            Texture = tex;
            return tex;
        }

        /// <summary>
        /// Mipmaps by hand: colour is averaged over opaque texels only, and a texel stays opaque if any of its
        /// children was. Forests stay dense and sprites keep their shape when zoomed out instead of eroding
        /// away under alpha testing.
        /// </summary>
        private static void WriteMips(Texture2D tex, Color[] level0)
        {
            var current = level0;
            int size = Size;
            for (int level = 0; level < tex.mipmapCount; level++)
            {
                tex.SetPixels(current, level);
                if (size == 1) break;
                int half = size / 2;
                var next = new Color[half * half];
                for (int y = 0; y < half; y++)
                    for (int x = 0; x < half; x++)
                    {
                        float r = 0, g = 0, b = 0;
                        int n = 0;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                var c = current[(y * 2 + dy) * size + x * 2 + dx];
                                if (c.a < 0.5f) continue;
                                r += c.r; g += c.g; b += c.b; n++;
                            }
                        next[y * half + x] = n == 0 ? new Color(0, 0, 0, 0) : new Color(r / n, g / n, b / n, 1f);
                    }
                current = next;
                size = half;
            }
        }

        /// <summary>Reserves a rectangle on the sprite shelves (aligned to 4 texels, with a transparent gap).</summary>
        private static Vector2Int Reserve(int w, int h)
        {
            const int gap = 4;
            int aw = (w + gap + 3) & ~3, ah = (h + gap + 3) & ~3;
            if (_shelfX + aw > Size)
            {
                _shelfX = 0;
                _shelfY += _shelfH;
                _shelfH = 0;
            }
            if (_shelfY + ah > TerrainStripY)
            {
                Debug.LogError("World atlas is full; some sprites will be missing.");
                return new Vector2Int(-1, -1);
            }
            var at = new Vector2Int(_shelfX, _shelfY);
            _shelfX += aw;
            _shelfH = Mathf.Max(_shelfH, ah);
            return at;
        }

        private static void Blit(PixelCanvas canvas, int ox, int oy, int offsetX, int offsetY)
        {
            for (int y = 0; y < canvas.Height; y++)
                for (int x = 0; x < canvas.Width; x++)
                {
                    Color32 c = canvas.Get(x, y);
                    if (c.a < 128) continue;
                    // Canvas rows count from the top; atlas rows from the bottom.
                    int ax = ox + offsetX + x, ay = oy + offsetY + (canvas.Height - 1 - y);
                    _pixels[ay * Size + ax] = new Color(
                        Mathf.GammaToLinearSpace(c.r / 255f) * 0.5f,
                        Mathf.GammaToLinearSpace(c.g / 255f) * 0.5f,
                        Mathf.GammaToLinearSpace(c.b / 255f) * 0.5f, 1f);
                }
        }

        private static WorldSprite Add(PixelCanvas canvas)
        {
            var at = Reserve(canvas.Width, canvas.Height);
            if (at.x < 0) return default;
            Blit(canvas, at.x, at.y, 0, 0);
            const float s = 1f / Size;
            return new WorldSprite
            {
                Uv0 = new Vector2(at.x * s, at.y * s),
                Uv1 = new Vector2((at.x + canvas.Width) * s, (at.y + canvas.Height) * s),
                Width = canvas.Width,
                Height = canvas.Height,
                PivotX = canvas.PivotX,
                PivotY = canvas.Height - canvas.PivotY, // canvas pivot is from the top
            };
        }

        /// <summary>
        /// Trees are drawn as one triangle (to keep vertex counts low across a forested mountain), so each tree
        /// sprite sits in the bottom-middle of a transparent cell twice its size: a triangle over the whole
        /// cell, with its base on the cell's bottom edge and its apex at the top middle, covers the sprite.
        /// </summary>
        private static WorldSprite AddTreeCell(PixelCanvas canvas)
        {
            int w = canvas.Width * 2, h = canvas.Height * 2;
            var at = Reserve(w, h);
            if (at.x < 0) return default;
            Blit(canvas, at.x, at.y, canvas.Width / 2, 0);
            const float s = 1f / Size;
            return new WorldSprite
            {
                Uv0 = new Vector2(at.x * s, at.y * s),
                Uv1 = new Vector2((at.x + w) * s, (at.y + h) * s),
                Width = w,
                Height = h,
                PivotX = canvas.Width / 2 + canvas.PivotX,
                PivotY = canvas.Height - canvas.PivotY,
            };
        }
    }
}
