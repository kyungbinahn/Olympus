using System.Text;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 선택한 자리 정보 패널. TerritoryPresenter.SelectionChanged와 StateStore.Changed를
    /// 듣기만 한다 — 복구·업그레이드·수확·가속은 Core에 위임하고, 결과는 다시 통지로
    /// 돌아온다. 이 패널이 직접 상태를 고치는 경로는 없다.
    ///
    /// 무엇을 보여 줄지의 판단은 Core의 <see cref="SlotPanel"/>이 끝내 두고, 여기서는 글자로만 바꾼다.
    /// </summary>
    public sealed class BuildingInfoPanelView : MonoBehaviour
    {
        private const string OkColor = "#9FD88A";
        private const string NgColor = "#F07A6E";

        [SerializeField] private TerritoryPresenter _presenter;

        [Header("뷰")]
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _costText;
        [SerializeField] private TMP_Text _messageText;

        [Tooltip("복구 / 업그레이드 / 승급 — 지금 상태에 맞는 한 가지를 한다.")]
        [SerializeField] private Button _repairButton;
        [SerializeField] private TMP_Text _repairButtonLabel;

        [SerializeField] private Button _collectButton;
        [SerializeField] private TMP_Text _collectButtonLabel;

        [Tooltip("가속 버튼들. progression.json의 speedUps 순서대로 짝을 짓는다 — 남는 버튼은 숨긴다.")]
        [SerializeField] private List<Button> _speedUpButtons = new List<Button>();
        [SerializeField] private List<TMP_Text> _speedUpLabels = new List<TMP_Text>();

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
            _repairButton.onClick.AddListener(OnActionClicked);

            if (_collectButton != null)
                _collectButton.onClick.AddListener(OnCollectClicked);

            for (int i = 0; i < _speedUpButtons.Count; i++)
            {
                int index = i;   // 람다가 루프 변수를 물면 전부 마지막 값이 된다.
                _speedUpButtons[i].onClick.AddListener(() => OnSpeedUpClicked(index));
            }

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

        private void OnSelectionChanged(int slotId)
        {
            _messageText.text = "";
            Refresh();
        }

        private void OnStateChanged(StateDelta delta) => Refresh();

        private float _nextLiveRefresh;

        private void Update()
        {
            // 공사 남은 시간과 생산 쌓인 양은 상태 변경 없이도 시간이 지나며 바뀐다.
            // 패널이 켜져 있을 때만, 그리고 0.25초마다만 다시 그린다 — 매 프레임 문자열을 새로
            // 만들면 쓰레기가 쌓인다.
            if (!_root.activeSelf || Time.unscaledTime < _nextLiveRefresh)
                return;

            _nextLiveRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            SlotPanel panel;
            if (!SlotPanel.TryDescribe(_presenter.Runtime, _presenter.SelectedSlotId, out panel))
            {
                _root.SetActive(false);
                return;
            }

            _root.SetActive(true);

            BuildingStatusView s = panel.Status;
            string name = _presenter.Strings.Get(s.DisplayNameKey);
            _nameText.text = s.Level > 0 ? name + "  Lv." + s.Level : name;

            switch (s.Phase)
            {
                case BuildingPhase.Ruined:
                    ShowRuined(panel);
                    break;

                case BuildingPhase.Constructing:
                    ShowConstructing(panel);
                    break;

                default:
                    ShowComplete(panel);
                    break;
            }
        }

        private void ShowRuined(SlotPanel panel)
        {
            if (panel.IsLocked)
            {
                _statusText.text = "잠김 — 본부 Lv." + panel.UnlockHqLevel + "에 열립니다";
                _costText.text = ProducesLine(panel, true);
                SetAction(false, false, "");
            }
            else
            {
                _statusText.text = "폐허";

                var sb = new StringBuilder();
                sb.Append("복구 비용  ").Append(FormatCosts(panel.Status.RepairCosts));
                string produces = ProducesLine(panel, true);
                if (produces.Length > 0)
                    sb.Append('\n').Append(produces);

                _costText.text = sb.ToString();
                SetAction(true, panel.RepairCheck.Started, "복구");
            }

            SetCollect(false, "");
            SetSpeedUps(false);
        }

        private void ShowConstructing(SlotPanel panel)
        {
            _statusText.text = "공사 중  " + FormatDuration(panel.Status.RemainingConstructionMs);
            _costText.text = "헤르메스의 날개로 앞당길 수 있습니다";
            SetAction(false, false, "");
            SetCollect(false, "");
            SetSpeedUps(true);
        }

        private void ShowComplete(SlotPanel panel)
        {
            _statusText.text = panel.IsProducer
                ? ProducesLine(panel, false)
                : "완성";

            UpgradePlan plan = panel.Upgrade;
            var sb = new StringBuilder();

            switch (plan.Rejection)
            {
                case BuildRejection.MaxLevel:
                    sb.Append("최고 레벨입니다");
                    break;

                case BuildRejection.HqLevelTooLow:
                    sb.Append("Lv.").Append(plan.TargetLevel).Append("로 올리려면 ")
                      .Append(Colored("본부 Lv." + plan.RequiredHqLevel, NgColor)).Append("이 필요합니다");
                    break;

                default:
                    // 본부는 조건 줄을 먼저 — "무엇이 모자라 못 올리나"가 비용보다 중요하다.
                    for (int i = 0; i < plan.Requirements.Count; i++)
                    {
                        sb.Append(FormatRequirement(plan.Requirements[i])).Append('\n');
                    }

                    sb.Append("Lv.").Append(plan.TargetLevel).Append("  ")
                      .Append(FormatCosts(plan.Costs))
                      .Append("  · ").Append(FormatDuration(plan.DurationMs));
                    break;
            }

            _costText.text = sb.ToString();

            bool showAction = plan.Rejection != BuildRejection.MaxLevel && plan.Rejection != BuildRejection.HqLevelTooLow;
            SetAction(showAction, plan.CanStart, panel.IsHq ? "승급" : "업그레이드");

            SetCollect(panel.IsProducer, panel.CanCollect
                ? "수확 +" + panel.Stored
                : "수확");

            if (_collectButton != null)
                _collectButton.interactable = panel.CanCollect;

            SetSpeedUps(false);
        }

        private string FormatRequirement(RequirementStatus rs)
        {
            string label;
            switch (rs.Requirement.Kind)
            {
                case RequirementKind.Prosperity:
                    label = "번영도";
                    break;

                case RequirementKind.BuildingLevel:
                    BuildingDef def;
                    label = _presenter.Runtime.Catalog.TryGet(rs.Requirement.Target, out def)
                        ? _presenter.Strings.Get(def.DisplayNameKey) + " Lv."
                        : rs.Requirement.Target;
                    break;

                default:
                    label = rs.Requirement.Target;
                    break;
            }

            string line = label + " " + rs.Current + "/" + rs.Target;
            return rs.IsMet ? Colored("충족  " + line, OkColor) : Colored("미달  " + line, NgColor);
        }

        private static string ProducesLine(SlotPanel panel, bool beforeRepair)
        {
            if (!panel.IsProducer)
                return "";

            string kind = ResourceLabels.Of(panel.ProducesKind);

            if (beforeRepair)
                return "복구하면 " + kind + " 분당 " + panel.BaseRatePerMinute + " 생산";

            return kind + " 분당 " + panel.RatePerMinute + "  ·  쌓임 " + panel.Stored + "/" + panel.Capacity;
        }

        private void SetAction(bool visible, bool interactable, string label)
        {
            _repairButton.gameObject.SetActive(visible);
            _repairButton.interactable = interactable;
            if (visible)
                _repairButtonLabel.text = label;
        }

        private void SetCollect(bool visible, string label)
        {
            if (_collectButton == null)
                return;

            _collectButton.gameObject.SetActive(visible);
            if (visible)
                _collectButtonLabel.text = label;
        }

        private void SetSpeedUps(bool visible)
        {
            IReadOnlyList<SpeedUpItemDef> items = _presenter.Runtime.Progression.SpeedUps;
            CountPool bag = _presenter.Runtime.State.Items;

            for (int i = 0; i < _speedUpButtons.Count; i++)
            {
                bool has = visible && i < items.Count;
                _speedUpButtons[i].gameObject.SetActive(has);

                if (!has)
                    continue;

                long count = bag.Get(items[i].ItemId);
                _speedUpButtons[i].interactable = count > 0;

                if (i < _speedUpLabels.Count)
                    _speedUpLabels[i].text = ShortDuration(items[i].ReduceMs) + "\n×" + count;
            }
        }

        private void OnActionClicked()
        {
            SlotPanel panel;
            if (!SlotPanel.TryDescribe(_presenter.Runtime, _presenter.SelectedSlotId, out panel))
                return;

            BuildResult result = panel.Status.Phase == BuildingPhase.Ruined
                ? _presenter.TryRepairSelected()
                : _presenter.TryUpgradeSelected();

            _messageText.text = result.Started ? "" : TerritoryPresenter.DescribeRejection(result.Rejection);
            Refresh();
        }

        private void OnCollectClicked()
        {
            CollectRejection rejection;
            long got = _presenter.TryCollectSelected(out rejection);

            _messageText.text = rejection == CollectRejection.None ? "" : DescribeCollect(rejection);
            if (got > 0)
                Debug.Log("수확 +" + got);

            Refresh();
        }

        private void OnSpeedUpClicked(int index)
        {
            IReadOnlyList<SpeedUpItemDef> items = _presenter.Runtime.Progression.SpeedUps;
            if (index >= items.Count)
                return;

            SpeedUpRejection r = _presenter.TrySpeedUpSelected(items[index].ItemId);
            _messageText.text = r == SpeedUpRejection.None ? "" : DescribeSpeedUp(r);
            Refresh();
        }

        private static string DescribeCollect(CollectRejection r)
        {
            switch (r)
            {
                case CollectRejection.NothingStored: return "아직 쌓인 것이 없습니다";
                case CollectRejection.NotComplete: return "공사 중에는 생산이 멈춥니다";
                case CollectRejection.NotProducer: return "생산하는 건물이 아닙니다";
                default: return r.ToString();
            }
        }

        private static string DescribeSpeedUp(SpeedUpRejection r)
        {
            switch (r)
            {
                case SpeedUpRejection.NoItem: return "헤르메스의 날개가 없습니다";
                case SpeedUpRejection.NotConstructing: return "공사 중이 아닙니다";
                default: return r.ToString();
            }
        }

        private static string Colored(string text, string hex) => "<color=" + hex + ">" + text + "</color>";

        private static string FormatDuration(long ms)
        {
            if (ms <= 0L)
                return "즉시";

            long totalSeconds = (ms + 999L) / 1000L;
            long h = totalSeconds / 3600L;
            long m = totalSeconds % 3600L / 60L;
            long s = totalSeconds % 60L;

            return h > 0
                ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                : m.ToString("00") + ":" + s.ToString("00");
        }

        private static string ShortDuration(long ms)
        {
            long minutes = ms / 60000L;
            return minutes >= 60 ? (minutes / 60) + "시간" : minutes + "분";
        }

        private static string FormatCosts(IReadOnlyList<CostLine> costs)
        {
            if (costs == null || costs.Count == 0)
                return "무료";

            var sb = new StringBuilder();

            for (int i = 0; i < costs.Count; i++)
            {
                if (i > 0)
                    sb.Append("  ");

                CostLine c = costs[i];
                string text = ResourceLabels.Of(c.Kind) + " " + c.Amount;
                sb.Append(c.Affordable ? text : Colored(text + "(부족)", NgColor));
            }

            return sb.ToString();
        }
    }
}
