using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.Territory;

namespace Olympus.Core.Tests
{
    public class WallRingTests
    {
        private static readonly WorldXZ Front = new WorldXZ(1f, 1f);

        private static WallConfig Config() => new WallConfig { Unit = 4f, PiecesPerSpan = 3, Margin = 2.5f, Blend = 3f, SmoothDegrees = 10f, Clearance = 1.5f };

        // 가로로 긴 마을 — 정사각형이 아니라 이 모양을 따라가야 한다.
        private static List<GroundPatch> LongVillage() => new List<GroundPatch>
        {
            new GroundPatch(new WorldXZ(-14f, 0f), 2.5f, 2.5f, 0f),
            new GroundPatch(new WorldXZ(-4f, 1f), 3f, 3f, 0f),
            new GroundPatch(new WorldXZ(6f, -1f), 2.5f, 2.5f, 30f),
            new GroundPatch(new WorldXZ(16f, 0f), 1.5f, 1.5f, 0f),
        };

        private static List<WallPiece> Of(WallPlan plan, WallPieceKind kind) => plan.Pieces.Where(p => p.Kind == kind).ToList();

        [Test]
        public void 기둥_사이마다_직선_세_조각이고_성문은_하나다()
        {
            WallPlan plan = WallRing.Plan(LongVillage(), Config(), 8f, Front);

            int spans = plan.Outline.Count - 1;
            Assert.That(Of(plan, WallPieceKind.Straight).Count, Is.EqualTo(spans * 3));
            Assert.That(Of(plan, WallPieceKind.Post).Count, Is.EqualTo(plan.Outline.Count));
            Assert.That(Of(plan, WallPieceKind.Gate).Count, Is.EqualTo(1));

            // 조각은 아트 길이(4m)에서 크게 벗어나지 않게 늘이거나 줄인다.
            foreach (WallPiece s in Of(plan, WallPieceKind.Straight))
                Assert.That(s.Span, Is.InRange(3f, 5f));
        }

        [Test]
        public void 둘레는_마을_모양을_따라간다()
        {
            WallPlan plan = WallRing.Plan(LongVillage(), Config(), 8f, Front);

            float minX = plan.Outline.Min(p => p.X), maxX = plan.Outline.Max(p => p.X);
            float minZ = plan.Outline.Min(p => p.Z), maxZ = plan.Outline.Max(p => p.Z);
            // 마을이 가로 약 34m·세로 약 7m — 성벽도 가로로 길어야 한다(정사각형이면 둘이 같다).
            Assert.That(maxX - minX, Is.GreaterThan((maxZ - minZ) * 1.6f));
        }

        [Test]
        public void 모든_건물은_여유를_두고_안에_있다()
        {
            List<GroundPatch> village = LongVillage();
            WallPlan plan = WallRing.Plan(village, Config(), 8f, Front);

            foreach (GroundPatch b in village)
                Assert.That(WallRing.Room(plan, b), Is.GreaterThanOrEqualTo(1.5f), b.Center.ToString());
            Assert.That(plan.InsideDistance(new WorldXZ(-4f, 1f)), Is.GreaterThan(0f));
            Assert.That(plan.InsideDistance(new WorldXZ(0f, 40f)), Is.LessThan(0f));
        }

        [Test]
        public void 성문은_전선_쪽을_바라본다()
        {
            WallPlan plan = WallRing.Plan(LongVillage(), Config(), 8f, Front);
            WallPiece gate = Of(plan, WallPieceKind.Gate).Single();

            Assert.That(gate.Span, Is.EqualTo(8f).Within(0.05f));
            Assert.That(gate.Center.X, Is.GreaterThan(0f));
            Assert.That(gate.Center.Z, Is.GreaterThan(0f));
            Assert.That(gate.YawDegrees, Is.InRange(0f, 90f));   // 바깥이 +X+Z 쪽
        }

        [Test]
        public void 직선은_바깥을_보게_돌아간다()
        {
            WallPlan plan = WallRing.Plan(LongVillage(), Config(), 8f, Front);
            foreach (WallPiece s in Of(plan, WallPieceKind.Straight))
            {
                double rad = s.YawDegrees * Math.PI / 180.0;
                float outX = (float)Math.Sin(rad), outZ = (float)Math.Cos(rad);
                var probe = new WorldXZ(s.Center.X + outX, s.Center.Z + outZ);
                Assert.That(plan.InsideDistance(probe), Is.LessThan(0f), "조각 " + s.Center + "의 바깥 1m가 성 밖이어야 한다");
            }
        }

        [Test]
        public void 건물이_늘면_성벽이_넓어진다()
        {
            List<GroundPatch> small = LongVillage().Take(2).ToList();
            WallPlan a = WallRing.Plan(small, Config(), 8f, Front);
            WallPlan b = WallRing.Plan(LongVillage(), Config(), 8f, Front);
            Assert.That(Area(b.Outline), Is.GreaterThan(Area(a.Outline)));
        }

        [Test]
        public void 거리장은_안이_음수_밖이_양수다()
        {
            WallPlan plan = WallRing.Plan(LongVillage(), Config(), 8f, Front);
            var pixels = new byte[80 * 80];
            WallField.Bake(pixels, 80, 80, new WorldXZ(-40f, -40f), 1f, plan.Outline);

            Assert.That(WallField.Decode(pixels[40 * 80 + 36]), Is.LessThan(0f));     // (-3.5, 0.5) 마을 가운데
            Assert.That(WallField.Decode(pixels[79 * 80 + 40]), Is.GreaterThan(10f)); // (0.5, 39.5) 멀리 밖
        }

        [Test]
        public void 본부_레벨에_맞는_단계를_고르고_숫자가_어긋나면_알린다()
        {
            WallConfig config = Config();
            config.Add(new WallTier(1, 8f));
            config.Add(new WallTier(3, 4f));

            Assert.That(config.ForHq(0), Is.Null);
            Assert.That(config.ForHq(2).HqLevel, Is.EqualTo(1));
            Assert.That(config.ForHq(9).HqLevel, Is.EqualTo(3));
            Assert.That(config.Validate(), Is.Empty);

            config.Add(new WallTier(2, 8f));   // 레벨 순서가 어긋남
            Assert.That(config.Validate().Count, Is.EqualTo(1));
        }

        private static float Area(List<WorldXZ> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].X * poly[i].Z - poly[i].X * poly[j].Z;
            return Math.Abs(a) * 0.5f;
        }
    }
}
