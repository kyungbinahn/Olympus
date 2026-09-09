using Olympus.Core.Grid;

namespace Olympus.Core.State
{
    public enum BuildingPhase
    {
        /// <summary>공사 중. 완료 시각이 지나면 Complete가 된다.</summary>
        Constructing = 0,

        /// <summary>완성. 생산·기능이 돌아간다.</summary>
        Complete = 1,
    }

    /// <summary>
    /// 기지에 놓인 건물 하나.
    ///
    /// setter가 <c>internal</c>인 것은 의도적이다 — Game·UI 조립체에서는 읽기만 되고,
    /// 값을 바꾸는 길은 <see cref="StateStore.Apply"/> 하나뿐이다. 그 규약이
    /// 나중에 서버를 붙일 때의 비용을 결정한다(상태를 곳곳에서 직접 고치기 시작하면
    /// 서버 도입이 재작성이 된다).
    /// </summary>
    public sealed class BuildingInstance
    {
        /// <summary>이 기지 안에서 유일한 인스턴스 번호. 격자 점유의 주인 키로도 쓴다.</summary>
        public int Id { get; }

        /// <summary>어떤 건물인지 — 정의 테이블의 키.</summary>
        public string DefId { get; }

        public GridPos Anchor { get; internal set; }

        public GridFootprint Footprint { get; }

        public BuildingPhase Phase { get; internal set; }

        public int Level { get; internal set; }

        /// <summary>
        /// 공사 완료 시각(유닉스 밀리초). 남은 시간을 로컬에서 세지 않고
        /// "끝나는 시각"을 들고 있다가 시계와 비교한다 — 서버가 권위를 갖는 형태와 같아서
        /// 나중에 서버 값으로 바꿔도 계산이 그대로다. Complete면 0.
        /// </summary>
        public long ConstructionEndsAtUnixMs { get; internal set; }

        internal BuildingInstance(
            int id,
            string defId,
            GridPos anchor,
            GridFootprint footprint,
            BuildingPhase phase,
            int level,
            long constructionEndsAtUnixMs)
        {
            Id = id;
            DefId = defId;
            Anchor = anchor;
            Footprint = footprint;
            Phase = phase;
            Level = level;
            ConstructionEndsAtUnixMs = constructionEndsAtUnixMs;
        }

        /// <summary>지정 시각 기준 남은 공사 시간(밀리초). 다 됐으면 0.</summary>
        public long RemainingConstructionMs(long nowUnixMs)
        {
            if (Phase == BuildingPhase.Complete)
                return 0L;

            long remaining = ConstructionEndsAtUnixMs - nowUnixMs;
            return remaining > 0L ? remaining : 0L;
        }

        /// <summary>
        /// 지정 시각에 공사가 끝났는가. Phase를 바꾸지는 않는다 —
        /// 상태 전환은 델타로만 일어난다.
        /// </summary>
        public bool IsConstructionFinished(long nowUnixMs)
        {
            return Phase == BuildingPhase.Constructing && nowUnixMs >= ConstructionEndsAtUnixMs;
        }

        public override string ToString()
        {
            return "Building#" + Id + " " + DefId + " @" + Anchor + " " + Phase + " Lv" + Level;
        }
    }
}
