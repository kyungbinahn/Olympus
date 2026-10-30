using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.World;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 월드 생성 규칙. 씨앗 하나로 지도를 만들기 때문에 "같은 씨앗이면 같은 지도"가
    /// 무너지면 나중에 서버와 클라이언트가 다른 세계를 보게 된다 — 그래서 그 성질이
    /// 여기서 제일 중요한 시험이다.
    ///
    /// 시험은 작은 월드(128칸)로 돈다. 규칙은 크기와 무관하고, 512로 돌리면
    /// 시험 한 판이 느려진다.
    /// </summary>
    public class WorldGenerationTests
    {
        private static WorldGenSettings SmallWorld(int seed = 12345)
        {
            return new WorldGenSettings
            {
                Size = 128,
                Seed = seed,
                RuinedCityCount = 20,
                ResourceNodeCount = 60,
                MonsterCampCount = 30,
                MinCityDistance = 8,
            };
        }

        [Test]
        public void 같은_씨앗이면_지형이_한_칸도_다르지_않다()
        {
            // ★이 성질이 월드 설계의 전부다. 이게 깨지면 지도를 통째로 저장하거나
            // 주고받아야 하고, 서버가 붙는 순간 "서로 다른 세계"가 된다.
            WorldMap a = new WorldGenerator().Generate(SmallWorld());
            WorldMap b = new WorldGenerator().Generate(SmallWorld());

            foreach (GridPos c in a.Bounds.AllCells())
            {
                Assert.That(b.TerrainAt(c), Is.EqualTo(a.TerrainAt(c)), "지형이 다르다: " + c);
            }
        }

        [Test]
        public void 같은_씨앗이면_거점도_똑같이_놓인다()
        {
            WorldMap a = new WorldGenerator().Generate(SmallWorld());
            WorldMap b = new WorldGenerator().Generate(SmallWorld());

            Assert.That(b.SiteCount, Is.EqualTo(a.SiteCount));

            foreach (WorldSite s in a.Sites)
            {
                WorldSite other;
                Assert.That(b.TryGetSite(s.Position, out other), Is.True, "거점이 없다: " + s);
                Assert.That(other.Kind, Is.EqualTo(s.Kind));
                Assert.That(other.Level, Is.EqualTo(s.Level));
                Assert.That(other.Resource, Is.EqualTo(s.Resource));
            }
        }

        [Test]
        public void 씨앗이_다르면_다른_지도가_나온다()
        {
            WorldMap a = new WorldGenerator().Generate(SmallWorld(1));
            WorldMap b = new WorldGenerator().Generate(SmallWorld(2));

            int same = 0;
            int total = 0;

            foreach (GridPos c in a.Bounds.AllCells())
            {
                if (a.TerrainAt(c) == b.TerrainAt(c))
                    same++;

                total++;
            }

            // 지형이 네 종류뿐이라 우연히 겹치는 칸은 많다. 전부 같으면 씨앗이 안 먹은 것이다.
            Assert.That(same, Is.LessThan(total), "씨앗이 달라도 지도가 완전히 같다");
        }

        [Test]
        public void 거점은_물과_산에는_생기지_않는다()
        {
            // 물·산은 지나갈 수 없다. 거기에 거점이 생기면 영영 닿을 수 없는 목표가 된다.
            WorldMap map = new WorldGenerator().Generate(SmallWorld());

            foreach (WorldSite s in map.Sites)
            {
                Assert.That(map.IsPassable(s.Position), Is.True,
                    "못 가는 칸에 거점이 있다: " + s + " (" + map.TerrainAt(s.Position) + ")");
            }
        }

        [Test]
        public void 한_칸에_거점이_두_개_놓이지_않는다()
        {
            WorldMap map = new WorldGenerator().Generate(SmallWorld());

            var seen = new HashSet<GridPos>();

            foreach (WorldSite s in map.Sites)
            {
                Assert.That(seen.Add(s.Position), Is.True, "같은 칸에 거점이 겹친다: " + s.Position);
            }
        }

        [Test]
        public void 플레이어_기지는_정확히_하나다()
        {
            WorldMap map = new WorldGenerator().Generate(SmallWorld());

            int count = 0;
            foreach (WorldSite s in map.Sites)
            {
                if (s.Kind == WorldSiteKind.PlayerCity)
                    count++;
            }

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void 폐허_도시끼리는_최소_거리를_지킨다()
        {
            // 뭉쳐 나오면 지도의 절반이 비고 절반이 빽빽해진다.
            WorldGenSettings settings = SmallWorld();
            WorldMap map = new WorldGenerator().Generate(settings);

            var cities = new List<GridPos>();
            foreach (WorldSite s in map.Sites)
            {
                if (s.Kind == WorldSiteKind.RuinedCity)
                    cities.Add(s.Position);
            }

            Assert.That(cities.Count, Is.GreaterThan(0), "폐허 도시가 하나도 안 생겼다");

            for (int i = 0; i < cities.Count; i++)
            {
                for (int j = i + 1; j < cities.Count; j++)
                {
                    Assert.That(
                        GridPos.ChebyshevDistance(cities[i], cities[j]),
                        Is.GreaterThanOrEqualTo(settings.MinCityDistance),
                        cities[i] + " 와 " + cities[j] + " 가 너무 가깝다");
                }
            }
        }

        [Test]
        public void 높이_구간이_뒤섞이면_생성_전에_터진다()
        {
            // 저작 실수다. 지도가 이상하게 나온 뒤에 원인을 찾는 것보다 즉시 터지는 게 낫다.
            var settings = SmallWorld();
            settings.WaterLevel = 0.9f;
            settings.ForestLevel = 0.2f;

            Assert.That(
                () => new WorldGenerator().Generate(settings),
                Throws.InvalidOperationException);
        }

        [Test]
        public void 월드가_전부_바다면_이유를_대고_터진다()
        {
            var settings = SmallWorld();
            settings.WaterLevel = 0.999f;
            settings.ForestLevel = 0.9995f;
            settings.MountainLevel = 0.9999f;

            Assert.That(
                () => new WorldGenerator().Generate(settings),
                Throws.InvalidOperationException);
        }

        [Test]
        public void 채집지는_자원_종류를_들고_있다()
        {
            WorldMap map = new WorldGenerator().Generate(SmallWorld());

            int nodes = 0;
            foreach (WorldSite s in map.Sites)
            {
                if (s.Kind != WorldSiteKind.ResourceNode)
                    continue;

                nodes++;
                Assert.That(s.Level, Is.InRange(1, 5));
            }

            Assert.That(nodes, Is.GreaterThan(0), "채집지가 하나도 안 생겼다");
        }

        [Test]
        public void 지도_범위_밖을_물으면_터진다()
        {
            WorldMap map = new WorldGenerator().Generate(SmallWorld());

            Assert.That(
                () => map.TerrainAt(new GridPos(-1, 0)),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }

    /// <summary>
    /// 난수·잡음이 런타임에 따라 흔들리지 않는지 본다. 이게 흔들리면 월드 생성의
    /// 재현성이 통째로 무너진다(<c>System.Random</c>을 쓰지 않는 이유).
    /// </summary>
    public class DeterministicRandomTests
    {
        [Test]
        public void 같은_씨앗은_같은_수열을_준다()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(b.NextUInt(), Is.EqualTo(a.NextUInt()));
            }
        }

        [Test]
        public void 씨앗_0도_고정점에_빠지지_않는다()
        {
            // xorshift는 상태가 0이면 영원히 0을 뱉는다. 생성자가 그 자리를 피해야 한다.
            var r = new DeterministicRandom(0);

            uint first = r.NextUInt();
            uint second = r.NextUInt();

            Assert.That(first, Is.Not.EqualTo(0u));
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void 범위_난수는_범위를_벗어나지_않는다()
        {
            var r = new DeterministicRandom(7);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(r.NextInt(10, 20), Is.InRange(10, 19));
                Assert.That(r.NextFloat(), Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void 잡음은_0과_1_사이에_있다()
        {
            var noise = new ValueNoise(99);

            for (int i = 0; i < 500; i++)
            {
                float v = noise.Fractal(i * 0.03f, i * 0.017f, 4, 0.5f);
                Assert.That(v, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void 음수_좌표에서도_잡음이_이어진다()
        {
            // FloorToInt를 (int) 캐스팅으로 두면 음수에서 0 쪽으로 잘려 x=0 근처에
            // 눈에 보이는 이음매가 생긴다.
            var noise = new ValueNoise(5);

            float left = noise.Sample(-0.001f, 10f);
            float right = noise.Sample(0.001f, 10f);

            Assert.That(System.Math.Abs(left - right), Is.LessThan(0.05f),
                "x=0 경계에서 잡음이 끊긴다");
        }
    }
}
