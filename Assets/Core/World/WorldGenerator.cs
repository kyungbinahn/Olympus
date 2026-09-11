using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;

namespace Olympus.Core.World
{
    /// <summary>
    /// 월드 생성 설정. 사람이 눈으로 맞추는 값들이라 전부 밖에서 바꿀 수 있게 둔다
    /// (나중에 <c>Assets/Data/world-gen.json</c>에서 읽는다 — 기지 데이터와 같은 방식).
    /// </summary>
    public sealed class WorldGenSettings
    {
        /// <summary>한 변의 칸 수. 512면 26만 칸이고, 좌표는 X:0~511 / Y:0~511이 된다.</summary>
        public int Size = 512;

        public int Seed = 1;

        /// <summary>
        /// 잡음 한 칸이 지형에서 차지하는 크기. 값이 작을수록 대륙이 커진다
        /// (0.01이면 잡음 격자 한 칸이 월드 100칸이다).
        /// </summary>
        public float NoiseScale = 0.012f;

        public int Octaves = 4;
        public float Persistence = 0.5f;

        // 높이 구간. 아래에서부터 물 → 평원 → 숲 → 산.
        public float WaterLevel = 0.38f;
        public float ForestLevel = 0.62f;
        public float MountainLevel = 0.78f;

        public int RuinedCityCount = 120;
        public int ResourceNodeCount = 600;
        public int MonsterCampCount = 300;

        /// <summary>폐허 도시끼리 최소로 떨어져 있어야 하는 칸 수. 뭉치면 지도가 심심해진다.</summary>
        public int MinCityDistance = 12;

        public void Validate()
        {
            if (Size < 8)
                throw new ArgumentOutOfRangeException(nameof(Size), "월드는 최소 8칸이어야 한다.");

            if (!(WaterLevel < ForestLevel && ForestLevel < MountainLevel))
                throw new InvalidOperationException(
                    "높이 구간이 뒤섞였다 — 물 < 숲 < 산 순서여야 한다 (물 " + WaterLevel +
                    ", 숲 " + ForestLevel + ", 산 " + MountainLevel + ").");
        }
    }

    /// <summary>
    /// 씨앗 하나로 월드를 만든다. 같은 씨앗이면 언제 어디서 돌려도 같은 지도가 나온다 —
    /// 그래서 지도 전체를 저장하거나 주고받을 필요가 없고, 나중에 서버가 붙어도
    /// 씨앗 하나만 맞추면 클라이언트와 같은 지도를 본다.
    ///
    /// UnityEngine이 한 줄도 없어서 <c>dotnet test</c>로 규칙을 전부 검증할 수 있다.
    /// </summary>
    public sealed class WorldGenerator
    {
        public WorldMap Generate(WorldGenSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            settings.Validate();

            var bounds = new GridRect(0, 0, settings.Size, settings.Size);
            var map = new WorldMap(bounds, settings.Seed);

            FillTerrain(map, settings);

            // 거점 배치는 지형 다음이다 — 물·산을 피해야 하므로 지형이 먼저 있어야 한다.
            var random = new DeterministicRandom(settings.Seed ^ 0x5EED);

            PlacePlayerCity(map, settings);
            PlaceRuinedCities(map, settings, random);
            PlaceScattered(map, settings, random, WorldSiteKind.ResourceNode, settings.ResourceNodeCount);
            PlaceScattered(map, settings, random, WorldSiteKind.MonsterCamp, settings.MonsterCampCount);

            return map;
        }

        private static void FillTerrain(WorldMap map, WorldGenSettings settings)
        {
            var noise = new ValueNoise(settings.Seed);
            GridRect b = map.Bounds;

            for (int y = b.MinY; y < b.MaxYExclusive; y++)
            {
                for (int x = b.MinX; x < b.MaxXExclusive; x++)
                {
                    float e = noise.Fractal(
                        x * settings.NoiseScale,
                        y * settings.NoiseScale,
                        settings.Octaves,
                        settings.Persistence);

                    WorldTerrain terrain;
                    if (e < settings.WaterLevel) terrain = WorldTerrain.Water;
                    else if (e < settings.ForestLevel) terrain = WorldTerrain.Plains;
                    else if (e < settings.MountainLevel) terrain = WorldTerrain.Forest;
                    else terrain = WorldTerrain.Mountain;

                    map.SetTerrain(new GridPos(x, y), terrain);
                }
            }
        }

