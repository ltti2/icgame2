using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// All the game's pixel art, loaded from editable PNGs in <c>Assets/StreamingAssets/Art</c> when they are
    /// there, otherwise drawn in code (see <see cref="ArtSheets"/>). The world atlas and the UI atlas are built
    /// from it at startup, and rebuilt by <see cref="ArtHotReload"/> when the files change while playing.
    ///
    /// Folder layout:
    ///  - <c>&lt;sheet&gt;.png</c> + <c>&lt;sheet&gt;.json</c>: sprite sheets (trees, skiers, lifts, cars, lights,
    ///    buildings, UI icons, UI font). The JSON gives each sprite's rectangle and pivot.
    ///  - <c>ground/&lt;name&gt;.png</c>: 128x128 tiling ground textures (128 grey = no change).
    /// Missing files fall back to the code-drawn art, so deleting a PNG restores the default.
    /// Ice Coast > Art > Export Sprites to PNG (editor menu) writes any missing files.
    /// </summary>
    public sealed class ArtLibrary
    {
        public const string FolderName = "Art";
        public const string GroundFolderName = "ground";

        private readonly Dictionary<string, PixelCanvas> _sprites = new Dictionary<string, PixelCanvas>();
        private readonly Dictionary<SurfaceDetail, Color32[]> _ground = new Dictionary<SurfaceDetail, Color32[]>();

        /// <summary>Problems found while loading (bad sizes, missing entries). Logged once by the atlases.</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>How many sheets and ground textures came from PNG files.</summary>
        public int FilesLoaded { get; private set; }

        private static ArtLibrary _current;

        /// <summary>The art the atlases are built from, loaded on first use.</summary>
        public static ArtLibrary Current => _current ?? (_current = Load());

        /// <summary>Raised by <see cref="ReloadCurrent"/> after the files were read again (the UI atlas listens).</summary>
        public static event Action<ArtLibrary> Reloaded;

        /// <summary>Reads the files again, redraws the world atlas and tells the UI. Returns the new library.</summary>
        public static ArtLibrary ReloadCurrent()
        {
            _current = Load();
            WorldAtlas.Reload(_current);
            Reloaded?.Invoke(_current);
            return _current;
        }

        public static string RootPath => Path.Combine(Application.streamingAssetsPath, FolderName);
        public static string GroundPath => Path.Combine(RootPath, GroundFolderName);

        /// <summary>A sprite by name (see the ArtSheets.*Name helpers), or null if there is none.</summary>
        public PixelCanvas Sprite(string name) => _sprites.TryGetValue(name, out var c) ? c : null;

        /// <summary>A ground texture as atlas values (multiply by 2 for the multiplier), rows from the top.</summary>
        public Color32[] Ground(SurfaceDetail detail) => _ground.TryGetValue(detail, out var px) ? px : null;

        // =====================================================================
        // Loading
        // =====================================================================

        public static ArtLibrary Load()
        {
            var lib = new ArtLibrary();
            string root = RootPath;
            bool hasFolder = Directory.Exists(root);

            foreach (var sheet in ArtSheets.All())
            {
                string png = Path.Combine(root, sheet.Name + ".png");
                if (hasFolder && File.Exists(png) && TryReadPng(png, out var px, out int w, out int h, lib.Warnings))
                {
                    var manifest = ReadManifest(Path.Combine(root, sheet.Name + ".json"), sheet, lib.Warnings);
                    foreach (var kv in ArtSheets.Extract(sheet, manifest, px, w, h, lib.Warnings)) lib._sprites[kv.Key] = kv.Value;
                    lib.FilesLoaded++;
                }
                else
                {
                    foreach (var s in sheet.Sprites) lib._sprites[s.Name] = s.Canvas;
                }
            }

            int n = WorldAtlasConstants.TerrainCellSize;
            for (int i = 0; i < (int)SurfaceDetail.Count; i++)
            {
                var detail = (SurfaceDetail)i;
                string png = Path.Combine(GroundPath, ArtSheets.GroundName(detail) + ".png");
                if (hasFolder && File.Exists(png) && TryReadPng(png, out var px, out int w, out int h, lib.Warnings))
                {
                    if (w == n && h == n)
                    {
                        lib._ground[detail] = px;
                        lib.FilesLoaded++;
                        continue;
                    }
                    lib.Warnings.Add($"ground/{ArtSheets.GroundName(detail)}.png is {w}x{h}; ground textures must be {n}x{n}. Using the built-in one.");
                }
                lib._ground[detail] = ArtSheets.GroundPixels(detail);
            }
            return lib;
        }

        private static SheetManifest ReadManifest(string path, ArtSheet sheet, List<string> warnings)
        {
            if (!File.Exists(path)) return ArtSheets.DefaultManifest(sheet);
            try
            {
                var m = JsonUtility.FromJson<SheetManifest>(File.ReadAllText(path));
                if (m != null && m.sprites != null && m.sprites.Length > 0) return m;
                warnings.Add($"{Path.GetFileName(path)} has no sprites listed; using the built-in layout.");
            }
            catch (Exception e)
            {
                warnings.Add($"Couldn't read {Path.GetFileName(path)} ({e.Message}); using the built-in layout.");
            }
            return ArtSheets.DefaultManifest(sheet);
        }

        /// <summary>Reads a PNG into pixels with rows from the top (as image editors show them).</summary>
        private static bool TryReadPng(string path, out Color32[] pixels, out int width, out int height, List<string> warnings)
        {
            pixels = null;
            width = height = 0;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path), false))
                {
                    warnings.Add($"{Path.GetFileName(path)} isn't a PNG Unity can read.");
                    Dispose(tex);
                    return false;
                }
                width = tex.width;
                height = tex.height;
                var bottomUp = tex.GetPixels32();
                Dispose(tex);
                pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    Array.Copy(bottomUp, (height - 1 - y) * width, pixels, y * width, width);
                return true;
            }
            catch (Exception e)
            {
                warnings.Add($"Couldn't read {Path.GetFileName(path)}: {e.Message}");
                return false;
            }
        }

        // =====================================================================
        // Exporting the code-drawn art
        // =====================================================================

        /// <summary>
        /// Writes every sheet (PNG + JSON) and ground texture from the code-drawn art. Existing files are kept
        /// unless <paramref name="overwrite"/>. Returns how many files were written.
        /// </summary>
        public static int ExportDefaults(bool overwrite)
        {
            int written = 0;
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(GroundPath);
            foreach (var sheet in ArtSheets.All())
            {
                string png = Path.Combine(RootPath, sheet.Name + ".png");
                string json = Path.Combine(RootPath, sheet.Name + ".json");
                bool pngExists = File.Exists(png);
                if (overwrite || !pngExists)
                {
                    WritePng(png, ArtSheets.Compose(sheet), sheet.Width, sheet.Height);
                    written++;
                }
                // The JSON describes the PNG's layout, so only replace it together with the PNG.
                if (overwrite || !pngExists || !File.Exists(json))
                {
                    File.WriteAllText(json, ArtSheets.ManifestJson(sheet));
                    written++;
                }
            }
            int n = WorldAtlasConstants.TerrainCellSize;
            for (int i = 0; i < (int)SurfaceDetail.Count; i++)
            {
                var detail = (SurfaceDetail)i;
                string png = Path.Combine(GroundPath, ArtSheets.GroundName(detail) + ".png");
                if (!overwrite && File.Exists(png)) continue;
                WritePng(png, ArtSheets.GroundPixels(detail), n, n);
                written++;
            }
            return written;
        }

        private static void WritePng(string path, Color32[] topDown, int width, int height)
        {
            var bottomUp = new Color32[width * height];
            for (int y = 0; y < height; y++)
                Array.Copy(topDown, y * width, bottomUp, (height - 1 - y) * width, width);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(bottomUp);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Dispose(tex);
        }

        /// <summary>Destroy only works in play mode; the editor's export menu runs outside it.</summary>
        private static void Dispose(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        // =====================================================================
        // Change detection (hot reload)
        // =====================================================================

        /// <summary>A fingerprint of the art files' names, sizes and modification times. Changes when any file does.</summary>
        public static long FilesStamp()
        {
            string root = RootPath;
            if (!Directory.Exists(root)) return 0;
            long stamp = 17;
            try
            {
                foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (!(file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) continue;
                    var info = new FileInfo(file);
                    stamp = stamp * 31 + info.LastWriteTimeUtc.Ticks;
                    stamp = stamp * 31 + info.Length;
                    stamp = stamp * 31 + file.GetHashCode();
                }
            }
            catch (IOException) { }
            return stamp;
        }
    }
}
