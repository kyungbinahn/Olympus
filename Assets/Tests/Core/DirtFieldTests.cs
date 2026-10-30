using NUnit.Framework;
using Olympus.Core.Grid;

namespace Olympus.Core.Tests
{
    public class DirtFieldTests
    {
        [Test]
        public void 돌리지_않은_사각형의_거리는_변까지의_거리다()
        {
            var p = new GroundPatch(new WorldXZ(0f, 0f), 2f, 1f, 0f);

            Assert.That(p.SignedDistance(new WorldXZ(0f, 0f)), Is.EqualTo(-1f).Within(1e-4f));   // 가까운 변(Z)까지 1
            Assert.That(p.SignedDistance(new WorldXZ(3f, 0f)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(p.SignedDistance(new WorldXZ(5f, 5f)), Is.EqualTo(5f).Within(1e-4f));    // 모서리(2,1)에서 (3,4)
        }

        [Test]
        public void 돌린_사각형은_Unity_회전과_같은_쪽으로_돈다()
        {
            // X로 긴(4 × 0.5) 사각형을 90° 돌리면 Z로 길어진다(Euler(0,90,0)은 +X를 -Z로 보낸다).
            var p = new GroundPatch(new WorldXZ(10f, 10f), 2f, 0.25f, 90f);

            Assert.That(p.SignedDistance(new WorldXZ(10f, 11.5f)), Is.LessThan(0f));
            Assert.That(p.SignedDistance(new WorldXZ(11.5f, 10f)), Is.GreaterThan(0f));
        }

        [Test]
        public void 굽힌_값은_거리_범위로_잘리고_되읽힌다()
        {
            Assert.That(DirtField.Encode(-5f), Is.EqualTo(0));
            Assert.That(DirtField.Encode(9f), Is.EqualTo(255));
            Assert.That(DirtField.Decode(DirtField.Encode(0.3f)), Is.EqualTo(0.3f).Within(0.01f));
        }

        [Test]
        public void 굽기는_겹친_사각형의_가까운_쪽을_남기고_먼_곳은_흙이_아니다()
        {
            int w = 20, h = 20;
            var pixels = new byte[w * h];
            // 텍셀 0.5, 칸 1 — (0,0)~(10,10) 영역.
            DirtField.Bake(pixels, w, h, new WorldXZ(0f, 0f), 0.5f, 1f, new[]
            {
                new GroundPatch(new WorldXZ(3f, 3f), 1f, 1f, 0f),
                new GroundPatch(new WorldXZ(4f, 3f), 1f, 1f, 45f),
            });

            float at(float x, float z) => DirtField.Decode(pixels[(int)(z / 0.5f) * w + (int)(x / 0.5f)]);

            Assert.That(at(3.25f, 3.25f), Is.LessThan(0f));                          // 두 사각형 안
            Assert.That(at(9.75f, 9.75f), Is.EqualTo(DirtField.FarCells).Within(0.01f));   // 멀리
            Assert.That(at(1.25f, 3.25f), Is.EqualTo(0.75f).Within(0.02f));          // 첫 사각형 왼변(x=2)에서 0.75
        }
    }
}
