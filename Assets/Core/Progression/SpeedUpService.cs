using System;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Progression
{
    public enum SpeedUpRejection
    {
        None = 0,
        UnknownSlot,

        /// <summary>공사 중이 아니다.</summary>
        NotConstructing,

        /// <summary>가속 아이템이 아니다(데이터에 없는 이름).</summary>
        UnknownItem,

        /// <summary>가방에 그 아이템이 없다.</summary>
        NoItem,
    }

    /// <summary>
    /// 가속 아이템(헤르메스의 날개)으로 공사 끝나는 시각을 당긴다.
    ///
    /// 가속은 "남은 시간 줄이기"가 아니라 <b>끝나는 시각을 앞으로 옮기기</b>다. 타이머를 끝나는
    /// 시각으로 들고 있으니 가속도 같은 값을 고치는 델타 하나로 끝난다. 그래서 나중에 무료
    /// 가속(동맹 도움)이나 유료 즉시 완료가 생겨도 같은 길을 탄다 — 출처만 다르다.
    /// </summary>
    public sealed class SpeedUpService
    {
        private readonly StateStore _store;
        private readonly IClock _clock;
        private readonly ProgressionConfig _config;

        public SpeedUpService(StateStore store, IClock clock, ProgressionConfig config)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ProgressionConfig.Empty;
        }

        public SpeedUpRejection CanUse(int slotId, string itemId)
        {
            BuildingInstance b;
            SpeedUpItemDef item;
            return Check(slotId, itemId, out b, out item);
        }

        public SpeedUpRejection TryUse(int slotId, string itemId)
        {
            BuildingInstance b;
            SpeedUpItemDef item;
            SpeedUpRejection rejection = Check(slotId, itemId, out b, out item);

            if (rejection != SpeedUpRejection.None)
                return rejection;

            long now = _clock.NowUnixMs;
            long endsAt = b.ConstructionEndsAtUnixMs - item.ReduceMs;

            var delta = new StateDelta()
                .WithItem(CountChange.Delta(item.ItemId, -1))
                .WithCounter(CountChange.Delta(ProgressCounters.SpeedUp, 1));

            // 당겨서 이미 지났으면 다음 프레임을 기다리지 않고 같은 델타에서 끝낸다 —
            // 버튼을 눌렀는데 한 프레임 동안 "공사 중 00:00"이 보이면 고장 난 것처럼 읽힌다.
            if (endsAt <= now)
            {
                ProgressionRules.AppendCompletion(delta, b, b.Level + 1, now, _config);
            }
            else
            {
                delta.WithBuilding(BuildingChange.Update(
                    b.Id, b.Anchor, b.Phase, b.Level, endsAt, b.ProductionAnchorUnixMs));
            }

            _store.Apply(delta);
            return SpeedUpRejection.None;
        }

        private SpeedUpRejection Check(int slotId, string itemId, out BuildingInstance b, out SpeedUpItemDef item)
        {
            item = null;

            if (!_store.State.TryGetBuilding(slotId, out b))
                return SpeedUpRejection.UnknownSlot;

            if (b.Phase != BuildingPhase.Constructing || b.IsConstructionFinished(_clock.NowUnixMs))
                return SpeedUpRejection.NotConstructing;

            if (!_config.TryGetSpeedUp(itemId, out item))
                return SpeedUpRejection.UnknownItem;

            if (_store.State.Items.Get(itemId) <= 0L)
                return SpeedUpRejection.NoItem;

            return SpeedUpRejection.None;
        }
    }
}
