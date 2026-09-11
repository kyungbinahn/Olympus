using System;

namespace Olympus.Core.Grid
{
    /// <summary>
    /// "잡은 지점이 손가락에 붙어 따라오는" 드래그 팬 계산.
    ///
    /// <b>여기가 두 번 틀렸던 자리다(2026-09-10, 2026-09-11).</b> 틀린 방식은 이랬다 —
    /// 드래그를 시작할 때 손가락 아래의 지면 좌표를 <i>한 번</i> 재어 두고, 매 프레임
    /// 지금 손가락 아래 좌표와 뺐다. 문제는 지면 좌표를 구하는 데 쓰는 카메라가
    /// 그 사이에 이미 움직였다는 것이다. 카메라가 회전 없이 평행 이동만 하므로
    /// <c>지면좌표 = 초점 + 화면오프셋</c>인데, 초점이 바뀐 뒤에 다시 재면
    /// 그 차이가 "드래그 시작 이후 총 이동량"이 아니라 <b>"직전 프레임 대비 이동량"</b>이 된다.
    /// 그래서 카메라가 매 프레임 시작점으로 되돌아갔다가 한 프레임치만 나아가고,
    /// 손가락을 멈추면 초점이 드래그 시작점으로 통째로 튕겨 돌아간다 — 화면에서는
    /// "드르르르륵" 걸리는 것처럼 보인다.
    ///
    /// 고친 방식은 <b>두 점을 같은 프레임의 같은 카메라로 재는 것</b>이다.
    /// 그래서 <see cref="FocusFor"/>는 두 좌표를 모두 인자로 받는다 —
    /// 호출부가 매 프레임 둘 다 새로 투영하게 만들어서, 같은 실수를 다시 하기 어렵게 했다.
    /// </summary>
    public sealed class DragPan
    {
        private WorldXZ _startFocus;

        public bool IsDragging { get; private set; }

        /// <summary>드래그를 시작한다. 그 순간의 초점을 기준으로 삼는다.</summary>
        public void Begin(WorldXZ focusAtDragStart)
        {
            _startFocus = focusAtDragStart;
            IsDragging = true;
        }

        public void End()
        {
            IsDragging = false;
        }

        /// <summary>
        /// 이번 프레임의 초점.
        ///
        /// ⚠️ 두 인자 모두 <b>지금 카메라로, 이번 프레임에</b> 투영한 값이어야 한다.
        /// <paramref name="grabPoint"/>는 "드래그를 시작한 <i>화면 좌표</i>가 지금 가리키는 지면 점"이고,
        /// <paramref name="pointerPoint"/>는 "지금 손가락이 가리키는 지면 점"이다.
        /// 둘 중 하나라도 예전 카메라로 잰 값을 넣으면 위 주석의 버그가 그대로 재현된다.
        /// </summary>
        public WorldXZ FocusFor(WorldXZ grabPoint, WorldXZ pointerPoint)
        {
            if (!IsDragging)
                throw new InvalidOperationException("Begin 없이 FocusFor를 불렀다.");

            return _startFocus + (grabPoint - pointerPoint);
        }
    }
}
