using UnityEngine;
using UnityEngine.Rendering;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Creates the materials all prototype geometry uses, from the IceCoast/UnlitVertexColor shader:
    ///  - Opaque: depth-tested, depth-writing and sunlit (terrain, trees, lifts, guests).
    ///  - Lines: like Opaque but unlit (contour lines).
    ///  - Overlay: alpha-blended, unlit and always on top (tool previews, highlights).
    /// The shader is passed in from GameBootstrap (a serialized reference) so it is included in builds.
    /// All of them sample the generated <see cref="WorldAtlas"/> (ground textures and sprites).
    /// </summary>
    public static class RenderMaterials
    {
        public const string ShaderName = "IceCoast/UnlitVertexColor";

        public static Material Opaque { get; private set; }
        public static Material Overlay { get; private set; }
        /// <summary>Depth-tested but unlit: contour lines keep their colour day and night.</summary>
        public static Material Lines { get; private set; }

        public static void Initialize(Shader shader)
        {
            if (shader == null) shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{ShaderName}' not found. Assign it on the GameBootstrap. Falling back to Sprites/Default.");
                shader = Shader.Find("Sprites/Default");
            }

            var atlas = WorldAtlas.Build();

            Opaque = new Material(shader) { name = "IceCoast Opaque", mainTexture = atlas };
            SetIfPresent(Opaque, "_ZTest", (float)CompareFunction.LessEqual);
            SetIfPresent(Opaque, "_ZWrite", 1f);
            SetIfPresent(Opaque, "_SrcBlend", (float)BlendMode.One);
            SetIfPresent(Opaque, "_DstBlend", (float)BlendMode.Zero);
            SetIfPresent(Opaque, "_Lit", 1f);
            Opaque.renderQueue = (int)RenderQueue.Geometry;

            Overlay = new Material(shader) { name = "IceCoast Overlay", mainTexture = atlas };
            SetIfPresent(Overlay, "_ZTest", (float)CompareFunction.Always);
            SetIfPresent(Overlay, "_ZWrite", 0f);
            SetIfPresent(Overlay, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetIfPresent(Overlay, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetIfPresent(Overlay, "_Lit", 0f); // tool previews keep their exact colours, day or night
            Overlay.renderQueue = (int)RenderQueue.Overlay;

            Lines = new Material(Opaque) { name = "IceCoast Lines" };
            SetIfPresent(Lines, "_Lit", 0f);
            Lines.renderQueue = (int)RenderQueue.Geometry + 10;
        }

        private static void SetIfPresent(Material m, string property, float value)
        {
            if (m.HasProperty(property)) m.SetFloat(property, value);
        }
    }
}
