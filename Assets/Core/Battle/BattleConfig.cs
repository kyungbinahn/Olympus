using System;
using System.Collections.Generic;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Battle
{
    /// <summary>병사 한 종류의 능력치. 아군·적이 같은 모양을 쓴다.</summary>
    public sealed class UnitDef
    {
        public string UnitId { get; }
        public string DisplayNameKey { get; }
        public long Hp { get; }
        public long Attack { get; }
        public long AttackIntervalMs { get; }

        /// <summary>몸 가장자리부터 잰 사거리. 근접은 1 안팎, 활은 6~7.</summary>
        public float Range { get; }

        /// <summary>초당 이동 거리.</summary>
        public float MoveSpeed { get; }

        /// <summary>몸 반지름 — 겹치지 않게 밀어내고, 사거리를 몸 가장자리부터 재는 데 쓴다.</summary>
        public float Radius { get; }

        public UnitDef(string unitId, string displayNameKey, long hp, long attack, long attackIntervalMs,
            float range, float moveSpeed, float radius)
        {
            if (string.IsNullOrEmpty(unitId))
                throw new ArgumentException("병사 이름은 비울 수 없다.", nameof(unitId));
            if (hp <= 0L || attack < 0L || attackIntervalMs <= 0L)
                throw new ArgumentOutOfRangeException(nameof(hp), unitId + ": 체력·공격 간격은 1 이상이어야 한다.");
            if (range < 0f || moveSpeed < 0f || radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(range), unitId + ": 사거리·속도·반지름이 잘못됐다.");

            UnitId = unitId;
            DisplayNameKey = displayNameKey;
            Hp = hp;
            Attack = attack;
            AttackIntervalMs = attackIntervalMs;
            Range = range;
            MoveSpeed = moveSpeed;
            Radius = radius;
        }

        public bool IsRanged => Range >= 3f;
    }

    /// <summary>
    /// 아군 병사의 출처 한 줄 — "병영 레벨마다 호플리테스 2명" 같은 것.
    /// <see cref="DefId"/>가 비어 있으면 건물과 무관하게 늘 있는 기본 수비대다.
    /// </summary>
    public sealed class ArmySource
    {
        public string DefId { get; }
        public string UnitId { get; }
        public int FixedCount { get; }
        public int CountPerLevel { get; }

        public ArmySource(string defId, string unitId, int fixedCount, int countPerLevel)
        {
            if (fixedCount < 0 || countPerLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedCount));

            DefId = string.IsNullOrEmpty(defId) ? null : defId;
            UnitId = unitId;
            FixedCount = fixedCount;
            CountPerLevel = countPerLevel;
        }
    }

    public enum BattleStat
    {
        Attack = 0,
        Hp = 1,
    }

    /// <summary>
    /// 건물 레벨이 주는 강화 한 줄 — "제철소 레벨마다 공격 +8%".
    /// <see cref="UnitId"/>가 비어 있으면 아군 전원.
    /// </summary>
    public sealed class ArmyBuff
    {
        public string DefId { get; }
        public string UnitId { get; }
        public BattleStat Stat { get; }
        public double PercentPerLevel { get; }

        public ArmyBuff(string defId, string unitId, BattleStat stat, double percentPerLevel)
        {
            if (string.IsNullOrEmpty(defId))
                throw new ArgumentException("강화를 주는 건물이 있어야 한다.", nameof(defId));

            DefId = defId;
            UnitId = string.IsNullOrEmpty(unitId) ? null : unitId;
            Stat = stat;
            PercentPerLevel = percentPerLevel;
        }
    }

    /// <summary>적 무리 한 줄. 배수는 그 스테이지만의 강화(같은 거인이 뒤로 갈수록 세다).</summary>
    public readonly struct EnemyGroup
    {
        public readonly string UnitId;
        public readonly int Count;
        public readonly double Multiplier;

        public EnemyGroup(string unitId, int count, double multiplier)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (multiplier <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(multiplier));

            UnitId = unitId;
            Count = count;
            Multiplier = multiplier;
        }
    }

    /// <summary>
    /// 스테이지 하나. 1번부터 차례로 깬다(탑포스 미션처럼 한 줄 진행) — 깬 스테이지 중
    /// 가장 높은 번호 하나만 기억하면 되고, 그게 본부 조건·임무 조건에 그대로 쓰인다.
    /// </summary>
    public sealed class StageDef
    {
        public int StageId { get; }
        public string NameKey { get; }
        public IReadOnlyList<EnemyGroup> Enemies { get; }
        public IReadOnlyList<ResourceCost> RewardResources { get; }
        public IReadOnlyList<ItemGrant> RewardItems { get; }

        public StageDef(int stageId, string nameKey, IReadOnlyList<EnemyGroup> enemies,
            IReadOnlyList<ResourceCost> rewardResources, IReadOnlyList<ItemGrant> rewardItems)
        {
            if (stageId < 1)
                throw new ArgumentOutOfRangeException(nameof(stageId), "스테이지는 1번부터다.");
            if (enemies == null || enemies.Count == 0)
                throw new ArgumentException("스테이지 " + stageId + "에 적이 없다.", nameof(enemies));

            StageId = stageId;
            NameKey = nameKey;
            Enemies = enemies;
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
            RewardItems = rewardItems ?? Array.Empty<ItemGrant>();
        }
    }

    /// <summary>
    /// 전투 규칙 한 벌. <c>Assets/Data/battle.json</c>에서 온다.
    /// </summary>
    public sealed class BattleConfig
    {
        private readonly Dictionary<string, UnitDef> _units = new Dictionary<string, UnitDef>();
        private readonly List<ArmySource> _sources = new List<ArmySource>();
        private readonly List<ArmyBuff> _buffs = new List<ArmyBuff>();
        private readonly List<StageDef> _stages = new List<StageDef>();

        /// <summary>이 시간 안에 못 끝내면 진다 — 서로 못 죽이는 교착을 끝내는 장치.</summary>
        public long TimeLimitMs { get; }

        public BattleConfig(long timeLimitMs = 90_000L)
        {
            if (timeLimitMs <= 0L)
                throw new ArgumentOutOfRangeException(nameof(timeLimitMs));

            TimeLimitMs = timeLimitMs;
        }

        public static BattleConfig Empty => new BattleConfig();

        public void AddUnit(UnitDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (_units.ContainsKey(def.UnitId))
                throw new InvalidOperationException("병사 종류가 중복이다: " + def.UnitId);

            _units.Add(def.UnitId, def);
        }

        public void AddSource(ArmySource source) => _sources.Add(source ?? throw new ArgumentNullException(nameof(source)));

        public void AddBuff(ArmyBuff buff) => _buffs.Add(buff ?? throw new ArgumentNullException(nameof(buff)));

        public void AddStage(StageDef stage)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (stage.StageId != _stages.Count + 1)
                throw new InvalidOperationException(
                    "스테이지는 1번부터 빠짐없이 차례로 있어야 한다 — " + (_stages.Count + 1) + "번 자리에 " + stage.StageId + "번이 왔다.");

            _stages.Add(stage);
        }

        public bool TryGetUnit(string unitId, out UnitDef def)
        {
            if (unitId == null)
            {
                def = null;
                return false;
            }

            return _units.TryGetValue(unitId, out def);
        }

        public bool TryGetStage(int stageId, out StageDef stage)
        {
            stage = stageId >= 1 && stageId <= _stages.Count ? _stages[stageId - 1] : null;
            return stage != null;
        }

        public int StageCount => _stages.Count;
        public IReadOnlyList<ArmySource> Sources => _sources;
        public IReadOnlyList<ArmyBuff> Buffs => _buffs;
        public IEnumerable<UnitDef> Units => _units.Values;

        /// <summary>데이터끼리 맞는지 본다. 문제를 문장으로 돌려준다(없으면 빈 목록).</summary>
        public List<string> Validate(BuildingCatalog catalog, ProgressionConfig progression)
        {
            var problems = new List<string>();

            for (int i = 0; i < _sources.Count; i++)
            {
                ArmySource s = _sources[i];
                CheckUnit(s.UnitId, "병사 출처", problems);
                CheckDef(s.DefId, "병사 출처", catalog, problems);
            }

            for (int i = 0; i < _buffs.Count; i++)
            {
                ArmyBuff b = _buffs[i];
                if (b.UnitId != null)
                    CheckUnit(b.UnitId, "강화", problems);
                CheckDef(b.DefId, "강화", catalog, problems);
            }

            for (int i = 0; i < _stages.Count; i++)
            {
                StageDef st = _stages[i];
                for (int k = 0; k < st.Enemies.Count; k++)
                    CheckUnit(st.Enemies[k].UnitId, "스테이지 " + st.StageId, problems);

                for (int k = 0; k < st.RewardItems.Count; k++)
                {
                    SpeedUpItemDef item;
                    if (progression == null || !progression.TryGetSpeedUp(st.RewardItems[k].ItemId, out item))
                        problems.Add("스테이지 " + st.StageId + " 보상: 모르는 아이템 '" + st.RewardItems[k].ItemId + "'.");
                }
            }

            if (_sources.Count == 0 && _stages.Count > 0)
                problems.Add("병사 출처가 하나도 없다 — 스테이지가 있는데 싸울 병사가 없다.");

            return problems;
        }

        private void CheckUnit(string unitId, string where, List<string> problems)
        {
            if (!_units.ContainsKey(unitId ?? ""))
                problems.Add(where + ": 모르는 병사 '" + unitId + "'.");
        }

        private static void CheckDef(string defId, string where, BuildingCatalog catalog, List<string> problems)
        {
            BuildingDef def;
            if (defId != null && !catalog.TryGet(defId, out def))
                problems.Add(where + ": 건물 '" + defId + "'이 건물 정의에 없다.");
        }
    }
}
