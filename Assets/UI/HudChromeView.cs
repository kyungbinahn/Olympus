using UnityEngine;
using UnityEngine.UI;
using Olympus.Game;

namespace Olympus.UI
{
    /// <summary>
    /// 하단 내비바 + 설정 버튼 — SLG 화면의 "껍데기"다. 상점·동맹·영웅·가방·설정은
    /// 그 기능 자체가 아직 없어서 눌러도 토스트만 뜬다.
    ///
    /// 외출(월드맵)만 실제로 동작한다 — 마일스톤 ③이 월드맵이라 그 화면이 먼저 생겼다.
    /// 나머지도 각 기능이 생기면 이 클래스의 onClick 배선만 바꾸면 된다
    /// (버튼 배치·모양은 그대로 재사용된다).
    /// </summary>
    public sealed class HudChromeView : MonoBehaviour
    {
        [SerializeField] private ToastView _toast;

        [SerializeField] private Button _exploreButton;
        [SerializeField] private Button _heroesButton;
        [SerializeField] private Button _allianceButton;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _bagButton;
        [SerializeField] private Button _worldMapButton;
        [SerializeField] private Button _settingsButton;

        private void Start()
        {
            Wire(_exploreButton, "탐색 — 준비중입니다");
            Wire(_heroesButton, "영웅 — 준비중입니다");
            Wire(_allianceButton, "동맹 — 준비중입니다");
            Wire(_shopButton, "상점 — 준비중입니다");
            Wire(_bagButton, "가방 — 준비중입니다");
            Wire(_settingsButton, "설정 — 준비중입니다");

            if (_worldMapButton != null)
                _worldMapButton.onClick.AddListener(SceneRouter.GoToWorld);
        }

        private void Wire(Button button, string message)
        {
            if (button == null)
                return;

            button.onClick.AddListener(() => _toast.Show(message));
        }
    }
}
