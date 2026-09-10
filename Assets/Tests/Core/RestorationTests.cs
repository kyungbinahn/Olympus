using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    public class RestorationMapTests
    {
        private StateStore _store;
        private BuildingCatalog _catalog;
        private ManualClock _clock;
        private ConstructionService _svc;

        private const string Small = "small";   // 1x1, 즉시 완성
        private const string Wide = "wide";     // 3x3, 즉시 완성

        [SetUp]
        public void SetUp()
        {
            var state = new GameState(new SquareGrid(1f), GridRect.Centered(20));
            _store = new StateStore(state);

            _catalog = new BuildingCatalog();
            _catalog.Add(new BuildingDef(Small, "k", GridFootprint.Single, 0L, null));
            _catalog.Add(new BuildingDef(Wide, "k", GridFootprint.Square(3), 0L, null));

            _clock = new ManualClock(0L);
            _svc = new ConstructionService(_store, _catalog, _clock);
        }

        private RestorationMap NewMap(float radius = 2f)
        {
            return new RestorationMap(GridRect.Centered(20), radius);
        }

        private void Recompute(RestorationMap map)
        {
            map.Recompute(_store.State.Buildings, _catalog);
        }

        [Test]
        public void 폐허만_있으면_녹지가_없다()
        {
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Small, new GridPos(0, 0)));
            _svc.InitializeFromLayout(layout);

            RestorationMap map = NewMap();
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.EqualTo(0));
            Assert.That(map.RestoredFraction, Is.EqualTo(0f));
        }

        [Test]
        public void 복구하면_주변이_원형으로_번진다()
        {
            // 1x1 건물 + 반경 2 = 중심에서 유클리드 거리 2 이하인 칸 13개.
            // 정사각(체비셰프)이었다면 25개다 — 모서리가 둥근 것이 이 설계의 요점이다.
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Small, new GridPos(0, 0)));
            _svc.InitializeFromLayout(layout);
            _svc.TryStartRepair(1);

            RestorationMap map = NewMap();
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.EqualTo(13));

            Assert.That(map.IsRestored(new GridPos(0, 0)), Is.True, "중심");
            Assert.That(map.IsRestored(new GridPos(2, 0)), Is.True, "축 방향 거리 2");
            Assert.That(map.IsRestored(new GridPos(1, 1)), Is.True, "대각 거리 √2");
            Assert.That(map.IsRestored(new GridPos(2, 1)), Is.False, "거리 √5 — 밖");
            Assert.That(map.IsRestored(new GridPos(2, 2)), Is.False, "모서리는 잘린다");
        }

        [Test]
        public void 큰_건물은_풋프린트_전체에서_번진다()
        {
            // 3x3 사각형까지의 거리가 2 이하인 칸 = 37개.
            // 점이 아니라 사각형까지의 거리를 재므로 건물이 크면 녹지도 넓다.
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Wide, new GridPos(0, 0)));
            _svc.InitializeFromLayout(layout);
            _svc.TryStartRepair(1);

            RestorationMap map = NewMap();
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.EqualTo(37));
            Assert.That(map.IsRestored(new GridPos(1, 1)), Is.True, "풋프린트 안");
            Assert.That(map.IsRestored(new GridPos(4, 1)), Is.True, "오른쪽 변에서 거리 2");
            Assert.That(map.IsRestored(new GridPos(5, 1)), Is.False, "거리 3 — 밖");
        }

        [Test]
        public void 공사중은_아직_녹지를_만들지_않는다()
        {
            // 완성 순간이 복구의 보상 시점이다 — 공사를 시작하자마자 초록이 되면
            // 기다릴 이유가 사라진다.
            var catalog = new BuildingCatalog();
            catalog.Add(new BuildingDef("slow", "k", GridFootprint.Single, 10_000L, null));

            var state = new GameState(new SquareGrid(1f), GridRect.Centered(20));
            var store = new StateStore(state);
            var svc = new ConstructionService(store, catalog, _clock);

            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, "slow", new GridPos(0, 0)));
            svc.InitializeFromLayout(layout);
            svc.TryStartRepair(1);

            RestorationMap map = NewMap();
            map.Recompute(store.State.Buildings, catalog);
            Assert.That(map.RestoredCellCount, Is.EqualTo(0), "공사 중에는 아직");

            _clock.Advance(10_000L);
            svc.CompleteFinished();
            map.Recompute(store.State.Buildings, catalog);

            Assert.That(map.RestoredCellCount, Is.EqualTo(13), "완성되면 번진다");
        }

        [Test]
        public void 반경이_0이면_풋프린트만_녹지가_된다()
        {
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Wide, new GridPos(0, 0)));
            _svc.InitializeFromLayout(layout);
            _svc.TryStartRepair(1);

            RestorationMap map = NewMap(0f);
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.EqualTo(9));
        }

        [Test]
        public void 기지_범위_밖으로는_번지지_않는다()
        {
            // 경계 칸을 복구하면 녹지가 잘린다. 안 자르면 지면 메쉬가 없는 칸을
            // 녹지로 표시하려다 어긋난다.
            GridRect bounds = GridRect.Centered(20);   // -10 ~ 9
            var layout = new BaseLayout(bounds);
            layout.Add(new BaseSlot(1, Small, new GridPos(9, 9)));   // 오른쪽 위 모서리
            _svc.InitializeFromLayout(layout);
            _svc.TryStartRepair(1);

            RestorationMap map = NewMap();
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.LessThan(13), "모서리라 잘려야 한다");

            foreach (GridPos c in bounds.AllCells())
            {
                if (map.IsRestored(c))
                    Assert.That(bounds.Contains(c), Is.True, "범위 밖 칸: " + c);
            }

            Assert.That(map.IsRestored(new GridPos(10, 9)), Is.False);
        }

        [Test]
        public void 여러_건물의_녹지가_합쳐진다()
        {
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Small, new GridPos(-5, 0)));
            layout.Add(new BaseSlot(2, Small, new GridPos(5, 0)));
            _svc.InitializeFromLayout(layout);

            RestorationMap map = NewMap();

            _svc.TryStartRepair(1);
            Recompute(map);
            Assert.That(map.RestoredCellCount, Is.EqualTo(13));

            _svc.TryStartRepair(2);
            Recompute(map);
            Assert.That(map.RestoredCellCount, Is.EqualTo(26), "멀리 떨어져 있으면 겹치지 않는다");
        }

        [Test]
        public void 겹치는_녹지는_두_번_세지_않는다()
        {
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Small, new GridPos(0, 0)));
            layout.Add(new BaseSlot(2, Small, new GridPos(2, 0)));
            _svc.InitializeFromLayout(layout);
            _svc.TryStartRepair(1);
            _svc.TryStartRepair(2);

            RestorationMap map = NewMap();
            Recompute(map);

            Assert.That(map.RestoredCellCount, Is.LessThan(26));
            Assert.That(map.IsRestored(new GridPos(1, 0)), Is.True, "둘 사이 칸");
        }

        [Test]
        public void 바뀌지_않으면_버전이_오르지_않는다()
        {
            // 뷰가 이 버전만 보고 메쉬를 다시 만든다 — 안 바뀌었는데 오르면
            // 매 프레임 2,304칸 메쉬를 재생성하게 된다.
            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Small, new GridPos(0, 0)));
            _svc.InitializeFromLayout(layout);

            RestorationMap map = NewMap();
            Recompute(map);
            int v0 = map.Version;

            Recompute(map);
            Recompute(map);
            Assert.That(map.Version, Is.EqualTo(v0), "같은 상태면 버전 유지");

            _svc.TryStartRepair(1);
            Recompute(map);
            Assert.That(map.Version, Is.GreaterThan(v0), "바뀌면 오른다");
        }

        [Test]
        public void 반경이_음수면_만들_때_터진다()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new RestorationMap(GridRect.Centered(10), -1f));
        }
    }
}
