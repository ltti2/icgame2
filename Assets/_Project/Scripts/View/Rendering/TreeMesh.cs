using IceCoast.Simulation.Config;
using IceCoast.Simulation.Core;
using UnityEngine;

namespace IceCoast.View.Rendering
{
    /// <summary>
    /// Shared tree drawing for the playable map and the scenery. Each tree slot is one pixel-art sprite (a
    /// spruce, fir or leafless Vermont hardwood, or a stand of three of them) drawn as a single cut-out
    /// triangle, so a thick forest stays cheap. Slots are spread over the tile's quarters so woods have no
    /// bald patches. Trees carry snow exactly when the ground under them does.
    /// </summary>
    public static class TreeMesh
    {
        /// <summary>Chance that a tree slot is a stand of three trees rather than one.</summary>
        public const float ClusterChance = 0.6f;

        /// <summary>
        /// Stable position inside the tile for tree slot k: each slot gets its own quarter of the tile (in a
        /// per-tile shuffled order) and is jittered within it, so trees cover the tile evenly.
        /// </summary>
        public static void Jitter(int x, int y, int seed, int k, out float jx, out float jy, out float size)
        {
            int start = (int)(SimRandom.Hash01(x, y, seed + 977) * 4f);
            int q = (start + k) & 3;
            jx = (q & 1) * 0.5f + 0.06f + SimRandom.Hash01(x, y, seed + k * 3) * 0.38f;
            jy = (q >> 1) * 0.5f + 0.06f + SimRandom.Hash01(x, y, seed + k * 3 + 1) * 0.38f;
            size = 0.8f + 0.5f * SimRandom.Hash01(x, y, seed + k * 3 + 2);
        }

        /// <summary>
        /// Which tree stands here: hardwoods mixed in on the lower slopes, spruce and fir above, only firs in
        /// the stunted forest near the summit. Depends only on position, never on snow, so a tree keeps its
        /// species (and its vertices) when the snow comes and goes.
        /// </summary>
        public static void Pick(int x, int y, int seed, int k, float heightSteps, SimConfig.TerrainSettings terrain,
                                out TreeKind kind, out int variant, out bool cluster)
        {
            cluster = SimRandom.Hash01(x, y, seed + k * 3 + 555) < ClusterChance;
            float span = Mathf.Max(1f, terrain.SummitElevationSteps - terrain.BaseElevationSteps);
            float up = (heightSteps - terrain.BaseElevationSteps) / span;
            float r = SimRandom.Hash01(x, y, seed + k * 3 + 2);
            if (heightSteps >= terrain.StuntedForestSteps) kind = TreeKind.Fir;
            else if (up < 0.45f && r < 0.45f - up * 0.6f) kind = TreeKind.Hardwood;
            else kind = r > 0.55f ? TreeKind.Fir : TreeKind.Spruce;
            variant = SimRandom.Hash01(x, y, seed + k * 3 + 1) > 0.5f ? 1 : 0;
        }

        public static WorldSprite Sprite(TreeKind kind, int variant, bool snowy, bool cluster)
            => cluster ? WorldAtlas.TreeCluster(kind, variant, snowy) : WorldAtlas.Tree(kind, variant, snowy);

        /// <summary>A tree (or stand of trees) on <paramref name="foot"/> (already depth-biased by the caller).</summary>
        public static void Add(MeshBuffer buffer, Vector3 foot, TreeKind kind, int variant, bool snowy, bool cluster, Color32 tint)
            => buffer.AddTree(foot, Sprite(kind, variant, snowy, cluster), 1f, tint);
    }
}
