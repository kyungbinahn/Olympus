using TMPro;
using UnityEngine;
using Olympus.Core.State;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 화면 상단 자원 표시. StateStore.Changed만 듣는다 — 상태를 직접 고치지 않는다.
    ///
    /// Start에서 구독하는 이유 — TerritoryPresenter.Awake가 Runtime을 만드는데,
    /// 서로 다른 게임 오브젝트라 Awake 순서가 보장되지 않는다. Unity는 같은 프레임의
    /// 모든 Awake가 끝난 뒤에 Start를 부르므로, 여기서는 Runtime이 이미 있다고 믿을 수 있다.
    /// </summary>
    public sealed class ResourceBarView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private TMP_Text _woodText;
        [SerializeField] private TMP_Text _stoneText;

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

        private void OnStateChanged(StateDelta delta)
        {
            if (delta.Resources == null)
                return;

            Refresh();
        }

        private void Refresh()
        {
            ResourcePool resources = _presenter.Runtime.State.Resources;

            _woodText.text = ResourceLabels.Of(ResourceKind.Wood) + " " + resources.Get(ResourceKind.Wood);
            _stoneText.text = ResourceLabels.Of(ResourceKind.Stone) + " " + resources.Get(ResourceKind.Stone);
        }
    }
}
