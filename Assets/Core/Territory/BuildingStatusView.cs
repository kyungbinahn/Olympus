using System;
using System.Collections.Generic;
using Olympus.Core.State;

namespace Olympus.Core.Territory
{
    /// <summary>비용 한 줄 + 지금 감당할 수 있는지. HUD가 부족한 자원을 짚어 보여줄 때 쓴다.</summary>
    public readonly struct CostLine
    {
        public readonly ResourceKind Kind;
        public readonly long Amount;
        public readonly bool Affordable;

        public CostLine(ResourceKind kind, long amount, bool affordable)
        {
            Kind = kind;
            Amount = amount;
            Affordable = affordable;
        }
    }

    /// <summary>
    /// 선택한 자리를 HUD에 어떻게 보여줄지 요약한 것.
    ///
    /// Phase별로 무엇을 보여줄지(비용/남은 시간/레벨) 가르는 판단을 뷰 코드에 흩어 두지
    /// 않고 여기 한 곳에 모았다 — 그래야 Unity 없이 dotnet test로 검증되고, 나중에
    /// 뷰를 UGUI에서 다른 것으로 바꿔도 판단 로직은 그대로 재사용된다.
    /// </summary>
    public readonly struct BuildingStatusView
    {
        public readonly int SlotId;
        public readonly string DefId;
        public readonly string DisplayNameKey;
        public readonly BuildingPhase Phase;
        public readonly int Level;

        /// <summary>공사 중이 아니면 0.</summary>
        public readonly long RemainingConstructionMs;

        /// <summary>폐허가 아니면 빈 목록 — 완성/공사 중인 자리는 다시 지을 비용이 없다.</summary>
        public readonly IReadOnlyList<CostLine> RepairCosts;

        /// <summary>폐허이고 비용을 전부 감당할 수 있을 때만 true.</summary>
        public readonly bool CanRepair;

        private BuildingStatusView(
            int slotId,
            string defId,
            string displayNameKey,
            BuildingPhase phase,
            int level,
            long remainingConstructionMs,
            IReadOnlyList<CostLine> repairCosts,
            bool canRepair)
        {
            SlotId = slotId;
            DefId = defId;
            DisplayNameKey = displayNameKey;
            Phase = phase;
            Level = level;
            RemainingConstructionMs = remainingConstructionMs;
            RepairCosts = repairCosts;
            CanRepair = canRepair;
        }

        public static BuildingStatusView Describe(
            BuildingInstance building, BuildingDef def, ResourcePool resources, long nowUnixMs)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (resources == null) throw new ArgumentNullException(nameof(resources));

            List<CostLine> costs = null;
            bool canRepair = false;

            if (building.IsRuined)
            {
                costs = new List<CostLine>(def.BuildCosts.Count);
                bool allAffordable = true;

                for (int i = 0; i < def.BuildCosts.Count; i++)
                {
                    ResourceCost c = def.BuildCosts[i];
                    bool affordable = resources.CanAfford(c.Kind, c.Amount);

                    if (!affordable)
                        allAffordable = false;

                    costs.Add(new CostLine(c.Kind, c.Amount, affordable));
                }

                canRepair = allAffordable;
            }

            return new BuildingStatusView(
                building.Id,
                building.DefId,
                def.DisplayNameKey,
                building.Phase,
                building.Level,
                building.RemainingConstructionMs(nowUnixMs),
                (IReadOnlyList<CostLine>)costs ?? Array.Empty<CostLine>(),
                canRepair);
        }
    }
}
