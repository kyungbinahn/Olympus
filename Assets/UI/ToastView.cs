using TMPro;
using UnityEngine;

namespace Olympus.UI
{
    /// <summary>
    /// 잠깐 떴다 사라지는 짧은 안내 문구. 아직 없는 기능 버튼(하단 내비바 등)을 눌렀을 때
    /// "준비중"을 말해주는 용도 — 그 버튼들 뒤에 진짜 화면이 생기면 onClick만 갈아끼우면 된다.
    /// </summary>
    public sealed class ToastView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _text;

        // 일시정지·배속과 무관하게 항상 같은 길이로 보이도록 unscaledTime을 쓴다.
        private float _hideAtUnscaledTime = -1f;

        private void Awake()
        {
            _root.SetActive(false);
        }

        public void Show(string message, float durationSeconds = 1.6f)
        {
            _text.text = message;
            _root.SetActive(true);
            _hideAtUnscaledTime = Time.unscaledTime + durationSeconds;
        }

        private void Update()
        {
            if (_hideAtUnscaledTime < 0f || Time.unscaledTime < _hideAtUnscaledTime)
                return;

            _root.SetActive(false);
            _hideAtUnscaledTime = -1f;
        }
    }
}