        /// <summary>
        /// 플레이어 기지는 한가운데에서 가장 가까운 빈 육지에 둔다.
        /// 중앙이 바다일 수 있으므로 나선으로 바깥을 훑는다 — "못 찾았다"로 끝나면
        /// 씨앗에 따라 게임이 시작조차 못 하므로, 범위를 다 훑을 때까지 찾는다.
        /// </summary>
        private static void PlacePlayerCity(WorldMap map, WorldGenSettings settings)
        {
            GridRect b = map.Bounds;
            var center = new GridPos(b.MinX + b.Width / 2, b.MinY + b.Height / 2);

            GridPos found;
            if (!TryFindNearestFree(map, center, out found))
                throw new InvalidOperationException(
                    "플레이어 기지를 놓을 육지를 못 찾았다 — 물 높이(" + settings.WaterLevel +
                    ")가 너무 높아 월드가 전부 바다일 수 있다.");

            map.AddSite(new WorldSite(WorldSiteKind.PlayerCity, found, 1));
        }

        private static bool TryFindNearestFree(WorldMap map, GridPos center, out GridPos result)
        {
            GridRect b = map.Bounds;
            int maxRadius = b.Width > b.Height ? b.Width : b.Height;

            for (int r = 0; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // 정사각 링의 테두리만 본다 — 안쪽은 이미 더 작은 r에서 훑었다.
                        if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r)
                            continue;

                        var cell = center.Offset(dx, dy);

                        if (!b.Contains(cell) || !map.IsPassable(cell))
                            continue;

                        WorldSite existing;
                        if (map.TryGetSite(cell, out existing))
                            continue;

                        result = cell;
                        return true;
                    }
                }
            }

            result = GridPos.Zero;
            return false;
        }

        private static void PlaceRuinedCities(WorldMap map, WorldGenSettings settings, DeterministicRandom random)
        {
            var placed = new List<GridPos>();

            foreach (WorldSite s in map.Sites)
            {
                if (s.Kind == WorldSiteKind.PlayerCity || s.Kind == WorldSiteKind.RuinedCity)
                    placed.Add(s.Position);
            }

            int attempts = settings.RuinedCityCount * 40;
            int made = 0;

            while (made < settings.RuinedCityCount && attempts-- > 0)
            {
                GridPos cell;
                if (!TryPickFreeLand(map, random, out cell))
                    continue;

                if (TooCloseToAny(placed, cell, settings.MinCityDistance))
                    continue;

                map.AddSite(new WorldSite(WorldSiteKind.RuinedCity, cell, random.NextInt(1, 6)));
                placed.Add(cell);
                made++;
            }
        }

        private static void PlaceScattered(
            WorldMap map, WorldGenSettings settings, DeterministicRandom random,
            WorldSiteKind kind, int count)
        {
            int attempts = count * 20;
            int made = 0;

            while (made < count && attempts-- > 0)
            {
                GridPos cell;
                if (!TryPickFreeLand(map, random, out cell))
                    continue;

                int level = random.NextInt(1, 6);

                WorldSite site = kind == WorldSiteKind.ResourceNode
                    ? new WorldSite(kind, cell, level, ResourceFor(map.TerrainAt(cell), random))
                    : new WorldSite(kind, cell, level);

                map.AddSite(site);
                made++;
            }
        }

        /// <summary>
        /// 지형이 무엇을 주는지 — 숲은 목재, 평원은 식량이 기본이고 가끔 다른 것이 섞인다.
        /// 지형과 산출이 붙어 있어야 "저기로 가면 목재가 있다"가 지도에서 읽힌다.
        /// </summary>
        private static ResourceKind ResourceFor(WorldTerrain terrain, DeterministicRandom random)
        {
            int roll = random.NextInt(100);

            if (terrain == WorldTerrain.Forest)
                return roll < 70 ? ResourceKind.Wood : (roll < 90 ? ResourceKind.Food : ResourceKind.Stone);

            return roll < 55 ? ResourceKind.Food : (roll < 85 ? ResourceKind.Stone : ResourceKind.Wood);
        }

        private static bool TryPickFreeLand(WorldMap map, DeterministicRandom random, out GridPos cell)
        {
            GridRect b = map.Bounds;

            cell = new GridPos(
                random.NextInt(b.MinX, b.MaxXExclusive),
                random.NextInt(b.MinY, b.MaxYExclusive));

            if (!map.IsPassable(cell))
                return false;

            WorldSite existing;
            return !map.TryGetSite(cell, out existing);
        }

        private static bool TooCloseToAny(List<GridPos> placed, GridPos cell, int minDistance)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                if (GridPos.ChebyshevDistance(placed[i], cell) < minDistance)
                    return true;
            }

            return false;
        }
    }
}
