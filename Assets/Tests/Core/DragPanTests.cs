using NUnit.Framework;
using Olympus.Core.Grid;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 드래그 팬 회귀 시험. 화면으로만 보고 있었던 탓에 같은 버그를 두 번 놓쳤다
    /// (2026-09-10 "드르르르륵 버벅임", 2026-09-11 고쳤다고 보고했는데 여전히 버벅임).
    ///
    /// 핵심은 <b>카메라가 초점을 따라 움직인다</b>는 되먹임을 시험이 흉내 내는 것이다.
    /// 그래야 "한 프레임만 계산해 보면 맞는데 여러 프레임 돌리면 틀리는" 이 버그가 잡힌다.
    /// </summary>
    public class DragPanTests
    {
        /// <summary>
        /// 카메라 흉내. 회전 없이 평행 이동만 하는 카메라에서는 화면 한 점이 가리키는
        /// 지면 좌표가 <c>초점 + 화면오프셋</c>이다 — 실제 카메라가 그렇게 동작한다.
        /// </summary>
        private static WorldXZ Project(WorldXZ focus, WorldXZ screenOffset)
        {
            return focus + screenOffset;
        }

        [Test]
        public void 손가락이_멈춰_있으면_초점도_그대로다()
        {
            // ★이 시험이 그 버그를 잡는다. 틀린 방식에서는 손가락을 멈춘 순간
            // 초점이 드래그 시작점으로 튕겨 돌아갔다.
            var pan = new DragPan();

            var focus = new WorldXZ(100f, 200f);
            var grabScreen = new WorldXZ(5f, -3f);

            pan.Begin(focus);

            for (int frame = 0; frame < 10; frame++)
            {
                // 매 프레임 "지금 카메라"로 두 점을 다시 투영한다.
                WorldXZ grabPoint = Project(focus, grabScreen);
                WorldXZ pointerPoint = Project(focus, grabScreen);   // 손가락이 안 움직였다

                focus = pan.FocusFor(grabPoint, pointerPoint);

                Assert.That(focus.X, Is.EqualTo(100f).Within(0.0001f), "프레임 " + frame);
                Assert.That(focus.Z, Is.EqualTo(200f).Within(0.0001f), "프레임 " + frame);
            }
        }

        [Test]
        public void 한_방향으로_끌면_총_이동량이_그대로_반영된다()
        {
            var pan = new DragPan();

            var focus = new WorldXZ(0f, 0f);
            var grabScreen = new WorldXZ(0f, 0f);

            pan.Begin(focus);

            // 손가락이 화면에서 오른쪽으로 10씩 세 프레임 움직인다.
            for (int i = 1; i <= 3; i++)
            {
                var pointerScreen = new WorldXZ(10f * i, 0f);

                focus = pan.FocusFor(
                    Project(focus, grabScreen),
                    Project(focus, pointerScreen));

                // 잡은 지점이 손가락을 따라가려면 초점은 반대로 그만큼 가야 한다.
                Assert.That(focus.X, Is.EqualTo(-10f * i).Within(0.0001f),
                    i + "번째 프레임 — 총 이동량이 반영돼야 한다");
            }
        }

        [Test]
        public void 갔다가_돌아오면_초점도_제자리로_돌아온다()
        {
            // 누적 오차가 있으면 여기서 드러난다.
            var pan = new DragPan();

            var focus = new WorldXZ(50f, 50f);
            var grabScreen = new WorldXZ(0f, 0f);

            pan.Begin(focus);

            float[] path = { 5f, 20f, 40f, 20f, 5f, 0f };

            foreach (float offset in path)
            {
                focus = pan.FocusFor(
                    Project(focus, grabScreen),
                    Project(focus, new WorldXZ(offset, 0f)));
            }

            Assert.That(focus.X, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(focus.Z, Is.EqualTo(50f).Within(0.0001f));
        }

        [Test]
        public void 드래그를_다시_시작하면_그_자리가_새_기준이_된다()
        {
            var pan = new DragPan();

            var focus = new WorldXZ(0f, 0f);
            var grabScreen = new WorldXZ(0f, 0f);

            pan.Begin(focus);
            focus = pan.FocusFor(Project(focus, grabScreen), Project(focus, new WorldXZ(30f, 0f)));
            Assert.That(focus.X, Is.EqualTo(-30f).Within(0.0001f));

            pan.End();

            // 손을 뗐다가 다시 잡으면 지금 초점이 새 기준이다 — 이전 드래그가 되살아나면 안 된다.
            pan.Begin(focus);
            focus = pan.FocusFor(Project(focus, grabScreen), Project(focus, new WorldXZ(10f, 0f)));

            Assert.That(focus.X, Is.EqualTo(-40f).Within(0.0001f));
        }

        [Test]
        public void Begin_없이_부르면_터진다()
        {
            var pan = new DragPan();

            Assert.That(
                () => pan.FocusFor(new WorldXZ(0f, 0f), new WorldXZ(0f, 0f)),
                Throws.InvalidOperationException);
        }
    }
}
