using System;
using System.Collections.Generic;
using Olympus.Core.Battle;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Chapter
{
    /// <summary>
    /// 병사 훈련 규칙(병영). 컨셉의 "300 용사"는 <b>사람 수를 세는 것</b>이 재미라서, 병사는 건물 레벨에서
    /// 저절로 생기지 않고 식량·목재와 시간을 들여 길러야 한다(docs/concept-gap.md 결정 B).
    /// 건물(병영)은 한 번에 맡길 수 있는 수와 속도를 준다.
    /// </summary>
    public sealed class TrainingDef
    {
        public string UnitId { get; }
        public string BarracksDefId { get; }
        public IReadOnlyList<ResourceCost> CostPerUnit { get; }

        /// <summary>병영 Lv.1에서 한 명을 기르는 데 드는 시간.</summary>
        public long MsPerUnit { get; }

        /// <summary>한 번에 맡길 수 있는 수 — Lv.1 값과 레벨마다 더하는 값.</summary>
        public int BatchBase { get; }
        public int BatchPerLevel { get; }

        /// <summary>병영 레벨마다 빨라지는 정도(%). Lv.2면 한 번, Lv.3면 두 번 더한다.</summary>
        public double SpeedPercentPerLevel { get; }

        public TrainingDef(string unitId, string barracksDefId, IReadOnlyList<ResourceCost> costPerUnit,
            long msPerUnit, int batchBase, int batchPerLevel, double speedPercentPerLevel)
        {
            if (string.IsNullOrEmpty(unitId)) throw new ArgumentException("훈련할 병사가 없다.", nameof(unitId));
            if (string.IsNullOrEmpty(barracksDefId)) throw new ArgumentException("훈련 건물이 없다.", nameof(barracksDefId));
            if (msPerUnit <= 0L) throw new ArgumentOutOfRangeException(nameof(msPerUnit));
            if (batchBase <= 0 || batchPerLevel < 0) throw new ArgumentOutOfRangeException(nameof(batchBase));

            UnitId = unitId;
            BarracksDefId = barracksDefId;
            CostPerUnit = costPerUnit ?? Array.Empty<ResourceCost>();
            MsPerUnit = msPerUnit;
            BatchBase = batchBase;
            BatchPerLevel = batchPerLevel;
            SpeedPercentPerLevel = speedPercentPerLevel;
        }

        public int BatchCap(int barracksLevel) =>
            barracksLevel <= 0 ? 0 : BatchBase + BatchPerLevel * (barracksLevel - 1);

        public long MsPerUnitAt(int barracksLevel)
        {
            if (barracksLevel <= 1)
                return MsPerUnit;

            double factor = 1.0 + SpeedPercentPerLevel * (barracksLevel - 1) / 100.0;
            return Math.Max(1L, (long)Math.Round(MsPerUnit / factor));
        }
    }

    /// <summary>
    /// 실시간으로 다가오는 위협 한 건(1장: 거인 1). 시작 조건이 차면 "도착 시각"이 정해지고,
    /// 그 전에 목표 수(300)를 채워 출정하거나, 도착하면 그때 가진 병사로 싸운다.
    /// 시간은 남은 시간이 아니라 <b>도착 시각</b>으로 들고 있다 — 앱을 꺼 둬도 거인은 계속 걸어온다.
    /// </summary>
    public sealed class ThreatDef
    {
        public string ThreatId { get; }

        /// <summary>본부가 이 레벨이 되면 거인이 보이기 시작한다.</summary>
        public int StartHqLevel { get; }

        /// <summary>보이고 나서 도착까지.</summary>
        public long ApproachMs { get; }

        /// <summary>지고 나면 다시 오기까지.</summary>
        public long RetryMs { get; }

        public int GoalTroops { get; }
        public string TroopUnitId { get; }
        public IReadOnlyList<EnemyGroup> Enemies { get; }

        /// <summary>적 몸 크기(판정·그림). 0이면 배수에서 계산(<see cref="BattleService.BuildEnemyArmy"/>와 같게).</summary>
        public float EnemyScale { get; }

        public int RowWidth { get; }

        /// <summary>병사 간격·처음 서는 거리. 0이면 시뮬레이션 기본값.</summary>
        public float Spacing { get; }
        public float StartDistance { get; }
        public long TimeLimitMs { get; }
        public IReadOnlyList<ResourceCost> RewardResources { get; }
        public IReadOnlyList<ItemGrant> RewardItems { get; }

        public string StartStoryKey { get; }
        public string WinStoryKey { get; }
        public string LoseStoryKey { get; }

        public ThreatDef(string threatId, int startHqLevel, long approachMs, long retryMs, int goalTroops,
            string troopUnitId, IReadOnlyList<EnemyGroup> enemies, float enemyScale, int rowWidth, long timeLimitMs,
            IReadOnlyList<ResourceCost> rewardResources, IReadOnlyList<ItemGrant> rewardItems,
            string startStoryKey, string winStoryKey, string loseStoryKey, float spacing = 0f, float startDistance = 0f)
        {
            if (string.IsNullOrEmpty(threatId)) throw new ArgumentException("위협 이름이 없다.", nameof(threatId));
            if (approachMs <= 0L || retryMs <= 0L) throw new ArgumentOutOfRangeException(nameof(approachMs), threatId + ": 도착 시간은 1 이상.");
            if (goalTroops <= 0) throw new ArgumentOutOfRangeException(nameof(goalTroops));
            if (enemies == null || enemies.Count == 0) throw new ArgumentException(threatId + ": 적이 없다.", nameof(enemies));
            if (timeLimitMs <= 0L) throw new ArgumentOutOfRangeException(nameof(timeLimitMs));

            ThreatId = threatId;
            StartHqLevel = startHqLevel;
            ApproachMs = approachMs;
            RetryMs = retryMs;
            GoalTroops = goalTroops;
            TroopUnitId = troopUnitId;
            Enemies = enemies;
            EnemyScale = enemyScale;
            RowWidth = rowWidth;
            Spacing = spacing;
            StartDistance = startDistance;
            TimeLimitMs = timeLimitMs;
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
            RewardItems = rewardItems ?? Array.Empty<ItemGrant>();
            StartStoryKey = startStoryKey;
            WinStoryKey = winStoryKey;
            LoseStoryKey = loseStoryKey;
        }

        /// <summary>이기면 1이 되는 카운터 — 본부 레벨 조건이 이걸 본다(<c>Counter</c> 조건).</summary>
        public string WonCounter => ChapterKeys.ThreatWon(ThreatId);
    }

    /// <summary>
    /// 상태에 남기는 키. 훈련 주문·도착 시각을 <see cref="GameState.Counters"/>에 둔다 —
    /// 새 상태 칸을 만들면 델타·적용·세이브·복원을 다 늘려야 하는데, 카운터는 이미 그 길을 다 탄다.
    /// 시각(유닉스 ms)도 음수가 아닌 정수라 그대로 들어간다. 0은 "없음"이다.
    /// </summary>
    public static class ChapterKeys
    {
        public static string Troops(string unitId) => "troop." + unitId;
        public static string TrainCount(string unitId) => "train." + unitId + ".count";
        public static string TrainStart(string unitId) => "train." + unitId + ".start";
        public static string TrainEnds(string unitId) => "train." + unitId + ".ends";

        /// <summary>지금까지 길러 낸 총수 — 줄지 않는다. 임무("병사 50명 기르기")가 본다.</summary>
        public const string Trained = "trained";

        public static string ThreatArrives(string threatId) => "threat." + threatId + ".arrives";
        public static string ThreatWon(string threatId) => "threat." + threatId + ".won";
        public static string ThreatTries(string threatId) => "threat." + threatId + ".tries";
    }

    public sealed class ChapterConfig
    {
        private readonly List<ThreatDef> _threats = new List<ThreatDef>();

        public TrainingDef Training { get; }
        public IReadOnlyList<ThreatDef> Threats => _threats;

        /// <summary>도적 습격 규칙. 없으면 null(습격 없음).</summary>
        public RaidDef Raids { get; private set; }

        public void SetRaids(RaidDef raids) => Raids = raids;

        public ChapterConfig(TrainingDef training)
        {
            Training = training;
        }

        public static ChapterConfig Empty => new ChapterConfig(null);

        public void AddThreat(ThreatDef def)
        {
            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].ThreatId == def.ThreatId)
                    throw new InvalidOperationException("위협 이름이 겹친다: " + def.ThreatId);
            }

            _threats.Add(def);
        }

        public bool TryGetThreat(string threatId, out ThreatDef def)
        {
            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].ThreatId == threatId)
                {
                    def = _threats[i];
                    return true;
                }
            }

            def = null;
            return false;
        }

        /// <summary>모르는 병사·건물을 가리키면 문제 목록에 적는다. 비어 있으면 통과.</summary>
        public List<string> Validate(BattleConfig battle, BuildingCatalog catalog)
        {
            var problems = new List<string>();
            UnitDef unit;

            if (Training != null)
            {
                if (battle == null || !battle.TryGetUnit(Training.UnitId, out unit))
                    problems.Add("훈련: 모르는 병사 '" + Training.UnitId + "'");

                BuildingDef def;
                if (catalog != null && !catalog.TryGet(Training.BarracksDefId, out def))
                    problems.Add("훈련: 모르는 건물 '" + Training.BarracksDefId + "'");
            }

            for (int i = 0; i < _threats.Count; i++)
            {
                ThreatDef t = _threats[i];
                if (battle == null || !battle.TryGetUnit(t.TroopUnitId, out unit))
                    problems.Add(t.ThreatId + ": 모르는 아군 병사 '" + t.TroopUnitId + "'");

                if (Training == null || Training.UnitId != t.TroopUnitId)
                    problems.Add(t.ThreatId + ": 아군 병사 '" + t.TroopUnitId + "'를 기르는 훈련이 없다");

                for (int k = 0; k < t.Enemies.Count; k++)
                {
                    if (battle == null || !battle.TryGetUnit(t.Enemies[k].UnitId, out unit))
                        problems.Add(t.ThreatId + ": 모르는 적 '" + t.Enemies[k].UnitId + "'");
                }
            }

            if (Raids != null)
            {
                if (battle == null || !battle.TryGetUnit(Raids.MilitiaUnitId, out unit))
                    problems.Add("도적 습격: 모르는 수비대 병사 '" + Raids.MilitiaUnitId + "'");

                for (int i = 0; i < Raids.Waves.Count; i++)
                {
                    RaidWave w = Raids.Waves[i];
                    for (int k = 0; k < w.Enemies.Count; k++)
                    {
                        if (battle == null || !battle.TryGetUnit(w.Enemies[k].UnitId, out unit))
                            problems.Add("도적단 Lv." + w.HqLevel + ": 모르는 적 '" + w.Enemies[k].UnitId + "'");
                    }
                }
            }

            return problems;
        }
    }
}
