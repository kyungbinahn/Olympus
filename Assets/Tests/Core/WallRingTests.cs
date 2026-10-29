using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.State;
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

        // 영토 단계 — 1 처음부터, 2 도적 소굴, 3 거인 1, 4 거인 2 (docs/village-growth.md).
        private static WallConfig Territory()
        {
            WallConfig config = Config();
            config.Add(new WallTier(1, 4f));
            config.Add(new WallTier(2, 4f, new[] { Requirement.Counter("front.outpost", 1) }));
            config.Add(new WallTier(3, 8f, new[] { Requirement.Counter("threat.giant_1.won", 1) }));
            config.Add(new WallTier(4, 6f, new[] { Requirement.Counter("threat.giant_2.won", 1) }));
            return config;
        }

        private static GameState StateWith(int hqLevel, params string[] counters)
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var store = new StateStore(state);
            var delta = new StateDelta();
            if (hqLevel > 0)
                delta.WithBuilding(BuildingChange.Add(1, "main_hall", new GridPos(0, 0), GridFootprint.Square(2), BuildingPhase.Complete, hqLevel, 0L, 0L));
            foreach (string c in counters)
                delta.WithCounter(CountChange.Absolute(c, 1));
            store.Apply(delta);
            return state;
        }

        private static readonly ProgressionConfig Hq = new ProgressionConfig("main_hall");

        [Test]
        public void 영토는_적을_무찌른_순간_넓어지고_중간을_건너뛰지_않는다()
        {
            WallConfig config = Territory();

            Assert.That(config.Current(StateWith(0, "front.outpost"), Hq), Is.Null, "본부가 폐허면 성벽도 없다");
            Assert.That(config.Current(StateWith(1), Hq).HqLevel, Is.EqualTo(1));
            Assert.That(config.Current(StateWith(1, "front.outpost"), Hq).HqLevel, Is.EqualTo(2), "본부 공사를 기다리지 않는다");
            Assert.That(config.Current(StateWith(2, "front.outpost", "threat.giant_1.won"), Hq).HqLevel, Is.EqualTo(3));

            // 거인 2만 이기고 도적 소굴을 안 부순 상태(디버그 등) — 2를 건너뛰고 4가 서지 않는다.
            Assert.That(config.Current(StateWith(1, "threat.giant_2.won"), Hq).HqLevel, Is.EqualTo(1));
        }

        [Test]
        public void 조건이_생기기_전_세이브는_본부_레벨보다_좁아지지_않는다()
        {
            // 거인 없이 본부 Lv.4까지 올라간 옛 세이브 — 열린 건물이 성 밖에 서면 안 된다.
            Assert.That(Territory().Current(StateWith(4, "front.outpost"), Hq).HqLevel, Is.EqualTo(4));
        }

        private static HqLevelDef Level(int level, Requirement requirement) =>
            new HqLevelDef(level, new[] { requirement }, null, 0L, 0, null, null, null);

        [Test]
        public void 본부_조건에_영토_조건이_빠지면_알린다()
        {
            var progression = new ProgressionConfig("main_hall");
            progression.AddHqLevel(Level(2, Requirement.Counter("front.outpost", 1)));
            progression.AddHqLevel(Level(3, Requirement.Prosperity(11)));   // 거인 1이 빠짐
            progression.AddHqLevel(Level(4, Requirement.Counter("threat.giant_2.won", 1)));

            List<string> problems = Territory().CheckAgainst(progression);
            Assert.That(problems.Count, Is.EqualTo(1), string.Join(" / ", problems));
            StringAssert.Contains("본부 Lv.3", problems[0]);
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
