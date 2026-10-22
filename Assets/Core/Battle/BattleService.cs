using System;
using System.Collections.Generic;
using Olympus.Core.Progression;
using Olympus.Core.State;

namespace Olympus.Core.Battle
{
    public enum BattleRejection
    {
        None = 0,

        /// <summary>전선 기능이 아직 안 열렸다(본부 레벨).</summary>
        Locked,

        /// <summary>그런 스테이지가 없다 — 다 깼거나 번호가 틀렸다.</summary>
        UnknownStage,

        /// <summary>다음 차례 스테이지가 아니다. 스테이지는 차례로만 깬다.</summary>
        NotNextStage,

        /// <summary>싸울 병사가 없다.</summary>
        NoArmy,
    }

    /// <summary>싸우기 전 화면 — "내 전투력 vs 적". 탑포스는 이 두 숫자를 나란히 띄운다.</summary>
    public readonly struct BattlePreview
    {
        public readonly BattleRejection Rejection;
        public readonly StageDef Stage;
        public readonly long PlayerPower;
        public readonly long EnemyPower;
        public readonly int PlayerCount;
        public readonly int EnemyCount;

        public BattlePreview(BattleRejection rejection, StageDef stage, long playerPower, long enemyPower,
            int playerCount, int enemyCount)
        {
            Rejection = rejection;
            Stage = stage;
            PlayerPower = playerPower;
            EnemyPower = enemyPower;
            PlayerCount = playerCount;
            EnemyCount = enemyCount;
        }

        public bool CanFight => Rejection == BattleRejection.None;
    }

    public readonly struct BattleOutcome
    {
        public readonly BattleRejection Rejection;
        public readonly bool Won;
        public readonly long DurationMs;
        public readonly int Survivors;

        public BattleOutcome(BattleRejection rejection, bool won, long durationMs, int survivors)
        {
            Rejection = rejection;
            Won = won;
            DurationMs = durationMs;
            Survivors = survivors;
        }
    }

    /// <summary>
    /// 전선 스테이지. 군대는 기지 건물에서 나온다 — 병영을 올리면 호플리테스가 늘고, 제철소를
    /// 올리면 세진다. 그래서 "건물을 올린다 → 전투력이 오른다 → 다음 스테이지를 깬다 → 본부 조건이
    /// 찬다"가 한 고리가 된다(탑포스의 시설 → 전선 → 기지 레벨 고리와 같은 모양).
    ///
    /// 영웅이 생기면 여기에 영웅 줄이 더해진다. 지금은 건물만 본다.
    /// </summary>
    public sealed class BattleService
    {
        /// <summary>깬 스테이지 중 가장 높은 번호를 담는 누적 기록 이름. 임무·본부 조건이 이 이름으로 본다.</summary>
        public const string StageCounter = "stage";

        /// <summary>전선 버튼의 기능 이름(<c>function-unlock.json</c>).</summary>
        public const string Feature = "battle";

        private readonly StateStore _store;
        private readonly BattleConfig _config;
        private readonly ProgressionConfig _progression;

        public BattleService(StateStore store, BattleConfig config, ProgressionConfig progression)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _config = config ?? BattleConfig.Empty;
            _progression = progression ?? ProgressionConfig.Empty;
        }

        public BattleConfig Config => _config;

        public int HighestCleared => (int)_store.State.Counters.Get(StageCounter);

        /// <summary>다음에 싸울 스테이지. 다 깼으면 null.</summary>
        public StageDef NextStage
        {
            get
            {
                StageDef stage;
                return _config.TryGetStage(HighestCleared + 1, out stage) ? stage : null;
            }
        }

        public bool IsUnlocked =>
            _progression.Features.IsUnlocked(Feature, ProgressionRules.HqLevel(_store.State, _progression));

        /// <summary>
        /// 지금 기지가 내보낼 수 있는 군대. 기본 수비대 + 건물 레벨만큼의 병사, 그리고 건물 강화.
        /// 순서가 일정해야 배치가 매번 같다 — 데이터에 적힌 출처 순서대로 세운다.
        /// </summary>
        public List<UnitSpawn> BuildPlayerArmy()
        {
            GameState state = _store.State;
            var army = new List<UnitSpawn>();

            for (int i = 0; i < _config.Sources.Count; i++)
            {
                ArmySource s = _config.Sources[i];
                UnitDef def;
                if (!_config.TryGetUnit(s.UnitId, out def))
                    continue;

                int count = s.FixedCount;
                if (s.DefId != null)
                {
                    int level = ProgressionRules.HighestLevel(state, s.DefId);
                    count = level > 0 ? s.FixedCount + s.CountPerLevel * level : 0;
                }

                double hp = Multiplier(def.UnitId, BattleStat.Hp);
                double attack = Multiplier(def.UnitId, BattleStat.Attack);

                for (int k = 0; k < count; k++)
                    army.Add(new UnitSpawn(def, hp, attack));
            }

            return army;
        }

