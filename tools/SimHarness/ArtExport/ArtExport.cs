using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using IceCoast.View.Rendering;
using UnityEngine;

/// <summary>
/// Writes every sprite sheet (PNG + JSON) and ground texture from the code-drawn art, the same files the editor
/// menu Ice Coast > Art > Export Missing Sprites to PNG writes inside Unity.
///
///   dotnet run -- [output folder] [--overwrite]
///       Defaults to Assets/StreamingAssets/Art. Existing PNGs are kept unless --overwrite.
///   dotnet run -- --verify RAW_FOLDER [art folder]
///       Checks that loading the sheets gives back exactly the code-drawn sprites. RAW_FOLDER holds each sheet
///       decoded to raw RGBA (sheet.rgba, with sheet.size = "w h"), e.g. by an image library, so the check
///       covers PNGs as another program wrote them.
/// </summary>
public static class ArtExport
{
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--verify") return Verify(args[1], args.Length > 2 ? args[2] : DefaultRoot());

        string root = DefaultRoot();
        bool overwrite = false;
        foreach (var a in args)
        {
            if (a == "--overwrite") overwrite = true;
            else root = a;
        }

        string ground = Path.Combine(root, "ground");
        Directory.CreateDirectory(ground);
        int written = 0;
        foreach (var sheet in ArtSheets.All())
        {
            string png = Path.Combine(root, sheet.Name + ".png");
            bool exists = File.Exists(png);
            if (overwrite || !exists)
            {
                WritePng(png, ArtSheets.Compose(sheet), sheet.Width, sheet.Height);
                File.WriteAllText(Path.Combine(root, sheet.Name + ".json"), ArtSheets.ManifestJson(sheet));
                written += 2;
                Console.WriteLine($"{sheet.Name,-16} {sheet.Width}x{sheet.Height}  cells {sheet.CellWidth}x{sheet.CellHeight}  {sheet.Sprites.Count} sprites");
            }
        }
        int n = WorldAtlasConstants.TerrainCellSize;
        for (int i = 0; i < (int)SurfaceDetail.Count; i++)
        {
            var d = (SurfaceDetail)i;
            string png = Path.Combine(ground, ArtSheets.GroundName(d) + ".png");
            if (!overwrite && File.Exists(png)) continue;
            WritePng(png, ArtSheets.GroundPixels(d), n, n);
            written++;
        }
        Console.WriteLine($"Wrote {written} files to {Path.GetFullPath(root)}");
        return 0;
    }

    private static string DefaultRoot()
    {
        // tools/SimHarness/ArtExport -> project root
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets", "_Project"))) return Path.Combine(dir, "Assets", "StreamingAssets", "Art");
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return Path.Combine("Assets", "StreamingAssets", "Art");
    }

    // =====================================================================
    // Round-trip check
    // =====================================================================

    private static int Verify(string rawFolder, string root)
    {
        int failures = 0, sprites = 0;
        var options = new JsonSerializerOptions { IncludeFields = true };
        foreach (var sheet in ArtSheets.All())
        {
            var size = File.ReadAllText(Path.Combine(rawFolder, sheet.Name + ".size")).Split(' ');
            int w = int.Parse(size[0]), h = int.Parse(size[1]);
            var bytes = File.ReadAllBytes(Path.Combine(rawFolder, sheet.Name + ".rgba"));
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            var manifest = JsonSerializer.Deserialize<SheetManifest>(File.ReadAllText(Path.Combine(root, sheet.Name + ".json")), options);

            var warnings = new List<string>();
            var loaded = ArtSheets.Extract(sheet, manifest, px, w, h, warnings);
            foreach (var wmsg in warnings) Console.WriteLine("  warning: " + wmsg);
            foreach (var s in sheet.Sprites)
            {
                sprites++;
                var a = s.Canvas;
                var b = loaded[s.Name];
                string problem = null;
                if (a.Width != b.Width || a.Height != b.Height) problem = $"size {b.Width}x{b.Height}, expected {a.Width}x{a.Height}";
                else if (a.PivotX != b.PivotX || a.PivotY != b.PivotY) problem = $"pivot {b.PivotX},{b.PivotY}, expected {a.PivotX},{a.PivotY}";
                else
                {
                    for (int y = 0; y < a.Height && problem == null; y++)
                        for (int x = 0; x < a.Width && problem == null; x++)
                        {
                            var ca = a.Get(x, y);
                            var cb = b.Get(x, y);
                            bool oa = ca.a >= 128, ob = cb.a >= 128;
                            if (oa != ob || (oa && (ca.r != cb.r || ca.g != cb.g || ca.b != cb.b)))
                                problem = $"pixel {x},{y} differs";
                        }
                }
                if (problem != null)
                {
                    failures++;
                    Console.WriteLine($"  FAIL {sheet.Name}/{s.Name}: {problem}");
                }
            }
        }
        Console.WriteLine($"{sprites} sprites checked, {failures} differences");
        return failures == 0 ? 0 : 1;
    }

    // =====================================================================
    // PNG writer: 8-bit RGBA, no filtering, zlib via System.IO.Compression
    // =====================================================================

    private static void WritePng(string path, Color32[] topDown, int width, int height)
    {
        var raw = new byte[height * (width * 4 + 1)];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0; // filter: none
            for (int x = 0; x < width; x++)
            {
                var c = topDown[y * width + x];
                raw[o++] = c.r; raw[o++] = c.g; raw[o++] = c.b; raw[o++] = c.a;
            }
        }
        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            idat = ms.ToArray();
        }

        using (var fs = File.Create(path))
        {
            fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)width);
            WriteBE(ihdr, 4, (uint)height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // RGBA
            Chunk(fs, "IHDR", ihdr);
            Chunk(fs, "IDAT", idat);
            Chunk(fs, "IEND", Array.Empty<byte>());
        }
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(typeBytes, 0xFFFFFFFFu);
        crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(byte[] data, uint crc)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static void WriteBE(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }
}
