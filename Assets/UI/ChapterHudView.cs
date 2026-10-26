using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Game.Chapter;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 1장 화면 — 상단 띠(거인 도착까지 · 용사 n/300 게이지 · 출정), 왼쪽 열 훈련 진행 칸, 병영 훈련 칸,
    /// 협곡 전투 중 배속·건너뛰기.
    ///
    /// 컨셉의 "시급성"은 이 띠가 맡는다: 남은 시간과 300이 늘 같이 보여야 "거인이 오기 전에 300"이 읽힌다.
    /// 그래서 띠는 정보 패널처럼 탭해야 뜨는 것이 아니라, 거인이 보이는 동안 늘 떠 있다.
    /// </summary>
    public sealed class ChapterHudView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private GiantThreatDirector _director;
        [SerializeField] private RaidDirector _raids;
        [SerializeField] private ToastView _toast;

        [Header("상단 띠")]
        [SerializeField] private GameObject _bannerRoot;
        [SerializeField] private Button _bannerButton;
        [Tooltip("작은 윗줄 — \"거인 도착까지\"")]
        [SerializeField] private TMP_Text _bannerCaption;
        [Tooltip("큰 숫자 — 남은 시간(전투 중엔 거인 체력 %)")]
        [SerializeField] private TMP_Text _bannerClock;
        [Tooltip("용사 n/300 + 게이지 묶음. 출정 버튼이 뜨면 그 자리를 비켜 준다.")]
        [SerializeField] private GameObject _troopRow;
        [SerializeField] private TMP_Text _troopText;
        [Tooltip("게이지 채움 — 오른쪽 앵커(anchorMax.x)를 비율로 움직인다. 늘림(9-slice) 그림이라 Filled를 못 쓴다.")]
        [SerializeField] private RectTransform _troopGaugeFill;
        [SerializeField] private Button _dispatchButton;
        [SerializeField] private TMP_Text _dispatchLabel;

        [Header("왼쪽 열 — 훈련 진행 칸")]
        [SerializeField] private GameObject _trainTile;
        [SerializeField] private Image _trainTileRing;
        [SerializeField] private TMP_Text _trainTileTime;

        [Header("병영 훈련")]
        [SerializeField] private GameObject _trainRoot;
        [SerializeField] private TMP_Text _trainInfo;
        [Tooltip("순서: 조금 / 많이 / 최대 / 날개")]
        [SerializeField] private Button[] _trainButtons = new Button[4];
        [SerializeField] private TMP_Text[] _trainLabels = new TMP_Text[4];

        [Header("협곡 전투")]
        [SerializeField] private GameObject _battleRoot;
        [SerializeField] private Button _speedButton;
        [SerializeField] private TMP_Text _speedLabel;
        [SerializeField] private Button _skipButton;

        private const int SmallBatch = 10;
        private const int LargeBatch = 50;

        private ThreatPhase _lastPhase = ThreatPhase.Defeated;
        private string _lastThreatId;
        private float _nextRefresh;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                enabled = false;
                return;
            }

            if (_bannerButton != null) _bannerButton.onClick.AddListener(OnBannerClicked);
            if (_dispatchButton != null) _dispatchButton.onClick.AddListener(OnDispatchClicked);
            if (_speedButton != null) _speedButton.onClick.AddListener(() => { _director.ToggleSpeed(); Refresh(); });
            if (_skipButton != null) _skipButton.onClick.AddListener(() => _director.Skip());

            for (int i = 0; i < _trainButtons.Length; i++)
            {
                int index = i;
                if (_trainButtons[i] != null)
                    _trainButtons[i].onClick.AddListener(() => OnTrainClicked(index));
            }

            if (_director != null)
                _director.BattleEnded += OnBattleEnded;

            if (_raids != null)
            {
                _raids.RaidSpotted += OnRaidSpotted;
                _raids.RaidEnded += OnRaidEnded;
            }

            _presenter.SelectionChanged += _ => Refresh();
            _presenter.Runtime.Store.Changed += _ => Refresh();

            // 처음 열었을 때 이미 오고 있던 거인은 "새로 나타났다"로 알리지 않는다.
            ThreatDef t = _presenter.Runtime.Threats.Current;
            if (t != null)
            {
                _lastThreatId = t.ThreatId;
                _lastPhase = _presenter.Runtime.Threats.Describe(t).Phase;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_director != null)
                _director.BattleEnded -= OnBattleEnded;

            if (_raids != null)
            {
                _raids.RaidSpotted -= OnRaidSpotted;
                _raids.RaidEnded -= OnRaidEnded;
            }
        }

        // ---------- 도적 습격 ----------

        private void OnRaidSpotted(RaidView v)
        {
            int count = 0;
            if (v.Wave != null)
                for (int i = 0; i < v.Wave.Enemies.Count; i++)
                    count += v.Wave.Enemies[i].Count;

            // 거인 이야기를 띄우는 중이면 덮지 않는다 — 큰 사건이 먼저다.
            if (_director != null && _director.Fighting)
                return;

            _toast?.Show("도적단 " + count + "명이 다가온다! 마을 수비대가 맞선다", 3f);
        }

        private void OnRaidEnded(RaidOutcome o)
        {
            if (!o.Resolved || (_director != null && _director.Fighting))
                return;

            var sb = new StringBuilder();
            sb.Append(o.Automatic ? "자리를 비운 사이 " : "");
            if (o.Won)
            {
                sb.Append("도적단을 물리쳤다!");
                for (int i = 0; i < o.Gained.Count; i++)
                    sb.Append(i == 0 ? "  " : " ").Append(ResourceLabels.Of(o.Gained[i].Kind)).Append(" +").Append(o.Gained[i].Amount);
            }
            else
            {
                sb.Append("도적단에게 밀렸다…");
                for (int i = 0; i < o.Lost.Count; i++)
                    sb.Append(i == 0 ? "  " : " ").Append(ResourceLabels.Of(o.Lost[i].Kind)).Append(" -").Append(o.Lost[i].Amount);
            }

            if (o.TroopsLost > 0)
                sb.Append("\n용사 ").Append(o.TroopsLost).Append("명을 잃었다");

            _toast?.Show(sb.ToString(), 3.5f);
            Refresh();
        }

        private void Update()
        {
            // 남은 시간·훈련 진행은 매 초 바뀐다 — 상태 통지가 없어도 다시 쓴다.
            if (Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            if (_presenter == null || _presenter.Runtime == null)
                return;

            RefreshBanner();
            RefreshTrainTile();
            RefreshTraining();
            RefreshBattle();
        }

        // ---------- 상단 띠 ----------

        private void RefreshBanner()
        {
            GiantThreatService threats = _presenter.Runtime.Threats;
            ThreatDef t = threats.Current;
            bool fighting = _director != null && _director.Fighting;

            if (t == null && !fighting)
            {
                SetActive(_bannerRoot, false);
                return;
            }

            ThreatView v = t != null ? threats.Describe(t) : default(ThreatView);
            AnnounceIfNew(t, v);

            if (!fighting && (t == null || v.Phase == ThreatPhase.Hidden || v.Phase == ThreatPhase.Defeated))
            {
                SetActive(_bannerRoot, false);
                return;
            }

            SetActive(_bannerRoot, true);
            TrainingView tv = _presenter.Runtime.Training.Describe();

            if (fighting)
            {
                BattleSimulation sim = _director.Simulation;
                int alive = sim != null ? sim.AliveCount(BattleTeam.Player) : 0;
                float hp = GiantHpFraction(sim);
                SetText(_bannerCaption, "협곡 전투 · 거인 체력");
                SetText(_bannerClock, Mathf.CeilToInt(hp * 100f) + "%");
                SetText(_troopText, "용사 " + alive + "명 싸우는 중" + (sim != null ? "  " + (sim.ElapsedMs / 1000) + "초" : ""));
                SetGauge(hp);
                SetActive(_troopRow, true);
                SetActive(_dispatchButton, false);
                return;
            }

            if (v.Phase == ThreatPhase.Arrived)
            {
                // 띠가 크림슨이라 경고색은 빨강이 아니라 금색으로 띄운다 — 빨강 위 빨강은 안 읽힌다.
                float left = _director != null ? _director.ArrivalCountdown : 0f;
                SetText(_bannerCaption, "<color=#FFD27A>거인이 협곡에 닿았다!</color>");
                SetText(_bannerClock, left > 0f ? Clock((long)(left * 1000f)) : "맞선다!");
            }
            else
            {
                SetText(_bannerCaption, v.Tries > 0 ? "거인이 다시 온다" : "거인 도착까지");
                SetText(_bannerClock, Clock(v.RemainingMs));
            }

            long visible = tv.VisibleTroops;
            long goal = v.Def.GoalTroops;
            bool full = visible >= goal;
            SetText(_troopText, "용사 " + (full ? "<color=#C8F5A0>" : "") + visible + "/" + goal + (full ? "</color>" : ""));
            SetGauge(goal > 0 ? (float)visible / goal : 0f);

            // 출정 버튼은 300이 찼을 때만 — 그때는 게이지가 할 말을 다 했으니 그 자리를 버튼에 내준다.
            bool canDispatch = v.Phase == ThreatPhase.Approaching && v.CanDispatch;
            SetActive(_troopRow, !canDispatch);
            SetActive(_dispatchButton, canDispatch);
            if (canDispatch)
                SetText(_dispatchLabel, "협곡으로 출정!");
        }

        private void SetGauge(float fraction)
        {
            if (_troopGaugeFill == null)
                return;

            fraction = Mathf.Clamp01(fraction);
            // 채움 그림은 양 끝이 둥근 알약이라 너무 짧으면 찌그러진다 — 거의 0이면 아예 숨긴다.
            bool show = fraction > 0.02f;
            if (_troopGaugeFill.gameObject.activeSelf != show)
                _troopGaugeFill.gameObject.SetActive(show);
            _troopGaugeFill.anchorMax = new Vector2(fraction, _troopGaugeFill.anchorMax.y);
        }

        /// <summary>왼쪽 열 훈련 칸 — 훈련이 도는 동안만. 병영을 누르지 않아도 "지금 기르는 중"이 보여야 한다.</summary>
        private void RefreshTrainTile()
        {
            if (_trainTile == null)
                return;

            TrainingService training = _presenter.Runtime.Training;
            TrainingView v = training.Def != null ? training.Describe() : default(TrainingView);
            bool show = training.Def != null && v.Running && !(_director != null && _director.Fighting);
            SetActive(_trainTile, show);
            if (!show)
                return;

            long now = _presenter.Runtime.Clock.NowUnixMs;
            long total = v.EndsAtUnixMs - v.StartedAtUnixMs;
            if (_trainTileRing != null)
                _trainTileRing.fillAmount = total > 0 ? Mathf.Clamp01((float)(now - v.StartedAtUnixMs) / total) : 1f;
            SetText(_trainTileTime, Clock(v.EndsAtUnixMs - now));
        }

        private void AnnounceIfNew(ThreatDef t, ThreatView v)
        {
            if (t == null)
                return;

            if (t.ThreatId != _lastThreatId)
            {
                _lastThreatId = t.ThreatId;
                _lastPhase = ThreatPhase.Hidden;
            }

            if (_lastPhase == ThreatPhase.Hidden && v.Phase == ThreatPhase.Approaching)
            {
                ShowStory(t.StartStoryKey, 6f);
                if (_director != null)
                    _director.FocusGiant();
            }

            _lastPhase = v.Phase;
        }

        private static float GiantHpFraction(BattleSimulation sim)
        {
            if (sim == null)
                return 0f;

            long hp = 0, max = 0;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                BattleUnit u = sim.Units[i];
                if (u.Team != BattleTeam.Enemy) continue;
                hp += u.Hp;
                max += u.MaxHp;
            }

            return max > 0 ? (float)hp / max : 0f;
        }

        private void OnBannerClicked()
        {
            if (_director != null)
                _director.FocusGiant();
        }

        private void OnDispatchClicked()
        {
            if (_director == null || !_director.Dispatch())
                _toast?.Show("아직 출정할 수 없습니다 — 용사가 모자랍니다");
        }

        private void OnBattleEnded(ThreatDef t, ThreatOutcome o)
        {
            string head = o.Won
                ? "승리! 용사 " + o.Sent + "명 중 " + o.Survivors + "명이 돌아왔다."
                : "패배… 용사 " + o.Sent + "명이 쓰러졌다.";
            string story = _presenter.Strings.Get(o.Won ? t.WinStoryKey : t.LoseStoryKey);
            _toast?.Show(head + "\n" + story, 7f);
            Refresh();
        }

        // ---------- 병영 훈련 ----------

        private void RefreshTraining()
        {
            TrainingService training = _presenter.Runtime.Training;
            TrainingDef def = training.Def;

            bool show = def != null && !(_director != null && _director.Fighting) && SelectedIsBarracks(def);
            SetActive(_trainRoot, show);
            if (!show)
                return;

            TrainingView v = training.Describe();
            var sb = new StringBuilder();
            sb.Append("<b>용사 훈련</b>  보유 ").Append(v.Troops).Append("명\n");

            if (v.Running)
            {
                sb.Append("훈련 중 ").Append(v.DoneSoFar).Append(" / ").Append(v.OrderCount).Append("명 — ")
                  .Append(Clock(v.EndsAtUnixMs - _presenter.Runtime.Clock.NowUnixMs)).Append(" 남음");
            }
            else if (v.Availability == TrainingRejection.GiantAtGate)
            {
                sb.Append("<color=#FF6A4D>거인이 닿았다 — 지금은 싸울 때다</color>");
            }
            else
            {
                sb.Append("1명: ").Append(CostLine(def)).Append(", ").Append((v.MsPerUnit / 1000.0).ToString("0.#")).Append("초")
                  .Append(" · 한 번에 ").Append(v.BatchCap).Append("명까지");
            }

            SetText(_trainInfo, sb.ToString());

            int max = training.MaxAffordable();
            int small = Mathf.Min(SmallBatch, v.BatchCap);
            int large = Mathf.Min(LargeBatch, v.BatchCap);

            SetTrainButton(0, "+" + small, !v.Running && training.CanStart(small) == TrainingRejection.None);
            SetTrainButton(1, "+" + large, !v.Running && large > small && training.CanStart(large) == TrainingRejection.None);
            SetTrainButton(2, "최대 " + max, !v.Running && max > 0);

            string wing = BestWing(v);
            SetTrainButton(3, wing == null ? "날개" : WingLabel(wing), v.Running && wing != null);
        }

        private bool SelectedIsBarracks(TrainingDef def)
        {
            BuildingInstance b;
            return _presenter.SelectedSlotId >= 0
                && _presenter.Runtime.State.TryGetBuilding(_presenter.SelectedSlotId, out b)
                && b.DefId == def.BarracksDefId
                && !b.IsRuined;
        }

        private void SetTrainButton(int index, string label, bool interactable)
        {
            if (index >= _trainButtons.Length || _trainButtons[index] == null)
                return;

            _trainButtons[index].interactable = interactable;
            if (index < _trainLabels.Length)
                SetText(_trainLabels[index], label);
        }

        /// <summary>남은 시간보다 크지 않은 날개 중 가장 큰 것 — 아끼는 쪽. 없으면 가진 것 중 가장 작은 것.</summary>
        private string BestWing(TrainingView v)
        {
            if (!v.Running)
                return null;

            long remaining = v.EndsAtUnixMs - _presenter.Runtime.Clock.NowUnixMs;
            string best = null, smallest = null;
            long bestMs = 0, smallestMs = long.MaxValue;

            foreach (SpeedUpItemDef s in _presenter.Runtime.Progression.SpeedUps)
            {
                if (_presenter.Runtime.State.Items.Get(s.ItemId) <= 0)
                    continue;

                if (s.ReduceMs <= remaining && s.ReduceMs > bestMs) { best = s.ItemId; bestMs = s.ReduceMs; }
                if (s.ReduceMs < smallestMs) { smallest = s.ItemId; smallestMs = s.ReduceMs; }
            }

            return best ?? smallest;
        }

        private string WingLabel(string itemId)
        {
            return _presenter.Strings.Get("item." + itemId).Replace("헤르메스의 ", "") +
                   " ×" + _presenter.Runtime.State.Items.Get(itemId);
        }

        private void OnTrainClicked(int index)
        {
            TrainingService training = _presenter.Runtime.Training;
            TrainingView v = training.Describe();
            TrainingRejection r;

            switch (index)
            {
                case 0: r = training.TryStart(Mathf.Min(SmallBatch, v.BatchCap)); break;
                case 1: r = training.TryStart(Mathf.Min(LargeBatch, v.BatchCap)); break;
                case 2: r = training.TryStart(training.MaxAffordable()); break;
                default:
                    string wing = BestWing(v);
                    r = wing == null ? TrainingRejection.NoItem : training.TrySpeedUp(wing);
                    break;
            }

            if (r != TrainingRejection.None)
                _toast?.Show(Describe(r));

            Refresh();
        }

        public static string Describe(TrainingRejection r)
        {
            switch (r)
            {
                case TrainingRejection.NoBarracks: return "병영을 먼저 되살려야 합니다";
                case TrainingRejection.Busy: return "이미 훈련 중입니다";
                case TrainingRejection.ZeroCount: return "기를 수 있는 용사가 없습니다 — 식량·목재를 모으세요";
                case TrainingRejection.OverCap: return "한 번에 맡길 수 있는 수를 넘었습니다 — 병영을 올리면 늘어납니다";
                case TrainingRejection.NotEnoughResources: return "식량·목재가 모자랍니다";
                case TrainingRejection.GiantAtGate: return "거인이 닿았습니다 — 지금은 싸울 때입니다";
                case TrainingRejection.NoItem: return "헤르메스의 날개가 없습니다";
                case TrainingRejection.NotTraining: return "훈련 중이 아닙니다";
                default: return r.ToString();
            }
        }

        private static string CostLine(TrainingDef def)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < def.CostPerUnit.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(ResourceLabels.Of(def.CostPerUnit[i].Kind)).Append(' ').Append(def.CostPerUnit[i].Amount);
            }
            return sb.ToString();
        }

        // ---------- 협곡 전투 ----------

        private void RefreshBattle()
        {
            bool fighting = _director != null && _director.Fighting;
            SetActive(_battleRoot, fighting);
            if (fighting)
                SetText(_speedLabel, "×" + _director.Speed);
        }

        // ---------- 공통 ----------

        private void ShowStory(string key, float seconds)
        {
            if (string.IsNullOrEmpty(key) || _toast == null)
                return;

            _toast.Show(_presenter.Strings.Get(key), seconds);
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
