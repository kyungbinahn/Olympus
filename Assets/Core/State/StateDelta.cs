using System.Collections.Generic;
using Olympus.Core.Grid;

namespace Olympus.Core.State
{
    public enum ChangeOp
    {
        Add = 0,
        Update = 1,
        Remove = 2,
    }

    /// <summary>자원 변경 한 줄. 증감(Delta)과 절대값(Absolute) 둘 다 표현한다.</summary>
    public readonly struct ResourceChange
    {
        public readonly ResourceKind Kind;
        public readonly long Value;

        /// <summary>true면 <see cref="Value"/>가 최종값, false면 증감분.</summary>
        public readonly bool IsAbsolute;

        private ResourceChange(ResourceKind kind, long value, bool isAbsolute)
        {
            Kind = kind;
            Value = value;
            IsAbsolute = isAbsolute;
        }

        /// <summary>로컬 로직이 쓰는 형태 — "나무 30 깎아라".</summary>
        public static ResourceChange Delta(ResourceKind kind, long delta) =>
            new ResourceChange(kind, delta, false);

        /// <summary>서버가 쓰는 형태 — "나무는 지금 1200이다". 어긋남이 누적되지 않는다.</summary>
        public static ResourceChange Absolute(ResourceKind kind, long value) =>
            new ResourceChange(kind, value, true);
    }

    /// <summary>
    /// 건물 변경 한 줄. Add·Update는 가변 필드를 전부 실어 통째로 덮는다.
    /// 부분 갱신을 허용하면 "어느 필드가 유효한가"를 호출부마다 따져야 해서
    /// 조용히 어긋나는 사고가 난다.
    /// </summary>
    public sealed class BuildingChange
    {
        public ChangeOp Op { get; private set; }
        public int Id { get; private set; }
        public string DefId { get; private set; }
        public GridPos Anchor { get; private set; }
        public GridFootprint Footprint { get; private set; }
        public BuildingPhase Phase { get; private set; }
        public int Level { get; private set; }
        public long ConstructionEndsAtUnixMs { get; private set; }

        private BuildingChange() { }

        public static BuildingChange Add(
            int id,
            string defId,
            GridPos anchor,
            GridFootprint footprint,
            BuildingPhase phase,
            int level,
            long constructionEndsAtUnixMs)
        {
            return new BuildingChange
            {
                Op = ChangeOp.Add,
                Id = id,
                DefId = defId,
                Anchor = anchor,
                Footprint = footprint,
                Phase = phase,
                Level = level,
                ConstructionEndsAtUnixMs = constructionEndsAtUnixMs,
            };
        }

        public static BuildingChange Update(
            int id,
            GridPos anchor,
            BuildingPhase phase,
            int level,
            long constructionEndsAtUnixMs)
        {
            return new BuildingChange
            {
                Op = ChangeOp.Update,
                Id = id,
                Anchor = anchor,
                Phase = phase,
                Level = level,
                ConstructionEndsAtUnixMs = constructionEndsAtUnixMs,
            };
        }

        public static BuildingChange Remove(int id)
        {
            return new BuildingChange { Op = ChangeOp.Remove, Id = id };
        }
    }

    /// <summary>
    /// 상태 변경분 봉투. 채워진 항목만 유효하고 null은 "변경 없음"이다.
    ///
    /// 이 모양은 뉴포리아의 <c>GeneralInfoDto</c>에서 가져왔다 — 거기서는 거의 모든 서버
    /// 응답이 이 봉투를 함께 실어 보내고, 적용기 하나가 매니저들에 팬아웃한 뒤 옵저버로
    /// 전파한다. 그래서 "서버가 봉투만 채우면 화폐 갱신·이펙트·보상 표시가 알아서 돈다"가
    /// 성립한다. 그 성질이 이 프로젝트에 서버를 나중에 붙일 수 있게 만드는 유일한 장치다.
    /// </summary>
    public sealed class StateDelta
    {
        public List<ResourceChange> Resources { get; private set; }
        public List<BuildingChange> Buildings { get; private set; }
        public long? ServerTimeUnixMs { get; set; }

        public bool IsEmpty =>
            (Resources == null || Resources.Count == 0)
            && (Buildings == null || Buildings.Count == 0)
            && !ServerTimeUnixMs.HasValue;

        public StateDelta WithResource(ResourceChange change)
        {
            if (Resources == null)
                Resources = new List<ResourceChange>(4);

            Resources.Add(change);
            return this;
        }

        public StateDelta WithResourceCosts(IReadOnlyList<ResourceCost> costs)
        {
            if (costs == null)
                return this;

            for (int i = 0; i < costs.Count; i++)
            {
                WithResource(ResourceChange.Delta(costs[i].Kind, -costs[i].Amount));
            }

            return this;
        }

        public StateDelta WithBuilding(BuildingChange change)
        {
            if (Buildings == null)
                Buildings = new List<BuildingChange>(4);

            Buildings.Add(change);
            return this;
        }
    }
}
