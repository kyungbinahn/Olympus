using System;
using System.Collections.Generic;
using Olympus.Core.Battle;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Chapter
{
    public enum ThreatPhase
    {
        /// <summary>아직 안 보인다(본부 레벨 미달).</summary>
        Hidden = 0,

        /// <summary>지평선에서 걸어오는 중. 목표 수를 채우면 먼저 출정할 수 있다.</summary>
        Approaching = 1,

        /// <summary>도착했다 — 지금 가진 병사로 맞서야 한다. 훈련은 막힌다.</summary>
        Arrived = 2,

        Defeated = 3,
    }

    public enum ThreatRejection
    {
        None = 0,
        UnknownThreat,
        NotActive,

        /// <summary>도착 전에는 목표 수(300)를 채워야 출정할 수 있다.</summary>
        NotEnoughTroops,
    }

    public readonly struct ThreatView
    {
        public readonly ThreatDef Def;
        public readonly ThreatPhase Phase;
        public readonly long ArrivesAtUnixMs;
        public readonly long RemainingMs;

        /// <summary>0 = 막 보이기 시작, 1 = 도착. 화면이 거인을 이만큼 앞으로 옮긴다.</summary>
        public readonly float Progress;

        public readonly long Troops;
        public readonly int Tries;

        public ThreatView(ThreatDef def, ThreatPhase phase, long arrivesAt, long remainingMs, float progress,
            long troops, int tries)
        {
            Def = def;
            Phase = phase;
            ArrivesAtUnixMs = arrivesAt;
            RemainingMs = remainingMs;
            Progress = progress;
            Troops = troops;
            Tries = tries;
        }

        public bool CanDispatch =>
            Phase == ThreatPhase.Arrived || (Phase == ThreatPhase.Approaching && Troops >= Def.GoalTroops);
    }

    public readonly struct ThreatOutcome
    {
        public readonly ThreatRejection Rejection;
        public readonly bool Won;
        public readonly int Sent;
        public readonly int Survivors;
        public readonly long DurationMs;

        public ThreatOutcome(ThreatRejection rejection, bool won, int sent, int survivors, long durationMs)
        {
            Rejection = rejection;
            Won = won;
            Sent = sent;
            Survivors = survivors;
            DurationMs = durationMs;
        }

        public int Lost => Sent - Survivors;
    }

    /// <summary>
    /// 실시간 위협(거인). 판단은 여기, 화면은 <see cref="Describe"/>를 보고 거인을 옮기고 남은 시간을 쓴다.
    ///
    /// 전투는 스테이지와 같은 방식이다 — 화면이 <see cref="CreateSimulation"/>을 재생하고, 끝나면
    /// <see cref="Resolve"/>가 같은 입력으로 다시 돌려 적용한다(무작위가 없어 둘이 같다).
    ///
    /// 도착했을 때 저절로 싸우지 않는 이유 — 앱이 꺼져 있는 사이에 결판이 나면 형님이 300 vs 1을 못 본다.
    /// 대신 도착하면 훈련을 막는다. 그래야 "도착 전에 300"이라는 시간 압박이 진짜가 된다.
    /// </summary>
    public sealed class GiantThreatService
    {
        private readonly StateStore _store;
        private readonly IClock _clock;
        private readonly ChapterConfig _config;
        private readonly ProgressionConfig _progression;
        private readonly BattleService _battles;

        public GiantThreatService(StateStore store, IClock clock, ChapterConfig config,
            ProgressionConfig progression, BattleService battles)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ChapterConfig.Empty;
            _progression = progression ?? ProgressionConfig.Empty;
            _battles = battles ?? throw new ArgumentNullException(nameof(battles));
        }

        public ChapterConfig Config => _config;

        /// <summary>지금 화면에 띄울 위협 — 안 끝난 것 중 첫째. 없으면 null.</summary>
        public ThreatDef Current
        {
            get
            {
                for (int i = 0; i < _config.Threats.Count; i++)
                {
                    ThreatDef t = _config.Threats[i];
                    if (_store.State.Counters.Get(t.WonCounter) == 0L)
                        return t;
                }

                return null;
            }
        }

        /// <summary>도착해서 맞서야 하는 위협이 있나 — 훈련이 이걸 보고 막힌다.</summary>
        public bool AnyArrived
        {
            get
            {
                for (int i = 0; i < _config.Threats.Count; i++)
                {
                    if (Describe(_config.Threats[i]).Phase == ThreatPhase.Arrived)
                        return true;
                }

                return false;
            }
        }

        public ThreatView Describe(ThreatDef t)
        {
            CountPool c = _store.State.Counters;
            long troops = c.Get(ChapterKeys.Troops(t.TroopUnitId));
            int tries = (int)c.Get(ChapterKeys.ThreatTries(t.ThreatId));

            if (c.Get(t.WonCounter) > 0L)
                return new ThreatView(t, ThreatPhase.Defeated, 0L, 0L, 1f, troops, tries);

            long arrives = c.Get(ChapterKeys.ThreatArrives(t.ThreatId));
            if (arrives == 0L)
                return new ThreatView(t, ThreatPhase.Hidden, 0L, t.ApproachMs, 0f, troops, tries);

            long now = _clock.NowUnixMs;
            long remaining = Math.Max(0L, arrives - now);

            // 다시 오는 길(진 뒤)은 RetryMs 동안 걸어온다 — 진행도는 그 길이로 잰다.
            long span = tries > 0 ? t.RetryMs : t.ApproachMs;
            float progress = 1f - (float)Math.Min(1.0, (double)remaining / span);

            ThreatPhase phase = remaining == 0L ? ThreatPhase.Arrived : ThreatPhase.Approaching;
            return new ThreatView(t, phase, arrives, remaining, progress, troops, tries);
        }

        /// <summary>시작 조건이 찬 위협의 도착 시각을 정한다. 새로 시작한 위협(없으면 null).</summary>
        public ThreatDef Tick()
        {
            int hq = ProgressionRules.HqLevel(_store.State, _progression);

            for (int i = 0; i < _config.Threats.Count; i++)
            {
                ThreatDef t = _config.Threats[i];
                if (hq < t.StartHqLevel || Describe(t).Phase != ThreatPhase.Hidden)
                    continue;

                // 한 번에 하나만 — 앞의 위협이 안 끝났으면 다음은 기다린다.
                if (Current != t)
                    return null;

                _store.Apply(new StateDelta().WithCounter(
                    CountChange.Absolute(ChapterKeys.ThreatArrives(t.ThreatId), _clock.NowUnixMs + t.ApproachMs)));
                return t;
            }

            return null;
        }

        public ThreatRejection CanDispatch(string threatId)
        {
            ThreatDef t;
            if (!_config.TryGetThreat(threatId, out t))
                return ThreatRejection.UnknownThreat;

            ThreatView v = Describe(t);
            if (v.Phase != ThreatPhase.Approaching && v.Phase != ThreatPhase.Arrived)
                return ThreatRejection.NotActive;

            if (!v.CanDispatch)
                return ThreatRejection.NotEnoughTroops;

            return ThreatRejection.None;
        }

        /// <summary>화면이 재생할 시뮬레이션. 적용은 <see cref="Resolve"/>.</summary>
        public BattleSimulation CreateSimulation(string threatId)
        {
            ThreatDef t;
            if (!_config.TryGetThreat(threatId, out t))
                throw new InvalidOperationException("없는 위협: " + threatId);

            return Build(t);
        }

        private BattleSimulation Build(ThreatDef t)
        {
            return new BattleSimulation(BuildPlayer(t), BuildEnemy(t), t.TimeLimitMs, t.RowWidth, t.Spacing, t.StartDistance);
        }

        private List<UnitSpawn> BuildPlayer(ThreatDef t)
        {
            var army = new List<UnitSpawn>();
            UnitDef def;
            if (!_battles.Config.TryGetUnit(t.TroopUnitId, out def))
                return army;

            long troops = _store.State.Counters.Get(ChapterKeys.Troops(t.TroopUnitId));
            double hp = _battles.BuffMultiplier(def.UnitId, BattleStat.Hp);
            double attack = _battles.BuffMultiplier(def.UnitId, BattleStat.Attack);

            for (long k = 0; k < troops; k++)
                army.Add(new UnitSpawn(def, hp, attack));

            return army;
        }

        private List<UnitSpawn> BuildEnemy(ThreatDef t)
        {
            var army = new List<UnitSpawn>();

            for (int i = 0; i < t.Enemies.Count; i++)
            {
                EnemyGroup g = t.Enemies[i];
                UnitDef def;
                if (!_battles.Config.TryGetUnit(g.UnitId, out def))
                    continue;

                float scale = t.EnemyScale > 0f ? t.EnemyScale
                    : g.Multiplier > 1.0 ? (float)Math.Min(1.6, 1.0 + (g.Multiplier - 1.0) * 0.25) : 1f;

                for (int k = 0; k < g.Count; k++)
                    army.Add(new UnitSpawn(def, g.Multiplier, g.Multiplier, scale));
            }

            return army;
        }

        /// <summary>
        /// 끝까지 싸우고 적용한다. 살아남은 병사만 돌아온다(죽은 병사는 잃는다 — 300을 다시 채우는 것이 진 값이다).
        /// 이기면 보상 + 이김 카운터(본부 조건). 지면 거인이 물러났다가 <see cref="ThreatDef.RetryMs"/> 뒤에 다시 온다.
        /// </summary>
        public ThreatOutcome Resolve(string threatId)
        {
            ThreatRejection rejection = CanDispatch(threatId);
            if (rejection != ThreatRejection.None)
                return new ThreatOutcome(rejection, false, 0, 0, 0L);

            ThreatDef t;
            _config.TryGetThreat(threatId, out t);

            int sent = (int)_store.State.Counters.Get(ChapterKeys.Troops(t.TroopUnitId));
            BattleSimulation sim = Build(t);
            sim.RunToEnd();

            int survivors = sim.AliveCount(BattleTeam.Player);
            long now = _clock.NowUnixMs;

            var delta = new StateDelta()
                .WithCounter(CountChange.Absolute(ChapterKeys.Troops(t.TroopUnitId), survivors))
                .WithCounter(CountChange.Delta(ChapterKeys.ThreatTries(t.ThreatId), 1));

            if (sim.PlayerWon)
            {
                delta.WithCounter(CountChange.Absolute(t.WonCounter, 1))
                    .WithCounter(CountChange.Absolute(ChapterKeys.ThreatArrives(t.ThreatId), 0))
                    .WithResources(t.RewardResources);

                for (int i = 0; i < t.RewardItems.Count; i++)
                    delta.WithItem(CountChange.Delta(t.RewardItems[i].ItemId, t.RewardItems[i].Amount));
            }
            else
            {
                delta.WithCounter(CountChange.Absolute(ChapterKeys.ThreatArrives(t.ThreatId), now + t.RetryMs));
            }

            _store.Apply(delta);
            return new ThreatOutcome(ThreatRejection.None, sim.PlayerWon, sent, survivors, sim.ElapsedMs);
        }
    }
}
