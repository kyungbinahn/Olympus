using System;
using System.Collections.Generic;

namespace Olympus.Core.Grid
{
    /// <summary>
    /// 격자 위의 직사각 영역. 기지의 건설 가능 범위를 나타낸다.
    ///
    /// 범위를 선언해서 들고 있는 이유 — 뉴포리아는 건설 가능 경계를 저작하지 않고
    /// 런타임에 전 건물 위치를 훑어 min/max를 구한 뒤 여백을 ±15 더하는 방식이다.
    /// 그러면 건물이 하나도 없을 때·첫 건물을 지울 때 경계가 흔들리고, 그 계산을
    /// 세션 단위로 캐시해 두는 탓에 "영지 재진입 후 배치가 안 된다"는 함정이 따라온다.
    /// 범위는 데이터로 선언하는 편이 예측 가능하고 시험도 쉽다.
    /// </summary>
    public readonly struct GridRect
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int Width;
        public readonly int Height;

        public GridRect(int minX, int minY, int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "영역 너비는 1 이상이어야 한다.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "영역 높이는 1 이상이어야 한다.");

            MinX = minX;
            MinY = minY;
            Width = width;
            Height = height;
        }

        /// <summary>원점을 중심으로 한 정사각 영역. 홀수 크기를 주면 (0,0)이 정확히 중앙 칸이 된다.</summary>
        public static GridRect Centered(int size)
        {
            int half = size / 2;
            return new GridRect(-half, -half, size, size);
        }

        public int MaxXExclusive => MinX + Width;

        public int MaxYExclusive => MinY + Height;

        public int CellCount => Width * Height;

        public bool Contains(GridPos p)
        {
            return p.X >= MinX && p.X < MaxXExclusive
                && p.Y >= MinY && p.Y < MaxYExclusive;
        }

        /// <summary>풋프린트를 앵커에 놓았을 때 네 변이 모두 영역 안에 들어오는가.</summary>
        public bool ContainsFootprint(GridPos anchor, GridFootprint footprint)
        {
            return anchor.X >= MinX
                && anchor.Y >= MinY
                && anchor.X + footprint.Width <= MaxXExclusive
                && anchor.Y + footprint.Height <= MaxYExclusive;
        }

        public IEnumerable<GridPos> AllCells()
        {
            for (int y = MinY; y < MaxYExclusive; y++)
            {
                for (int x = MinX; x < MaxXExclusive; x++)
                {
                    yield return new GridPos(x, y);
                }
            }
        }

        public override string ToString()
        {
            return "GridRect(" + MinX + ", " + MinY + ", " + Width + "x" + Height + ")";
        }
    }
}
