using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.Territory;

namespace Olympus.Core.Tests
{
    public class WallRingTests
    {
        private static List<WallPiece> Of(List<WallPiece> all, WallPieceKind kind) => all.Where(p => p.Kind == kind).ToList();

        [Test]
        public void 둘레는_조각_길이마다_기둥이고_성문_모서리만_잘린다()
        {
            // 한 변 5조각(20m), 성문 1조각.
            List<WallPiece> all = WallRing.Layout(new WorldXZ(0f, 0f), 10f, 4f, 1);

            Assert.That(Of(all, WallPieceKind.Straight).Count, Is.EqualTo(4 * 5 - 2));
            Assert.That(Of(all, WallPieceKind.Post).Count, Is.EqualTo(4 * 5 - 2 + 1));
            Assert.That(Of(all, WallPieceKind.Gate).Count, Is.EqualTo(1));

            // 성문 모서리(10,10)에는 기둥이 없고, 나머지 세 모서리에는 있다.
            var posts = Of(all, WallPieceKind.Post).Select(p => (p.Center.X, p.Center.Z)).ToList();
            Assert.That(posts, Has.No.Member((10f, 10f)));
            Assert.That(posts, Has.Member((-10f, -10f)));
            Assert.That(posts, Has.Member((10f, -10f)));
            Assert.That(posts, Has.Member((-10f, 10f)));
            Assert.That(posts, Has.Member((6f, 10f)));    // 잘린 끝
            Assert.That(posts, Has.Member((10f, 6f)));
        }

        [Test]
        public void 성문은_전선_쪽_대각선을_바라보고_잘린_길이만큼_걸친다()
        {
            WallPiece gate = WallRing.Layout(new WorldXZ(-0.5f, -0.5f), 18f, 4f, 2).Single(p => p.Kind == WallPieceKind.Gate);

            Assert.That(gate.YawDegrees, Is.EqualTo(45f));
            Assert.That(gate.Center.X, Is.EqualTo(-0.5f + 14f).Within(1e-4f));   // (10+18)/2
            Assert.That(gate.Center.Z, Is.EqualTo(-0.5f + 14f).Within(1e-4f));
            Assert.That(gate.Span, Is.EqualTo(8f * 1.41421f).Within(1e-3f));
        }

        [Test]
        public void 직선은_바깥을_보게_돌아간다()
        {
            List<WallPiece> all = WallRing.Layout(new WorldXZ(0f, 0f), 10f, 4f, 1);
            WallPiece front = all.First(p => p.Kind == WallPieceKind.Straight && p.Center.Z < -9f);
            WallPiece right = all.First(p => p.Kind == WallPieceKind.Straight && p.Center.X > 9f);

            Assert.That(front.YawDegrees, Is.EqualTo(180f));
            Assert.That(right.YawDegrees, Is.EqualTo(90f));
        }

        [Test]
        public void 본부_레벨에_맞는_단계를_고르고_숫자가_어긋나면_알린다()
        {
            var config = new WallConfig(4f);
            config.Add(new WallTier(1, 18f));
            config.Add(new WallTier(3, 22f));

            Assert.That(config.ForHq(0), Is.Null);
            Assert.That(config.ForHq(2).HalfSize, Is.EqualTo(18f));
            Assert.That(config.ForHq(9).HalfSize, Is.EqualTo(22f));
            Assert.That(config.Validate(), Is.Empty);

            config.Add(new WallTier(4, 23f));   // 한 변 46m — 4의 배수가 아니다
            Assert.That(config.Validate().Count, Is.EqualTo(1));
        }
    }
}
