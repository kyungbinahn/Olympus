using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 포인터(마우스·손가락) 판정을 한곳에 모아 둔 것. 기지 카메라·기지 탭·월드 카메라가
    /// 전부 이걸 쓴다.
    ///
    /// 왜 모아 두는가 — <see cref="Touchscreen.touches"/>는 손가락 슬롯 10개짜리
    /// <b>고정 배열</b>이라 아무도 화면에 닿지 않아도 Count가 항상 10이다. 이걸 모르고
    /// <c>touches[0]</c>을 "그 손가락"으로, <c>touches.Count</c>를 "손가락 수"로 쓰면
    /// 팬이 끊기고 탭이 안 먹는다. 실제로 안드로이드로 전환한 뒤 그 버그를 밟았고,
    /// 같은 로직이 세 파일에 복제돼 있어서 한 곳만 고치고 두 곳을 빠뜨렸다(2026-09-11).
    /// 그래서 판정은 여기 하나만 둔다.
    /// </summary>
    internal static class PointerGuard
    {
        /// <summary>
        /// 지금 포인터가 UI 위에 있는가. 이게 없으면 복구 버튼을 누르는 손가락이
        /// 그 아래 지면도 같이 팬·선택 판정을 받는다.
        /// </summary>
        public static bool IsOverUI()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return false;

            TouchControl active;
            if (TryGetActiveTouch(out active))
                return es.IsPointerOverGameObject(active.touchId.ReadValue());

            return es.IsPointerOverGameObject();
        }

        /// <summary>
        /// 지금 눌려 있는(또는 이번 프레임에 뗀) 첫 손가락. 터치 기기가 없거나
        /// 닿은 손가락이 없으면 false.
        ///
        /// 이번 프레임에 뗀 것도 포함하는 이유 — 떼는 순간의 위치가 탭 판정에 필요한데,
        /// 그 프레임에는 이미 isPressed가 false다.
        /// </summary>
        public static bool TryGetActiveTouch(out TouchControl active)
        {
            active = null;

            Touchscreen touch = Touchscreen.current;
            if (touch == null)
                return false;

            for (int i = 0; i < touch.touches.Count; i++)
            {
                TouchControl t = touch.touches[i];

                if (t.press.isPressed || t.press.wasReleasedThisFrame)
                {
                    active = t;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 눌려 있는 손가락을 <paramref name="buffer"/>에 채우고 총 개수를 돌려준다.
        /// 버퍼보다 많이 눌려 있어도 개수는 정확히 센다 — 핀치·팬 판정에는
        /// "하나인가 / 둘 이상인가"만 필요하다.
        /// </summary>
        public static int CollectActiveTouches(TouchControl[] buffer)
        {
            Touchscreen touch = Touchscreen.current;
            if (touch == null)
                return 0;

            int count = 0;

            for (int i = 0; i < touch.touches.Count; i++)
            {
                TouchControl t = touch.touches[i];
                if (!t.press.isPressed)
                    continue;

                if (buffer != null && count < buffer.Length)
                    buffer[count] = t;

                count++;
            }

            return count;
        }
    }
}
