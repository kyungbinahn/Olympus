using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>시험용 카탈로그·레이아웃 조립. DefId는 데이터 키라 세계관 단어를 써도 된다.</summary>
    internal static class TerritoryTestFactory
    {
        public const string Temple = "temple";
        public const string Barracks = "barracks";
        public const string Quarry = "quarry";
        public const string Instant = "instant";

        public static BuildingCatalog NewCatalog()
        {
            var catalog = new BuildingCatalog();

            catalog.Add(new BuildingDef(
                Temple, "building.temple", GridFootprint.Square(3), 120_000L,
                new List<ResourceCost>
                {
                    new ResourceCost(ResourceKind.Stone, 200),
                    new ResourceCost(ResourceKind.Wood, 50),
                }));

            catalog.Add(new BuildingDef(
                Barracks, "building.barracks", GridFootprint.Square(2), 60_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 100) }));

            catalog.Add(new BuildingDef(
                Quarry, "building.quarry", new GridFootprint(2, 1), 30_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 20) }));

            catalog.Add(new BuildingDef(
                Instant, "building.instant", GridFootprint.Single, 0L, null));

            return catalog;
        }

        /// <summary>겹치지 않는 정상 레이아웃.</summary>
        public static BaseLayout NewLayout()
        {
            var layout = new BaseLayout(new GridRect(0, 0, 20, 20));
            layout.Add(new BaseSlot(1, Temple, new GridPos(8, 8)));
            layout.Add(new BaseSlot(2, Barracks, new GridPos(2, 2)));
            layout.Add(new BaseSlot(3, Quarry, new GridPos(15, 3)));
            layout.Add(new BaseSlot(4, Instant, new GridPos(0, 19)));
            return layout;
        }
    }

    public class BaseLayoutTests
    {
        [Test]
        public void 정상_레이아웃은_문제가_없다()
        {
            BaseLayout layout = TerritoryTestFactory.NewLayout();

            Assert.That(layout.Validate(TerritoryTestFactory.NewCatalog()), Is.Empty);
            Assert.That(layout.SlotCount, Is.EqualTo(4));
        }

        [Test]
        public void 겹치는_자리는_상대_번호까지_대고_걸린다()
        {
            // 저작 실수를 로드 시점에 잡는 것이 이 검증의 전부다.
            // 검증이 없으면 화면에서 건물 두 채가 겹쳐 보이는 것으로만 드러나고,
            // 각도에 따라 그마저 안 보인다.
            var layout = new BaseLayout(new GridRect(0, 0, 20, 20));
            layout.Add(new BaseSlot(1, TerritoryTestFactory.Temple, new GridPos(5, 5)));
            layout.Add(new BaseSlot(2, TerritoryTestFactory.Barracks, new GridPos(6, 6)));

            IReadOnlyList<LayoutProblem> problems = layout.Validate(TerritoryTestFactory.NewCatalog());

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0].SlotId, Is.EqualTo(2));
            Assert.That(problems[0].Message, Does.Contain("Slot#1"));
            Assert.That(problems[0].Message, Does.Contain("겹친다"));
        }

        [Test]
        public void 범위를_벗어난_자리가_걸린다()
        {
            var layout = new BaseLayout(new GridRect(0, 0, 10, 10));
            layout.Add(new BaseSlot(1, TerritoryTestFactory.Temple, new GridPos(9, 9)));

            IReadOnlyList<LayoutProblem> problems = layout.Validate(TerritoryTestFactory.NewCatalog());

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0].Message, Does.Contain("범위를 벗어난다"));
        }

        [Test]
        public void 카탈로그에_없는_DefId가_걸린다()
        {
            var layout = new BaseLayout(new GridRect(0, 0, 10, 10));
            layout.Add(new BaseSlot(1, "없는건물", new GridPos(0, 0)));

            IReadOnlyList<LayoutProblem> problems = layout.Validate(TerritoryTestFactory.NewCatalog());

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0].Message, Does.Contain("없는건물"));
        }

        [Test]
        public void 자리_번호가_중복이면_등록에서_터진다()
        {
            var layout = new BaseLayout(new GridRect(0, 0, 10, 10));
            layout.Add(new BaseSlot(1, TerritoryTestFactory.Instant, new GridPos(0, 0)));

            Assert.Throws<System.InvalidOperationException>(() =>
                layout.Add(new BaseSlot(1, TerritoryTestFactory.Instant, new GridPos(5, 5))));
        }
    }

    public class ConstructionServiceTests
    {
        private StateStore _store;
        private BuildingCatalog _catalog;
        private ManualClock _clock;
        private ConstructionService _svc;

        [SetUp]
        public void SetUp()
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            _store = new StateStore(state);
            _catalog = TerritoryTestFactory.NewCatalog();
            _clock = new ManualClock(1_000_000L);
            _svc = new ConstructionService(_store, _catalog, _clock);

            _svc.InitializeFromLayout(TerritoryTestFactory.NewLayout());
            GiveResources(1000, 1000);
        }

        private void GiveResources(long wood, long stone)
        {
            _store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, wood))
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, stone)));
        }

        [Test]
        public void 기지에_들어가면_모든_자리가_폐허로_서_있다()
        {
            Assert.That(_store.State.BuildingCount, Is.EqualTo(4));

            foreach (BuildingInstance b in _store.State.Buildings)
            {
                Assert.That(b.IsRuined, Is.True, b.ToString());
                Assert.That(b.Level, Is.EqualTo(0));
            }

            // 폐허도 자리를 차지한다 — 탭 판정이 이 점유를 쓴다.
            // 신전 3x3 + 병영 2x2 + 채석장 2x1 + 1x1 = 16칸
            Assert.That(_store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(16));
        }

        [Test]
        public void 폐허가_선_칸을_탭하면_그_건물이_나온다()
        {
            BuildingInstance found;

            Assert.That(_store.State.TryGetBuildingAt(new GridPos(9, 9), out found), Is.True);
            Assert.That(found.DefId, Is.EqualTo(TerritoryTestFactory.Temple));

            Assert.That(_store.State.TryGetBuildingAt(new GridPos(0, 0), out found), Is.False);
        }

        [Test]
        public void 잘못된_레이아웃은_어느_자리가_문제인지_말하고_터진다()
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var svc = new ConstructionService(new StateStore(state), _catalog, _clock);

            var bad = new BaseLayout(new GridRect(0, 0, 20, 20));
            bad.Add(new BaseSlot(1, TerritoryTestFactory.Temple, new GridPos(5, 5)));
            bad.Add(new BaseSlot(7, TerritoryTestFactory.Barracks, new GridPos(6, 6)));

            var ex = Assert.Throws<System.InvalidOperationException>(
                () => svc.InitializeFromLayout(bad));

            Assert.That(ex.Message, Does.Contain("Slot#7"));
            Assert.That(state.BuildingCount, Is.EqualTo(0), "실패했으면 아무것도 세우지 않는다");
        }

        [Test]
        public void 복구를_시작하면_비용이_빠지고_공사중이_된다()
        {
            BuildResult r = _svc.TryStartRepair(2);

            Assert.That(r.Started, Is.True);
            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(900));

            BuildingInstance b;
            _store.State.TryGetBuilding(2, out b);
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(b.ConstructionEndsAtUnixMs, Is.EqualTo(1_060_000L));
            Assert.That(b.Anchor, Is.EqualTo(new GridPos(2, 2)), "자리는 안 움직인다");
        }

        [Test]
        public void 비용과_상태전환이_같은_델타로_들어가_통지가_한_번만_온다()
        {
            // 따로 적용되면 그 사이에 HUD가 한 번 그려져서
            // "자원은 빠졌는데 아직 폐허인" 프레임이 보인다.
            int notifications = 0;
            _store.Changed += _ => notifications++;

            _svc.TryStartRepair(2);

            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void 자원이_모자라면_시작되지_않고_아무것도_바뀌지_않는다()
        {
            GiveResources(10, 10);

            BuildResult r = _svc.TryStartRepair(1);

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.InsufficientResources));
            Assert.That(_store.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(10));

            BuildingInstance b;
            _store.State.TryGetBuilding(1, out b);
            Assert.That(b.IsRuined, Is.True);
        }

        [Test]
        public void 이미_공사중인_자리는_다시_시작되지_않는다()
        {
            _svc.TryStartRepair(2);
            long woodAfterFirst = _store.State.Resources.Get(ResourceKind.Wood);

            BuildResult r = _svc.TryStartRepair(2);

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.NotRuined));
            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(woodAfterFirst),
                "거부됐으면 비용이 두 번 빠지지 않는다");
        }

        [Test]
        public void 이미_복구된_자리도_다시_시작되지_않는다()
        {
            _svc.TryStartRepair(2);
            _clock.Advance(60_000L);
            _svc.CompleteFinished();

            BuildResult r = _svc.TryStartRepair(2);

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.NotRuined));
        }

        [Test]
        public void 없는_자리_번호는_거부한다()
        {
            BuildResult r = _svc.TryStartRepair(999);

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.UnknownSlot));
        }

        [Test]
        public void CanStartRepair는_상태를_바꾸지_않는다()
        {
            // 폐허 정보 창이 매 프레임 부를 수 있어야 한다.
            long woodBefore = _store.State.Resources.Get(ResourceKind.Wood);

            for (int i = 0; i < 5; i++)
            {
                _svc.CanStartRepair(1);
            }

            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(woodBefore));

            BuildingInstance b;
            _store.State.TryGetBuilding(1, out b);
            Assert.That(b.IsRuined, Is.True);
        }

        [Test]
        public void 시간이_지나야_완성되고_그_전에는_안_된다()
        {
            _svc.TryStartRepair(3);

            _clock.Advance(29_999L);
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0));

            _clock.Advance(1L);
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(1));

            BuildingInstance b;
            _store.State.TryGetBuilding(3, out b);
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Complete));
            Assert.That(b.Level, Is.EqualTo(1));
            Assert.That(b.ConstructionEndsAtUnixMs, Is.EqualTo(0L));
        }

        [Test]
        public void 한꺼번에_지난_여러_자리가_한_번에_완성된다()
        {
            // 앱이 백그라운드에 오래 있었던 경우다. "끝나는 시각 <= 지금"으로 판정하는
            // 덕에 프레임을 몇 개 놓쳐도 결과가 같다.
            _svc.TryStartRepair(1);
            _svc.TryStartRepair(2);
            _svc.TryStartRepair(3);

            _clock.Advance(600_000L);

            Assert.That(_svc.CompleteFinished(), Is.EqualTo(3));
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0), "두 번째 호출은 할 일이 없어야 한다");
        }

        [Test]
        public void 건설시간이_0이면_바로_완성으로_들어간다()
        {
            _svc.TryStartRepair(4);

            BuildingInstance b;
            _store.State.TryGetBuilding(4, out b);

            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Complete));
            Assert.That(b.Level, Is.EqualTo(1));
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0));
        }

        [Test]
        public void 복구해도_점유_칸_수는_변하지_않는다()
        {
            // 폐허와 완성 건물이 같은 자리를 쓴다 — 복구는 자리를 늘리지 않는다.
            int before = _store.State.BaseOccupancy.OccupiedCellCount;

            _svc.TryStartRepair(1);
            _clock.Advance(120_000L);
            _svc.CompleteFinished();

            Assert.That(_store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(before));
        }
    }

    public class BuildingCatalogTests
    {
        [Test]
        public void DefId가_중복이면_등록에서_터진다()
        {
            var catalog = new BuildingCatalog();
            catalog.Add(new BuildingDef("a", "k", GridFootprint.Single, 0L, null));

            Assert.Throws<System.InvalidOperationException>(() =>
                catalog.Add(new BuildingDef("a", "k2", GridFootprint.Single, 0L, null)));
        }

        [Test]
        public void 없는_DefId는_false를_준다()
        {
            var catalog = new BuildingCatalog();

            BuildingDef def;
            Assert.That(catalog.TryGet("nope", out def), Is.False);
            Assert.That(catalog.TryGet(null, out def), Is.False);
        }
    }
}
