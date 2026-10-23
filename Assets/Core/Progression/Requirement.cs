using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Progression
{
    public enum RequirementKind
    {
        /// <summary>그 종류 건물 중 가장 높은 레벨이 N 이상. 본부 레벨도 이걸로 쓴다(본부 건물의 레벨).</summary>
        BuildingLevel = 0,

        /// <summary>
        /// 번영도(본부를 뺀 모든 건물 레벨의 합)가 N 이상. 탑포스의 "별"에 대응한다 —
        /// 시설을 올릴수록 쌓이고, 써도 줄지 않는 누적 진척도.
        /// </summary>
        Prosperity = 1,

        /// <summary>누적 기록(<see cref="GameState.Counters"/>)이 N 이상.</summary>
        Counter = 2,
    }

    /// <summary>
    /// 확인만 하고 쓰지 않는 조건 하나. 본부 레벨업 조건과 임무 목표가 같은 모양을 쓴다 —
    /// 둘이 각자 조건 문법을 가지면 "임무는 끝났다는데 본부 조건은 안 찼다" 같은 어긋남이 생긴다.
    /// </summary>
    public sealed class Requirement
    {
        public RequirementKind Kind { get; }

        /// <summary>BuildingLevel이면 건물 종류, Counter면 기록 이름. Prosperity면 비운다.</summary>
        public string Target { get; }

        public long Amount { get; }

        public Requirement(RequirementKind kind, string target, long amount)
        {
            if (amount < 0L)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (kind != RequirementKind.Prosperity && string.IsNullOrEmpty(target))
                throw new ArgumentException(kind + " 조건에는 대상이 있어야 한다.", nameof(target));

            Kind = kind;
            Target = target;
            Amount = amount;
        }

        public static Requirement BuildingLevel(string defId, int level) =>
            new Requirement(RequirementKind.BuildingLevel, defId, level);

        public static Requirement Prosperity(long amount) =>
            new Requirement(RequirementKind.Prosperity, null, amount);

        public static Requirement Counter(string key, long amount) =>
            new Requirement(RequirementKind.Counter, key, amount);

        public override string ToString() => Kind + " " + Target + " " + Amount;
    }

    /// <summary>조건 하나를 지금 상태로 잰 결과. HUD가 "번영도 5/8"처럼 띄운다.</summary>
    public readonly struct RequirementStatus
    {
        public readonly Requirement Requirement;
        public readonly long Current;

        public RequirementStatus(Requirement requirement, long current)
        {
            Requirement = requirement;
            Current = current;
        }

        public long Target => Requirement.Amount;

        public bool IsMet => Current >= Requirement.Amount;
    }

    /// <summary>
    /// 상태에서 진척 수치를 읽는다. 전부 상태에서 계산하고 따로 저장하지 않는다 —
    /// "본부 레벨"이나 "번영도"를 별도 필드로 두면 건물 레벨과 갈라질 수 있다.
    /// 상태에서 다시 셀 수 있는 것은 저장하지 않는다.
    /// </summary>
    public static class ProgressionRules
    {
        /// <summary>본부 레벨 = 본부 건물의 레벨. 본부가 폐허면 0.</summary>
        public static int HqLevel(GameState state, ProgressionConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.HqDefId))
                return 0;

            return HighestLevel(state, config.HqDefId);
        }

        public static int HighestLevel(GameState state, string defId)
        {
            int best = 0;

            foreach (BuildingInstance b in state.Buildings)
            {
                if (b.DefId == defId && b.Level > best)
                    best = b.Level;
            }

            return best;
        }

        /// <summary>
        /// 번영도 — 본부를 뺀 모든 건물 레벨의 합.
        ///
        /// 녹지 복구율을 "별"로 쓰지 않은 이유 — 복구율은 녹지 반경(눈으로 맞추는 슬라이더)에
        /// 따라 달라진다. 화면을 다듬으려고 반경을 바꿨는데 본부 레벨업 조건까지 같이
        /// 움직이면 안 된다. 보이는 것을 맞추는 손잡이와 규칙은 분리한다.
        /// </summary>
        public static long Prosperity(GameState state, ProgressionConfig config)
        {
            string hq = config == null ? null : config.HqDefId;
            long sum = 0L;

            foreach (BuildingInstance b in state.Buildings)
            {
                if (b.DefId != hq)
                    sum += b.Level;
            }

            return sum;
        }

        /// <summary>지금 돌고 있는 공사 수(복구·업그레이드 모두).</summary>
        public static int ConstructingCount(GameState state)
        {
            int count = 0;

            foreach (BuildingInstance b in state.Buildings)
            {
                if (b.Phase == BuildingPhase.Constructing)
                    count++;
            }

            return count;
        }

        /// <summary>지금 본부 레벨의 동시 공사 한도. 0이면 제한 없음.</summary>
        public static int QueueSlots(GameState state, ProgressionConfig config)
        {
            HqLevelDef def;
            if (config == null || !config.TryGetHqLevel(HqLevel(state, config), out def))
                return 0;

            return def.BuildQueueSlots;
        }

        /// <summary>새 공사를 하나 더 시작하면 한도를 넘는가. 즉시 끝나는 공사는 자리를 차지하지 않는다.</summary>
        public static bool IsQueueFull(GameState state, ProgressionConfig config, long durationMs)
        {
            if (durationMs <= 0L)
                return false;

            int slots = QueueSlots(state, config);
            return slots > 0 && ConstructingCount(state) >= slots;
        }

        public static RequirementStatus Evaluate(Requirement r, GameState state, ProgressionConfig config)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            switch (r.Kind)
            {
                case RequirementKind.BuildingLevel:
                    return new RequirementStatus(r, HighestLevel(state, r.Target));

                case RequirementKind.Prosperity:
                    return new RequirementStatus(r, Prosperity(state, config));

                case RequirementKind.Counter:
                    return new RequirementStatus(r, state.Counters.Get(r.Target));

                default:
                    throw new InvalidOperationException("모르는 조건 종류: " + r.Kind);
            }
        }

        public static bool AllMet(IReadOnlyList<Requirement> requirements, GameState state, ProgressionConfig config)
        {
            for (int i = 0; i < requirements.Count; i++)
            {
                if (!Evaluate(requirements[i], state, config).IsMet)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 공사가 끝난 건물을 완성 상태로 넘기는 변경을 델타에 싣는다. 본부가 새 레벨에
        /// 도달했으면 그 레벨 보상도 같은 델타에 싣는다.
        ///
        /// 완성 처리를 여기 한 곳에 둔 이유 — 완성은 세 경로(타이머 만료·즉시 완성·가속으로
        /// 당김)로 일어난다. 경로마다 따로 쓰면 그중 하나에서 보상이나 생산 시작 시각을
        /// 빠뜨린다. 녹지 재계산을 타이머 경로에만 붙였다가 즉시 완성에서 녹지가 안 뜬
        /// 사고(<c>TerritoryRuntime</c> 주석)와 같은 부류다.
        /// </summary>
        /// <param name="completedAtUnixMs">
        /// 공사가 실제로 끝난 시각. 지금이 아니라 끝나는 시각을 넣는다 — 앱을 꺼 둔 사이에
        /// 끝났으면 그때부터 생산이 쌓여 있어야 한다.
        /// </param>
        public static void AppendCompletion(
            StateDelta delta, BuildingInstance b, int newLevel, long completedAtUnixMs, ProgressionConfig config)
        {
            delta.WithBuilding(BuildingChange.Update(
                b.Id, b.Anchor, BuildingPhase.Complete, newLevel, 0L, completedAtUnixMs));

            if (config == null || b.DefId != config.HqDefId)
                return;

            HqLevelDef reached;
            if (!config.TryGetHqLevel(newLevel, out reached))
                return;

            delta.WithResources(reached.RewardResources);

            for (int i = 0; i < reached.RewardItems.Count; i++)
            {
                ItemGrant g = reached.RewardItems[i];
                delta.WithItem(CountChange.Delta(g.ItemId, g.Amount));
            }
        }
    }
}
