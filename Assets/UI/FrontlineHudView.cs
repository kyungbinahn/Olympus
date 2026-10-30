using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Chapter;
using Olympus.Core.Progression;
using Olympus.Game.Chapter;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 성문 밖 상시 전선(탑포스식) — 위협 띠 아래 얇은 한 줄: 적 기지 이름 · 체력 바 · "무너지기까지 m:ss".
    /// 화력이 버팀보다 약하면 바 대신 빨간 경고("밀리고 있다")를 띄운다. 누르면 카메라가 그 기지로 간다.
    /// 기지가 무너지면 이야기 한 줄 + 보상을 알림으로 띄운다.
    ///
    /// 판단(얼마나 깎였나·언제 무너지나)은 전부 Core(<see cref="FrontlineService"/>)에 있고 여기서는 읽기만 한다.
    /// 남은 시간은 상태 통지 없이도 매 초 바뀌므로 0.25초마다 다시 쓴다(<see cref="ChapterHudView"/>와 같은 방식).
    /// </summary>
    public sealed class FrontlineHudView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private ToastView _toast;

        [SerializeField] private GameObject _root;
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _nameText;
        [Tooltip("체력 바 묶음(바탕+채움). 밀릴 때는 숨기고 그 자리에 경고를 쓴다.")]
        [SerializeField] private GameObject _barRoot;
        [Tooltip("남은 체력 채움 — 오른쪽 앵커(anchorMax.x)를 비율로 움직인다. 늘림(9-slice) 그림이라 Filled를 못 쓴다.")]
        [SerializeField] private RectTransform _barFill;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _warningText;

        [Tooltip("기지가 무너졌을 때 알림을 띄워 두는 시간(초). 이야기 한 줄 + 보상을 읽을 시간이다.")]
        [SerializeField] private float _destroyedToastSeconds = 5.5f;

        private float _nextRefresh;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null || _presenter.Runtime.Frontline == null)
            {
                SetActive(_root, false);
                enabled = false;
                return;
            }

            if (_button != null)
                _button.onClick.AddListener(OnClicked);

            _presenter.Runtime.Frontline.OutpostDestroyed += OnOutpostDestroyed;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_presenter != null && _presenter.Runtime != null && _presenter.Runtime.Frontline != null)
                _presenter.Runtime.Frontline.OutpostDestroyed -= OnOutpostDestroyed;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            FrontView v = _presenter.Runtime.Frontline.Describe();

            // 다 무너뜨렸거나(다음 장 전) 전선이 없는 장이면 줄 자체를 거둔다.
            SetActive(_root, v.HasOutpost);
            if (!v.HasOutpost)
                return;

            SetText(_nameText, _presenter.Strings.Get(v.Outpost.NameKey));

            if (v.Waiting)
            {
                SetActive(_barRoot, true);
                SetActive(_warningText, false);
                SetBar(1f - v.Fraction);
                SetText(_statusText, "본부 Lv." + v.Outpost.MinHqLevel + "부터 공격");
                return;
            }

            // 밀릴 때는 "몇 분 남았나"가 아니라 "무엇을 해야 하나"가 할 말이다 — 바를 비키고 경고를 크게.
            SetActive(_barRoot, !v.Stalled);
            SetActive(_statusText, !v.Stalled);
            SetActive(_warningText, v.Stalled);

            if (v.Stalled)
            {
                SetText(_warningText, "밀리고 있다 — 병영·훈련장을 올려라");
                return;
            }

            SetBar(1f - v.Fraction);
            SetText(_statusText, "무너지기까지 " + Clock(v.RemainingMs));
        }

        private void OnClicked()
        {
            var director = Object.FindFirstObjectByType<FrontlineDirector>();
            if (director != null)
                director.FocusOutpost();
        }

        private void OnOutpostDestroyed(OutpostDef outpost)
        {
            if (_toast == null || outpost == null)
                return;

            var sb = new StringBuilder();
            sb.Append(_presenter.Strings.Get(outpost.NameKey)).Append(" 함락!");

            if (!string.IsNullOrEmpty(outpost.StoryKey))
                sb.Append('\n').Append(_presenter.Strings.Get(outpost.StoryKey));

            string reward = RewardText(outpost);
            if (reward.Length > 0)
                sb.Append('\n').Append(reward);

            _toast.Show(sb.ToString(), _destroyedToastSeconds);
            Refresh();
        }

        private string RewardText(OutpostDef outpost)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < outpost.RewardResources.Count; i++)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(ResourceLabels.Of(outpost.RewardResources[i].Kind)).Append(" +").Append(outpost.RewardResources[i].Amount);
            }

            for (int i = 0; i < outpost.RewardItems.Count; i++)
            {
                if (sb.Length > 0) sb.Append("  ");
                SpeedUpItemDef item;
                string name = _presenter.Runtime.Progression.TryGetSpeedUp(outpost.RewardItems[i].ItemId, out item)
                    ? _presenter.Strings.Get(item.DisplayNameKey)
                    : outpost.RewardItems[i].ItemId;
                sb.Append(name).Append(" +").Append(outpost.RewardItems[i].Amount);
            }

            return sb.ToString();
        }

        private void SetBar(float fraction)
        {
            if (_barFill == null)
                return;

            fraction = Mathf.Clamp01(fraction);
            // 채움 그림은 양 끝이 둥근 알약이라 너무 짧으면 찌그러진다 — 거의 0이면 아예 숨긴다.
            bool show = fraction > 0.02f;
            if (_barFill.gameObject.activeSelf != show)
                _barFill.gameObject.SetActive(show);
            _barFill.anchorMax = new Vector2(fraction, _barFill.anchorMax.y);
        }

        private static string Clock(long ms)
        {
            long s = System.Math.Max(0L, (ms + 999) / 1000);
            return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00")
                             : (s / 60) + ":" + (s % 60).ToString("00");
        }

        private static void SetText(TMP_Text t, string value)
        {
            if (t != null && t.text != value)
                t.text = value;
        }

        private static void SetActive(Component c, bool active)
        {
            if (c != null && c.gameObject.activeSelf != active)
                c.gameObject.SetActive(active);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }
    }
}
