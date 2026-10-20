using IceCoast.Simulation.World;
using IceCoast.View.Iso;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Immediate-mode overlay for tools: call Begin(), add highlights and lines, End() applies.
    /// Drawn with the always-on-top material so previews are never hidden by terrain.
    /// </summary>
    public sealed class PreviewRenderer : MonoBehaviour
    {
        private Mesh _mesh;
        private readonly MeshBuffer _buffer = new MeshBuffer();

        public void Initialize()
        {
            _mesh = MeshBuffer.CreateDynamicMesh("Tool Preview");
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RenderMaterials.Overlay;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Begin() => _buffer.Clear();
        public void End() => _buffer.ApplyTo(_mesh);

        /// <summary>Fills a tile's projected diamond (following the slope).</summary>
        public void Tile(WorldMap map, int x, int y, Color32 color)
        {
            if (!map.InBounds(x, y)) return;
            map.GetCorners(x, y, out int h00, out int h10, out int h01, out int h11);
            _buffer.AddQuad(
                IsoProjection.ToWorld(x, y, h00),
                IsoProjection.ToWorld(x + 1, y, h10),
                IsoProjection.ToWorld(x + 1, y + 1, h11),
                IsoProjection.ToWorld(x, y + 1, h01),
                color);
        }

        /// <summary>Outline of a rectangle of tiles, following the terrain along its edges.</summary>
        public void RectOutline(WorldMap map, int x0, int y0, int w, int h, Color32 color, float width = 0.08f)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                Edge(map, x, y0, x + 1, y0, color, width);
                Edge(map, x, y0 + h, x + 1, y0 + h, color, width);
            }
            for (int y = y0; y < y0 + h; y++)
            {
                Edge(map, x0, y, x0, y + 1, color, width);
                Edge(map, x0 + w, y, x0 + w, y + 1, color, width);
            }
        }

        private void Edge(WorldMap map, int ax, int ay, int bx, int by, Color32 color, float width)
        {
            var a = IsoProjection.ToWorld(ax, ay, map.GetVertexHeight(ax, ay));
            var b = IsoProjection.ToWorld(bx, by, map.GetVertexHeight(bx, by));
            _buffer.AddLine(a, b, width, color);
        }

        public void Line(Vector3 a, Vector3 b, Color32 color, float width = 0.08f) => _buffer.AddLine(a, b, width, color);

        /// <summary>A small marker above a grid vertex (terraform cursor).</summary>
        public void VertexMarker(WorldMap map, int vx, int vy, Color32 color)
        {
            var p = IsoProjection.ToWorld(vx, vy, map.GetVertexHeight(vx, vy));
            _buffer.AddQuad(p + new Vector3(0f, 0.15f, 0f), p + new Vector3(0.22f, 0f, 0f), p + new Vector3(0f, -0.15f, 0f), p + new Vector3(-0.22f, 0f, 0f), color);
        }
    }
}
