using System.Collections.Generic;

namespace Olympus.Core.Grid
{
    /// <summary>배치가 거부된 이유. UI가 사용자에게 무엇이 문제인지 말해줄 수 있어야 한다.</summary>
    public enum PlacementRejection
    {
        None = 0,

        /// <summary>건설 가능 범위를 벗어났다.</summary>
        OutOfBounds,

        /// <summary>이미 다른 것이 그 칸을 쓰고 있다.</summary>
        Overlapping,
    }

    /// <summary>배치 가능 여부와, 안 되면 그 이유.</summary>
    public readonly struct PlacementCheck
    {
        public readonly bool Allowed;
        public readonly PlacementRejection Rejection;

        /// <summary>겹침으로 거부된 경우 걸린 상대. 그 외에는 -1.</summary>
        public readonly int BlockingOccupantId;

        private PlacementCheck(bool allowed, PlacementRejection rejection, int blockingOccupantId)
        {
            Allowed = allowed;
            Rejection = rejection;
            BlockingOccupantId = blockingOccupantId;
        }

        public static PlacementCheck Ok => new PlacementCheck(true, PlacementRejection.None, -1);

        public static PlacementCheck OutOfBounds =>
            new PlacementCheck(false, PlacementRejection.OutOfBounds, -1);

        public static PlacementCheck Overlapping(int blockingOccupantId) =>
            new PlacementCheck(false, PlacementRejection.Overlapping, blockingOccupantId);
    }

    /// <summary>
    /// 어느 칸이 누구에게 점유됐는지를 들고 있는 판.
    ///
    /// 점유를 칸 → 주인 사전으로 들고 있어서 겹침 판정이 풋프린트 칸 수만큼만 걸린다.
    /// (뉴포리아는 건물 전체를 훑는 쌍별 비교라 건물 수에 비례한다. 지금 규모에서는
    ///  둘 다 충분히 빠르지만, 사전 쪽은 "이 칸을 누가 쓰고 있나"를 바로 답할 수 있어
    ///  탭 판정·툴팁에 같은 자료구조를 그대로 쓸 수 있다.)
    /// </summary>
    public sealed class GridOccupancy
    {
        private readonly Dictionary<GridPos, int> _ownerByCell = new Dictionary<GridPos, int>();

        public GridRect Bounds { get; }

        public GridOccupancy(GridRect bounds)
        {
            Bounds = bounds;
        }

        public int OccupiedCellCount => _ownerByCell.Count;

        public bool TryGetOccupant(GridPos cell, out int occupantId)
        {
            return _ownerByCell.TryGetValue(cell, out occupantId);
        }

        public bool IsFree(GridPos cell) => !_ownerByCell.ContainsKey(cell);

        /// <summary>
        /// 놓을 수 있는지 본다. <paramref name="ignoreOccupantId"/>는 이동(재배치) 중인
        /// 자기 자신을 겹침 판정에서 빼기 위한 것 — 없으면 제자리에서 살짝 옮기는 것조차
        /// 자기와 겹친다고 거부된다.
        /// </summary>
        public PlacementCheck Check(GridPos anchor, GridFootprint footprint, int ignoreOccupantId = -1)
        {
            if (!Bounds.ContainsFootprint(anchor, footprint))
                return PlacementCheck.OutOfBounds;

            foreach (GridPos cell in footprint.CellsAt(anchor))
            {
                int owner;
                if (_ownerByCell.TryGetValue(cell, out owner) && owner != ignoreOccupantId)
                    return PlacementCheck.Overlapping(owner);
            }

            return PlacementCheck.Ok;
        }

        /// <summary>
        /// 점유를 등록한다. 놓을 수 없는 자리면 아무것도 바꾸지 않고 false를 돌려준다
        /// (부분 점유가 남으면 판이 조용히 오염되므로, 검사와 등록을 한 호출로 묶는다).
        /// </summary>
        public bool TryPlace(int occupantId, GridPos anchor, GridFootprint footprint)
        {
            if (!Check(anchor, footprint, occupantId).Allowed)
                return false;

            foreach (GridPos cell in footprint.CellsAt(anchor))
            {
                _ownerByCell[cell] = occupantId;
            }

            return true;
        }

        /// <summary>그 주인이 쓰던 칸을 전부 비운다. 비운 칸 수를 돌려준다.</summary>
        public int RemoveOccupant(int occupantId)
        {
            var toRemove = new List<GridPos>();

            foreach (KeyValuePair<GridPos, int> pair in _ownerByCell)
            {
                if (pair.Value == occupantId)
                    toRemove.Add(pair.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                _ownerByCell.Remove(toRemove[i]);
            }

            return toRemove.Count;
        }

        public void Clear()
        {
            _ownerByCell.Clear();
        }
    }
}
