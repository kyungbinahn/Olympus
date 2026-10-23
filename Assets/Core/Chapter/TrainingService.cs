using System;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Chapter
{
    public enum TrainingRejection
    {
        None = 0,

        /// <summary>훈련 규칙이 없다(데이터 없음).</summary>
        NoTraining,

        /// <summary>병영이 아직 복구되지 않았다.</summary>
        NoBarracks,

        /// <summary>이미 훈련 중이다 — 병영 하나에 주문 하나.</summary>
        Busy,

        ZeroCount,

        /// <summary>한 번에 맡길 수 있는 수를 넘는다(병영 레벨).</summary>
        OverCap,

        NotEnoughResources,

        /// <summary>거인이 이미 도착했다 — 지금은 싸울 때다. 도착 시각이 의미를 가지려면 막아야 한다.</summary>
        GiantAtGate,

        NotTraining,
        UnknownItem,
        NoItem,
    }

    public readonly struct TrainingView
    {
        public readonly TrainingRejection Availability;
        public readonly int BarracksLevel;
        public readonly int BatchCap;
        public readonly long MsPerUnit;
        public readonly long Troops;

        public readonly bool Running;
        public readonly int OrderCount;
        public readonly long StartedAtUnixMs;
        public readonly long EndsAtUnixMs;

        /// <summary>주문 가운데 지금까지 다 기른 수(화면이 0/300을 실시간으로 올리는 데 쓴다). 들어오는 건 주문이 끝날 때다.</summary>
        public readonly int DoneSoFar;

        public TrainingView(TrainingRejection availability, int barracksLevel, int batchCap, long msPerUnit, long troops,
            bool running, int orderCount, long startedAt, long endsAt, int doneSoFar)
        {
            Availability = availability;
            BarracksLevel = barracksLevel;
            BatchCap = batchCap;
            MsPerUnit = msPerUnit;
            Troops = troops;
            Running = running;
            OrderCount = orderCount;
            StartedAtUnixMs = startedAt;
            EndsAtUnixMs = endsAt;
            DoneSoFar = doneSoFar;
        }

        /// <summary>화면에 보일 병사 수 = 이미 있는 병사 + 훈련 중 다 자란 몫.</summary>
        public long VisibleTroops => Troops + DoneSoFar;
    }

    /// <summary>
    /// 병영 훈련. 주문 = (수, 시작 시각, 끝나는 시각). 끝나는 시각이 지나면 <see cref="CompleteFinished"/>가
    /// 병사를 한꺼번에 넣는다. 화면은 그 사이 "다 자란 몫"을 계산해 숫자를 올려 보여 준다.
    ///
    /// 주문을 한 명씩 쪼개 넣지 않는 이유 — 매 프레임 상태를 바꾸면 세이브·통지가 잦고, 도중에 끊긴 앱이
    /// 몇 명까지 받았는지 따로 들고 있어야 한다. 끝나는 시각 하나면 어느 시점에 열어도 같은 답이 나온다.
    /// </summary>
    public sealed class TrainingService
    {
        private readonly StateStore _store;
        private readonly IClock _clock;
        private readonly ChapterConfig _config;
        private readonly ProgressionConfig _progression;

        /// <summary>훈련을 막는 바깥 사정(거인 도착). 런타임이 이어 준다.</summary>
        internal Func<bool> IsBlocked { get; set; }

        public TrainingService(StateStore store, IClock clock, ChapterConfig config, ProgressionConfig progression)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ChapterConfig.Empty;
            _progression = progression ?? ProgressionConfig.Empty;
        }

        public TrainingDef Def => _config.Training;

        public long Troops => Def == null ? 0L : _store.State.Counters.Get(ChapterKeys.Troops(Def.UnitId));

        public TrainingView Describe()
        {
            TrainingDef def = Def;
            if (def == null)
                return new TrainingView(TrainingRejection.NoTraining, 0, 0, 0L, 0L, false, 0, 0L, 0L, 0);

            CountPool c = _store.State.Counters;
            int level = ProgressionRules.HighestLevel(_store.State, def.BarracksDefId);
            int count = (int)c.Get(ChapterKeys.TrainCount(def.UnitId));
            long start = c.Get(ChapterKeys.TrainStart(def.UnitId));
            long ends = c.Get(ChapterKeys.TrainEnds(def.UnitId));
            bool running = count > 0;

            int done = 0;
            if (running)
            {
                long now = _clock.NowUnixMs;
                long span = Math.Max(1L, ends - start);
                done = now >= ends ? count : (int)Math.Min(count, (now - start) * count / span);
                done = Math.Max(0, done);
            }

            TrainingRejection availability = level <= 0 ? TrainingRejection.NoBarracks
                : running ? TrainingRejection.Busy
                : Blocked ? TrainingRejection.GiantAtGate
                : TrainingRejection.None;

            return new TrainingView(availability, level, def.BatchCap(level), def.MsPerUnitAt(level),
                c.Get(ChapterKeys.Troops(def.UnitId)), running, count, start, ends, done);
        }

        private bool Blocked => IsBlocked != null && IsBlocked();

        /// <summary>지금 자원과 병영 레벨로 한 번에 맡길 수 있는 최대 수.</summary>
        public int MaxAffordable()
        {
            TrainingView v = Describe();
            if (v.Availability != TrainingRejection.None)
                return 0;

            long max = v.BatchCap;
            for (int i = 0; i < Def.CostPerUnit.Count; i++)
            {
                ResourceCost cost = Def.CostPerUnit[i];
                if (cost.Amount > 0L)
                    max = Math.Min(max, _store.State.Resources.Get(cost.Kind) / cost.Amount);
            }

            return (int)Math.Max(0L, max);
        }

        public TrainingRejection CanStart(int count)
        {
            TrainingView v = Describe();
            if (v.Availability != TrainingRejection.None)
                return v.Availability;

            if (count <= 0)
                return TrainingRejection.ZeroCount;

            if (count > v.BatchCap)
                return TrainingRejection.OverCap;

            for (int i = 0; i < Def.CostPerUnit.Count; i++)
            {
                ResourceCost cost = Def.CostPerUnit[i];
                if (!_store.State.Resources.CanAfford(cost.Kind, cost.Amount * count))
                    return TrainingRejection.NotEnoughResources;
            }

            return TrainingRejection.None;
        }

        public TrainingRejection TryStart(int count)
        {
            TrainingRejection rejection = CanStart(count);
            if (rejection != TrainingRejection.None)
                return rejection;

            TrainingDef def = Def;
            long now = _clock.NowUnixMs;
            int level = ProgressionRules.HighestLevel(_store.State, def.BarracksDefId);
            long ends = now + def.MsPerUnitAt(level) * count;

            var delta = new StateDelta()
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainCount(def.UnitId), count))
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainStart(def.UnitId), now))
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainEnds(def.UnitId), ends));

            for (int i = 0; i < def.CostPerUnit.Count; i++)
                delta.WithResource(ResourceChange.Delta(def.CostPerUnit[i].Kind, -def.CostPerUnit[i].Amount * count));

            _store.Apply(delta);
            return TrainingRejection.None;
        }

        /// <summary>
        /// 헤르메스의 날개로 훈련을 당긴다 — 건물 공사와 같은 가속권. 시작 시각도 같이 당겨서
        /// "다 자란 몫" 계산이 뒤로 가지 않게 한다(당겼는데 숫자가 줄면 고장으로 읽힌다).
        /// </summary>
        public TrainingRejection TrySpeedUp(string itemId)
        {
            TrainingDef def = Def;
            if (def == null)
                return TrainingRejection.NoTraining;

            TrainingView v = Describe();
            if (!v.Running || _clock.NowUnixMs >= v.EndsAtUnixMs)
                return TrainingRejection.NotTraining;

            SpeedUpItemDef item;
            if (!_progression.TryGetSpeedUp(itemId, out item))
                return TrainingRejection.UnknownItem;

            if (_store.State.Items.Get(itemId) <= 0L)
                return TrainingRejection.NoItem;

            long start = Math.Max(1L, v.StartedAtUnixMs - item.ReduceMs);
            long ends = v.EndsAtUnixMs - item.ReduceMs;

            var delta = new StateDelta()
                .WithItem(CountChange.Delta(itemId, -1))
                .WithCounter(CountChange.Delta(ProgressCounters.SpeedUp, 1));

            if (ends <= _clock.NowUnixMs)
            {
                AppendDelivery(delta, def, v.OrderCount);
            }
            else
            {
                delta.WithCounter(CountChange.Absolute(ChapterKeys.TrainStart(def.UnitId), start));
                delta.WithCounter(CountChange.Absolute(ChapterKeys.TrainEnds(def.UnitId), ends));
            }

            _store.Apply(delta);
            return TrainingRejection.None;
        }

        /// <summary>끝난 주문을 병사로 넣는다. 넣은 수(없으면 0).</summary>
        public int CompleteFinished()
        {
            TrainingDef def = Def;
            if (def == null)
                return 0;

            TrainingView v = Describe();
            if (!v.Running || _clock.NowUnixMs < v.EndsAtUnixMs)
                return 0;

            var delta = new StateDelta();
            AppendDelivery(delta, def, v.OrderCount);
            _store.Apply(delta);
            return v.OrderCount;
        }

        private static void AppendDelivery(StateDelta delta, TrainingDef def, int count)
        {
            delta.WithCounter(CountChange.Delta(ChapterKeys.Troops(def.UnitId), count))
                .WithCounter(CountChange.Delta(ChapterKeys.Trained, count))
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainCount(def.UnitId), 0))
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainStart(def.UnitId), 0))
                .WithCounter(CountChange.Absolute(ChapterKeys.TrainEnds(def.UnitId), 0));
        }
    }
}
