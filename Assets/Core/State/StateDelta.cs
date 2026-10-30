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
    /// 이름으로 세는 값 하나의 변경 — 아이템 개수와 누적 기록(카운터)이 같은 모양을 쓴다.
    /// 자원(<see cref="ResourceChange"/>)과 따로 둔 이유는 <see cref="ResourceKind"/> 주석에 있다 —
    /// 자원 축은 넷으로 고정하고, 늘어나는 것들은 문자열 키 공간에 둔다.
    /// </summary>
    public readonly struct CountChange
    {
        public readonly string Key;
        public readonly long Value;
        public readonly bool IsAbsolute;

        private CountChange(string key, long value, bool isAbsolute)
        {
            Key = key;
            Value = value;
            IsAbsolute = isAbsolute;
        }

        public static CountChange Delta(string key, long delta) => new CountChange(key, delta, false);

        public static CountChange Absolute(string key, long value) => new CountChange(key, value, true);
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
        public long ProductionAnchorUnixMs { get; private set; }

        private BuildingChange() { }

        public static BuildingChange Add(
            int id,
            string defId,
            GridPos anchor,
            GridFootprint footprint,
            BuildingPhase phase,
            int level,
            long constructionEndsAtUnixMs,
            long productionAnchorUnixMs)
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
                ProductionAnchorUnixMs = productionAnchorUnixMs,
            };
        }

        public static BuildingChange Update(
            int id,
            GridPos anchor,
            BuildingPhase phase,
            int level,
            long constructionEndsAtUnixMs,
            long productionAnchorUnixMs)
        {
            return new BuildingChange
            {
                Op = ChangeOp.Update,
                Id = id,
                Anchor = anchor,
                Phase = phase,
                Level = level,
                ConstructionEndsAtUnixMs = constructionEndsAtUnixMs,
                ProductionAnchorUnixMs = productionAnchorUnixMs,
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

        /// <summary>아이템(헤르메스의 날개 등) 개수 변경.</summary>
        public List<CountChange> Items { get; private set; }

        /// <summary>
        /// 누적 기록 변경("수확한 횟수" 같은 것). 상태만 보고는 알 수 없는 "해 봤는가"를
        /// 임무 조건으로 쓰려고 둔다.
        /// </summary>
        public List<CountChange> Counters { get; private set; }

        /// <summary>임무 체인에서 지금 몇 번째인가. null이면 변경 없음.</summary>
        public int? QuestIndex { get; set; }

        public long? ServerTimeUnixMs { get; set; }

        public bool IsEmpty =>
            (Resources == null || Resources.Count == 0)
            && (Buildings == null || Buildings.Count == 0)
            && (Items == null || Items.Count == 0)
            && (Counters == null || Counters.Count == 0)
            && !QuestIndex.HasValue
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

        public StateDelta WithResources(IReadOnlyList<ResourceCost> gains)
        {
            if (gains == null)
                return this;

            for (int i = 0; i < gains.Count; i++)
            {
                WithResource(ResourceChange.Delta(gains[i].Kind, gains[i].Amount));
            }

            return this;
        }

        public StateDelta WithItem(CountChange change)
        {
            if (Items == null)
                Items = new List<CountChange>(2);

            Items.Add(change);
            return this;
        }

        public StateDelta WithCounter(CountChange change)
        {
            if (Counters == null)
                Counters = new List<CountChange>(2);

            Counters.Add(change);
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
