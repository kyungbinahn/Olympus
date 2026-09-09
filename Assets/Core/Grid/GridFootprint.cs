using System;
using System.Collections.Generic;

namespace Olympus.Core.Grid
{
    /// <summary>
    /// 건물이 격자에서 차지하는 칸 수. 앵커 칸을 기준으로 +X/+Y 방향으로 뻗는다.
    ///
    /// 앵커를 "최소 코너"로 고정하는 이유 — 중심 기준으로 잡으면 짝수 크기(2×2, 4×4)에서
    /// 중심이 칸 경계에 놓여 반칸 오차가 생기고, 그걸 막으려고 크기별 예외가 늘어난다.
    /// (뉴포리아가 풋프린트를 경로 접미사 파싱·레벨 데이터·비주얼 바운즈 나눗셈의
    ///  3단 폴백으로 추정하는 상태인데, 그 복잡도의 뿌리가 이 모호함이다.)
    /// 최소 코너 기준이면 점유 범위가 [X, X+W) × [Y, Y+H) 로 예외 없이 딱 떨어진다.
    /// </summary>
    public readonly struct GridFootprint
    {
        public readonly int Width;
        public readonly int Height;

        public GridFootprint(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "풋프린트 너비는 1 이상이어야 한다.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "풋프린트 높이는 1 이상이어야 한다.");

            Width = width;
            Height = height;
        }

        public static GridFootprint Single => new GridFootprint(1, 1);

        public static GridFootprint Square(int size) => new GridFootprint(size, size);

        public int CellCount => Width * Height;

        /// <summary>앵커에 놓았을 때 실제로 덮는 칸들.</summary>
        public IEnumerable<GridPos> CellsAt(GridPos anchor)
        {
            for (int dy = 0; dy < Height; dy++)
            {
                for (int dx = 0; dx < Width; dx++)
                {
                    yield return new GridPos(anchor.X + dx, anchor.Y + dy);
                }
            }
        }

        /// <summary>
        /// 앵커에 놓았을 때 덮는 영역의 월드 중심. 건물 모델을 놓을 자리다.
        /// 짝수 크기여도 정확히 가운데가 나온다.
        /// </summary>
        public WorldXZ CenterAt(GridPos anchor, SquareGrid grid)
        {
            WorldXZ min = grid.CellToWorld(anchor);
            float halfSpanX = (Width - 1) * 0.5f * grid.CellSize;
            float halfSpanZ = (Height - 1) * 0.5f * grid.CellSize;
            return new WorldXZ(min.X + halfSpanX, min.Z + halfSpanZ);
        }

        public override string ToString() => Width + "x" + Height;
    }
}
