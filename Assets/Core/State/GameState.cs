using System.Collections.Generic;
using Olympus.Core.Grid;

namespace Olympus.Core.State
{
    /// <summary>
    /// 클라이언트가 들고 있는 권위 상태.
    ///
    /// 밖에서는 읽기만 된다. 바꾸는 길은 <see cref="StateStore.Apply"/> 하나다.
    /// 지금은 그 델타를 로컬 로직이 만들고, 서버가 붙으면 서버 응답이 만든다 —
    /// 적용하는 쪽 코드는 그대로다.
    /// </summary>
    public sealed class GameState
    {
        private readonly Dictionary<int, BuildingInstance> _buildings =
            new Dictionary<int, BuildingInstance>();

        public ResourcePool Resources { get; } = new ResourcePool();

        /// <summary>가방 — 헤르메스의 날개(가속) 같은 소모품.</summary>
        public CountPool Items { get; } = new CountPool();

        /// <summary>누적 기록. 수확 횟수·가속 사용 횟수처럼 "해 봤는가"를 센다.</summary>
        public CountPool Counters { get; } = new CountPool();

        /// <summary>임무 체인에서 지금 몇 번째 임무인가(0부터). 체인 길이와 같으면 전부 끝낸 것.</summary>
        public int QuestIndex { get; internal set; }

        /// <summary>기지 격자의 점유 판. 건물 변경 시 스토어가 함께 갱신한다.</summary>
        public GridOccupancy BaseOccupancy { get; }

        /// <summary>기지 격자의 칸↔월드 변환.</summary>
        public SquareGrid BaseGrid { get; }

        /// <summary>
        /// 마지막으로 확인한 서버 시각. 서버가 없는 동안은 로컬 시계 값이 들어온다.
        /// 타이머는 이 값이 아니라 <see cref="Olympus.Core.Time.IClock"/>을 보고,
        /// 이 필드는 "서버와 얼마나 어긋나 있나"를 재는 용도로 남겨둔다.
        /// </summary>
        public long LastKnownServerTimeUnixMs { get; internal set; }

        public GameState(SquareGrid baseGrid, GridRect baseBounds)
        {
            BaseGrid = baseGrid;
            BaseOccupancy = new GridOccupancy(baseBounds);
        }

        public int BuildingCount => _buildings.Count;

        public bool TryGetBuilding(int id, out BuildingInstance building)
        {
            return _buildings.TryGetValue(id, out building);
        }

        public IEnumerable<BuildingInstance> Buildings => _buildings.Values;

        /// <summary>그 칸을 쓰는 건물. 없으면 false.</summary>
        public bool TryGetBuildingAt(GridPos cell, out BuildingInstance building)
        {
            building = null;

            int occupantId;
            if (!BaseOccupancy.TryGetOccupant(cell, out occupantId))
                return false;

            return _buildings.TryGetValue(occupantId, out building);
        }

        /// <summary>다음에 쓸 건물 인스턴스 번호. 스토어가 Add 델타를 만들 때 쓴다.</summary>
        public int NextBuildingId { get; internal set; } = 1;

        internal Dictionary<int, BuildingInstance> MutableBuildings => _buildings;
    }
}
