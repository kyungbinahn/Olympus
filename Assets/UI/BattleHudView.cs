using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Battle;
using Olympus.Core.State;
using Olympus.Game.Battle;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 전선 화면 HUD — 위에는 스테이지와 "내 전투력 vs 적", 아래에는 단계별 버튼.
    /// 싸우기 전(시작·돌아가기) → 싸우는 중(배속·건너뛰기) → 결과(기지로·다음 스테이지).
    /// 판단은 전부 <see cref="BattlePresenter"/>·Core에 있고, 여기서는 보여 주고 전달만 한다.
    /// </summary>
    public sealed class BattleHudView : MonoBehaviour
    {
        private const string OkColor = "#9FD88A";
        private const string NgColor = "#F07A6E";

        [SerializeField] private BattlePresenter _presenter;
        [SerializeField] private TextAsset _strings;

        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _powerText;
        [SerializeField] private TMP_Text _statusText;

        [SerializeField] private GameObject _readyPanel;
        [SerializeField] private TMP_Text _readyText;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _backButton;

        [SerializeField] private GameObject _fightPanel;
        [SerializeField] private Button _speedButton;
        [SerializeField] private TMP_Text _speedLabel;
        [SerializeField] private Button _skipButton;

        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private TMP_Text _resultTitle;
        [SerializeField] private TMP_Text _resultBody;
        [SerializeField] private Button _homeButton;
        [SerializeField] private Button _nextButton;

        private LocalizedStrings _text;

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                enabled = false;   // 기지로 돌아가는 중이다(BattlePresenter.Awake).
                return;
            }

            _text = LocalizedStrings.Load(_strings);

            _startButton.onClick.AddListener(_presenter.StartBattle);
            _backButton.onClick.AddListener(_presenter.Leave);
            _speedButton.onClick.AddListener(_presenter.ToggleSpeed);
            _skipButton.onClick.AddListener(_presenter.Skip);
            _homeButton.onClick.AddListener(_presenter.Leave);
            _nextButton.onClick.AddListener(_presenter.NextStage);

            _presenter.PhaseChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_presenter != null)
                _presenter.PhaseChanged -= Refresh;
        }

        private void Update()
        {
            if (_presenter.Phase != BattlePhase.Fighting)
                return;

            BattleSimulation sim = _presenter.Simulation;
            long left = (_presenter.Runtime.Battles.Config.TimeLimitMs - sim.ElapsedMs) / 1000L;
            _statusText.text = "아군 " + sim.AliveCount(BattleTeam.Player) + "   적 " + sim.AliveCount(BattleTeam.Enemy) +
                               "   남은 시간 " + (left / 60L).ToString("00") + ":" + (left % 60L).ToString("00");
        }

        private void Refresh()
        {
            BattlePhase phase = _presenter.Phase;

            _readyPanel.SetActive(phase == BattlePhase.Ready || phase == BattlePhase.Locked || phase == BattlePhase.NoStage);
            _fightPanel.SetActive(phase == BattlePhase.Fighting);
            _resultPanel.SetActive(phase == BattlePhase.Result);

            switch (phase)
            {
                case BattlePhase.Locked:
                    _titleText.text = "전선";
                    _powerText.text = "";
                    _statusText.text = "";
                    _readyText.text = "본부 레벨이 오르면 전선이 열립니다.";
                    _startButton.gameObject.SetActive(false);
                    return;

                case BattlePhase.NoStage:
                    _titleText.text = "전선";
                    _powerText.text = "";
                    _statusText.text = "";
                    _readyText.text = "모든 스테이지를 깼습니다. 다음 전선은 준비중입니다.";
                    _startButton.gameObject.SetActive(false);
                    return;
            }

            StageDef stage = _presenter.Stage;
            BattlePreview p = _presenter.Preview;

            _titleText.text = "스테이지 " + stage.StageId + "  ·  " + _text.Get(stage.NameKey);

            string mine = p.PlayerPower >= p.EnemyPower ? Colored(p.PlayerPower.ToString("N0"), OkColor) : Colored(p.PlayerPower.ToString("N0"), NgColor);
            _powerText.text = "내 전투력 " + mine + "   vs   적 " + p.EnemyPower.ToString("N0");

            if (phase == BattlePhase.Ready)
            {
                _statusText.text = "";
                _readyText.text = "아군  " + Composition(BattleTeam.Player) + "\n적  " + Composition(BattleTeam.Enemy);
                _startButton.gameObject.SetActive(true);
                _startButton.interactable = p.CanFight;
            }
            else if (phase == BattlePhase.Fighting)
            {
                _speedLabel.text = "x" + _presenter.Speed;
            }
            else if (phase == BattlePhase.Result)
            {
                ShowResult(stage);
            }
        }

        private void ShowResult(StageDef stage)
        {
            BattleOutcome o = _presenter.Outcome;
            _statusText.text = "";

            if (o.Won)
            {
                _resultTitle.text = Colored("승리!", OkColor);
                _resultBody.text = "보상  " + RewardText(stage);
                _nextButton.gameObject.SetActive(_presenter.Runtime.Battles.NextStage != null);
            }
            else
            {
                _resultTitle.text = Colored("패배", NgColor);

                // 탑포스 패배 화면의 "더 강해지기"를 우리 문법으로 — 무엇을 올리면 세지나.
                _resultBody.text = "병영·훈련장을 올리면 병사가 늘고,\n제철소는 공격을, 성문·치유의 집은 체력을 올립니다.";
                _nextButton.gameObject.SetActive(false);
            }
        }

        /// <summary>"호플리테스 6 · 궁수 2" — 종류별 머릿수.</summary>
        private string Composition(BattleTeam team)
        {
            var counts = new List<KeyValuePair<UnitDef, int>>();

            foreach (BattleUnit u in _presenter.Simulation.Units)
            {
                if (u.Team != team)
                    continue;

                int index = counts.FindIndex(kv => kv.Key == u.Def);
                if (index < 0)
                    counts.Add(new KeyValuePair<UnitDef, int>(u.Def, 1));
                else
                    counts[index] = new KeyValuePair<UnitDef, int>(u.Def, counts[index].Value + 1);
            }

            var sb = new StringBuilder();
            for (int i = 0; i < counts.Count; i++)
            {
                if (i > 0) sb.Append("  ·  ");
                sb.Append(_text.Get(counts[i].Key.DisplayNameKey)).Append(' ').Append(counts[i].Value);
            }

            return sb.Length > 0 ? sb.ToString() : "없음";
        }

        private string RewardText(StageDef stage)
        {
            var sb = new StringBuilder();

            foreach (ResourceCost r in stage.RewardResources)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(ResourceLabels.Of(r.Kind)).Append(" +").Append(r.Amount);
            }

            foreach (var item in stage.RewardItems)
            {
                Olympus.Core.Progression.SpeedUpItemDef def;
                string name = _presenter.Runtime.Progression.TryGetSpeedUp(item.ItemId, out def)
                    ? _text.Get(def.DisplayNameKey)
                    : item.ItemId;

                if (sb.Length > 0) sb.Append("  ");
                sb.Append(name).Append(" +").Append(item.Amount);
            }

            return sb.ToString();
        }

        private static string Colored(string text, string hex) => "<color=" + hex + ">" + text + "</color>";
    }
}
