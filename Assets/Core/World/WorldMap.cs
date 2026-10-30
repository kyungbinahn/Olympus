using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;

namespace Olympus.Core.World
{
    /// <summary>
    /// 월드 한 칸의 지형. 칸마다 하나씩 들고 있으므로 byte로 저장한다
    /// (512x512면 26만 칸이다 — 칸마다 객체를 만들면 안 된다).
    /// </summary>
    public enum WorldTerrain : byte
    {
        /// <summary>바다·호수. 지나갈 수 없고 아무것도 세울 수 없다.</summary>
        Water = 0,

        Plains = 1,

        Forest = 2,

        /// <summary>산. 지나갈 수 없다 — 진군 경로를 막아 지형이 전략이 되게 한다.</summary>
        Mountain = 3,
    }

    public enum WorldSiteKind : byte
    {
        None = 0,

        /// <summary>플레이어의 기지. 탭하면 기지 화면으로 들어간다.</summary>
        PlayerCity = 1,

        /// <summary>폐허가 된 도시. 그릭로만의 세계관대로 되찾을 대상이다.</summary>
        RuinedCity = 2,

        /// <summary>채집지. <see cref="WorldSite.Resource"/>가 무엇을 주는지 말한다.</summary>
        ResourceNode = 3,

        MonsterCamp = 4,
    }

    /// <summary>
    /// 월드 위의 거점 하나. 칸 하나를 차지한다.
    ///
    /// 기지의 <c>BaseSlot</c>과 달리 풋프린트가 없다 — 월드에서는 도시 한 채가
    /// 한 칸이고, 크기는 화면에서 아이콘 크기로만 표현한다. 칸을 여러 개 먹게 하면
    /// 좌표 하나로 주소를 매기는 성질(X:512, Y:318 식)이 깨진다.
    /// </summary>
    public readonly struct WorldSite
    {
        public readonly WorldSiteKind Kind;
        public readonly GridPos Position;

        /// <summary>레벨. 채집지·몬스터 야영지의 등급이고, 도시는 규모다.</summary>
        public readonly int Level;

        /// <summary><see cref="WorldSiteKind.ResourceNode"/>일 때만 의미가 있다.</summary>
        public readonly ResourceKind Resource;

        public WorldSite(WorldSiteKind kind, GridPos position, int level, ResourceKind resource = ResourceKind.Wood)
        {
            Kind = kind;
            Position = position;
            Level = level;
            Resource = resource;
        }

        public override string ToString()
        {
            return Kind == WorldSiteKind.ResourceNode
                ? Kind + "(" + Resource + " Lv" + Level + ") @" + Position
                : Kind + "(Lv" + Level + ") @" + Position;
        }
    }

    /// <summary>
    /// 월드 지도 전체. 지형은 칸마다, 거점은 있는 칸에만 들고 있다.
    ///
    /// 생성은 <see cref="WorldGenerator"/>가 하고 이 클래스는 담기만 한다 —
    /// 나중에 서버가 내려주는 지도를 받을 때도 같은 그릇을 쓰기 위해서다.
    /// </summary>
    public sealed class WorldMap
    {
        private readonly WorldTerrain[] _terrain;
        private readonly Dictionary<GridPos, WorldSite> _sites = new Dictionary<GridPos, WorldSite>();

        public GridRect Bounds { get; }

        /// <summary>이 지도를 만든 씨앗. 같은 값이면 같은 지도가 나온다.</summary>
        public int Seed { get; }

        public WorldMap(GridRect bounds, int seed)
        {
            Bounds = bounds;
            Seed = seed;
            _terrain = new WorldTerrain[bounds.CellCount];
        }

        public int SiteCount => _sites.Count;

        public IEnumerable<WorldSite> Sites => _sites.Values;

        public WorldTerrain TerrainAt(GridPos cell)
        {
            if (!Bounds.Contains(cell))
                throw new ArgumentOutOfRangeException(nameof(cell), "월드 범위 밖이다: " + cell);

            return _terrain[IndexOf(cell)];
        }

        /// <summary>지나갈 수 있는 칸인가. 물과 산은 막힌다.</summary>
        public bool IsPassable(GridPos cell)
        {
            WorldTerrain t = TerrainAt(cell);
            return t != WorldTerrain.Water && t != WorldTerrain.Mountain;
        }

        public bool TryGetSite(GridPos cell, out WorldSite site)
        {
            return _sites.TryGetValue(cell, out site);
        }

        internal void SetTerrain(GridPos cell, WorldTerrain terrain)
        {
            _terrain[IndexOf(cell)] = terrain;
        }

        internal void AddSite(WorldSite site)
        {
            if (_sites.ContainsKey(site.Position))
                throw new InvalidOperationException("이미 거점이 있는 칸이다: " + site.Position);

            _sites.Add(site.Position, site);
        }

        private int IndexOf(GridPos cell)
        {
            return (cell.Y - Bounds.MinY) * Bounds.Width + (cell.X - Bounds.MinX);
        }
    }
}
