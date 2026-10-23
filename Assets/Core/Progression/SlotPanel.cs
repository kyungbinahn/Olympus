using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// 선택한 자리 패널이 보여 줄 것 전부. 복구·업그레이드·생산·공사 판단을 한 번에 모아 둔다.
    ///
    /// <see cref="BuildingStatusView"/>를 감싸는 이유 — 그건 "폐허/공사 중/완성"만 아는 요약이고,
    /// 본부 레벨이 생기면서 같은 자리가 "잠김·대기열 가득·상한 도달·생산 중" 같은 상태를 더 갖게 됐다.
    /// 그 판단이 뷰에 흩어지면 UGUI를 열어 봐야만 맞는지 알 수 있다. 여기 두면 시험이 붙는다.
    /// </summary>
    public readonly struct SlotPanel
    {
        public readonly BuildingStatusView Status;

        public readonly bool IsHq;

        /// <summary>폐허일 때 — 지금 복구할 수 있는지와 아니면 왜인지.</summary>
        public readonly BuildResult RepairCheck;

        /// <summary>이 건물이 열리는 본부 레벨. 잠김 안내에 쓴다.</summary>
        public readonly int UnlockHqLevel;

        /// <summary>완성일 때 — 다음 레벨 계획.</summary>
        public readonly UpgradePlan Upgrade;

        public readonly bool IsProducer;
        public readonly ResourceKind ProducesKind;
        public readonly long RatePerMinute;

        /// <summary>레벨 1 기준 분당 생산량 — 폐허일 때 "복구하면 무엇을 얼마나 주나"를 보여 준다.</summary>
        public readonly long BaseRatePerMinute;

        public readonly long Stored;
        public readonly long Capacity;

        public SlotPanel(
            BuildingStatusView status,
            bool isHq,
            BuildResult repairCheck,
            int unlockHqLevel,
            UpgradePlan upgrade,
            bool isProducer,
            ResourceKind producesKind,
            long ratePerMinute,
            long baseRatePerMinute,
            long stored,
            long capacity)
        {
            Status = status;
            IsHq = isHq;
            RepairCheck = repairCheck;
            UnlockHqLevel = unlockHqLevel;
            Upgrade = upgrade;
            IsProducer = isProducer;
            ProducesKind = producesKind;
            RatePerMinute = ratePerMinute;
            BaseRatePerMinute = baseRatePerMinute;
            Stored = stored;
            Capacity = capacity;
        }

        public bool IsLocked => Status.Phase == BuildingPhase.Ruined && RepairCheck.Rejection == BuildRejection.HqLevelTooLow;

        public bool CanCollect => IsProducer && Stored > 0L;

        public static bool TryDescribe(TerritoryRuntime runtime, int slotId, out SlotPanel panel)
        {
            panel = default;

            BuildingInstance b;
            if (slotId < 0 || !runtime.State.TryGetBuilding(slotId, out b))
                return false;

            BuildingDef def;
            if (!runtime.Catalog.TryGet(b.DefId, out def))
                return false;

            long now = runtime.Clock.NowUnixMs;
            BuildingStatusView status = BuildingStatusView.Describe(b, def, runtime.State.Resources, now);

            BuildResult repair = b.IsRuined ? runtime.Construction.CanStartRepair(slotId) : BuildResult.Fail(BuildRejection.NotRuined);
            UpgradePlan upgrade = runtime.Upgrades.Describe(slotId);

            bool producer = def.Production != null;

            panel = new SlotPanel(
                status,
                b.DefId == runtime.Progression.HqDefId,
                repair,
                def.UnlockHqLevel,
                upgrade,
                producer,
                producer ? def.Production.Kind : default(ResourceKind),
                ProductionService.RatePerMinute(b, def),
                producer ? def.Production.PerMinute : 0L,
                ProductionService.StoredAmount(b, def, now),
                ProductionService.Capacity(b, def));

            return true;
        }
    }
}
