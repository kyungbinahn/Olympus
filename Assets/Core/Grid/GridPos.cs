using System;

namespace Olympus.Core.Grid
{
    /// <summary>
    /// 사각 격자의 한 칸. 정수 좌표다.
    ///
    /// 축 이름을 X/Y로 쓴다 — 뉴포리아·고양이는 헥스 계보 때문에 q/r을 쓰는데,
    /// 우리 격자는 사각이라 q/r을 물려받을 이유가 없다. 헥스에서 오는 오프셋 시프트·
    /// 큐브 라운딩 같은 것이 아예 없으므로 좌표 변환이 순수 선형으로 끝난다.
    /// </summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridPos Zero => new GridPos(0, 0);

        public GridPos Offset(int dx, int dy) => new GridPos(X + dx, Y + dy);

        /// <summary>체비셰프 거리 — 대각 이동을 1로 세는 거리. 사각 격자의 기본 거리다.</summary>
        public static int ChebyshevDistance(GridPos a, GridPos b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return dx > dy ? dx : dy;
        }

        /// <summary>맨해튼 거리 — 대각 이동을 금지할 때 쓴다.</summary>
        public static int ManhattanDistance(GridPos a, GridPos b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridPos other && Equals(other);

        public override int GetHashCode()
        {
            // 좌표가 음수여도 잘 흩어지도록 섞는다.
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);

        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);

        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
