using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 하단 임무 바 — 딱 한 줄. 탑포스의 퀘스트 바를 옮긴 것이다.
    ///
    /// 바를 누르면 그 임무의 건물로 카메라가 날아가고 선택된다. 목표를 채우면 "받기" 버튼이
    /// 켜지고, 누르면 보상이 들어오며 바로 다음 임무가 뜬다. 판단(달성했나·무엇을 주나)은
    /// 전부 Core(<see cref="QuestService"/>)에 있고 여기서는 보여 주고 전달만 한다.
    /// </summary>
    public sealed class QuestBarView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private ToastView _toast;

        [SerializeField] private Button _barButton;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private Button _claimButton;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                Debug.LogError("QuestBarView가 TerritoryPresenter에 배선되지 않았다.");
                enabled = false;
                return;
            }

            _presenter.Runtime.Store.Changed += OnStateChanged;
            _barButton.onClick.AddListener(OnBarClicked);
            _claimButton.onClick.AddListener(OnClaimClicked);

            Refresh();
        }

        private void OnDestroy()
        {
            if (_presenter != null && _presenter.Runtime != null)
                _presenter.Runtime.Store.Changed -= OnStateChanged;
        }

        private void OnStateChanged(StateDelta delta) => Refresh();

        private void Refresh()
        {
            QuestView view = _presenter.Runtime.Quests.Current;

            if (!view.HasQuest)
            {
                _titleText.text = "모든 임무를 마쳤습니다";
                _progressText.text = "다음 이야기는 준비중입니다";
                _claimButton.gameObject.SetActive(false);
                return;
            }

            _titleText.text = _presenter.Strings.Get(view.Quest.TitleKey);

            // 0/1처럼 "했나 안 했나"뿐인 목표는 숫자 대신 말로 — "0/1"은 목표가 아니라 오류처럼 보인다.
            _progressText.text = view.Progress.Target <= 1
                ? (view.CanClaim ? "완료!" : "임무 " + view.Number + "/" + view.Total)
                : view.Progress.Current + "/" + view.Progress.Target + "   ·   임무 " + view.Number + "/" + view.Total;

            _claimButton.gameObject.SetActive(true);
            _claimButton.interactable = view.CanClaim;
        }

        private void OnBarClicked()
        {
            QuestView view = _presenter.Runtime.Quests.Current;
            if (!view.HasQuest)
                return;

            // 전투 임무는 건물이 아니라 전선 화면으로 안내한다.
            if (view.Quest.Objective.Kind == RequirementKind.Counter
                && view.Quest.Objective.Target == Olympus.Core.Battle.BattleService.StageCounter)
            {
                if (_presenter.Runtime.Battles.IsUnlocked)
                    Olympus.Game.SceneRouter.GoToBattle();
                return;
            }

            _presenter.FocusOnDef(view.Quest.FocusDefId);
        }

        private void OnClaimClicked()
        {
            QuestView before = _presenter.Runtime.Quests.Current;

            if (_presenter.Runtime.Quests.TryClaim() != QuestClaimRejection.None)
                return;

            if (_toast != null)
                _toast.Show("임무 완료 — " + RewardText(before.Quest));
        }

        private string RewardText(QuestDef quest)
        {
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < quest.RewardResources.Count; i++)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(ResourceLabels.Of(quest.RewardResources[i].Kind)).Append(" +").Append(quest.RewardResources[i].Amount);
            }

            for (int i = 0; i < quest.RewardItems.Count; i++)
            {
                if (sb.Length > 0) sb.Append("  ");
                SpeedUpItemDef item;
                string name = _presenter.Runtime.Progression.TryGetSpeedUp(quest.RewardItems[i].ItemId, out item)
                    ? _presenter.Strings.Get(item.DisplayNameKey)
                    : quest.RewardItems[i].ItemId;
                sb.Append(name).Append(" +").Append(quest.RewardItems[i].Amount);
            }

            return sb.Length > 0 ? sb.ToString() : "보상 없음";
        }
    }
}
