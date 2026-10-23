using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Battle;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.Game.Battle
{
    /// <summary>
    /// 병사 한 종류에 붙일 실제 모델. 비워 두면 기본 도형 병사로 선다.
    ///
    /// 모델에 <see cref="Animator"/>가 있으면 세 신호를 보낸다 — <c>Speed</c>(float, 걸으면 1),
    /// <c>Attack</c>(trigger), <c>Die</c>(trigger). 애니메이터 컨트롤러가 이 이름을 쓰면 된다.
    /// 없으면 도형 병사처럼 흔들기·찌르기·넘어지기로 흉내 낸다.
    /// </summary>
    /// <summary>소품을 어디에 어떻게 드나. 아트 납품 규칙: 소품의 "앞"(창끝·방패 앞면)이 +Z, 원점이 쥐는 점.</summary>
    public enum PropSocket
    {
        /// <summary>오른손으로 쥐고, T포즈에서 창끝이 정면을 향한다.</summary>
        SpearRightHand = 0,

        /// <summary>왼 팔뚝에 끼우고, T포즈에서 앞면이 위를 본다 — 팔을 내리면 바깥을 본다.</summary>
        ShieldLeftForearm = 1,
    }

    [System.Serializable]
    public sealed class BattlePropEntry
    {
        public GameObject model;
        public Material material;

        [Tooltip("적 진영일 때 입힐 머티리얼(팀색이 다른 판). 비우면 위의 것을 쓴다.")]
        public Material enemyMaterial;

        public PropSocket socket;
    }

    [System.Serializable]
    public sealed class BattleUnitModelEntry
    {
        public string unitId;
        public GameObject model;

        [Tooltip("이 병사의 동작. 비우면 모델에 붙어 온 것을 쓴다.")]
        public RuntimeAnimatorController controller;

        [Tooltip("입힐 머티리얼. 아래 적 머티리얼이 비어 있으면 진영 색(파랑/붉은 갈색)을 몸 전체에 덧칠한다.")]
        public Material material;

        [Tooltip("적 진영일 때 입힐 머티리얼. 채우면 덧칠하지 않는다 — 팀색이 마스크 영역(볏·망토)에만 " +
                 "구워진 판을 진영마다 따로 쓰는 것이다. 위의 머티리얼이 아군 판이 된다.")]
        public Material enemyMaterial;

        [Tooltip("손에 드는 것(창·방패).")]
        public List<BattlePropEntry> props = new List<BattlePropEntry>();

        [Tooltip("모델 크기 보정. 1이면 병사 몸 높이(반지름 × 4.5)에 맞춘다.")]
        public float scale = 1f;

        [Tooltip("사람 뼈대(Humanoid) 모델은 어깨 위치로 정면을 재서 저절로 돌린다 — 이 값은 그 위에 더하는 손 보정(도)이다. " +
                 "뼈대가 없는 모델은 이 값만 쓴다.")]
        public float yaw = 0f;
    }

    public enum BattlePhase
    {
        /// <summary>양 진영이 서 있고 "전투 시작"을 기다린다.</summary>
        Ready = 0,
        Fighting = 1,
        Result = 2,

        /// <summary>스테이지를 다 깼다.</summary>
        NoStage = 3,

        /// <summary>전선이 아직 안 열렸다(본부 레벨).</summary>
        Locked = 4,
    }

    /// <summary>
    /// 전선 화면의 흐름(준비→싸움→결과). 병사 그리기·재생은 <see cref="BattleArena"/>가 한다.
    /// 전선 화면을 조립하고 돌린다. 판단(누가 이기나·보상)은 전부 Core(<see cref="BattleService"/>)에 있다.
    ///
    /// 화면은 Core의 시뮬레이션을 한 걸음(50ms)씩 돌리며 그리기만 한다. 끝나면 결과 적용은
    /// <see cref="BattleService.Fight"/>가 같은 입력으로 다시 끝까지 돌려서 한다 — 시뮬레이션에
    /// 무작위가 없어서 둘이 같다(시험이 그걸 확인한다). 화면이 결과를 들고 가 적용하지 않는 이유는,
    /// 그러면 "화면이 이겼다고 말하면 이긴 것"이 되어 서버가 붙을 때 그 길을 다 막아야 하기 때문이다.
    /// </summary>
    public sealed class BattlePresenter : MonoBehaviour
    {
        [Tooltip("병사를 그리고 재생하는 부분. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private BattleArena _arena;

        private TerritoryRuntime _runtime;

        public BattlePhase Phase { get; private set; }
        public StageDef Stage { get; private set; }
        public BattlePreview Preview { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public TerritoryRuntime Runtime => _runtime;
        public BattleSimulation Simulation => _arena != null ? _arena.Simulation : null;
        public BattleArena Arena => _arena;

        /// <summary>1 또는 2.</summary>
        public int Speed => _arena != null ? _arena.Speed : 1;

        /// <summary>단계가 바뀔 때마다 알린다. HUD가 패널을 갈아 끼운다.</summary>
        public event System.Action PhaseChanged;

        private void Awake()
        {
            if (_arena == null)
                _arena = GetComponent<BattleArena>();

            // 전투는 기지 세션의 군대·진도로 싸운다. 기지를 거치지 않고 이 씬을 바로 틀었으면 기지로 돌린다.
            if (!TerritorySession.HasRuntime)
            {
                Debug.Log("전투 씬은 기지에서 들어와야 합니다 — 기지 씬으로 이동합니다.");
                SceneRouter.GoToTerritory();
                enabled = false;
                return;
            }

            if (_arena == null)
            {
                Debug.LogError("BattleArena가 없습니다 — Olympus/Setup/전투 씬 만들기를 다시 누르세요.");
                enabled = false;
                return;
            }

            _runtime = TerritorySession.Runtime;
            _arena.Finished += Finish;

            BattleService battles = _runtime.Battles;

            if (!battles.IsUnlocked)
            {
                Phase = BattlePhase.Locked;
                return;
            }

            Stage = battles.NextStage;
            if (Stage == null)
            {
                Phase = BattlePhase.NoStage;
                return;
            }

            Preview = battles.Preview(Stage.StageId);
            _arena.Show(battles.CreateSimulation(Stage.StageId));
            Phase = BattlePhase.Ready;
        }

        private void OnDestroy()
        {
            if (_arena != null)
                _arena.Finished -= Finish;
        }

        public void StartBattle()
        {
            if (Phase != BattlePhase.Ready || !Preview.CanFight)
                return;

            Phase = BattlePhase.Fighting;
            _arena.Play();
            PhaseChanged?.Invoke();
        }

        public void ToggleSpeed()
        {
            _arena.Speed = _arena.Speed == 1 ? 2 : 1;
            PhaseChanged?.Invoke();
        }

        public void Skip()
        {
            if (Phase == BattlePhase.Fighting)
                _arena.SkipToEnd();
        }

        public void Leave() => SceneRouter.GoToTerritory();

        /// <summary>다음 스테이지 — 씬을 다시 연다. 군대가 새 진도에서 다시 짜인다.</summary>
        public void NextStage() => SceneRouter.GoToBattle();

        private void Finish()
        {
            if (Phase != BattlePhase.Fighting)
                return;

            Outcome = _runtime.Battles.Fight(Stage.StageId);

            if (Outcome.Won != Simulation.PlayerWon)
            {
                // 일어나면 안 된다 — 시뮬레이션에 무작위나 순서 의존이 끼어들었다는 뜻이다.
                Debug.LogError("화면의 전투 결과와 적용된 결과가 다릅니다. 시뮬레이션이 결정론을 잃었습니다.");
            }

            if (Outcome.Won && TerritorySession.SaveEnabled)
                SaveStore.Save(_runtime.CaptureSave());

            Phase = BattlePhase.Result;
            PhaseChanged?.Invoke();
        }
    }
}
