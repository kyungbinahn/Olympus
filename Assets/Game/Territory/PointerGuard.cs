using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 포인터가 지금 UI 위에 있는지. 카메라 팬(<see cref="TerritoryCamera"/>)과 탭 선택
    /// (<see cref="TerritoryPresenter"/>) 둘 다 이걸로 HUD 위에서 일어난 입력을 걸러낸다 —
    /// 안 그러면 복구 버튼을 누르는 손가락이 그 아래 지면도 같이 팬·선택 판정을 받는다.
    ///
    /// <see cref="Touchscreen.touches"/>는 손가락 슬롯 10개짜리 고정 배열이라 닿지 않아도
    /// Count가 항상 10이다 — touches[0]을 "그 손가락"이라고 가정하면 실제 손가락이 다른
    /// 슬롯에 잡혔을 때 조용히 틀린 판정을 준다. 눌려 있는 슬롯을 직접 찾는다.
    /// </summary>
    internal static class PointerGuard
    {
        public static bool IsOverUI()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return false;

            Touchscreen touch = Touchscreen.current;
            if (touch != null)
            {
                for (int i = 0; i < touch.touches.Count; i++)
                {
                    TouchControl t = touch.touches[i];
                    if (t.press.isPressed)
                        return es.IsPointerOverGameObject(t.touchId.ReadValue());
                }

                return false;
            }

            return es.IsPointerOverGameObject();
        }
    }
}
