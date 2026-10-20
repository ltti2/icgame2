using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Reusable CPU-side mesh builder. Lists are kept between rebuilds so steady-state rebuilding
    /// allocates nothing. All prototype geometry is flat-coloured triangles.
    ///
    /// Besides position and colour, every vertex carries a normal in grid space (x, y = grid axes, z = up)
    /// used by the shader's sunlight (zero means "lit like flat ground": trees, lifts, guests) and a UV into
    /// the <see cref="WorldAtlas"/>. Plain shapes get the atlas's neutral texel, so they draw in their colour.
    /// </summary>
    public sealed class MeshBuffer
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(4096);
        public readonly List<Color32> Colors = new List<Color32>(4096);
        public readonly List<Vector3> Normals = new List<Vector3>(4096);
        public readonly List<Vector2> Uvs = new List<Vector2>(4096);
        public readonly List<int> Indices = new List<int>(6144);

        public int VertexCount => Vertices.Count;

        /// <summary>
        /// False for meshes lit like flat ground anyway (trees): the mesh gets no normal channel, which the
        /// shader treats as flat. Saves 12 bytes a vertex across a forested mountain. Reset by Clear.
        /// </summary>
        public bool IncludeNormals = true;

        public void Clear()
        {
            Vertices.Clear();
            Colors.Clear();
            Normals.Clear();
            Uvs.Clear();
            Indices.Clear();
            IncludeNormals = true;
        }

        private void AddVertex(Vector3 position, Color32 color)
        {
            Vertices.Add(position);
            Colors.Add(color);
            Normals.Add(Vector3.zero);
            Uvs.Add(WorldAtlas.NeutralUv);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 color) => AddTriangle(a, b, c, color, color, color);

        /// <summary>Triangle with a colour per corner (the GPU blends between them).</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 ca, Color32 cb, Color32 cc)
        {
            int i = Vertices.Count;
            AddVertex(a, ca); AddVertex(b, cb); AddVertex(c, cc);
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
        }

        /// <summary>Quad a-b-c-d (in order around the edge), split along a-c.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
        {
            int i = Vertices.Count;
            AddVertex(a, color); AddVertex(b, color); AddVertex(c, color); AddVertex(d, color);
            Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
            Indices.Add(i); Indices.Add(i + 2); Indices.Add(i + 3);
        }

        /// <summary>A terrain tile: quad a-b-c-d split along a-c, with a slope normal for sunlight and a ground texture.</summary>
        public void AddTerrainQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color32 color,
                                   Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            int i = Vertices.Count;
            AddQuad(a, b, c, d, color);
            for (int k = 0; k < 4; k++) Normals[i + k] = normal;
            Uvs[i] = uvA; Uvs[i + 1] = uvB; Uvs[i + 2] = uvC; Uvs[i + 3] = uvD;
        }

        /// <summary>
        /// A pixel-art sprite standing on (or hanging from) <paramref name="anchor"/>, which is where the sprite's
        /// pivot goes. One texel = one screen pixel at zoom 1 when <paramref name="scale"/> is 1.
        /// </summary>
        public void AddSprite(Vector3 anchor, in WorldSprite sprite, bool flip, Color32 tint, float scale = 1f)
            => AddSprite(anchor, sprite, flip, tint, scale, scale);

        /// <summary>
        /// A sprite scaled separately across and up: used to stretch lift towers and terminal legs to whatever
        /// height the cable is at, while keeping them one pixel-column wide.
        /// </summary>
        public void AddSprite(Vector3 anchor, in WorldSprite sprite, bool flip, Color32 tint, float scaleX, float scaleY)
        {
            if (!sprite.IsValid) return;
            float ux = WorldAtlas.UnitsPerTexel * scaleX, uy = WorldAtlas.UnitsPerTexel * scaleY;
            float px = flip ? sprite.Width - sprite.PivotX : sprite.PivotX;
            float x0 = anchor.x - px * ux, x1;
            float y0 = anchor.y - sprite.PivotY * uy, y1;
            // Snap to whole screen pixels at zoom 1 so sprites stay crisp.
            x0 = Mathf.Round(x0 * 32f) / 32f; x1 = x0 + sprite.Width * ux;
            y0 = Mathf.Round(y0 * 32f) / 32f; y1 = y0 + sprite.Height * uy;
            float u0 = flip ? sprite.Uv1.x : sprite.Uv0.x, u1 = flip ? sprite.Uv0.x : sprite.Uv1.x;

            int i = Vertices.Count;
            AddQuad(new Vector3(x0, y0, anchor.z), new Vector3(x1, y0, anchor.z), new Vector3(x1, y1, anchor.z), new Vector3(x0, y1, anchor.z), tint);
            Uvs[i] = new Vector2(u0, sprite.Uv0.y);
            Uvs[i + 1] = new Vector2(u1, sprite.Uv0.y);
            Uvs[i + 2] = new Vector2(u1, sprite.Uv1.y);
            Uvs[i + 3] = new Vector2(u0, sprite.Uv1.y);
        }

        /// <summary>
        /// A tree sprite drawn as a single triangle (see WorldAtlas.AddTreeCell): base on the ground, apex above,
        /// the sprite cut out of it by alpha. Three vertices per tree keeps a forested mountain cheap.
        /// </summary>
        public void AddTree(Vector3 foot, in WorldSprite cell, float scale, Color32 tint)
        {
            if (!cell.IsValid) return;
            float u = WorldAtlas.UnitsPerTexel * scale;
            float left = foot.x - cell.PivotX * u, right = left + cell.Width * u;
            float bottom = foot.y - cell.PivotY * u, top = bottom + cell.Height * u;
            int i = Vertices.Count;
            AddTriangle(new Vector3(left, bottom, foot.z), new Vector3(right, bottom, foot.z), new Vector3((left + right) * 0.5f, top, foot.z), tint);
            SetTreeUvs(i, cell);
        }

        /// <summary>Points an existing tree triangle (starting at vertex <paramref name="i"/>) at another tree sprite.</summary>
        public void SetTreeUvs(int i, in WorldSprite cell)
        {
            Uvs[i] = cell.Uv0;
            Uvs[i + 1] = new Vector2(cell.Uv1.x, cell.Uv0.y);
            Uvs[i + 2] = new Vector2((cell.Uv0.x + cell.Uv1.x) * 0.5f, cell.Uv1.y);
        }

        /// <summary>A screen-space line of constant width, drawn as a thin quad.</summary>
        public void AddLine(Vector3 from, Vector3 to, float width, Color32 color)
        {
            Vector2 dir = (Vector2)(to - from);
            if (dir.sqrMagnitude < 1e-8f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Vector3 off = new Vector3(n.x, n.y, 0f);
            AddQuad(from - off, to - off, to + off, from + off, color);
        }

        /// <summary>Axis-aligned screen rectangle centred horizontally on <paramref name="bottomCenter"/>.</summary>
        public void AddUprightRect(Vector3 bottomCenter, float width, float height, Color32 color)
        {
            float hw = width * 0.5f;
            AddQuad(bottomCenter + new Vector3(-hw, 0f, 0f), bottomCenter + new Vector3(hw, 0f, 0f),
                    bottomCenter + new Vector3(hw, height, 0f), bottomCenter + new Vector3(-hw, height, 0f), color);
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Vertices);
            mesh.SetColors(Colors);
            if (IncludeNormals) mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Indices, 0, false);
            mesh.RecalculateBounds();
        }

        /// <summary>Recolours an existing mesh with identical topology (cheap path for snow/overlay changes).</summary>
        public void ApplyColorsOnly(Mesh mesh)
        {
            if (mesh.vertexCount != Colors.Count) return;
            mesh.SetColors(Colors);
        }

        /// <summary>Recolours and re-textures an existing mesh with identical topology (snow came or went).</summary>
        public void ApplyColorsAndUvs(Mesh mesh)
        {
            if (mesh.vertexCount != Colors.Count || Uvs.Count != Colors.Count) return;
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
        }

        /// <summary>Re-textures an existing mesh with identical topology.</summary>
        public void ApplyUvsOnly(Mesh mesh)
        {
            if (mesh.vertexCount != Uvs.Count) return;
            mesh.SetUVs(0, Uvs);
        }

        public static Mesh CreateDynamicMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            return mesh;
        }
    }
}
