using System;
using IceCoast.Simulation.Core;
using IceCoast.Simulation.World;
using NUnit.Framework;

namespace IceCoast.Tests
{
    [TestFixture]
    public class WorldTests
    {
        [Test]
        public void VertexOnChunkCorner_IsSharedByAllFourChunks()
        {
            var map = new WorldMap(64, 64);
            map.SetVertexHeight(32, 32, 77);

            Assert.AreEqual(77, map.GetChunk(0, 0).VertexHeight[Chunk.VertexIndex(32, 32)]);
            Assert.AreEqual(77, map.GetChunk(1, 0).VertexHeight[Chunk.VertexIndex(0, 32)]);
            Assert.AreEqual(77, map.GetChunk(0, 1).VertexHeight[Chunk.VertexIndex(32, 0)]);
            Assert.AreEqual(77, map.GetChunk(1, 1).VertexHeight[Chunk.VertexIndex(0, 0)]);
            Assert.AreEqual(77, map.GetVertexHeight(32, 32));
        }

        [Test]
        public void TileCorners_ReadAcrossChunkBorder()
        {
            var map = new WorldMap(64, 64);
            map.SetVertexHeight(32, 10, 5);
            map.GetCorners(31, 10, out _, out int h10, out _, out _);
            map.GetCorners(32, 10, out int h00, out _, out _, out _);
            Assert.AreEqual(5, h10);
            Assert.AreEqual(5, h00);
        }

        [Test]
        public void Slope_OfUniformRamp_MatchesRiseOverRun()
        {
            var map = new WorldMap(32, 32);
            // 1 step per tile along x: 1 m / 10 m = 10%.
            for (int vy = 0; vy <= 32; vy++)
                for (int vx = 0; vx <= 32; vx++)
                    map.SetVertexHeight(vx, vy, vx);

            var scale = new TerrainSettingsView(new IceCoast.Simulation.Config.SimConfig.TerrainSettings());
            Assert.AreEqual(10f, TerrainQueries.SlopePercent(map, scale, 5, 5), 0.001f);
        }

        [Test]
        public void GeneratedTerrain_RespectsMaxStepPerEdge()
        {
            var sim = TestGames.NewSmallGame();
            var map = sim.Map;
            int max = sim.Config.Terrain.MaxStepPerEdge;
            for (int vy = 0; vy < map.Height; vy++)
            {
                for (int vx = 0; vx < map.Width; vx++)
                {
                    int h = map.GetVertexHeight(vx, vy);
                    Assert.LessOrEqual(Math.Abs(h - map.GetVertexHeight(vx + 1, vy)), max);
                    Assert.LessOrEqual(Math.Abs(h - map.GetVertexHeight(vx, vy + 1)), max);
                }
            }
        }

        [Test]
        public void GeneratedTerrain_SummitIsTopLeft_BaseIsBottomRight()
        {
            var sim = TestGames.NewSmallGame();
            var map = sim.Map;
            float summit = map.TileCenterHeight(map.Width / 8, map.Height / 8);
            float baseArea = map.TileCenterHeight(map.Width - 10, map.Height - 10);
            Assert.Greater(summit, baseArea + 50f);
        }

        [Test]
        public void NewGame_OwnsOnlyTheStarterParcel()
        {
            var sim = TestGames.NewSmallGame();
            int owned = 0;
            foreach (var p in sim.World.Parcels) if (p.Owned) owned++;
            Assert.AreEqual(1, owned);
            Assert.IsTrue(sim.Map.IsOwned(sim.World.ResortEntrance));
            Assert.IsFalse(sim.Map.IsOwned(new Int2(2, 2)));
        }

        [Test]
        public void Occupant_PacksKindAndId()
        {
            ushort packed = Occupant.Pack(OccupantKind.LiftTower, 1234);
            Assert.AreEqual(OccupantKind.LiftTower, Occupant.KindOf(packed));
            Assert.AreEqual(1234, Occupant.IdOf(packed));
        }

        [Test]
        public void SimRandom_IsDeterministic()
        {
            var a = new SimRandom(99);
            var b = new SimRandom(99);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }
    }
}
