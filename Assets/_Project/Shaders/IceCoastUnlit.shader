// Vertex-coloured shader used for all prototype geometry (terrain, trees, lifts, guests, tool previews).
// Works in both the Built-in pipeline and URP: a pass with no LightMode tag is drawn by URP as
// "SRPDefaultUnlit".
//
// Depth testing is what makes the isometric sorting work: every vertex gets a Z from its grid
// diagonal (see IsoProjection.Depth), so nearer tiles hide farther ones with no sort keys at all.
// Tool previews use the same shader with ZTest Always + alpha blending + _Lit 0 so they draw on top, unshaded.
//
// Lighting (set each frame by DayLightController through global shader properties):
//   - Sun: direction and colour follow the time of day. Normals and the sun direction are in GRID space
//     (x, y = grid axes, z = up), not Unity world space, because the iso meshes are flat in world space.
//   - Sky: steeper ground is darker, so downhill always reads darker than the flats above it.
// Contour lines are separate geometry (ContourRenderer) so they can sit on top of the forest.
//
// Trail lights: on lit (opaque) geometry the vertex colour's ALPHA carries lamp light, 255 = none, 0 = full.
// Lamp light is a floor under the day/night lighting, so lit trails glow warm at night and lamp heads shine.
//
// Texture (_MainTex = WorldAtlas, generated at startup): final colour = vertex colour x texel x 2, so a
// texel of 0.5 leaves the vertex colour as it is. Ground textures are multipliers around 0.5 over the tile
// colour; sprites (trees, skiers, chairs) store their colour at half strength and are drawn with a white
// vertex colour. Texels with alpha below 0.5 are cut out. Meshes without UVs sample (0,0), which is neutral.
Shader "IceCoast/UnlitVertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _MainTex ("World atlas (generated)", 2D) = "grey" {}
        _Lit ("Lit (0 = flat colour, for overlays)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
                        #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Lit;
            sampler2D _MainTex;

            // Globals (DayLightController). Defaults of 0 are handled so the scene is visible without it.
            float4 _IC_SunDir;        // grid space, normalised; w = sun intensity 0..1
            float4 _IC_SunColor;      // linear
            float4 _IC_AmbientColor;  // linear
            float _IC_SkyShade;       // 0..1: how much steep slopes darken
            float _IC_LightAmount;    // 0..1: 0 = flat colours (used while overlays are on)
            float4 _IC_LampColor;     // linear colour of trail-light glow (DayLightController)

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 normal : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                fixed4 c = v.color;
                // The palette is authored in sRGB. Unity converts material colours for Linear projects but
                // not vertex colours, so do it here or everything looks washed out.
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                o.color = c * _Tint;
                o.normal = v.normal;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = i.color;

                // Atlas: cut-out sprites, and detail x2 (0.5 = unchanged). The atlas holds linear values; in a
                // Gamma-space project convert the detail so sprite colours still come out as authored.
                float4 tex = tex2D(_MainTex, i.uv);
                clip(tex.a - 0.5);
                float3 detail = tex.rgb * 2.0;
                #ifdef UNITY_COLORSPACE_GAMMA
                detail = LinearToGammaSpace(detail);
                #endif
                col.rgb *= detail;

                // Objects without a normal (trees, lifts, guests) are lit like flat ground.
                float3 n = dot(i.normal, i.normal) > 0.25 ? normalize(i.normal) : float3(0, 0, 1);

                // Lambert, normalised so flat ground gets the same light at any sun height; this keeps
                // midwinter's low sun readable while still raking light across slopes.
                float3 sun = _IC_SunDir.xyz;
                float flat = max(sun.z, 0.25);
                float direct = saturate(dot(n, sun)) / flat;
                float3 light = _IC_AmbientColor.rgb + _IC_SunColor.rgb * _IC_SunDir.w * min(direct, 1.6);

                // Sky term: steep ground darker, regardless of where the sun is.
                float sky = lerp(1.0, pow(saturate(n.z), 2.5), _IC_SkyShade);
                light *= sky;

                // Trail lights (vertex alpha, see top): never darker than the lamp light reaching here.
                float lamp = saturate(1.0 - i.color.a) * _Lit;
                light = max(light, _IC_LampColor.rgb * lamp);

                float amount = _Lit * _IC_LightAmount;
                col.rgb *= lerp(float3(1, 1, 1), light, amount);

                return col;
            }
            ENDCG
        }
    }
}
