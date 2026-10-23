using TMPro;
using UnityEngine;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 화면 상단 자원 표시 + 본부 줄(레벨·번영도·공사 대기열). StateStore.Changed만 듣는다 —
    /// 상태를 직접 고치지 않는다.
    ///
    /// 식량은 <c>food</c>(본부 Lv.2), 금은 <c>gold</c>(Lv.5) 기능이 열려야 보인다. 탑포스가 Lv.5에서 상단 자원 바를
    /// "식량·철"로 바꾸는 순간과 같다 — 쓸 곳이 없는 자원을 처음부터 0으로 띄워 두면 그냥 소음이다.
    ///
    /// Start에서 구독하는 이유 — TerritoryPresenter.Awake가 Runtime을 만드는데,
    /// 서로 다른 게임 오브젝트라 Awake 순서가 보장되지 않는다. Unity는 같은 프레임의
    /// 모든 Awake가 끝난 뒤에 Start를 부르므로, 여기서는 Runtime이 이미 있다고 믿을 수 있다.
    /// </summary>
    public sealed class ResourceBarView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private TMP_Text _hqText;
        [SerializeField] private TMP_Text _woodText;
        [SerializeField] private TMP_Text _stoneText;
        [SerializeField] private TMP_Text _foodText;
        [SerializeField] private TMP_Text _goldText;

        [Tooltip("자원 이름(목재·석재…)을 숫자 앞에 쓴다. 아이콘이 붙으면 HUD 빌더가 끈다 — 아이콘과 이름이 겹치면 읽기만 느리다.")]
        [SerializeField] private bool _showLabels = true;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                Debug.LogError("ResourceBarView가 TerritoryPresenter에 배선되지 않았다.");
                enabled = false;
                return;
            }

            _presenter.Runtime.Store.Changed += OnStateChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_presenter != null && _presenter.Runtime != null)
                _presenter.Runtime.Store.Changed -= OnStateChanged;
        }

        // 건물 변경도 듣는다 — 본부 레벨·번영도·대기열이 건물에서 나온다.
        private void OnStateChanged(StateDelta delta) => Refresh();

        private void Refresh()
        {
            TerritoryRuntime rt = _presenter.Runtime;
            ResourcePool resources = rt.State.Resources;

            _woodText.text = Line(ResourceKind.Wood, resources);
            _stoneText.text = Line(ResourceKind.Stone, resources);

            // 식량은 거인이 보이는 본부 Lv.2부터(병사 훈련 재료), 금은 Lv.5부터 — function-unlock.json.
            SetLine(_foodText, rt.IsFeatureUnlocked("food"), Line(ResourceKind.Food, resources));
            SetLine(_goldText, rt.IsFeatureUnlocked("gold"), Line(ResourceKind.Gold, resources));

            if (_hqText != null)
                _hqText.text = HqLine(rt);
        }

        private string Line(ResourceKind kind, ResourcePool resources)
        {
            string value = resources.Get(kind).ToString("N0");
            return _showLabels ? ResourceLabels.Of(kind) + " " + value : value;
        }

        private static string HqLine(TerritoryRuntime rt)
        {
            string line = "본부 Lv." + rt.HqLevel + "   번영도 " + ProgressionRules.Prosperity(rt.State, rt.Progression);

            // 대기열은 한도가 생긴 뒤(Lv.4~)에만 보인다 — 한도가 없을 때 "공사 3/∞"는 의미가 없다.
            int slots = ProgressionRules.QueueSlots(rt.State, rt.Progression);
            if (slots > 0)
                line += "   공사 " + ProgressionRules.ConstructingCount(rt.State) + "/" + slots;

            return line;
        }

        private static void SetLine(TMP_Text text, bool visible, string value)
        {
            if (text == null)
                return;

            text.gameObject.SetActive(visible);
            if (visible)
                text.text = value;
        }
    }
}
