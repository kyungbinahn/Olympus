using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// 어느 칸이 복구됐는지(녹지가 됐는지) 계산한다.
    ///
    /// 세계는 황폐한 상태로 시작하고, 폐허를 복구할 때마다 그 주변부터 녹지가 번진다.
    /// 전역 단계 전환(복구도가 임계치를 넘으면 지면이 통째로 바뀜)이 아니라 국소 확산인
    /// 이유는 보상이 어디서 오느냐다 — 전역 방식은 화면이 바뀌어도 "내가 방금 한 행동"과
    /// 연결되지 않는다. 국소 확산은 폐허를 하나 고치면 바로 그 자리가 초록이 되므로,
    /// 플레이어가 자기 행동이 세계를 바꿨다는 것을 즉시 본다. 그게 "진행 = 복구"의 핵심이다.
    ///
    /// 순수 로직이라 Unity 없이 시험된다.
    /// </summary>
    public sealed class RestorationMap
    {
        private readonly GridRect _bounds;
        private readonly HashSet<GridPos> _restored = new HashSet<GridPos>();

        private float _radius;

        /// <summary>
        /// 복구된 건물에서 녹지가 번지는 반경(칸).
        ///
        /// 세팅 가능하게 둔 이유 — 이 값이 화면에서 어떻게 읽히는지는 실제로 돌려 보고
        /// 눈으로 정해야 한다. 반경 2로 시작했더니 건물이 여백 1칸으로 붙어 있어서
        /// 녹지가 전부 건물에 가려지고 틈만 초록으로 보였다. 값을 바꾸면 다음 계산에서
        /// 반영되도록 무효화 표시만 남긴다.
        /// </summary>
        public float Radius
        {
            get { return _radius; }
            set
            {
                if (value < 0f)
                    throw new ArgumentOutOfRangeException(nameof(value), "반경은 음수일 수 없다.");

                if (_radius == value)
                    return;

                _radius = value;
                _radiusDirty = true;
            }
        }

        private bool _radiusDirty;

        /// <summary>
        /// 내용이 바뀔 때마다 오른다. 뷰가 이 값을 기억해 두고 달라졌을 때만
        /// 메쉬를 다시 만들면 된다 — 매 프레임 집합을 비교할 필요가 없다.
        /// </summary>
        public int Version { get; private set; }

        public RestorationMap(GridRect bounds, float radius = 2f)
        {
            if (radius < 0f)
                throw new ArgumentOutOfRangeException(nameof(radius), "반경은 음수일 수 없다.");

            _bounds = bounds;
            _radius = radius;
        }

        public int RestoredCellCount => _restored.Count;

        public bool IsRestored(GridPos cell) => _restored.Contains(cell);

        /// <summary>복구도 0~1. HUD에 진행률을 띄울 때 쓴다.</summary>
        public float RestoredFraction =>
            _bounds.CellCount == 0 ? 0f : (float)_restored.Count / _bounds.CellCount;

        /// <summary>
        /// 건물 상태에서 녹지 칸을 다시 계산한다. 바뀌었으면 <see cref="Version"/>이 오른다.
        ///
        /// <b>완성된</b> 건물만 녹지를 만든다 — 공사 중은 아직이다. 완성 순간에 주변이
        /// 초록으로 변하는 것이 복구의 보상 시점이 된다.
        /// </summary>
        public void Recompute(IEnumerable<BuildingInstance> buildings, BuildingCatalog catalog)
        {
            if (buildings == null)
                throw new ArgumentNullException(nameof(buildings));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var next = new HashSet<GridPos>();

            foreach (BuildingInstance b in buildings)
            {
                if (b.Phase != BuildingPhase.Complete)
                    continue;

                BuildingDef def;
                if (!catalog.TryGet(b.DefId, out def))
                    continue;

                AddHalo(next, b.Anchor, def.Footprint);
            }

            // 반경이 바뀌었으면 결과가 같아도 한 번은 통과시켜 버전을 올린다 —
            // 그래야 뷰가 다시 그린다. (같은 반경으로 다시 계산할 때는 건너뛴다.)
            if (!_radiusDirty && SetsEqual(next, _restored))
                return;

            _radiusDirty = false;
            _restored.Clear();
            foreach (GridPos c in next)
            {
                _restored.Add(c);
            }

            Version++;
        }

        /// <summary>
        /// 풋프린트 사각형에서 반경 안에 드는 칸을 모두 넣는다.
        ///
        /// 체비셰프(정사각 후광)가 아니라 사각형까지의 유클리드 거리를 쓴다 —
        /// 정사각으로 번지면 인공적으로 각져 보이고, 이건 자연이 되찾아 오는 연출이다.
        /// 모서리가 둥글어야 유기적으로 읽힌다.
        /// </summary>
        private void AddHalo(HashSet<GridPos> target, GridPos anchor, GridFootprint footprint)
        {
            int pad = (int)Math.Ceiling(Radius);

            int minX = anchor.X - pad;
            int maxX = anchor.X + footprint.Width - 1 + pad;
            int minY = anchor.Y - pad;
            int maxY = anchor.Y + footprint.Height - 1 + pad;

            // 풋프린트가 덮는 칸 범위 (경계 포함)
            int fpMinX = anchor.X;
            int fpMaxX = anchor.X + footprint.Width - 1;
            int fpMinY = anchor.Y;
            int fpMaxY = anchor.Y + footprint.Height - 1;

            float radiusSq = Radius * Radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new GridPos(x, y);

                    if (!_bounds.Contains(cell))
                        continue;

                    // 점에서 축정렬 사각형까지의 거리 — 사각형 안이면 0이다.
                    int dx = Math.Max(0, Math.Max(fpMinX - x, x - fpMaxX));
                    int dy = Math.Max(0, Math.Max(fpMinY - y, y - fpMaxY));

                    if (dx * dx + dy * dy <= radiusSq)
                        target.Add(cell);
                }
            }
        }

        private static bool SetsEqual(HashSet<GridPos> a, HashSet<GridPos> b)
        {
            if (a.Count != b.Count)
                return false;

            foreach (GridPos c in a)
            {
                if (!b.Contains(c))
                    return false;
            }

            return true;
        }
    }
}
