using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 선택한 자리 정보 패널. TerritoryPresenter.SelectionChanged와 StateStore.Changed를
    /// 듣기만 한다 — 복구는 Core(ConstructionService)에 위임하고, 결과는 다시 통지로
    /// 돌아온다. 이 패널이 직접 상태를 고치는 경로는 없다.
    /// </summary>
    public sealed class BuildingInfoPanelView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;

        [Header("뷰")]
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _costText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _repairButton;
        [SerializeField] private TMP_Text _repairButtonLabel;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                Debug.LogError("BuildingInfoPanelView가 TerritoryPresenter에 배선되지 않았다.");
                enabled = false;
                return;
            }

            _presenter.SelectionChanged += OnSelectionChanged;
            _presenter.Runtime.Store.Changed += OnStateChanged;
            _repairButton.onClick.AddListener(OnRepairClicked);

            Refresh();
        }

        private void OnDestroy()
        {
            if (_presenter == null)
                return;

            _presenter.SelectionChanged -= OnSelectionChanged;

            if (_presenter.Runtime != null)
                _presenter.Runtime.Store.Changed -= OnStateChanged;
        }

        private void OnSelectionChanged(int slotId) => Refresh();

        private void OnStateChanged(StateDelta delta) => Refresh();

        private void Update()
        {
            // 공사 남은 시간은 상태 변경 없이도 매 프레임 줄어든다 — 패널이 켜져 있을 때만 갱신한다.
            if (!_root.activeSelf || _presenter.SelectedSlotId < 0)
                return;

            BuildingStatusView view;
            if (_presenter.TryGetStatus(_presenter.SelectedSlotId, out view)
                && view.Phase == BuildingPhase.Constructing)
            {
                _statusText.text = FormatRemaining(view.RemainingConstructionMs);
            }
        }

        private void Refresh()
        {
            _messageText.text = "";

            int slotId = _presenter.SelectedSlotId;
            BuildingStatusView view;

            if (slotId < 0 || !_presenter.TryGetStatus(slotId, out view))
            {
                _root.SetActive(false);
                return;
            }

            _root.SetActive(true);
            _nameText.text = _presenter.Strings.Get(view.DisplayNameKey);

            switch (view.Phase)
            {
                case BuildingPhase.Ruined:
                    _statusText.text = "폐허";
                    ShowCosts(view.RepairCosts);
                    ShowRepairButton(true, view.CanRepair);
                    break;

                case BuildingPhase.Constructing:
                    _statusText.text = FormatRemaining(view.RemainingConstructionMs);
                    ShowCosts(null);
                    ShowRepairButton(false, false);
                    break;

                default:
                    _statusText.text = "완성 Lv" + view.Level;
                    ShowCosts(null);
                    ShowRepairButton(false, false);
                    break;
            }
        }

        private void ShowCosts(IReadOnlyList<CostLine> costs)
        {
            bool hasCosts = costs != null && costs.Count > 0;
            _costText.gameObject.SetActive(hasCosts);

            if (hasCosts)
                _costText.text = FormatCosts(costs);
        }

        private void ShowRepairButton(bool visible, bool interactable)
        {
            _repairButton.gameObject.SetActive(visible);
            _repairButton.interactable = interactable;
            if (visible)
                _repairButtonLabel.text = "복구";
        }

        private void OnRepairClicked()
        {
            BuildResult result = _presenter.TryRepairSelected();

            if (!result.Started)
                _messageText.text = TerritoryPresenter.DescribeRejection(result.Rejection);

            Refresh();
        }

        private static string FormatRemaining(long ms)
        {
            long totalSeconds = (ms + 999L) / 1000L;
            long m = totalSeconds / 60L;
            long s = totalSeconds % 60L;
            return "공사 중 " + m.ToString("00") + ":" + s.ToString("00");
        }

        private static string FormatCosts(IReadOnlyList<CostLine> costs)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < costs.Count; i++)
            {
                if (i > 0)
                    sb.Append("   ");

                CostLine c = costs[i];
                sb.Append(ResourceLabels.Of(c.Kind)).Append(' ').Append(c.Amount);

                if (!c.Affordable)
                    sb.Append(" (부족)");
            }

            return sb.ToString();
        }
    }
}
