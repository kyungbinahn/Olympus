using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// 업그레이드 하나를 지금 시작할 수 있는지, 하면 무엇이 드는지. HUD가 그대로 띄운다.
    /// 판단을 뷰에 두지 않고 여기서 끝내는 이유는 <see cref="BuildingStatusView"/>와 같다.
    /// </summary>
    public readonly struct UpgradePlan
    {
        public readonly bool CanStart;
        public readonly BuildRejection Rejection;

        /// <summary>올라갈 레벨. 다음 레벨이 없으면 지금 레벨.</summary>
        public readonly int TargetLevel;

        public readonly IReadOnlyList<CostLine> Costs;
        public readonly long DurationMs;

        /// <summary>본부일 때만 채워진다 — 번영도·선행 건물 조건.</summary>
        public readonly IReadOnlyList<RequirementStatus> Requirements;

        /// <summary>건물 레벨 상한(= 본부 레벨)에 막혔을 때, 몇 레벨 본부가 필요한가.</summary>
        public readonly int RequiredHqLevel;

        public UpgradePlan(
            bool canStart,
            BuildRejection rejection,
            int targetLevel,
            IReadOnlyList<CostLine> costs,
            long durationMs,
            IReadOnlyList<RequirementStatus> requirements,
            int requiredHqLevel)
        {
            CanStart = canStart;
            Rejection = rejection;
            TargetLevel = targetLevel;
            Costs = costs ?? Array.Empty<CostLine>();
            DurationMs = durationMs;
            Requirements = requirements ?? Array.Empty<RequirementStatus>();
            RequiredHqLevel = requiredHqLevel;
        }
    }

    /// <summary>
    /// 완성된 건물을 다음 레벨로 올린다. 본부도 같은 길을 탄다 — 다만 본부는 본부 레벨 표의
    /// 조건(번영도·선행 건물)을 보고, 다른 건물은 "본부 레벨이 곧 상한"을 본다.
    ///
    /// 그 상한 규칙은 뉴포리아(본부 레벨 게이트가 1,105회 등장)와 탑포스(시설 상한 = 기지 레벨)가
    /// 똑같이 쓰는 이 장르의 뼈대다. 그래서 "건물만 계속 올리기"도 "본부만 올리기"도 막히고,
    /// 둘을 번갈아 하게 된다.
    /// </summary>
    public sealed class UpgradeService
    {
        private readonly StateStore _store;
        private readonly BuildingCatalog _catalog;
        private readonly IClock _clock;
        private readonly ProgressionConfig _config;

        public UpgradeService(StateStore store, BuildingCatalog catalog, IClock clock, ProgressionConfig config)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ProgressionConfig.Empty;
        }

        /// <summary>상태를 바꾸지 않는다 — 패널이 매 프레임 불러도 안전하다.</summary>
        public UpgradePlan Describe(int slotId)
        {
            GameState state = _store.State;

            BuildingInstance b;
            if (!state.TryGetBuilding(slotId, out b))
                return Fail(BuildRejection.UnknownSlot, 0);

            BuildingDef def;
            if (!_catalog.TryGet(b.DefId, out def))
                return Fail(BuildRejection.UnknownDef, b.Level);

            if (b.Phase != BuildingPhase.Complete)
                return Fail(BuildRejection.NotComplete, b.Level);

            int target = b.Level + 1;
            bool isHq = b.DefId == _config.HqDefId;

            IReadOnlyList<ResourceCost> costs;
            long duration;
            List<RequirementStatus> requirements = null;
            BuildRejection blocker = BuildRejection.None;
            int requiredHq = 0;

            if (isHq)
            {
                HqLevelDef next;
                if (!_config.TryGetHqLevel(target, out next))
                    return Fail(BuildRejection.MaxLevel, b.Level);

                costs = next.Costs;
                duration = next.DurationMs;
                requirements = new List<RequirementStatus>(next.Requirements.Count);

                for (int i = 0; i < next.Requirements.Count; i++)
                {
                    RequirementStatus rs = ProgressionRules.Evaluate(next.Requirements[i], state, _config);
                    requirements.Add(rs);

                    if (!rs.IsMet)
                        blocker = BuildRejection.RequirementsNotMet;
                }
            }
            else
            {
                UpgradeStep step;
                if (!_config.TryGetUpgradeStep(target, out step))
                    return Fail(BuildRejection.MaxLevel, b.Level);

                costs = Scale(def.BuildCosts, step.CostMultiplier);
                duration = step.DurationMs;

                if (target > ProgressionRules.HqLevel(state, _config))
                {
                    blocker = BuildRejection.HqLevelTooLow;
                    requiredHq = target;
                }
            }

            List<CostLine> lines = ToCostLines(costs, state.Resources);

            if (blocker == BuildRejection.None && ProgressionRules.IsQueueFull(state, _config, duration))
                blocker = BuildRejection.QueueFull;

            if (blocker == BuildRejection.None && !state.Resources.CanAfford(costs))
                blocker = BuildRejection.InsufficientResources;

            return new UpgradePlan(
                blocker == BuildRejection.None, blocker, target, lines, duration, requirements, requiredHq);
        }

        /// <summary>
        /// 업그레이드를 시작한다. 생산 건물이면 쌓인 것을 같은 델타로 먼저 거둬 준다 —
        /// 공사 중에는 생산이 멈추므로, 안 거두고 시작하면 쌓인 것이 사라진다(플레이어 입장에서는 도둑맞은 것).
        /// </summary>
        public BuildResult TryStart(int slotId)
        {
            UpgradePlan plan = Describe(slotId);
            if (!plan.CanStart)
                return BuildResult.Fail(plan.Rejection);

            BuildingInstance b;
            _store.State.TryGetBuilding(slotId, out b);

            BuildingDef def;
            _catalog.TryGet(b.DefId, out def);

            long now = _clock.NowUnixMs;
            var delta = new StateDelta();

            for (int i = 0; i < plan.Costs.Count; i++)
            {
                delta.WithResource(ResourceChange.Delta(plan.Costs[i].Kind, -plan.Costs[i].Amount));
            }

            if (def.Production != null)
            {
                long stored = ProductionService.StoredAmount(b, def, now);
                if (stored > 0L)
                    delta.WithResource(ResourceChange.Delta(def.Production.Kind, stored));
            }

            if (plan.DurationMs <= 0L)
            {
                ProgressionRules.AppendCompletion(delta, b, plan.TargetLevel, now, _config);
            }
            else
            {
                delta.WithBuilding(BuildingChange.Update(
                    b.Id, b.Anchor, BuildingPhase.Constructing, b.Level, now + plan.DurationMs, now));
            }

            _store.Apply(delta);
            return BuildResult.Ok;
        }

        /// <summary>
        /// 복구 비용에 배수를 곱한다. 올림이 아니라 반올림인 이유 — 배수 1.5에 비용 25가
        /// 37.5가 되는 식의 경계에서 올림은 매번 한쪽으로만 밀어서, 레벨이 쌓이면
        /// 데이터에 적은 곡선보다 눈에 띄게 비싸진다.
        /// </summary>
        public static IReadOnlyList<ResourceCost> Scale(IReadOnlyList<ResourceCost> baseCosts, double multiplier)
        {
            var scaled = new List<ResourceCost>(baseCosts.Count);

            for (int i = 0; i < baseCosts.Count; i++)
            {
                long amount = (long)Math.Round(baseCosts[i].Amount * multiplier, MidpointRounding.AwayFromZero);
                scaled.Add(new ResourceCost(baseCosts[i].Kind, amount));
            }

            return scaled;
        }

        private static List<CostLine> ToCostLines(IReadOnlyList<ResourceCost> costs, ResourcePool resources)
        {
            var lines = new List<CostLine>(costs.Count);

            for (int i = 0; i < costs.Count; i++)
            {
                lines.Add(new CostLine(costs[i].Kind, costs[i].Amount, resources.CanAfford(costs[i].Kind, costs[i].Amount)));
            }

            return lines;
        }

        private static UpgradePlan Fail(BuildRejection rejection, int level)
        {
            return new UpgradePlan(false, rejection, level, null, 0L, null, 0);
        }
    }
}
