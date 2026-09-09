using System;

namespace Olympus.Core.Grid
{
    /// <summary>
    /// 칸 ↔ 월드 좌표 변환. 칸 (0,0)의 중심이 월드 (0,0)이다.
    ///
    /// 변환이 순수 선형이라 여기 담긴 수학은 이 파일 전부다.
    /// (뉴포리아의 헥스 변환은 2/3·x, √3/3·z 계수에 큐브 라운딩과
    ///  <c>roundR += Floor(0.50000001f * roundQ)</c> 같은 매직넘버 보정이 붙어 있다.
    ///  사각 격자를 고르면 그 보정이 존재할 이유가 없어진다.)
    /// </summary>
    public sealed class SquareGrid
    {
        public float CellSize { get; }

        public SquareGrid(float cellSize)
        {
            if (cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize), "칸 크기는 0보다 커야 한다.");

            CellSize = cellSize;
        }

        /// <summary>칸의 중심 월드 좌표.</summary>
        public WorldXZ CellToWorld(GridPos cell)
        {
            return new WorldXZ(cell.X * CellSize, cell.Y * CellSize);
        }

        /// <summary>
        /// 월드 좌표가 속한 칸. 칸 중심이 격자점이므로 반올림으로 떨어진다.
        /// 정확히 경계에 놓인 점은 큰 쪽 칸에 속한다(<see cref="MathF.Round"/>가 아니라
        /// Floor(v + 0.5)를 쓰는 이유 — .5에서 짝수로 붕는 은행가 반올림을 피한다).
        /// </summary>
        public GridPos WorldToCell(WorldXZ world)
        {
            return new GridPos(
                RoundHalfUp(world.X / CellSize),
                RoundHalfUp(world.Z / CellSize));
        }

        public GridPos WorldToCell(float x, float z)
        {
            return WorldToCell(new WorldXZ(x, z));
        }

        private static int RoundHalfUp(float v)
        {
            return (int)Math.Floor(v + 0.5f);
        }
    }
}
