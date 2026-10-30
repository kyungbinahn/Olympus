using System;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Progression
{
    public enum CollectRejection
    {
        None = 0,
        UnknownSlot,
        UnknownDef,

        /// <summary>생산하는 건물이 아니다.</summary>
        NotProducer,

        /// <summary>완성 상태가 아니다 — 폐허·공사 중에는 생산이 멈춘다.</summary>
        NotComplete,

        /// <summary>아직 1개도 안 쌓였다.</summary>
        NothingStored,
    }

    /// <summary>누적 기록 이름. 임무 조건(<see cref="RequirementKind.Counter"/>)이 이 이름으로 센다.</summary>
    public static class ProgressCounters
    {
        public const string Collect = "collect";
        public const string SpeedUp = "speedup";
    }

    /// <summary>
    /// 생산 건물에 쌓인 자원을 계산하고 거둔다.
    ///
    /// 쌓인 양은 상태에 들고 있지 않다 — 생산 시작 시각(<see cref="BuildingInstance.ProductionAnchorUnixMs"/>)과
    /// 지금의 차로 매번 계산한다. 매 초 더하는 방식이면 앱이 꺼진 동안 생산이 멈추고, 서버가 붙으면
    /// 클라이언트와 서버가 따로 더하다 어긋난다.
    /// </summary>
    public sealed class ProductionService
    {
        private const long MsPerMinute = 60_000L;

        private readonly StateStore _store;
        private readonly BuildingCatalog _catalog;
        private readonly IClock _clock;

        public ProductionService(StateStore store, BuildingCatalog catalog, IClock clock)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>레벨을 반영한 분당 생산량. 생산 건물이 아니면 0.</summary>
        public static long RatePerMinute(BuildingInstance b, BuildingDef def)
        {
            if (def.Production == null || b.Level <= 0)
                return 0L;

            return def.Production.PerMinute * b.Level;
        }

        /// <summary>보관 한도. 한도도 레벨 배로 늘어난다(같은 시간만큼 쌓아 둘 수 있다).</summary>
        public static long Capacity(BuildingInstance b, BuildingDef def)
        {
            return RatePerMinute(b, def) * (def.Production == null ? 0L : def.Production.CapMinutes);
        }

        public static long StoredAmount(BuildingInstance b, BuildingDef def, long nowUnixMs)
        {
            if (b.Phase != BuildingPhase.Complete)
                return 0L;

            long rate = RatePerMinute(b, def);
            if (rate <= 0L)
                return 0L;

            long elapsed = nowUnixMs - b.ProductionAnchorUnixMs;
            if (elapsed <= 0L)
                return 0L;

            long cap = Capacity(b, def);

            // 곱하기 전에 한도를 넘는 시간이면 바로 한도를 돌려준다 — 생산 시작 시각이 0인
            // 옛 세이브에서 elapsed가 50년어치라 곱하면 넘칠 수 있다.
            if (elapsed >= def.Production.CapMinutes * MsPerMinute)
                return cap;

            long amount = elapsed * rate / MsPerMinute;
            return amount < cap ? amount : cap;
        }

        public CollectRejection CanCollect(int slotId)
        {
            BuildingInstance b;
            BuildingDef def;
            return Check(slotId, out b, out def);
        }

        /// <summary>쌓인 것을 거둔다. 거둔 양을 돌려준다(못 거두면 0).</summary>
        public long TryCollect(int slotId, out CollectRejection rejection)
        {
            BuildingInstance b;
            BuildingDef def;
            rejection = Check(slotId, out b, out def);

            if (rejection != CollectRejection.None)
                return 0L;

            var delta = new StateDelta();
            long amount = AppendCollect(delta, b, def, _clock.NowUnixMs);
            delta.WithCounter(CountChange.Delta(ProgressCounters.Collect, 1));

            _store.Apply(delta);
            return amount;
        }

        /// <summary>
        /// 거둘 게 있는 생산 건물을 전부 거둔다. 거둔 건물 수를 돌려준다.
        /// 한 델타로 묶는다 — 건물마다 따로 적용하면 자원 표시가 건물 수만큼 깜빡인다.
        /// </summary>
        public int CollectAll()
        {
            long now = _clock.NowUnixMs;
            StateDelta delta = null;
            int count = 0;

            foreach (BuildingInstance b in _store.State.Buildings)
            {
                BuildingDef def;
                if (!_catalog.TryGet(b.DefId, out def) || StoredAmount(b, def, now) <= 0L)
                    continue;

                if (delta == null)
                    delta = new StateDelta();

                AppendCollect(delta, b, def, now);
                count++;
            }

            if (delta == null)
                return 0;

            delta.WithCounter(CountChange.Delta(ProgressCounters.Collect, 1));
            _store.Apply(delta);
            return count;
        }

        /// <summary>
        /// 거두기 변경을 싣는다. 생산 시작 시각은 "지금"으로 덮지 않고 <b>거둔 양만큼만</b> 민다 —
        /// 지금으로 덮으면 분 단위 생산에서 1개가 되기 직전의 몫이 매번 버려져서, 자주 거둘수록
        /// 손해를 보는 이상한 규칙이 된다. 한도까지 찼을 때만 지금으로 맞춘다(넘친 시간은 원래 버려진다).
        /// </summary>
        private static long AppendCollect(StateDelta delta, BuildingInstance b, BuildingDef def, long now)
        {
            long amount = StoredAmount(b, def, now);
            long rate = RatePerMinute(b, def);

            long nextAnchor;
            if (amount >= Capacity(b, def))
            {
                nextAnchor = now;
            }
            else
            {
                // 거둔 양을 만드는 데 든 시간(올림). 이만큼만 앞으로 민다.
                long consumedMs = (amount * MsPerMinute + rate - 1L) / rate;
                nextAnchor = b.ProductionAnchorUnixMs + consumedMs;
            }

            delta.WithResource(ResourceChange.Delta(def.Production.Kind, amount));
            delta.WithBuilding(BuildingChange.Update(
                b.Id, b.Anchor, b.Phase, b.Level, b.ConstructionEndsAtUnixMs, nextAnchor));

            return amount;
        }

        private CollectRejection Check(int slotId, out BuildingInstance b, out BuildingDef def)
        {
            def = null;

            if (!_store.State.TryGetBuilding(slotId, out b))
                return CollectRejection.UnknownSlot;

            if (!_catalog.TryGet(b.DefId, out def))
                return CollectRejection.UnknownDef;

            if (def.Production == null)
                return CollectRejection.NotProducer;

            if (b.Phase != BuildingPhase.Complete)
                return CollectRejection.NotComplete;

            if (StoredAmount(b, def, _clock.NowUnixMs) <= 0L)
                return CollectRejection.NothingStored;

            return CollectRejection.None;
        }
    }
}