        public List<UnitSpawn> BuildEnemyArmy(StageDef stage)
        {
            var army = new List<UnitSpawn>();

            for (int i = 0; i < stage.Enemies.Count; i++)
            {
                EnemyGroup g = stage.Enemies[i];
                UnitDef def;
                if (!_config.TryGetUnit(g.UnitId, out def))
                    continue;

                // 배수가 크면 몸도 조금 키운다 — 같은 거인이라도 센 놈은 커 보여야 읽힌다.
                float scale = g.Multiplier > 1.0 ? (float)Math.Min(1.6, 1.0 + (g.Multiplier - 1.0) * 0.25) : 1f;

                for (int k = 0; k < g.Count; k++)
                    army.Add(new UnitSpawn(def, g.Multiplier, g.Multiplier, scale));
            }

            return army;
        }

        /// <summary>건물 레벨이 주는 배수. 같은 능력치 강화는 더한다(곱하지 않는다 — 곱하면 후반에 폭주한다).</summary>
        private double Multiplier(string unitId, BattleStat stat)
        {
            double percent = 0.0;

            for (int i = 0; i < _config.Buffs.Count; i++)
            {
                ArmyBuff b = _config.Buffs[i];
                if (b.Stat != stat || (b.UnitId != null && b.UnitId != unitId))
                    continue;

                percent += b.PercentPerLevel * ProgressionRules.HighestLevel(_store.State, b.DefId);
            }

            return 1.0 + percent / 100.0;
        }

        public BattlePreview Preview(int stageId)
        {
            StageDef stage;
            _config.TryGetStage(stageId, out stage);

            BattleRejection rejection = Check(stageId, stage);

            List<UnitSpawn> player = BuildPlayerArmy();
            List<UnitSpawn> enemy = stage == null ? new List<UnitSpawn>() : BuildEnemyArmy(stage);
            var sim = new BattleSimulation(player, enemy, _config.TimeLimitMs);

            if (rejection == BattleRejection.None && player.Count == 0)
                rejection = BattleRejection.NoArmy;

            return new BattlePreview(rejection, stage,
                sim.PowerOf(BattleTeam.Player), sim.PowerOf(BattleTeam.Enemy), player.Count, enemy.Count);
        }

        /// <summary>
        /// 화면이 재생할 시뮬레이션. 결과 적용은 하지 않는다 — 재생이 끝나면 <see cref="Fight"/>를 부른다.
        /// 같은 입력이라 화면에서 본 결과와 <see cref="Fight"/>의 결과가 같다.
        /// </summary>
        public BattleSimulation CreateSimulation(int stageId)
        {
            StageDef stage;
            if (!_config.TryGetStage(stageId, out stage))
                throw new InvalidOperationException("없는 스테이지: " + stageId);

            return new BattleSimulation(BuildPlayerArmy(), BuildEnemyArmy(stage), _config.TimeLimitMs);
        }

        /// <summary>
        /// 끝까지 싸우고, 이기면 보상과 진도를 한 델타로 적용한다. 지면 아무것도 바뀌지 않는다
        /// (병사는 건물에서 나오므로 잃는 것이 없다 — 다시 도전하면 된다).
        /// </summary>
        public BattleOutcome Fight(int stageId)
        {
            StageDef stage;
            _config.TryGetStage(stageId, out stage);

            BattleRejection rejection = Check(stageId, stage);
            if (rejection != BattleRejection.None)
                return new BattleOutcome(rejection, false, 0L, 0);

            List<UnitSpawn> player = BuildPlayerArmy();
            if (player.Count == 0)
                return new BattleOutcome(BattleRejection.NoArmy, false, 0L, 0);

            var sim = new BattleSimulation(player, BuildEnemyArmy(stage), _config.TimeLimitMs);
            sim.RunToEnd();

            if (sim.PlayerWon)
            {
                var delta = new StateDelta()
                    .WithResources(stage.RewardResources)
                    .WithCounter(CountChange.Absolute(StageCounter, stage.StageId));

                for (int i = 0; i < stage.RewardItems.Count; i++)
                    delta.WithItem(CountChange.Delta(stage.RewardItems[i].ItemId, stage.RewardItems[i].Amount));

                _store.Apply(delta);
            }

            return new BattleOutcome(BattleRejection.None, sim.PlayerWon, sim.ElapsedMs, sim.AliveCount(BattleTeam.Player));
        }

        private BattleRejection Check(int stageId, StageDef stage)
        {
            if (!IsUnlocked)
                return BattleRejection.Locked;

            if (stage == null)
                return BattleRejection.UnknownStage;

            if (stageId != HighestCleared + 1)
                return BattleRejection.NotNextStage;

            return BattleRejection.None;
        }
    }
}
